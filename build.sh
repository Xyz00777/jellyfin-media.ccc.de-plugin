#!/usr/bin/env bash
# Easy build script for systems without nix
# Prerequisites: .NET 10.0 SDK (https://dotnet.microsoft.com/download/dotnet/10.0)
#
# Usage:
#   ./build.sh           - Build the plugin
#   ./build.sh test       - Build and run tests
#   ./build.sh release    - Release build
#   ./build.sh clean      - Clean build artifacts
#   ./build.sh help       - Show help

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

check_dotnet() {
    if ! command -v dotnet &> /dev/null; then
        echo -e "${RED}ERROR: dotnet SDK not found${NC}"
        echo ""
        echo "Install .NET 10.0 SDK first:"
        echo "  Linux:   https://dotnet.microsoft.com/download/dotnet/10.0"
        echo "  macOS:   brew install dotnet-sdk"
        echo "  Windows: https://dotnet.microsoft.com/download/dotnet/10.0"
        echo ""
        echo "Or use nix:"
        echo "  nix develop              (if you have nix with flakes)"
        echo "  Or: direnv allow         (if you use direnv)"
        exit 1
    fi

    local version=$(dotnet --version 2>/dev/null || echo "0")
    if [[ ! "$version" =~ ^10 ]]; then
        echo -e "${YELLOW}WARNING: dotnet version $version detected, but .NET 10.0 is recommended${NC}"
        echo "  The plugin targets net10.0. Build may fail."
        echo "  Download: https://dotnet.microsoft.com/download/dotnet/10.0"
    fi
}

build() {
    echo -e "${GREEN}Building Jellyfin Media.CCC.de Plugin...${NC}"
    dotnet build "$SCRIPT_DIR/Jellyfin.Plugin.MediaCccDe.sln"
    echo -e "${GREEN}Build complete!${NC}"
}

build_release() {
    echo -e "${GREEN}Building Release...${NC}"
    dotnet build "$SCRIPT_DIR/Jellyfin.Plugin.MediaCccDe.sln" -c Release
    echo -e "${GREEN}Release build complete!${NC}"
    echo ""
    echo "Output: bin/Release/net10.0/Jellyfin.Plugin.MediaCccDe.dll"
    echo ""
    echo "Copy to Jellyfin plugin directory:"
    echo "  Linux:   /var/lib/jellyfin/plugins/"
    echo "  macOS:   ~/.local/share/jellyfin/plugins/"
    echo "  Windows: C:\\ProgramData\\Jellyfin\\Server\\plugins\\"
}

package() {
    echo -e "${GREEN}Packaging plugin for distribution...${NC}"
    local version="1.1.0"
    mkdir -p "$SCRIPT_DIR/dist"

    cp "$SCRIPT_DIR/bin/Release/net10.0/Jellyfin.Plugin.MediaCccDe.dll" "$SCRIPT_DIR/dist/"
    cp "$SCRIPT_DIR/meta.json" "$SCRIPT_DIR/dist/"

    cd "$SCRIPT_DIR/dist"
    if command -v zip &> /dev/null; then
        zip -j "media-ccc-de-plugin-${version}.zip" Jellyfin.Plugin.MediaCccDe.dll meta.json
    elif command -v python3 &> /dev/null; then
        python3 -m zipfile -c "media-ccc-de-plugin-${version}.zip" Jellyfin.Plugin.MediaCccDe.dll meta.json
    else
        echo -e "${RED}ERROR: zip or python3 is required for packaging${NC}"
        exit 1
    fi
    cd "$SCRIPT_DIR"

    echo -e "${GREEN}Package created: dist/media-ccc-de-plugin-${version}.zip${NC}"
}

test() {
    echo -e "${GREEN}Running tests...${NC}"
    dotnet test "$SCRIPT_DIR/Jellyfin.Plugin.MediaCccDe.sln" -c Release --verbosity normal
    echo -e "${GREEN}Tests complete!${NC}"
}

clean() {
    echo -e "${YELLOW}Cleaning build artifacts...${NC}"
    dotnet clean "$SCRIPT_DIR/Jellyfin.Plugin.MediaCccDe.sln"
    rm -rf "$SCRIPT_DIR/bin" "$SCRIPT_DIR/obj" "$SCRIPT_DIR/Tests/bin" "$SCRIPT_DIR/Tests/obj"
    echo -e "${GREEN}Clean complete!${NC}"
}

help() {
    echo "Jellyfin Media.CCC.de Plugin - Build Script"
    echo ""
    echo "Usage: $0 {build|test|release|package|clean|help}"
    echo ""
    echo "Commands:"
    echo "  build     Build the plugin (Debug)"
    echo "  test      Build and run all tests"
    echo "  release   Build Release, run tests, and package ZIP"
    echo "  package   Package DLL + meta.json into distributable ZIP"
    echo "  clean     Remove all build artifacts"
    echo "  help      Show this help message"
    echo ""
    echo "Prerequisites:"
    echo "  .NET 10.0 SDK - https://dotnet.microsoft.com/download/dotnet/10.0"
    echo ""
    echo "Alternative (with nix):"
    echo "  nix develop     # Enter dev shell with all dependencies"
    echo "  direnv allow     # Auto-load dev shell (if direnv installed)"
}

# Main
check_dotnet

case "${1:-build}" in
    build)   build;;
    test)    build && test;;
    release) build_release && test && package;;
    package) package;;
    clean)   clean;;
    help|-h|--help) help;;
    *)
        echo -e "${RED}Unknown command: $1${NC}"
        help
        exit 1
        ;;
esac
