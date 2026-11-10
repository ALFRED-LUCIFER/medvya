#!/usr/bin/env bash
# MDV-P1-067: the Terms and DPA wording is a draft until counsel signs it off (MDV-P1-044). Usage: tools/check-legal-marker.sh <environment>
# Fails for any environment but dev while the marker is still in the terms source.
set -euo pipefail
cd "$(dirname "$0")/.."
[ "${1:?environment name}" = dev ] && exit 0
if grep -rqF '[LEGAL: counsel to confirm]' docs/compliance/terms-dpa-licence-clause.md packages/i18n/messages; then
  echo "::error::Terms and DPA still carry [LEGAL: counsel to confirm]; no deploy to '$1' before counsel signs off (MDV-P1-044)"; exit 1
fi
