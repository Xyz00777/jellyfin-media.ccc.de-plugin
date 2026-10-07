#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPOSITORY_ROOT="$(cd -- "$SCRIPT_DIR/.." && pwd)"
IMAGE="${JELLYFIN_IMAGE:-docker.io/jellyfin/jellyfin:12.2}"
SKIP_BUILD="${SKIP_BUILD:-0}"
KEEP_TEST_DATA="${KEEP_TEST_DATA:-0}"
ADMIN_USER="${JELLYFIN_TEST_USER:-admin}"
ADMIN_PASSWORD="${JELLYFIN_TEST_PASSWORD:-MediaCccTest-$(date +%s)-x}"
CONTAINER_NAME="media-ccc-jellyfin-smoke-${$}"
TEST_ROOT=""
CONTAINER_STARTED=0

log() {
    printf '[media-ccc-smoke] %s\n' "$*"
}

fail() {
    printf '[media-ccc-smoke] ERROR: %s\n' "$*" >&2
    exit 1
}

require_command() {
    command -v "$1" >/dev/null 2>&1 || fail "Required command not found: $1"
}

cleanup() {
    local exit_code=$?

    if [[ "$CONTAINER_STARTED" == 1 ]]; then
        if [[ "$exit_code" != 0 ]]; then
            log "Jellyfin container logs:"
            podman logs "$CONTAINER_NAME" >&2 || true
        fi
        podman rm --force "$CONTAINER_NAME" >/dev/null 2>&1 || true
    fi

    if [[ -n "$TEST_ROOT" ]]; then
        if [[ "$KEEP_TEST_DATA" == 1 ]]; then
            log "Keeping test data at $TEST_ROOT"
        else
            rm -rf -- "$TEST_ROOT"
        fi
    fi

    exit "$exit_code"
}

trap cleanup EXIT INT TERM

require_command podman
require_command curl
require_command jq

if [[ "$SKIP_BUILD" != 1 ]]; then
    log "Building the plugin and release package"
    bash "$REPOSITORY_ROOT/build.sh" release
fi

PLUGIN_DLL="$REPOSITORY_ROOT/dist/Jellyfin.Plugin.MediaCccDe.dll"
PLUGIN_META="$REPOSITORY_ROOT/meta.json"
[[ -f "$PLUGIN_DLL" ]] || fail "Plugin DLL not found: $PLUGIN_DLL"
[[ -f "$PLUGIN_META" ]] || fail "Plugin metadata not found: $PLUGIN_META"

PLUGIN_VERSION="$(jq -er '.version' "$PLUGIN_META")"
TEST_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/media-ccc-jellyfin.XXXXXX")"
CONFIG_DIR="$TEST_ROOT/config"
CACHE_DIR="$TEST_ROOT/cache"
MEDIA_DIR="$TEST_ROOT/media"
PLUGIN_DIR="$CONFIG_DIR/plugins/Media.CCC.de_$PLUGIN_VERSION"
mkdir -p "$PLUGIN_DIR" "$CACHE_DIR" "$MEDIA_DIR"
cp -- "$PLUGIN_DLL" "$PLUGIN_META" "$PLUGIN_DIR/"

log "Starting $IMAGE"
podman run --detach \
    --name "$CONTAINER_NAME" \
    --publish 127.0.0.1::8096/tcp \
    --volume "$CONFIG_DIR:/config:Z" \
    --volume "$CACHE_DIR:/cache:Z" \
    --volume "$MEDIA_DIR:/media:Z" \
    "$IMAGE" >/dev/null
CONTAINER_STARTED=1

HOST_PORT=""
for _ in {1..30}; do
    PORT_MAPPING="$(podman port "$CONTAINER_NAME" 8096/tcp 2>/dev/null || true)"
    if [[ -n "$PORT_MAPPING" ]]; then
        HOST_PORT="${PORT_MAPPING##*:}"
        break
    fi
    sleep 1
done
[[ -n "$HOST_PORT" ]] || fail "Jellyfin did not publish port 8096"
BASE_URL="http://127.0.0.1:$HOST_PORT"

log "Waiting for Jellyfin at $BASE_URL"
READY=0
for _ in {1..120}; do
    HEALTH_STATUS="$(curl --fail --silent "$BASE_URL/health" 2>/dev/null || true)"
    if [[ "$HEALTH_STATUS" == "Healthy" ]]; then
        READY=1
        break
    fi
    sleep 1
done
[[ "$READY" == 1 ]] || fail "Jellyfin did not become ready"

post_startup_json() {
    local endpoint="$1"
    local body="$2"
    local status

    for _ in {1..60}; do
        status="$(curl --silent \
            --output /dev/null \
            --write-out '%{http_code}' \
            --request POST "$BASE_URL$endpoint" \
            --header 'Content-Type: application/json' \
            --data "$body" || true)"

        case "$status" in
            2??) return 0 ;;
            000|503) sleep 1 ;;
            *) fail "Jellyfin startup endpoint $endpoint returned HTTP $status" ;;
        esac
    done

    fail "Jellyfin startup endpoint $endpoint remained unavailable"
}

log "Bootstrapping the Jellyfin server"
post_startup_json /Startup/Configuration \
    '{"ServerName":"MediaCCC Podman Smoke Test","UICulture":"en-US","MetadataCountryCode":"US","PreferredMetadataLanguage":"en"}'
for _ in {1..60}; do
    if curl --fail --silent "$BASE_URL/Startup/User" >/dev/null 2>&1; then
        break
    fi
    sleep 1
done
post_startup_json /Startup/User \
    "$(jq -cn --arg name "$ADMIN_USER" --arg password "$ADMIN_PASSWORD" '{Name:$name,Password:$password}')"
post_startup_json /Startup/RemoteAccess \
    '{"EnableRemoteAccess":false,"EnableAutomaticPortMapping":false}'
post_startup_json /Startup/Complete '{}'

AUTHORIZATION='MediaBrowser Client="MediaCCC Podman Smoke Test", Device="Podman", DeviceId="media-ccc-smoke", Version="1.0.0"'
AUTH_RESPONSE="$(curl --fail --silent --show-error \
    --request POST "$BASE_URL/Users/AuthenticateByName" \
    --header 'Content-Type: application/json' \
    --header "Authorization: $AUTHORIZATION" \
    --data "$(jq -cn --arg name "$ADMIN_USER" --arg password "$ADMIN_PASSWORD" '{Username:$name,Pw:$password}')")"
TOKEN="$(jq -er '.AccessToken' <<<"$AUTH_RESPONSE")"
REQUEST_AUTHORIZATION="$AUTHORIZATION, Token=$TOKEN"

api_get() {
    curl --fail --silent --show-error \
        --header "Authorization: $REQUEST_AUTHORIZATION" \
        "$BASE_URL$1"
}

log "Verifying the addon is loaded"
PLUGINS="$(api_get /Plugins)"
if ! jq -e 'any(.[]; ((.Id // "") | ascii_downcase | gsub("-"; "")) == "e225c91aef1141cab9136491f15c2992")' <<<"$PLUGINS" >/dev/null; then
    jq -r '.[] | "\(.Name // "<unnamed>") \(.Id // "<no-id>")"' <<<"$PLUGINS" >&2
    fail "MediaCCC.de plugin was not found in Jellyfin's plugin list"
fi

log "Verifying an authenticated addon endpoint"
SYNC_HISTORY="$(api_get /media_ccc/sync/status)"
jq -e 'type == "array"' <<<"$SYNC_HISTORY" >/dev/null \
    || fail "Addon sync-history endpoint did not return an array"

log "Verifying acronym-based conference event hydration"
CONFERENCE_EVENTS="$(api_get /media_ccc/conferences/37c3/events)"
jq -e 'type == "array" and length > 0 and any(.[]; (.recordings // []) | length > 0)' <<<"$CONFERENCE_EVENTS" >/dev/null \
    || fail "Addon conference event endpoint returned no hydrated recordings"

log "Verifying the live media.ccc.de integration"
CONFERENCES="$(api_get /media_ccc/conferences)"
jq -e 'type == "array" and length > 0' <<<"$CONFERENCES" >/dev/null \
    || fail "Addon conference endpoint returned no conferences"

CONFERENCE_COUNT="$(jq -er 'length' <<<"$CONFERENCES")"
log "PASS: Jellyfin bootstrapped, addon loaded, authenticated API works, and $CONFERENCE_COUNT conferences were fetched"
