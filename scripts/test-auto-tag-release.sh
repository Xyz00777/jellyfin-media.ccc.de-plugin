#!/usr/bin/env bash
# Hermetic tests for scripts/auto-tag-release.sh.
#
# The script creates and pushes a real tag, so every case runs against a throwaway working
# repository whose origin is a bare repository in a temporary directory. No test can reach
# the real remote or create a real release tag.
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
SOURCE="$SCRIPT_DIR/auto-tag-release.sh"
FIXTURE="$ROOT_DIR/Tests/Fixtures/check-version.csproj.fixture"

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

# Builds a working repository declaring version $1, with a bare repository as its origin.
make_repo() {
    local version="$1" root
    root="$(mktemp -d "${TMPDIR:-/tmp}/auto-tag-test.XXXXXX")"
    mkdir -p "$root/scripts"
    cp "$SOURCE" "$root/scripts/auto-tag-release.sh"
    sed -e "s/__VERSION__/$version/g" "$FIXTURE" >"$root/Jellyfin.Plugin.MediaCccDe.csproj"

    git init -q --bare "$root/origin.git"
    git -C "$root" init -q
    git -C "$root" config user.email test@example.com
    git -C "$root" config user.name Test
    git -C "$root" add -A
    git -C "$root" commit -q -m "fixture"
    git -C "$root" remote add origin "$root/origin.git"
    git -C "$root" push -q -u origin HEAD
    printf '%s' "$root"
}

remote_tags() { git -C "$1/origin.git" tag -l; }

printf 'Auto-tag release tests\n'

# 1. A version with no tag is released: annotated, pushed, and reported to the caller.
root="$(make_repo 1.2.3)"
output="$(cd "$root" && GITHUB_OUTPUT="$root/gh_output" bash scripts/auto-tag-release.sh 2>&1)"
rc=$?
if [[ $rc -eq 0 && "$(remote_tags "$root")" == "v1.2.3" && "$(cat "$root/gh_output")" == "tag=v1.2.3" ]]; then
    pass "an untagged version is tagged, pushed, and reported"
else
    fail "an untagged version is tagged, pushed, and reported"
fi

# 2. Running again must not move or re-create the tag, but it must still report the tag so a
#    re-run can recover from a dispatch that failed after the tag was pushed.
output="$(cd "$root" && GITHUB_OUTPUT="$root/gh_output2" bash scripts/auto-tag-release.sh 2>&1)"
rc=$?
if [[ $rc -eq 0 && "$output" == *"already exists"* && "$(cat "$root/gh_output2")" == "tag=v1.2.3" ]]; then
    pass "an already tagged version is left alone and still reported"
else
    fail "an already tagged version is left alone and still reported"
fi
rm -rf "$root"

# 3. A tag already pointing at an older commit must survive a later commit that reuses the
#    version. Force-moving it would silently repoint an already-published release.
root="$(make_repo 1.2.4)"
git -C "$root" tag -a v1.2.4 -m "Release v1.2.4" HEAD
git -C "$root" push -q origin refs/tags/v1.2.4
before="$(git -C "$root/origin.git" rev-parse v1.2.4)"
git -C "$root" commit -q --allow-empty -m "later commit, same version"
output="$(cd "$root" && bash scripts/auto-tag-release.sh 2>&1)"
rc=$?
after="$(git -C "$root/origin.git" rev-parse v1.2.4)"
if [[ $rc -eq 0 && "$output" == *"already exists"* && "$before" == "$after" ]]; then
    pass "an existing tag is never moved to a later commit"
else
    fail "an existing tag is never moved to a later commit"
fi
rm -rf "$root"

# 4. --dry-run must report the intent without creating anything.
root="$(make_repo 1.2.5)"
output="$(cd "$root" && bash scripts/auto-tag-release.sh --dry-run 2>&1)"
rc=$?
if [[ $rc -eq 0 && "$output" == *"Would create and push v1.2.5"* &&
    -z "$(remote_tags "$root")" ]] &&
    ! git -C "$root" rev-parse -q --verify refs/tags/v1.2.5 >/dev/null 2>&1; then
    pass "--dry-run reports without creating or pushing a tag"
else
    fail "--dry-run reports without creating or pushing a tag"
fi
rm -rf "$root"

# 5. A version that is not X.Y.Z is refused rather than turned into a junk tag.
root="$(make_repo 1.2)"
output="$(cd "$root" && bash scripts/auto-tag-release.sh 2>&1)"
rc=$?
if [[ $rc -ne 0 && "$output" == *"not X.Y.Z"* ]]; then
    pass "a version that is not X.Y.Z is refused"
else
    fail "a version that is not X.Y.Z is refused"
fi
rm -rf "$root"

# 6. A project file with no version at all is refused.
root="$(make_repo 1.2.6)"
printf '<Project>\n</Project>\n' >"$root/Jellyfin.Plugin.MediaCccDe.csproj"
output="$(cd "$root" && bash scripts/auto-tag-release.sh 2>&1)"
rc=$?
if [[ $rc -ne 0 && "$output" == *"No <Version>"* ]]; then
    pass "a project file without a version is refused"
else
    fail "a project file without a version is refused"
fi
rm -rf "$root"

# 7. An unreachable remote must not be mistaken for a missing tag. ls-remote exits 128 when
#    it cannot connect and 2 when the ref is genuinely absent; only the second means "release".
root="$(make_repo 1.2.7)"
git -C "$root" remote set-url origin "$root/does-not-exist.git"
output="$(cd "$root" && bash scripts/auto-tag-release.sh 2>&1)"
rc=$?
if [[ $rc -ne 0 && "$output" == *"refusing to guess"* ]]; then
    pass "an unreachable remote aborts instead of starting a release"
else
    fail "an unreachable remote aborts instead of starting a release"
fi
rm -rf "$root"

# 8. An annotated tag needs a tagger identity, and a CI runner is not guaranteed to have one
#    in its global git config. The tag must be attributed without relying on the environment.
root="$(make_repo 1.2.8)"
git -C "$root" config --unset user.name 2>/dev/null || true
git -C "$root" config --unset user.email 2>/dev/null || true
output="$(cd "$root" && env HOME="$root" GIT_CONFIG_GLOBAL=/dev/null GIT_CONFIG_NOSYSTEM=1 bash scripts/auto-tag-release.sh 2>&1)"
rc=$?
tagger="$(git -C "$root" for-each-ref --format='%(taggername)' refs/tags/v1.2.8)"
if [[ $rc -eq 0 && "$tagger" == "github-actions" ]]; then
    pass "a tag is attributed with no ambient git identity"
else
    fail "a tag is attributed with no ambient git identity"
fi
rm -rf "$root"

# 9. Regression: a dispatch that fails after the tag is pushed must be recoverable by a
#    re-run. When the second run reported nothing, the retry published nothing and still
#    exited 0, so the release silently never appeared.
root="$(make_repo 1.2.9)"
(cd "$root" && GITHUB_OUTPUT="$root/out1" bash scripts/auto-tag-release.sh >/dev/null 2>&1)
first_rc=$?
(cd "$root" && GITHUB_OUTPUT="$root/out2" bash scripts/auto-tag-release.sh >/dev/null 2>&1)
second_rc=$?
if [[ $first_rc -eq 0 && $second_rc -eq 0 &&
    "$(cat "$root/out1")" == "tag=v1.2.9" && "$(cat "$root/out2")" == "tag=v1.2.9" ]]; then
    pass "a re-run still reports the tag, so a failed dispatch is recoverable"
else
    fail "a re-run still reports the tag, so a failed dispatch is recoverable"
fi
rm -rf "$root"

printf '\n%d checks, %d failures\n' "$checks" "$failures"
[[ "$failures" -eq 0 ]]
