#!/bin/sh
# Reports trailing whitespace, tab characters, and missing final newlines in tracked text files.
set -eu

status=0

check_file() {
    file=$1

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
}

# The file list is piped in, so run the loop in the current shell rather than a
# subshell; otherwise a non-zero status set inside it would be discarded.
git ls-files >/tmp/check-whitespace-files.$$
trap 'rm -f /tmp/check-whitespace-files.$$' EXIT INT TERM

while IFS= read -r file; do
    case "$file" in
        *.sh|*.json|*.yml|*.yaml|*.html) ;;
        *) continue ;;
    esac
    check_file "$file"
done </tmp/check-whitespace-files.$$

exit "$status"
