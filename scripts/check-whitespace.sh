#!/bin/sh
set -eu

status=0
git ls-files | while IFS= read -r file; do
    case "$file" in
        *.sh|*.json|*.yml|*.yaml|*.html) ;;
        *) continue ;;
    esac

    if grep -n '[[:blank:]]$' "$file" >/dev/null; then
        printf '%s: trailing whitespace found\n' "$file" >&2
        status=1
    fi

    if grep -n "$(printf '\t')" "$file" >/dev/null; then
        printf '%s: tab character found\n' "$file" >&2
        status=1
    fi

    if [ -s "$file" ] && [ "$(tail -c 1 "$file" | wc -l)" -eq 0 ]; then
        printf '%s: missing final newline\n' "$file" >&2
        status=1
    fi
done
exit "$status"
