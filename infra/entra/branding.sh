#!/usr/bin/env bash
# MDV-P1-060: Entra External ID company branding for the hosted email-code, MFA set-up and recovery pages (the parts of the 6 Nov
# sign-in design Medvya does not render). Logo, background colour, sign-in text and privacy/terms links per country, from files in
# this repo; no secret anywhere. Idempotent: every run sets the same values (PATCH/PUT), and creates a localization only if missing.
#
#   az login --tenant <tenant-id> --allow-no-subscriptions     # an Organizational Branding Administrator of that tenant
#   infra/entra/branding.sh <tenant-id> <IN|AE> <site-url> [--print]
#
# --print shows the request bodies and changes nothing. Run by a person, never by CI; record each run in
# docs/05-delivery/session-handoff.md. Entra hosted pages take only a logo, colours, background and text: no layout changes.
set -euo pipefail

usage() { echo "usage: $0 <tenant-id> <IN|AE> <https site url> [--print]" >&2; exit 2; }
[ $# -ge 3 ] && [ $# -le 4 ] || usage
tenant=$1 country=$2 site=${3%/} print=${4:-}
[[ $tenant =~ ^[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$ ]] || usage
[[ $country == IN || $country == AE ]] || usage
[[ $site =~ ^https://[A-Za-z0-9.-]+(:[0-9]+)?(/[A-Za-z0-9._/-]*)?$ ]] || usage
[[ -z $print || $print == --print ]] || usage

dir=$(cd "$(dirname "$0")/branding" && pwd)
repo=$(cd "$dir/../../.." && pwd)
graph="https://graph.microsoft.com/v1.0/organization/$tenant/branding"

colour=$(jq -er '.color["brand-950"]' "$repo/packages/tokens/src/tokens.json")
# shortcut: privacy and terms both open the trust centre until the legal pages exist (P1-044); then point each at its own page.
links=$(jq -n --arg u "$site/trust" '{customPrivacyAndCookiesUrl: $u, customTermsOfUseUrl: $u}')
default=$(jq -e --arg c "$colour" --argjson l "$links" '.default + {backgroundColor: $c} + $l' "$dir/$country.json")
locales=$(jq -er '.localizations | keys[]' "$dir/$country.json")
localized() { jq -e --argjson links "$links" --arg c "$colour" --arg id "$1" '.localizations[$id] + {backgroundColor: $c} + $links' "$dir/$country.json"; }

if [[ $print == --print ]]; then
  echo "PATCH $graph (default)"; echo "$default"
  for l in $locales; do echo "PATCH or POST $graph/localizations/$l"; localized "$l"; done
  echo "PUT $graph/localizations/0/bannerLogo < banner-logo.png"; echo "PUT $graph/localizations/0/squareLogo < square-logo.png"
  exit 0
fi

# Never brand a tenant other than the one named: the az sign-in must be in it.
[ "$(az account show --query tenantId -o tsv)" = "$tenant" ] || { echo "az is signed in to another tenant: az login --tenant $tenant --allow-no-subscriptions" >&2; exit 1; }

# Localizations first: creating one also creates the default branding when the tenant has none yet (Graph behaviour).
for l in $locales; do
  body=$(localized "$l")
  if az rest --method get --url "$graph/localizations/$l" -o none 2>/dev/null; then
    az rest --method patch --url "$graph/localizations/$l" --headers "Content-Type=application/json" --body "$body" -o none
  else
    az rest --method post --url "$graph/localizations" --headers "Content-Type=application/json" --body "$(jq --arg id "$l" '. + {id: $id}' <<<"$body")" -o none
  fi
  echo "branding $l: set"
done

az rest --method patch --url "$graph" --headers "Content-Type=application/json" "Accept-Language=0" --body "$default" -o none
echo "branding default: set"

# Images are binary, which az rest does not send intact; curl gets a short-lived token on stdin, never on the command line.
token=$(az account get-access-token --resource-type ms-graph --query accessToken -o tsv)
for image in bannerLogo:banner-logo.png squareLogo:square-logo.png; do
  printf 'Authorization: Bearer %s\n' "$token" | curl -fsS -X PUT -H @- -H "Content-Type: image/png" \
    --data-binary "@$dir/${image#*:}" "$graph/localizations/0/${image%%:*}" -o /dev/null
  echo "branding ${image%%:*}: set"
done
