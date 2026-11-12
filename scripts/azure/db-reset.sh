#!/usr/bin/env bash
# Recreates the application's schemas in Azure PostgreSQL from the current configs/postgres/init/01-create-db.sql,
# and with --seed loads the development seed (02-seed-data.sql) on top. Meant for the demo deployment while it holds
# no real data: until migrations exist (ADR-D005) it is how a schema change reaches Azure.
#
#   scripts/azure/db-reset.sh           drop every schema in infra/db-schemas.txt and create the tables again, empty
#   scripts/azure/db-reset.sh --seed    the same, then load 02-seed-data.sql
#
# DESTRUCTIVE: every row in those schemas is lost. It shows the row counts and asks for confirmation first. Blobs
# are not touched, so files uploaded before the reset stay in the storage account with no row pointing at them.
#
# One transaction: the operator drops and recreates the schemas (owned by healthyhires_app, as db-grant-identity.sh
# makes them) and the extensions, then switches to healthyhires_app for the DDL and the seed, so every object belongs
# to the application role. A failure anywhere leaves the database as it was.
#
# Needs PostgreSQL and the VM running (just azure-resume; the tunnel goes through the VM), psql 18+ locally and this
# machine on NetBird. The API keeps running; restart or redeploy it afterwards so it does not hold on to the old tables.
set -euo pipefail
source "$(dirname "$0")/lib.sh"

seed=false
for arg in "$@"; do
    case $arg in
        --seed) seed=true ;;
        *) die "usage: db-reset.sh [--seed]" ;;
    esac
done

load_env
need psql ssh

ddl="$root/configs/postgres/init/01-create-db.sql"
seed_file="$root/configs/postgres/init/02-seed-data.sql"
[[ -f $ddl ]] || die "$ddl not found"
[[ $seed == false || -f $seed_file ]] || die "$seed_file not found"

# Same check as db-schema.sh: every schema the DDL creates must be one db-grant-identity.sh manages.
mapfile -t ddl_schemas < <(sed -nE 's/^CREATE SCHEMA IF NOT EXISTS ([a-z][a-z0-9_]*);$/\1/p' "$ddl")
mapfile -t granted < <(grep -E '^[a-z][a-z0-9_]*$' "$root/infra/db-schemas.txt")
(( ${#ddl_schemas[@]} > 0 )) || die "found no CREATE SCHEMA statement in $ddl"
for schema in "${ddl_schemas[@]}"; do
    [[ " ${granted[*]} " == *" $schema "* ]] \
        || die "$ddl creates schema $schema, which infra/db-schemas.txt does not list; add it and run just azure-db-grant"
done
mapfile -t ddl_extensions < <(grep -E '^CREATE EXTENSION IF NOT EXISTS [a-z][a-z0-9_]* WITH SCHEMA public;$' "$ddl")

pg=$(output postgresServerName)
state=$(az postgres flexible-server show --resource-group "$rg" --name "$pg" --query state -o tsv)
[[ $state == Ready ]] || die "PostgreSQL $pg is $state; run just azure-resume first"
vm=$(output vmName)
state=$(az vm show --resource-group "$rg" --name "$vm" --show-details --query powerState -o tsv)
[[ $state == "VM running" ]] || die "$vm is '$state' and the tunnel goes through it; run just azure-resume first"

open_pg_tunnel

operator() {
    psql "host=localhost port=$port user=$HH_OPERATOR_UPN dbname=healthyhires" -v ON_ERROR_STOP=1 -q "$@"
}

schema_list=$(printf "'%s'," "${granted[@]}")
schema_list=${schema_list%,}

# Exact row counts of every table in the schemas; the tables are small enough to count.
row_counts() {
    operator -At -F $'\t' <<SQL | while IFS=$'\t' read -r table rows; do log "  $table: $rows"; done
SELECT format('SELECT %L, count(*) FROM %I.%I', schemaname || '.' || tablename, schemaname, tablename)
FROM pg_tables WHERE schemaname IN ($schema_list) ORDER BY schemaname, tablename
\gexec
SQL
}

log "Rows that the reset deletes"
row_counts
warn "every row in schemas ${granted[*]} on $pg is deleted"

if [[ -t 0 ]]; then
    read -r -p "Type the server name ($pg) to continue: " answer
    [[ $answer == "$pg" ]] || die "not confirmed; nothing changed"
else
    die "not a terminal; run it interactively to confirm"
fi

log "Recreating schemas ${granted[*]} from 01-create-db.sql$([[ $seed == true ]] && echo ' and loading 02-seed-data.sql')"
{
    # Waits at most 30 s for the running API to let go of a table, rather than hanging behind it.
    echo "SET lock_timeout = '30s';"
    echo "SET client_min_messages = warning;"
    for schema in "${granted[@]}"; do
        echo "DROP SCHEMA IF EXISTS $schema CASCADE;"
        echo "CREATE SCHEMA $schema AUTHORIZATION healthyhires_app;"
    done
    printf '%s\n' "${ddl_extensions[@]}"
    echo "SET LOCAL ROLE healthyhires_app;"
    # The schemas and extensions exist now; the application role may not create either, even with IF NOT EXISTS.
    sed -E -e '/^CREATE SCHEMA IF NOT EXISTS [a-z][a-z0-9_]*;$/d' \
        -e '/^CREATE EXTENSION IF NOT EXISTS [a-z][a-z0-9_]* WITH SCHEMA public;$/d' "$ddl"
    if [[ $seed == true ]]; then
        cat "$seed_file"
    fi
} | operator --single-transaction -f -

log "Rows now"
row_counts
log "Done. Restart the API so it reconnects to the new tables: gh workflow run deploy --repo $HH_GITHUB_REPOSITORY"
