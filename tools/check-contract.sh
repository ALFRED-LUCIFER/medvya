#!/usr/bin/env bash
# API contract drift check (MDV-P0-040): re-export Host.Api's OpenAPI document, regenerate the TypeScript client,
# and fail if either differs from what is committed. Fix by running the same two steps and committing the result.
set -euo pipefail
cd "$(dirname "$0")/.."
export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
dotnet build src/Host.Api -p:ExportOpenApi=true --nologo -v:q
pnpm --filter @medvya/api-client generate
git diff --exit-code -- packages/api-client || { echo "API contract drifted: commit the regenerated packages/api-client." >&2; exit 1; }
