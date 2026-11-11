#!/usr/bin/env bash
# Gives the API the Microsoft Graph application permissions it needs to manage staff accounts (iam.md §4.2): create
# and list users, find the HealthyHires API's enterprise application, and assign users app roles on it. Run after the
# first provisioning and after every VM rebuild, because a rebuilt VM gets a new identity
# (infrastructure-operations-architecture.md §4.3).
#
# Who holds the permissions depends on where the company tenant is (iam.md §4.4):
#   - the subscription's tenant: the VM's managed identity itself;
#   - another tenant: the API registration there, which gets a federated credential trusting the managed identity,
#     so the API signs in to the company tenant without a secret.
#
# Granting an application permission is admin consent, so the operator needs the Entra role Privileged Role
# Administrator or Global Administrator in the tenant that holds them. Idempotent: a permission already held is left
# alone, and the federated credential is updated to the current identity.
set -euo pipefail
source "$(dirname "$0")/lib.sh"
load_env
need jq
[[ -n ${HH_ENTRA_TENANT_ID:-} && -n ${HH_API_SCOPE:-} ]] || die "HH_ENTRA_TENANT_ID and HH_API_SCOPE are empty in $env_file (guide §4)"

graph_api=https://graph.microsoft.com/v1.0
identity=$(output vmPrincipalId)
subscription_tenant=$(az account show --query tenantId -o tsv)
if cross_tenant; then tenant=$HH_ENTRA_TENANT_ID; else tenant=$subscription_tenant; fi

# Graph in the chosen tenant, with the operator's token for it rather than for the subscription's tenant.
token=$(az account get-access-token --tenant "$tenant" --resource-type ms-graph --query accessToken -o tsv) \
    || die "not signed in to tenant $tenant; run: az login --tenant $tenant --allow-no-subscriptions"
graph() { az rest --skip-authorization-header --headers "Authorization=Bearer $token" "$@"; }

if cross_tenant; then
    api_client_id=${HH_API_SCOPE#api://}
    api_client_id=${api_client_id%%/*}
    log "Trusting the VM's managed identity on the API registration in tenant $tenant"
    app=$(graph --method GET --url "$graph_api/applications(appId='$api_client_id')?\$select=id" --query id -o tsv)
    credential=$(jq -nc --arg issuer "https://login.microsoftonline.com/$subscription_tenant/v2.0" \
        --arg subject "$identity" '{issuer: $issuer, subject: $subject, audiences: ["api://AzureADTokenExchange"]}')
    existing=$(graph --method GET --url "$graph_api/applications/$app/federatedIdentityCredentials" \
        --query "value[?name=='healthyhires-vm'].id" -o tsv)
    if [[ -n $existing ]]; then
        graph --method PATCH --url "$graph_api/applications/$app/federatedIdentityCredentials/$existing" \
            --body "$credential" --output none
    else
        graph --method POST --url "$graph_api/applications/$app/federatedIdentityCredentials" \
            --body "$(jq -c '. + {name: "healthyhires-vm"}' <<< "$credential")" --output none
    fi
    principal=$(graph --method GET --url "$graph_api/servicePrincipals(appId='$api_client_id')?\$select=id" \
        --query id -o tsv)
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

log "Done: the API can manage staff accounts. New permissions reach the identity's tokens within about an hour."
