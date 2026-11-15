#!/usr/bin/env bash
# MDV-P5-040 (ADR-014): the platform console's app registration in the company (workforce) tenant, and its client secret in Key Vault.
# Single tenant, web platform, authorization code flow only (no implicit grant: the authorize endpoint issues no ID or access token), one
# redirect URI https://<console host>/signin-oidc. The secret goes from Entra into Key Vault through a private temporary file; it is never
# printed or put on a command line. Idempotent: a later run finds the registration by name, sets the same redirect URI and replaces the
# secret (every older secret is removed; restart the console web app afterwards so its Key Vault reference reads the new one).
#
#   az login --tenant <tenant-id>      # Application Administrator in the tenant, and Key Vault Secrets Officer on the vault
#   infra/entra/console-app.sh <tenant-id> <console host> <key vault name>
#
# Prints the application (client) id for the repository variable CONSOLE_SIGNIN_CLIENT_ID. Run by a person, never by CI; the steps around it
# are in docs/runbooks/platform-console-azure.md.
# shortcut: the vault must be in the same tenant as the app registration (true for dev); for a separate workforce tenant, run the
# Entra part signed in there and the Key Vault step signed in to the subscription's tenant.
set -euo pipefail

usage() { echo "usage: $0 <tenant-id> <console host, e.g. console-medvya-dev-abc123.azurewebsites.net> <key vault name>" >&2; exit 2; }
[ $# -eq 3 ] || usage
tenant=$1 host=$2 vault=$3
[[ $tenant =~ ^[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$ ]] || usage
[[ $host =~ ^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)+$ ]] || usage
[[ $vault =~ ^[A-Za-z][A-Za-z0-9-]{1,22}[A-Za-z0-9]$ ]] || usage

# Never register in a tenant other than the one named: the az sign-in must be in it.
[ "$(az account show --query tenantId -o tsv)" = "$tenant" ] || { echo "az is signed in to another tenant: az login --tenant $tenant" >&2; exit 1; }

name="Medvya platform console ($host)"
redirect="https://$host/signin-oidc"
ids=$(az ad app list --display-name "$name" --query "[].appId" -o tsv)
if [ -z "$ids" ]; then
  app=$(az ad app create --display-name "$name" --sign-in-audience AzureADMyOrg --web-redirect-uris "$redirect" \
    --enable-id-token-issuance false --enable-access-token-issuance false --query appId -o tsv)
  echo "app registration: created"
elif [ "$(wc -l <<<"$ids")" -eq 1 ]; then
  app=$ids
  az ad app update --id "$app" --web-redirect-uris "$redirect" --enable-id-token-issuance false --enable-access-token-issuance false
  echo "app registration: found, redirect URI and flows set"
else
  echo "more than one app registration is named '$name'; delete the extra ones first" >&2; exit 1
fi

# Sign-in needs the app's service principal in the tenant.
az ad sp show --id "$app" -o none 2>/dev/null || { az ad sp create --id "$app" -o none; echo "service principal: created"; }

# A reset (not --append) leaves exactly one secret. No trailing newline: the file is the secret's exact value.
umask 077
secret=$(mktemp)
trap 'rm -f "$secret"' EXIT
az ad app credential reset --id "$app" --display-name "console-$(date -u +%Y%m%d)" --years 1 --query password -o tsv | tr -d '\n' > "$secret"
[ -s "$secret" ] || { echo "Entra returned no secret" >&2; exit 1; }
az keyvault secret set --vault-name "$vault" --name platform-console-client-secret --file "$secret" -o none
echo "client secret: replaced in Key Vault $vault as platform-console-client-secret (expires in one year)"

echo "CONSOLE_SIGNIN_CLIENT_ID=$app"
