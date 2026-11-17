#!/bin/sh
# PostToolUse (Edit, Write): lint the one file just changed. Never blocks: findings go back to Claude as context.
input=$(cat)
path=$(printf '%s' "$input" | jq -r '.tool_input.file_path // empty')
[ -f "$path" ] || exit 0
root=$(git -C "$(dirname "$path")" rev-parse --show-toplevel 2>/dev/null) || exit 0
rel=${path#"$root"/}

report() {
  jq -n --arg msg "$1" '{hookSpecificOutput: {hookEventName: "PostToolUse", additionalContext: $msg}}'
  exit 0
}

# Nearest file named $1 between the edited file and the repo root.
nearest() {
  d=$(dirname "$path")
  while [ "$d" != "$root" ] && [ "$d" != "/" ]; do
    [ -e "$d/$1" ] && { echo "$d"; return; }
    d=$(dirname "$d")
  done
}

case "$rel" in
  web/*.ts|web/*.tsx|packages/*.ts|packages/*.tsx|mobile/*.ts|mobile/*.tsx)
    config=$(nearest .oxlintrc.json); pkg=$(nearest package.json)
    [ -n "$config" ] && [ -n "$pkg" ] && [ -x "$pkg/node_modules/.bin/oxlint" ] || exit 0
    out=$(cd "$pkg" && node_modules/.bin/oxlint -c "$config/.oxlintrc.json" "$path" 2>&1) || report "oxlint findings in $rel:
$(printf '%s' "$out" | tail -40)"
    ;;
  src/AiService/*.py)
    command -v uv >/dev/null 2>&1 || exit 0
    out=$(cd "$root/src/AiService" && uv run --frozen ruff check "$path" 2>&1 && uv run --frozen ruff format --check "$path" 2>&1) || report "ruff findings in $rel:
$(printf '%s' "$out" | tail -40)"
    ;;
esac
exit 0
