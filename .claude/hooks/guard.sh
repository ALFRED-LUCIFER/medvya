#!/bin/sh
# PreToolUse guard (Bash, Edit, Write): blocks what AGENTS.md and CLAUDE.md forbid. Exit 2 = blocked, reason on stderr.
input=$(cat)
tool=$(printf '%s' "$input" | jq -r '.tool_name // empty')

block() { echo "Blocked by .claude/hooks/guard.sh: $1" >&2; exit 2; }

case "$tool" in
  Bash)
    cmd=$(printf '%s' "$input" | jq -r '.tool_input.command // empty')
    case "$cmd" in *--no-verify*) block "never bypass hooks with --no-verify (AGENTS.md 5)." ;; esac
    case "$cmd" in *"gh pr merge"*--admin*) block "never merge with --admin (CLAUDE.md 5)." ;; esac
    case "$cmd" in *gh_issues.py*--refresh*) block "gh_issues.py --refresh overwrites issues from the source (AGENTS.md 2.1)." ;; esac
    if printf '%s' "$cmd" | grep -Eq 'git[[:space:]]+push' \
      && printf '%s' "$cmd" | grep -Eq '(^|[[:space:]])(--force(-with-lease)?|-f)([[:space:]=]|$)|[[:space:]]\+[A-Za-z]' \
      && printf '%s' "$cmd" | grep -Eq '(^|[[:space:]:/+])main([[:space:]]|$)'; then
      block "never force-push main (AGENTS.md 5)."
    fi
    if printf '%s' "$cmd" | grep -Eq 'git[[:space:]]+commit'; then
      dir=$(printf '%s' "$input" | jq -r '.cwd // empty')
      branch=$(git -C "${dir:-.}" branch --show-current 2>/dev/null)
      [ "$branch" = "main" ] && block "do not commit on main; create a branch first (AGENTS.md 7)."
    fi
    ;;
  Edit|Write|MultiEdit)
    path=$(printf '%s' "$input" | jq -r '.tool_input.file_path // empty')
    case "$path" in
      */docs/05-delivery/tickets/_source/*|docs/05-delivery/tickets/_source/*) ;;
      */docs/05-delivery/tickets/*|docs/05-delivery/tickets/*) block "generated ticket pages: edit docs/05-delivery/tickets/_source and run python3 -I tools/tickets/build.py." ;;
      */docs/02-design/source/*|docs/02-design/source/*) block "docs/02-design/source holds read-only design imports." ;;
    esac
    ;;
esac
exit 0
