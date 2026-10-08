#!/usr/bin/env bash
# Hermetic tests for the release guard in scripts/check-version.sh.
#
# The guard can only be exercised against real git history, so each case builds a throwaway
# repository with a minimal project file and manifest, then runs the real script inside it.
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
SOURCE_CHECK="$SCRIPT_DIR/check-version.sh"

RED=$'\033[31m'
GREEN=$'\033[32m'
NC=$'\033[0m'

failures=0
checks=0

fail() {
    failures=$((failures + 1))
    printf '  %sFAIL%s %s\n' "$RED" "$NC" "$1" >&2
}

pass() {
    checks=$((checks + 1))
    printf '  %sok%s   %s\n' "$GREEN" "$NC" "$1"
}

# Builds a repository whose csproj and manifest agree on $1, then copies the real check in.
make_repo() {
    local version="$1" root
    root="$(mktemp -d "${TMPDIR:-/tmp}/check-version-test.XXXXXX")"
    mkdir -p "$root/scripts"

    sed -e "s/__VERSION__/$version/g" "$ROOT_DIR/Tests/Fixtures/check-version.csproj.fixture" \
        > "$root/Jellyfin.Plugin.MediaCccDe.csproj"
    sed -e "s/__VERSION__/$version.0/g" "$ROOT_DIR/Tests/Fixtures/check-version.meta.json.fixture" \
        > "$root/meta.json"
    cp "$SOURCE_CHECK" "$root/scripts/check-version.sh"

    git -C "$root" init -q
    git -C "$root" config user.email test@example.com
    git -C "$root" config user.name Test
    git -C "$root" add -A
    git -C "$root" commit -q -m "fixture"
    printf '%s' "$root"
}

# Runs the check in $1 and reports whether it exited zero.
run_check() {
    local root="$1"
    shift
    (cd "$root" && env "$@" bash scripts/check-version.sh >/dev/null 2>&1)
}

# Commits a change that does not touch the version.
add_unversioned_change() {
    local root="$1"
    printf 'change\n' >> "$root/meta.json.changelog-marker"
    git -C "$root" add -A
    git -C "$root" commit -q -m "unrelated change"
}

bump_version() {
    local root="$1" version="$2"
    sed -i "s|<Version>.*</Version>|<Version>$version</Version>|" "$root/Jellyfin.Plugin.MediaCccDe.csproj"
    sed -i "s|\"version\".*|\"version\": \"$version.0\",|" "$root/meta.json"
    git -C "$root" add -A
    git -C "$root" commit -q -m "bump to $version"
}

printf 'Release guard tests\n'

# 1. A pending version with no tag is the normal case before a release.
root="$(make_repo 1.2.3)"
if run_check "$root" SKIP_RELEASE_GUARD=; then
    pass "an untagged version passes"
else
    fail "an untagged version passes"
fi

# 2. Tagging the current commit is fine: that release does contain this code.
git -C "$root" tag -a v1.2.3 -m "v1.2.3"
if run_check "$root" SKIP_RELEASE_GUARD=; then
    pass "a tag pointing at the current commit passes"
else
    fail "a tag pointing at the current commit passes"
fi

# 3. New code on an already-released version must be refused.
add_unversioned_change "$root"
if run_check "$root" SKIP_RELEASE_GUARD=; then
    fail "new code on a released version is refused"
else
    pass "new code on a released version is refused"
fi

# 4. The documented escape hatch works, and says so.
output="$(cd "$root" && SKIP_RELEASE_GUARD=1 bash scripts/check-version.sh 2>&1)"
if [[ $? -eq 0 && "$output" == *"Release guard skipped"* ]]; then
    pass "SKIP_RELEASE_GUARD=1 bypasses the guard and reports it"
else
    fail "SKIP_RELEASE_GUARD=1 bypasses the guard and reports it"
fi

# 5. The commit-message opt-out works, and says which opt-out was used.
root2="$(make_repo 1.3.0)"
git -C "$root2" tag -a v1.3.0 -m "v1.3.0"
printf 'change\n' >> "$root2/meta.json.changelog-marker"
git -C "$root2" add -A
git -C "$root2" commit -q -m "docs only [no-release]"
marker_output="$(cd "$root2" && bash scripts/check-version.sh 2>&1)"
if [[ $? -eq 0 && "$marker_output" == *"[no-release] in the commit message"* ]]; then
    pass "[no-release] in the commit message opts out and names itself"
else
    fail "[no-release] in the commit message opts out and names itself"
fi
rm -rf "$root2"

# 6. A commit without the marker is still refused, so the opt-out cannot be accidental.
root3="$(make_repo 1.3.1)"
git -C "$root3" tag -a v1.3.1 -m "v1.3.1"
add_unversioned_change "$root3"
if run_check "$root3" SKIP_RELEASE_GUARD=; then
    fail "an unmarked commit on a released version is refused"
else
    pass "an unmarked commit on a released version is refused"
fi
rm -rf "$root3"

# 7. Reproduce the pull_request checkout: HEAD is a synthetic merge commit whose message
#    hides the marker, so the branch tip is only reachable through RELEASE_GUARD_HEAD_SHA.
root4="$(make_repo 1.3.2)"
git -C "$root4" tag -a v1.3.2 -m "v1.3.2"
base_branch="$(git -C "$root4" branch --show-current)"
add_unversioned_change "$root4"
git -C "$root4" checkout -q -b side
printf 'docs\n' >> "$root4/meta.json.changelog-marker"
git -C "$root4" add -A
git -C "$root4" commit -q -m "docs only [no-release]"
side_sha="$(git -C "$root4" rev-parse HEAD)"
git -C "$root4" checkout -q "$base_branch"
git -C "$root4" merge -q --no-ff -m "Merge '$side_sha' into $base_branch" side
merge_sha="$(git -C "$root4" rev-parse HEAD)"
if [[ "$(cd "$root4" && git log -1 --format=%B "$merge_sha")" != *"[no-release]"* ]]; then
    if run_check "$root4" SKIP_RELEASE_GUARD= RELEASE_GUARD_HEAD_SHA="$side_sha"; then
        pass "the marker is read from the branch tip, not the merge commit"
    else
        fail "the marker is read from the branch tip, not the merge commit"
    fi
    if run_check "$root4" SKIP_RELEASE_GUARD= RELEASE_GUARD_HEAD_SHA="$merge_sha"; then
        fail "a merge commit without the marker does not opt out"
    else
        pass "a merge commit without the marker does not opt out"
    fi
else
    fail "the synthetic merge commit fixture did not reproduce"
fi
rm -rf "$root4"

# 8. Bumping the version clears the guard.
bump_version "$root" 1.2.4
if run_check "$root" SKIP_RELEASE_GUARD=; then
    pass "bumping the version clears the guard"
else
    fail "bumping the version clears the guard"
fi

# 9. The release path is unaffected: EXPECTED_VERSION still pins the tag to the version.
if run_check "$root" EXPECTED_VERSION=1.2.4; then
    pass "EXPECTED_VERSION matching the project version passes"
else
    fail "EXPECTED_VERSION matching the project version passes"
fi
if run_check "$root" EXPECTED_VERSION=9.9.9; then
    fail "EXPECTED_VERSION disagreeing with the project version fails"
else
    pass "EXPECTED_VERSION disagreeing with the project version fails"
fi

rm -rf "$root"

printf '\n%d checks, %d failures\n' "$checks" "$failures"
[[ "$failures" -eq 0 ]]
