#!/usr/bin/env bash
# Gives the API the Microsoft Graph application permissions it needs to manage staff accounts (iam.md §4.2): create
# and list users, find the HealthyHires API's enterprise application, and assign users app roles on it. Run after the
# first provisioning and after every VM rebuild, because a rebuilt VM gets a new identity
# (infrastructure-operations-architecture.md §4.3).
#
# Who holds the permissions depends on where the company tenant is (iam.md §4.4):
#   - the subscription's tenant: the VM's managed identity itself;
#   - another tenant: the multitenant registration "HealthyHires Graph (<environment>)" in the subscription's tenant,
#     whose federated credential trusts the managed identity, added to the company tenant. Its client id goes to the
#     GitHub environment as ENTRA_GRAPH_CLIENT_ID, so the next deploy signs the API in that way, without a secret.
#
# Granting an application permission is admin consent, so the operator needs the Entra role Privileged Role
# Administrator or Global Administrator in the company tenant. Idempotent: a permission already held is left alone, and
# the federated credential is updated to the current identity.
set -euo pipefail
source "$(dirname "$0")/lib.sh"
load_env
need jq gh
[[ -n ${HH_ENTRA_TENANT_ID:-} ]] || die "HH_ENTRA_TENANT_ID is empty in $env_file (guide §4)"

graph_api=https://graph.microsoft.com/v1.0
identity=$(output vmPrincipalId)
subscription_tenant=$(az account show --query tenantId -o tsv)
cross=false
cross_tenant && cross=true

# In the subscription's tenant, as the operator there. Entra accepts a managed identity's token as a federated
# credential only for a registration in the identity's own tenant (AADSTS700236 otherwise), and reaches another
# tenant only through a multitenant registration added to it.
if $cross; then
    name="HealthyHires Graph ($HH_ENVIRONMENT)"
    graph_app=$(az ad app list --display-name "$name" --query '[].appId' -o tsv)
    [[ $(wc -w <<< "$graph_app") -le 1 ]] || die "several app registrations are named $name in tenant $subscription_tenant"
    if [[ -z $graph_app ]]; then
        log "Registering $name in tenant $subscription_tenant"
        graph_app=$(az ad app create --display-name "$name" --sign-in-audience AzureADMultipleOrgs --query appId -o tsv)
    fi
    log "Trusting the VM's managed identity on $name"
    credential=$(jq -nc --arg issuer "https://login.microsoftonline.com/$subscription_tenant/v2.0" --arg subject "$identity" \
        '{name: "healthyhires-vm", issuer: $issuer, subject: $subject, audiences: ["api://AzureADTokenExchange"]}')
    if az ad app federated-credential show --id "$graph_app" --federated-credential-id healthyhires-vm -o none 2> /dev/null; then
        az ad app federated-credential update --id "$graph_app" --federated-credential-id healthyhires-vm \
            --parameters "$credential" -o none
    else
        az ad app federated-credential create --id "$graph_app" --parameters "$credential" -o none
    fi
fi

# The operator's admin account in the company tenant is often another account than the subscription's. az keeps every
# signed-in account, so the one in that tenant is made current for the token: HH_ENTRA_ADMIN_UPN when several are.
use_tenant_account() {
    local users
    users=$(az account list --all --query "[?tenantId=='$1'].user.name" -o tsv | sort -u)
    [[ -n ${HH_ENTRA_ADMIN_UPN:-} ]] && users=$(grep -ix "$HH_ENTRA_ADMIN_UPN" <<< "$users" || true)
    [[ -n $users ]] || die "no admin account signed in to tenant $1; run: az login --tenant $1 --allow-no-subscriptions"
    [[ $(wc -l <<< "$users") == 1 ]] \
        || die "several accounts are signed in to tenant $1 ($(paste -sd ' ' <<< "$users")); set HH_ENTRA_ADMIN_UPN to the admin one"
    az account set --subscription "$(az account list --all --query "[?tenantId=='$1' && user.name=='$users'] | [0].id" -o tsv)"
}

if $cross; then use_tenant_account "$HH_ENTRA_TENANT_ID"; fi
token=$(az account get-access-token --resource-type ms-graph --query accessToken -o tsv) || die "could not get a Graph token"
az account set --subscription "$HH_SUBSCRIPTION_ID"
graph() { az rest --skip-authorization-header --headers "Authorization=Bearer $token" "$@"; }

if $cross; then
    # The registration's service principal in the company tenant holds the permissions there. A registration created a
    # moment ago can take a minute to become visible to another tenant.
    principal=$(graph --method GET --url "$graph_api/servicePrincipals(appId='$graph_app')?\$select=id" \
        --query id -o tsv 2> /dev/null || true)
    for attempt in 1 2 3 4 5 6; do
        [[ -z $principal ]] || break
        log "Adding $name to tenant $HH_ENTRA_TENANT_ID (attempt $attempt)"
        principal=$(graph --method POST --url "$graph_api/servicePrincipals" --body "{\"appId\":\"$graph_app\"}" \
            --query id -o tsv 2> /dev/null || true)
        [[ -n $principal ]] || sleep 10
    done
    [[ -n $principal ]] || die "could not add $name ($graph_app) to tenant $HH_ENTRA_TENANT_ID"
else
    principal=$identity
fi

# Microsoft Graph's own service principal in the tenant, which defines the permissions as app roles.
graph_sp=$(graph --method GET \
    --url "$graph_api/servicePrincipals(appId='00000003-0000-0000-c000-000000000000')?\$select=id,appRoles")
graph_id=$(jq -r .id <<< "$graph_sp")
held=$(graph --method GET --url "$graph_api/servicePrincipals/$principal/appRoleAssignments" \
    --query "value[?resourceId=='$graph_id'].appRoleId" -o tsv)

for permission in User.ReadWrite.All Application.Read.All AppRoleAssignment.ReadWrite.All; do
    role_id=$(jq -r --arg value "$permission" '.appRoles[] | select(.value == $value) | .id' <<< "$graph_sp")
    [[ -n $role_id ]] || die "Microsoft Graph has no application permission $permission"
    if grep -qx "$role_id" <<< "$held"; then
        log "already granted: $permission"
        continue
    fi
    graph --method POST --url "$graph_api/servicePrincipals/$principal/appRoleAssignments" \
        --body "{\"principalId\":\"$principal\",\"resourceId\":\"$graph_id\",\"appRoleId\":\"$role_id\"}" --output none
    log "granted: $permission"
done

# The API learns how to reach Graph from the GitHub environment (deploy/render-bundle.sh): the registration's client id
# when cross-tenant, nothing when the managed identity calls Graph itself.
if $cross; then
    gh variable set ENTRA_GRAPH_CLIENT_ID --repo "$HH_GITHUB_REPOSITORY" --env production --body "$graph_app"
else
    gh variable delete ENTRA_GRAPH_CLIENT_ID --repo "$HH_GITHUB_REPOSITORY" --env production 2> /dev/null || true
fi

log "Done: the API can manage staff accounts after its next deploy. New permissions and a new federated credential can take several minutes, permissions up to an hour, to take effect."
