{
  description = "Jellyfin Media.CCC.de Plugin development environment";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";
    flake-utils.url = "github:numtide/flake-utils";
  };

  outputs = { nixpkgs, flake-utils, ... }:
    flake-utils.lib.eachDefaultSystem (system:
      let
        pkgs = nixpkgs.legacyPackages.${system};
        dotnet-sdk = pkgs.dotnet-sdk_10;
      in
      {
        devShells.default = pkgs.mkShell {
          name = "jellyfin-ccc-media-de";

          packages = with pkgs; [
            dotnet-sdk
            dotnetPackages.Nuget
            shellcheck
            actionlint
            zip
          ];

          shellHook = ''
            echo "🛠  Jellyfin Media.CCC.de Plugin dev shell"
            echo "   dotnet --version: $(dotnet --version)"
            echo ""
            echo "   Commands:"
            echo "     dotnet build              - Build the plugin"
            echo "     dotnet test               - Run all tests"
            echo "     dotnet build -c Release   - Release build"
          '';
        };
      }
    );
}
