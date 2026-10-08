#!/usr/bin/env bash
# Generates a Jellyfin plugin repository manifest from meta.json and a release ZIP.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
META="${META_JSON:-$ROOT_DIR/meta.json}"

die() { printf 'Manifest generation failed: %s\n' "$*" >&2; exit 1; }

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
    printf '%s\n' "$line" | sed -E 's/^[[:space:]]*"[^"]+"[[:space:]]*:[[:space:]]*"([^"]*)"[,]?[[:space:]]*$/\1/'
}

ZIP_VERSION="${1:-$(json_field version)}"
ASSET_URL="${2:-${ASSET_URL:-}}"
ZIP_PATH="${3:-${ZIP_PATH:-$ROOT_DIR/dist/media-ccc-de-plugin-${ZIP_VERSION}.zip}}"
[[ -n "$ZIP_VERSION" ]] || die "version is required"
[[ -n "$ASSET_URL" ]] || die "asset URL is required (argument 2 or ASSET_URL)"

for field in guid name description overview owner category version targetAbi changelog timestamp; do
    value="$(json_field "$field")" || die "could not read '$field' from $META"
    [[ -n "$value" ]] || die "meta.json field '$field' is empty"
done

CHECKSUM=""
if [[ -f "$ZIP_PATH" ]]; then
    CHECKSUM="$(sha256sum "$ZIP_PATH" | awk '{print $1}')"
fi

OUTPUT="${OUTPUT_PATH:-$ROOT_DIR/dist/manifest.json}"
mkdir -p "$(dirname "$OUTPUT")"
python3 - "$META" "$ASSET_URL" "$CHECKSUM" "$OUTPUT" <<'PY'
import json
import sys
from pathlib import Path

meta_path, source_url, checksum, output_path = sys.argv[1:]
meta = json.loads(Path(meta_path).read_text(encoding="utf-8"))
manifest = [{
    "guid": meta["guid"],
    "name": meta["name"],
    "description": meta["description"],
    "overview": meta["overview"],
    "owner": meta["owner"],
    "category": meta["category"],
    "versions": [{
        "version": meta["version"],
        "changelog": meta["changelog"],
        "targetAbi": meta["targetAbi"],
        "sourceUrl": source_url,
        "checksum": checksum,
        "timestamp": meta["timestamp"],
    }],
}]
rendered = json.dumps(manifest, indent=2, ensure_ascii=False) + "\n"
Path(output_path).write_text(rendered, encoding="utf-8")
print(rendered, end="")
PY
