#!/usr/bin/env bash
# Creates and pushes the release tag for the version declared in the project file.
#
# CI calls this only after the build for that commit has passed. Tagging a commit whose
# checks failed is how a release ends up missing the code it was meant to ship, which is the
# failure this repository already hit once: security fixes merged while the published
# release for their version was built from older code.
set -euo pipefail

usage() {
    cat <<'EOF'
Usage: scripts/auto-tag-release.sh [--dry-run]

Creates and pushes an annotated tag v<version> for the <Version> declared in
Jellyfin.Plugin.MediaCccDe.csproj, unless that tag already exists.

  --dry-run   Report what would happen without creating or pushing anything.

Exits 0 without tagging when the version is already tagged, so the caller can treat
"nothing to release" and "tag pushed" as the same successful outcome.
EOF
}

mode=push
case "${1:-}" in
    --dry-run) mode=dry-run ;;
    -h | --help)
        usage
        exit 0
        ;;
    '') ;;
    *)
        printf 'Unknown argument: %s\n' "$1" >&2
        usage >&2
        exit 2
        ;;
esac

CSPROJ="${CSPROJ:-Jellyfin.Plugin.MediaCccDe.csproj}"

die() {
    printf '%s\n' "$1" >&2
    exit 1
}

version="$(sed -nE 's/^[[:space:]]*<Version>([^<]+)<\/Version>.*/\1/p' "$CSPROJ" | head -n1)"

[[ -n "$version" ]] || die "No <Version> found in $CSPROJ; refusing to tag."
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] ||
    die "Version '$version' is not X.Y.Z; refusing to tag v$version."

tag="v$version"

# --exit-code reports 2 for "no such ref" and 128 for "could not reach the remote". Treating
# those the same would let a network failure look like a missing tag and start a release for
# a version that is already published.
tag_exists() {
    git rev-parse -q --verify "refs/tags/$tag" >/dev/null 2>&1 && return 0

    local status=0
    git ls-remote --exit-code --tags origin "refs/tags/$tag" >/dev/null 2>&1 || status=$?
    case "$status" in
        0) return 0 ;;
        2) return 1 ;;
        *) die "Could not ask origin whether $tag exists (git ls-remote exited $status); refusing to guess." ;;
    esac
}

if tag_exists; then
    printf '%s already exists; nothing to release.\n' "$tag"
    exit 0
fi

if [[ "$mode" == dry-run ]]; then
    printf 'Would create and push %s at %s\n' "$tag" "$(git rev-parse --short HEAD)"
    exit 0
fi

# Never --force: an existing tag is left alone by the check above, so a push here can only
# ever add a ref. A moved tag would silently change what an already-published release is.
git tag -a "$tag" -m "Release $tag" HEAD
git push origin "refs/tags/$tag"
printf 'Created and pushed %s at %s\n' "$tag" "$(git rev-parse --short HEAD)"

if [[ -n "${GITHUB_OUTPUT:-}" ]]; then
    printf 'tag=%s\n' "$tag" >>"$GITHUB_OUTPUT"
fi
