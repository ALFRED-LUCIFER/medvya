#!/usr/bin/env bash
# API contract drift check (MDV-P0-040): re-export the OpenAPI documents of Host.Api (openapi.json) and of the platform console host
# (console-openapi.json), regenerate the TypeScript schemas, and fail if any differs from what is committed. Fix by running the same
# steps and committing the result.
set -euo pipefail
cd "$(dirname "$0")/.."
export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
dotnet build src/Host.Api -p:ExportOpenApi=true --nologo -v:q
dotnet build src/Host.Platform -p:ExportOpenApi=true --nologo -v:q
pnpm --filter @medvya/api-client generate
git diff --exit-code -- packages/api-client || { echo "API contract drifted: commit the regenerated packages/api-client." >&2; exit 1; }
# git diff does not see a new, untracked document.
[ -z "$(git ls-files --others --exclude-standard -- packages/api-client)" ] || { echo "API contract drifted: commit the new files in packages/api-client." >&2; exit 1; }
