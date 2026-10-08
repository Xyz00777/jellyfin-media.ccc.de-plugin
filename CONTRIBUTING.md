# Contributing

## Prerequisites

Install the .NET 10 SDK. CI uses `10.0.x`; `global.json` pins `10.0.100` with `rollForward: latestMinor`. A Nix flake is provided: run `nix develop` for a development shell, or run `direnv allow` once if you use direnv. The `.envrc` runs `use flake`.

## Build and test

Run `dotnet build --configuration Release` and `dotnet test`. You can also use `./build.sh {build|test|release|package|clean|help}`. The `release` command builds in Release mode, runs tests, and packages `dist/media-ccc-de-plugin-<version>.zip`.

CI runs on every push and pull request on `ubuntu-latest`: restore, Release build with `--no-restore`, then Release test with `--no-build`. Make sure your change passes locally before opening a pull request. Release builds treat warnings as errors for the main plugin project, except `CS1591` and `CS1573` via `NoWarn`. The test project currently emits nullable warnings without failing.

## Releasing

To cut a release, bump `<Version>` in `Jellyfin.Plugin.MediaCccDe.csproj` and `"version"` in `meta.json` (the csproj uses three parts, such as `1.1.0`, while the manifest uses four, such as `1.1.0.0`). Update `"changelog"` in `meta.json` to describe the change. If the `Jellyfin.Controller` / `Jellyfin.Model` package versions change, update `"targetAbi"` in `meta.json` to match the `Jellyfin.Controller` version; a lower `targetAbi` can make Jellyfin accept the plugin and then disable it at load time.

Commit the changes, then create and push a matching annotated tag:

```bash
git tag -a v<version> -m "Release <version>"
git push origin v<version>
```

Pushing the tag triggers `.github/workflows/release.yml`, which validates the tag and manifest/project versions, runs the build and tests, refreshes the manifest timestamp, packages `dist/media-ccc-de-plugin-<version>.zip` with exactly `Jellyfin.Plugin.MediaCccDe.dll` and `meta.json`, and publishes it as a GitHub Release. The workflow deliberately fails if the tag, csproj version, and manifest version disagree; fix the files rather than bypassing the check. It skips publishing if a GitHub Release for that tag already exists. The workflow rewrites `meta.json`'s `timestamp` inside the published artifact, so the timestamp in git and the timestamp in the shipped ZIP are not expected to match.

## Version consistency

`scripts/check-version.sh` is the single source of truth for version checks. It runs in three places:

- **pre-commit** — via `.pre-commit-config.yaml`
- **CI** — as the first step of `.github/workflows/ci.yml`
- **releases** — `.github/workflows/release.yml`, with `EXPECTED_VERSION` pinned to the tag

It verifies that `meta.json`'s `version` matches the `.csproj` `<Version>` (allowing one trailing `.0`, since the manifest uses Jellyfin's four-part format) and that `targetAbi` matches the `Jellyfin.Controller` package version. Run it by hand any time with:

```bash
bash scripts/check-version.sh
```

The hook is a local pre-commit hook, so it needs no network access. Install the `pre-commit` tool and the hook activates automatically. Contributors who install `pre-commit` from https://pre-commit.com get it with no extra setup; everyone else can just run the script before committing.

## Project layout

- `Api/`: media.ccc.de HTTP client
- `Controllers/`: ASP.NET controllers
- `Models/`: models and DTOs
- `Pages/`: embedded HTML and JavaScript pages
- `Providers/`: Jellyfin metadata, subtitle, and image providers
- `Services/`: business logic and interfaces
- `Configuration/`: plugin configuration page HTML
- `Tests/Unit/` and `Tests/Integration/`: unit and integration tests
- Root `Plugin.cs` and `ServiceRegistrator.cs`: plugin setup and service registration

## Conventions

- Write the test first, following TDD.
- Keep controllers thin and put business logic in services.
- Register new services in `ServiceRegistrator.cs`.
- Add dashboard pages to `Plugin.cs`'s `GetPages()`.
- Keep `meta.json`'s `version` and `targetAbi` in sync with the `.csproj`. `targetAbi` must match the referenced `Jellyfin.Controller` version, currently `12.2.0.0` / `12.2.0`.
- When adding a page, include its HTML and JavaScript under `Pages/` in the `.csproj` `EmbeddedResource` globs.
- Jellyfin 12's dashboard does not execute plugin page scripts. Pages must work as plain server-rendered HTML and links, not rely on dashboard JavaScript.

## Smoke test

`scripts/podman-smoke-test.sh` runs a disposable Jellyfin 12.2 container. It needs Podman, `curl`, `jq`, and internet access to media.ccc.de. Set `KEEP_TEST_DATA=1` to keep temporary data, or `SKIP_BUILD=1` to reuse the existing build.

## Pull requests

Fork the repository, create a branch, and keep commits focused with clear messages. Make sure `dotnet test` passes, and describe what changed and why in your pull request.
