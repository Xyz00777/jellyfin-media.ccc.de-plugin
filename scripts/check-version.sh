#!/usr/bin/env bash
# Keeps plugin and Jellyfin ABI versions aligned; shared by pre-commit, CI, and releases.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
META="$ROOT_DIR/meta.json"
CSPROJ="$ROOT_DIR/Jellyfin.Plugin.MediaCccDe.csproj"

die() { printf 'Version check failed: %s\n' "$*" >&2; exit 1; }

json_field() {
    local key="$1"
    if command -v python3 >/dev/null 2>&1; then
        python3 -c 'import json,sys; v=json.load(open(sys.argv[1])).get(sys.argv[2]); print(v if isinstance(v,str) else "")' "$META" "$key" && return
    fi
    if command -v jq >/dev/null 2>&1; then
        jq -r --arg key "$key" 'if .[$key] | type == "string" then .[$key] else empty end' "$META" && return
    fi
    # meta.json is flat and its top-level keys use exactly two leading spaces.
    local line
    line="$(grep -E '^  "'"$key"'"[[:space:]]*:' "$META" | head -n 1 || true)"
    [[ -n "$line" ]] || return 1
    printf '%s\n' "$line" | sed -E 's/^[[:space:]]*"[^"]+"[[:space:]]*:[[:space:]]*"([^"]*)"[[:space:]]*,?[[:space:]]*$/\1/'
}

xml_version() {
    awk -F'[<>]' '/<Version>[[:space:]]*[^<]+[[:space:]]*<\/Version>/ {gsub(/[[:space:]]/, "", $3); print $3; exit}' "$CSPROJ"
}

xml_controller_version() {
    awk '
        /<PackageReference[[:space:]]/ {
            controller = ($0 ~ /Include="Jellyfin\.Controller"/)
            if (controller && match($0, /Version="[^"]+"/)) {
                value = substr($0, RSTART + 9, RLENGTH - 10); print value; exit
            }
        }
        controller && /<Version>/ {
            line = $0; sub(/^.*<Version>/, "", line); sub(/<\/Version>.*$/, "", line)
            gsub(/[[:space:]]/, "", line); print line; exit
        }
        controller && /<\/PackageReference>/ { controller = 0 }
    ' "$CSPROJ"
}

behind() {
    local left="$1" right="$2" i
    local -a l r
    IFS=. read -r -a l <<< "$left"
    IFS=. read -r -a r <<< "$right"
    for ((i=0; i<${#l[@]} || i<${#r[@]}; i++)); do
        local a="${l[i]:-0}" b="${r[i]:-0}"
        [[ "$a" =~ ^[0-9]+$ && "$b" =~ ^[0-9]+$ ]] || return 1
        ((10#$a < 10#$b)) && return 0
        ((10#$a > 10#$b)) && return 1
    done
    return 1
}

meta_version="$(json_field version)" || die "could not read top-level 'version' from $META"
target_abi="$(json_field targetAbi)" || die "could not read top-level 'targetAbi' from $META"
project_version="$(xml_version)"
controller_version="$(xml_controller_version)"
[[ -n "$project_version" ]] || die "could not read <Version> from $CSPROJ"
[[ -n "$controller_version" ]] || die "could not read Jellyfin.Controller PackageReference Version from $CSPROJ"

if [[ "$meta_version" != "$project_version" && "$meta_version" != "$project_version.0" ]]; then
    if behind "$meta_version" "$project_version"; then
        die "plugin version mismatch: $META version='$meta_version' is behind $CSPROJ <Version>='$project_version'; update meta.json version to '$project_version' (or '$project_version.0')."
    elif behind "$project_version" "$meta_version"; then
        die "plugin version mismatch: $CSPROJ <Version>='$project_version' is behind $META version='$meta_version'; update the project version or manifest to the intended release."
    else
        die "plugin version mismatch: $META version='$meta_version' must equal $CSPROJ <Version>='$project_version' or '$project_version.0'; update meta.json version."
    fi
fi

# ABI manifests conventionally use four components (12.2.0.0) for the three-part NuGet version (12.2.0).
if [[ "$target_abi" != "$controller_version" && "$target_abi" != "$controller_version.0" && "$target_abi" != "$controller_version.0.0" ]]; then
    if behind "$target_abi" "$controller_version"; then
        die "Jellyfin ABI mismatch: $META targetAbi='$target_abi' is behind $CSPROJ Jellyfin.Controller Version='$controller_version'. Jellyfin may accept this plugin during installation and then silently disable it at load time with Could not load file or assembly 'MediaBrowser.Controller'; update targetAbi to '$controller_version.0'."
    fi
    die "Jellyfin ABI mismatch: $META targetAbi='$target_abi' does not match $CSPROJ Jellyfin.Controller Version='$controller_version'; use '$controller_version.0' (the expected four-part ABI form) or another explicitly equivalent allowed form."
fi

if [[ -n "${EXPECTED_VERSION:-}" && "$EXPECTED_VERSION" != "$project_version" ]]; then
    die "release tag mismatch: EXPECTED_VERSION='$EXPECTED_VERSION' but $CSPROJ <Version>='$project_version'; update the project version or build the matching tag."
fi

# The release workflow is tag-triggered, so merging to main publishes nothing and nothing
# else in this file can tell a released version from a pending one. Without this, code
# carrying security fixes merged while the tag for its version already pointed at older
# code, leaving the published release without those fixes and no failing check anywhere.
# A tag sitting on the current commit is fine: that release does contain this code.
if [[ -z "${EXPECTED_VERSION:-}" ]] && command -v git >/dev/null 2>&1 && git rev-parse --git-dir >/dev/null 2>&1; then
    tagged_commit="$(git rev-parse -q --verify "refs/tags/v${project_version}^{commit}" 2>/dev/null || true)"
    head_commit="$(git rev-parse -q --verify HEAD 2>/dev/null || true)"
    if [[ -n "$tagged_commit" && "$tagged_commit" != "$head_commit" ]]; then
        # CI cannot be handed an environment variable by a contributor, so the commit
        # message carries the opt-out for a change that must not need one. It ends up in
        # the reviewed history instead of being an invisible job setting.
        skip_reason="${SKIP_RELEASE_GUARD:+environment}"
        if [[ -z "$skip_reason" ]]; then
            # A pull_request checkout lands on a synthetic merge commit whose message is
            # "Merge ... into ...", so the marker has to be read from the branch tip that
            # CI passes in, or from any commit the pull request adds.
            marker_commit="${RELEASE_GUARD_HEAD_SHA:-HEAD}"
            if git log -1 --format=%B "$marker_commit" 2>/dev/null | grep -qi '\[no-release\]' \
                || git log --format=%B "origin/main..$marker_commit" 2>/dev/null | grep -qi '\[no-release\]'; then
                skip_reason='[no-release] in the commit message'
            fi
        fi

        if [[ -n "$skip_reason" ]]; then
            printf 'Release guard skipped via %s: v%s is already published and %s does not bump the version.\n' \
                "$skip_reason" "$project_version" "${head_commit:0:7}" >&2
        else
            die "version ${project_version} is already published as tag v${project_version} from $(git rev-parse --short "refs/tags/v${project_version}^{commit}"): bump <Version> in $CSPROJ and version in $META so these changes ship in a new release, or add [no-release] to the commit message for a change that does not need one."
        fi
    fi
fi

printf 'Version check passed: plugin %s (%s)\n' "$project_version" "$meta_version"
printf 'Jellyfin targetAbi %s matches Jellyfin.Controller %s.\n' "$target_abi" "$controller_version"
