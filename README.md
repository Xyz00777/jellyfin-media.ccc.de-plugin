# Jellyfin Media.CCC.de Plugin

A Jellyfin plugin for conference recordings published on media.ccc.de.

Jellyfin 12's dashboard renders plugin pages but does not execute their page scripts. The plugin therefore serves its pages and forms directly, and dashboard links open those standalone pages.

## Features

- Browse conferences and talks in the `CCC Archive` TV library.
- Add talks to a personal watchlist and download them for offline viewing.
- Set server-wide audio and subtitle preferences, and per-user language preferences for watchlist downloads.
- Fetch subtitles on demand, with an optional setting to pre-fetch one selected subtitle during sync.
- Synchronize archive `.strm` files and inspect sync status and history through the API.
- Create the `CCC Archive` library automatically on startup when it does not already exist.

## Compatibility

| Plugin version | Jellyfin | .NET |
|----------------|----------|------|
| 1.1.2          | 12.2+    | 10   |

Plugin 1.1.2 targets Jellyfin 12.2+ and .NET 10. It is not supported on Jellyfin 10.11: this build compiles against the Jellyfin 12.x API surface. A Jellyfin 10.11 build would need `Jellyfin.Controller` 10.11.x and `TargetFramework` `net9.0`; no such build is published.

The plugin project references `Jellyfin.Controller` and `Jellyfin.Model` 12.2.0, with runtime assets excluded. `meta.json` declares `targetAbi` `12.2.0.0`. Keep `targetAbi` in sync with the `Jellyfin.Controller` package version. A server cannot satisfy a reference to an assembly newer than the one it ships. Declaring a lower `targetAbi` can make the server accept the plugin, then disable it at load time with `Could not load file or assembly 'MediaBrowser.Controller'`. Supporting an older 12.x server requires building against that server's package version, not just lowering `targetAbi`.

## Installation

Each release carries two assets: a plugin ZIP named `media-ccc-de-plugin-<version>.zip`, and a `manifest.json` for Jellyfin's plugin-repository mechanism.

There are three ways to install, in order of convenience:

1. **Unpack the ZIP** into Jellyfin's versioned plugin directory (see below) and restart Jellyfin.
2. **Add the repository** under Dashboard > Plugins > Repositories > Add, using the download URL of the release's `manifest.json` asset, then install the plugin from the catalog. This page expects the URL of a plugin-repository `manifest.json`, not a ZIP, which is why the ZIP cannot be uploaded there.
3. **Build from source**, which you must do for Jellyfin 10.11 or a different Jellyfin API version:

Clone the repository and build the package:

```bash
git clone https://github.com/Xyz00777/jellyfin-media.ccc.de-plugin.git
cd jellyfin-media.ccc.de-plugin
./build.sh release
```

The package is written to `dist/media-ccc-de-plugin-<version>.zip`. It contains `Jellyfin.Plugin.MediaCccDe.dll` and `meta.json`. Alternatively, build and copy both files manually. The DLL is built at `bin/Release/net10.0/Jellyfin.Plugin.MediaCccDe.dll`.

Jellyfin discovers plugins in a versioned subdirectory. For example, use `Media.CCC.de_<PLUGIN_VERSION>` under the applicable plugins directory, and put the DLL and `meta.json` together there.

| Operating system | Plugins directory |
|------------------|-------------------|
| Linux            | `/var/lib/jellyfin/plugins/` |
| Windows          | `C:\ProgramData\Jellyfin\Server\plugins\` |
| macOS            | `~/.local/share/jellyfin/plugins/` |

Restart Jellyfin after installing the files.

### Podman smoke test

The smoke test requires Podman, `curl`, `jq`, and working internet access to media.ccc.de. Run:

```bash
bash scripts/podman-smoke-test.sh
```

The script builds the release package, starts a disposable Jellyfin 12.2 container, installs the plugin into its mounted config, creates the first administrator through Jellyfin's startup API, authenticates, verifies that the plugin loaded, checks an authenticated addon endpoint, verifies acronym-based event hydration with recordings, and fetches live conference data from media.ccc.de. It removes the container and temporary data automatically. Set `KEEP_TEST_DATA=1` to retain temporary data for failure inspection, or `SKIP_BUILD=1` to reuse existing release output.

### Uninstalling and upgrading

Delete the plugin's versioned directory from the plugins folder and restart Jellyfin to uninstall it. To upgrade, install the new files in the versioned directory and restart. Plugin data under `{PluginConfigurationsPath}` and `{DataPath}` is not removed automatically.

## Configuration

The plugin's configuration page is registered as `MediaCCCDe` and is not in the main menu. The server menu section includes `Browse`, `CCC Watchlist`, and `Language Preferences`. `MediaCCC Settings` and `MediaCCCDe Sync Log` are registered pages but are not main-menu items.

| Setting | Type | Default | Details |
|---------|------|---------|---------|
| `WatchlistPath` | string | empty | Empty resolves to `{PluginConfigurationsPath}/ccc-media/watchlists`. A set path is resolved relative to `{PluginConfigurationsPath}`. |
| `PreferredQuality` | string | `hd` (shown as `HD`) | Server-wide preferred quality. |
| `PreferredAudioLanguages` | list of strings | empty | Server-wide audio language preferences. |
| `PreferredSubtitleLanguages` | list of strings | empty | Used when pre-fetching subtitles during sync. |
| `SyncIntervalHours` | integer | `6` | Hours, clamped to the range 1–168. |
| `DownloadSubtitles` | boolean | `false` | Pre-fetch one selected subtitle sidecar next to each generated `.strm` during a sync. This is independent of on-demand subtitle search. |

### Opening MediaCCC settings

Open the standalone settings page:

```text
https://<your-jellyfin>/media_ccc/settings
```

Use `https://` unless the page is opened on the server itself. The access token is a reusable secret for plugin settings, so the unlock form only accepts it over HTTPS; a request from another machine over plain HTTP is rejected with `426 Upgrade Required`. Requests that arrive over loopback are exempt, because a local install has no network path to observe and Jellyfin's default is plain HTTP locally.

To obtain the token, read it from the server's filesystem:

```text
{PluginConfigurationsPath}/settings-access.txt
```

The plugin creates this file on first start and points at it in the server log. **The token is never written to the log**, because logs are routinely exported, attached to support requests, and archived. On Linux and macOS the file is created readable only by the account running Jellyfin. To rotate the token, delete that file and restart Jellyfin; every plugin session cookie is invalidated.

Paste the token value into the unlock form field and submit; the settings page does not read a `token` query parameter. After a successful unlock, a signed cookie lasts 30 days. It is always issued with the `Secure`, `HttpOnly`, and `SameSite=Strict` attributes. Its HMAC key is derived at runtime from the token using SHA-256; no signing secret is committed to the repository.

If Jellyfin runs behind a reverse proxy, configure the proxy's forwarded-protocol headers so the plugin sees the original scheme. Otherwise a request forwarded as plain HTTP is refused even when the client used HTTPS.

### Subtitles

Subtitles are fetched on demand by default. Nothing is downloaded until a user asks for a subtitle:

1. Open a talk and choose **... > Subtitles > Search for subtitles**.
2. Tracks published by media.ccc.de appear under **Media.CCC.de**. Only subtitle MIME types are offered; video and audio recordings are filtered out.
3. Choose a track. The plugin stores a sidecar next to the `.strm`. It then behaves like a local subtitle: it is available to every client, selectable, and remembered across restarts. An already downloaded non-empty track is never fetched again.

About a third of published talks have a subtitle.

**Temporary Jellyfin 12.2 workaround:** Jellyfin 12.2 cannot save subtitles for remote `.strm` items. `SubtitleManager.TrySaveSubtitle` throws a `NullReferenceException`, and the download and upload endpoints answer `204` while writing nothing. Until [the upstream issue](https://github.com/jellyfin/jellyfin/issues/18352) is resolved, this plugin writes the sidecar itself during the search and notifies the library monitor so the scanner registers it. The plugin logs a `TEMPORARY:` warning on every save. This workaround must be removed once the upstream issue is resolved.

The optional `DownloadSubtitles` setting is a separate bulk pre-fetch path for seeding a library up front. It is off by default.

### Per-user language preferences

Open the standalone page:

```text
https://<your-jellyfin>/media_ccc/settings/languages
```

On the first visit, provide your own Jellyfin username and password. They are posted to `/media_ccc/settings/languages/identify`, checked against the server to identify the account, then discarded; neither is stored or logged, and the identity comes from Jellyfin's response rather than the form. Because the password grants your account's Jellyfin access, the form only accepts it over HTTPS unless the request arrives over loopback. A signed cookie remembers the identified user for 90 days; disabling or deleting your Jellyfin account invalidates that session immediately, because the plugin re-checks the account on every request.

These preferences apply to watchlist downloads. Streaming uses one shared `.strm` per talk and cannot vary by user; the server-wide `PreferredAudioLanguages` setting determines what that `.strm` points to. The preferences are also available through `GET` and `POST` at `/media_ccc/languages/audio` and `/media_ccc/languages/subtitles`. The dashboard's `Language Preferences` menu link opens this standalone page rather than providing a working dashboard form.

## Languages

The plugin's own web interface ships in **English and German**. The language is chosen per request from the `Accept-Language` header your browser sends, falling back to English. Every plugin page has a language switcher in the top-right corner, and you can force a language with a `?lang=en` or `?lang=de` query parameter, for example:

```text
https://<your-jellyfin>/media_ccc/browse?lang=de
```

This is separate from the audio and subtitle language preferences below, which choose the *media* language rather than the interface language.

## Usage

The server menu includes links to **Browse**, **CCC Watchlist**, and **Language Preferences**. Each of those entries opens a short pointer page in the dashboard that immediately forwards to the plugin-served page, because Jellyfin 12's dashboard does not execute plugin page scripts. The `MediaCCC Settings` and `MediaCCCDe Sync Log` pages are registered but are not in that menu; reach them by URL:

| Page | URL |
|------|-----|
| Browse conferences | `https://<your-jellyfin>/media_ccc/browse` |
| Watchlist | `https://<your-jellyfin>/media_ccc/watchlist/page` |
| Sync log | `https://<your-jellyfin>/media_ccc/sync/log` |
| Settings | `https://<your-jellyfin>/media_ccc/settings` |
| Language preferences | `https://<your-jellyfin>/media_ccc/settings/languages` |

Browse and the watchlist identify you once with your own Jellyfin login and then remember you with a signed cookie, the same handshake the language preferences page uses. Sync log history is readable by anyone who can reach the server URL; the sync *actions* still require an elevated user.

`CCC Archive` is a TV library backed by `{PluginConfigurationsPath}/archive`. Per-user watchlist libraries are created for sanitized usernames. During synchronization, stale archive `.strm` files that no longer correspond to anything in the current API response are removed. Downloaded watchlist videos are not automatically cleaned up.

### Where downloads land

By default, watchlist downloads go under `{PluginConfigurationsPath}/ccc-media/watchlists/<username>/`. Files are flat in the user's library root, named `<talk-slug>-<event-guid>.<format>`.

### Download limits

Any authenticated user can queue a download for any recording in the catalog, so the plugin bounds how much storage the queue can consume. These limits are fixed constants in `Services/DownloadLimits.cs`:

| Limit | Value |
|-------|-------|
| Unfinished downloads per user | 250 |
| Unfinished downloads across all users | 500 |
| Finished queue items retained | 500 |
| Size of a single recording | 32 GiB |
| Size of one user's watchlist library | 100 GiB |
| Free space kept in reserve on the download volume | 512 MiB |

A recording is checked against these limits twice: once when it is queued, and again while it transfers, so a false or missing `Content-Length` cannot get around them. When a limit is reached the request is refused with a `DownloadQuotaExceededException`, which the API reports as `429` and the pages render as a message. Free space that the platform cannot report is treated as unconstrained; the per-user byte quota still applies. If a recording has no format, the extension falls back to `mp4`.

## Architecture

### Library structure

```text
CCC Archive/
├── 37C3/
│   ├── Season 01/           # conference day 1
│   │   ├── opening.strm
│   │   └── ...
│   └── Season 02/
└── 36C3/
```

Day-to-season numbering is derived from event dates relative to a resolved conference anchor day. Season directories are zero-padded as `Season NN`; not every event necessarily maps to a clean numbered season.

The `CCC Archive` library is created automatically on startup as a TV library. Creation is duplicate-safe: it matches on the library name or archive path and skips creation if either already exists. If creation fails, an error is logged saying manual setup may be required.

### Data storage

| What | Path |
|------|------|
| Per-user data | `{DataPath}/plugins/ccc-media/data/user-{userId}.json` |
| Download queue | `{DataPath}/plugins/ccc-media/data/download-queue.json` |
| Sync history | `{DataPath}/sync-logs.json` |
| Settings access token | `{PluginConfigurationsPath}/settings-access.txt` |
| Default watchlist downloads | `{PluginConfigurationsPath}/ccc-media/watchlists/<username>/` |
| Archive `.strm` tree | `{PluginConfigurationsPath}/archive/` |

Sync history is stored at the `DataPath` root, not in the plugin data directory.

### API endpoints

The `/media_ccc` routes require an authenticated user. The `/media_ccc/sync` routes require an elevated user. The `/media_ccc/settings*` routes are not covered by `[Authorize]`; they use the settings access token or the user API-key handshake instead.

| Method | Endpoint | Access | Description |
|--------|----------|--------|-------------|
| GET | `/media_ccc/conferences` | Authenticated | List conferences. |
| GET | `/media_ccc/conferences/{conferenceId}/events` | Authenticated | List events for a conference. |
| GET | `/media_ccc/events/{guid}` | Authenticated | Get an event. |
| GET | `/media_ccc/events/recent` | Authenticated | Get recent events. |
| POST | `/media_ccc/watchlist/{eventGuid}` | Authenticated | Add an event to the user's watchlist. |
| DELETE | `/media_ccc/watchlist/{eventGuid}` | Authenticated | Remove an event from the user's watchlist. |
| GET | `/media_ccc/watchlist` | Authenticated | Get the user's watchlist. |
| GET | `/media_ccc/languages/audio` | Authenticated | Get audio language preferences. |
| POST | `/media_ccc/languages/audio` | Authenticated | Set audio language preferences. |
| GET | `/media_ccc/languages/subtitles` | Authenticated | Get subtitle language preferences. |
| POST | `/media_ccc/languages/subtitles` | Authenticated | Set subtitle language preferences. |
| POST | `/media_ccc/sync/trigger` | Elevated user | Trigger synchronization. |
| GET | `/media_ccc/sync/status` | Elevated user | Get sync status. |
| GET | `/media_ccc/sync/history` | Elevated user | Get sync history. |
| DELETE | `/media_ccc/sync/history` | Elevated user | Delete sync history. |
| GET | `/media_ccc/downloads` | Authenticated | Get the user's download queue. |
| POST | `/media_ccc/downloads/{eventGuid}` | Authenticated | Enqueue a talk download for the user. |
| GET | `/media_ccc/settings` | Settings access token | Get plugin settings. |
| POST | `/media_ccc/settings` | Settings access token | Save plugin settings. |
| POST | `/media_ccc/settings/unlock` | Settings access token | Unlock settings using the form token. |
| GET | `/media_ccc/settings/download` | Settings access token | Download settings. |
| GET | `/media_ccc/settings/languages` | Jellyfin login | Get the language preferences page. |
| POST | `/media_ccc/settings/languages` | Jellyfin login | Save per-user language preferences. |
| GET | `/media_ccc/settings/languages/identify` | Jellyfin login | Identify the user using their Jellyfin login. |

### Plugin-served HTML pages

These render the web interface. They are the paths the dashboard menu entries forward to, and they accept `?lang=en` or `?lang=de`.

| Method | Endpoint | Access | Description |
|--------|----------|--------|-------------|
| GET | `/media_ccc/browse` | Any | Browse conferences. Accepts `q`, `year`, `conference`. |
| POST | `/media_ccc/browse/events` | Any | Select a conference and list its events. |
| POST | `/media_ccc/browse/add` | Identified user | Add a talk to the watchlist and queue its download. |
| POST | `/media_ccc/browse/identify` | Any | Identify the user with a Jellyfin login. |
| GET | `/media_ccc/watchlist/page` | Any | Show the watchlist and download queue. |
| POST | `/media_ccc/watchlist/page/start` | Identified user | Queue downloads for the watchlist. |
| POST | `/media_ccc/watchlist/page/remove` | Identified user | Remove one item. Posts `eventGuid`. |
| POST | `/media_ccc/watchlist/page/retry` | Identified user | Retry a failed item. Posts `eventGuid`. |
| POST | `/media_ccc/watchlist/page/identify` | Any | Identify the user with a Jellyfin login. |
| GET | `/media_ccc/sync/log` | Any | Show the synchronization history. |
| GET | `/media_ccc/sync/history/confirm` | Any | Confirmation step before clearing the history. |
| POST | `/media_ccc/sync/history/confirm` | Elevated user | Clear the synchronization history. |
| POST | `/media_ccc/sync/log/trigger` | Elevated user | Trigger a synchronization from the page. |

## Development

### Development environment

Use `nix develop` with Nix flakes, or run `direnv allow` with direnv. `flake.nix` and `.envrc` provide the development shell; `.envrc` runs `use flake`. Alternatively, use the .NET 10 SDK.

The repository pins SDK `10.0.100` in `global.json`, with `rollForward: latestMinor` and `allowPrerelease: false`.

### Build and test

The build script accepts `build`, `test`, `release`, `package`, `clean`, and `help`:

```bash
./build.sh release
```

`release` runs a Release build, tests, and packaging. Release builds set `TreatWarningsAsErrors=true` with `NoWarn=CS1591;CS1573` for the main plugin project, and the test project treats warnings as errors too. Plain .NET commands also work:

```bash
dotnet build --configuration Release
dotnet test
```

`./build.sh package` produces `dist/media-ccc-de-plugin-<version>.zip`, containing exactly `Jellyfin.Plugin.MediaCccDe.dll` and `meta.json`, both copied to `dist/` first. `scripts/generate-manifest.sh` builds the matching `manifest.json` locally.

### Code quality

`.editorconfig` pins the formatting this codebase already uses: four-space indentation, Allman braces, block-scoped namespaces with `using` outside the namespace, LF line endings, and sorted `System` usings. CI and the local pre-commit hooks both verify it:

```bash
dotnet format Jellyfin.Plugin.MediaCccDe.sln --verify-no-changes --no-restore
bash scripts/check-version.sh
sh scripts/check-whitespace.sh
```

CI additionally runs `shellcheck` over the shell scripts and `actionlint` over the workflow files on every push and pull request. All pre-commit hooks are local shell, so they need no network access and pull in no third-party code; the two linters skip themselves when the tool is not installed. See [CONTRIBUTING.md](CONTRIBUTING.md) for how to enable the hooks.

### Project structure

```text
├── Api/                    # HTTP client for the media.ccc.de API
├── Controllers/            # ASP.NET API controllers
├── Models/                 # Data models and DTOs
├── Pages/                  # Embedded HTML pages and page scripts
├── Providers/              # Jellyfin metadata, subtitle, and image providers
├── Services/               # Business logic services and interfaces
├── Configuration/          # Plugin configuration page HTML
├── scripts/                # Developer helper scripts
├── Plugin.cs               # Plugin entry point and dashboard page registration
├── ServiceRegistrator.cs   # Dependency injection setup
└── Tests/                  # Unit and integration tests
```

Other files at the repository root include `build.sh`, `meta.json`, `flake.nix`, `flake.lock`, `.envrc`, `global.json`, `.github/workflows/ci.yml`, `Jellyfin.Plugin.MediaCccDe.sln`, and `Jellyfin.Plugin.MediaCccDe.csproj`.

### Technology stack

- .NET 10, with `Jellyfin.Controller` and `Jellyfin.Model` 12.2.0.
- ASP.NET Core controller APIs supplied through Jellyfin plugin dependencies, not a direct package reference.
- `System.Text.Json` for persistence.
- xUnit and Moq for tests. `InternalsVisibleTo` is set for `MediaCccDe.Tests`.

## Limitations

- Streaming uses a shared `.strm` per talk, so language preferences for streaming are server-wide rather than per-user.
- Downloaded watchlist videos are not automatically cleaned up.
- There is no download-priority queue or bandwidth throttling.
- Jellyfin 12.2's remote `.strm` subtitle save limitation requires the temporary workaround described above.

## Troubleshooting

Jellyfin writes its server log to its configured log location. If the plugin fails to load, the server log may show `Could not load file or assembly 'MediaBrowser.Controller'` when the server cannot satisfy the plugin's assembly reference. Check that the plugin build targets a compatible Jellyfin API and that `targetAbi` matches its `Jellyfin.Controller` package version.

If automatic `CCC Archive` library creation fails, an error is logged saying `Manual setup may be required`. The Podman smoke test fetches live data from media.ccc.de and therefore needs working internet access.

## License

MIT. See [LICENSE](LICENSE).

### Content licensing

The MIT licence covers **this plugin's source code only**. The plugin ships no media: it writes `.strm` files that point at `media.ccc.de`, and it stores subtitles downloaded from that site. Nothing is redistributed here.

The talks themselves are published by the Chaos Computer Club under **per-talk licences**, typically Creative Commons Attribution 4.0, shown in the recording's intro or outro or in its metadata. See [media.ccc.de/about.html](https://media.ccc.de/about.html). If a recording carries no encoded licence, the Chaos Computer Club asks that you contact the event organisers via the "Fahrplan" link on the recording's page.

Two consequences worth knowing before you enable subtitle downloads:

- Attribution requirements belong to the recording, not to this plugin. If you redistribute a talk or its subtitles, credit the speaker and conference and link the licence.
- A subtitle fetched by this plugin is a derivative of a Creative Commons work. CC BY permits that, but the licence terms travel with the file, and a sidecar sitting in a Jellyfin library folder may not display the attribution its recording requires. Check the terms for the specific talk if you intend to redistribute anything.

## Contributing

Contributions can be submitted as pull requests. See [CONTRIBUTING.md](CONTRIBUTING.md) for the development setup, conventions, and review process.

## Support

- Issues: [GitHub Issues](https://github.com/Xyz00777/jellyfin-media.ccc.de-plugin/issues)
- Discussions: [GitHub Discussions](https://github.com/Xyz00777/jellyfin-media.ccc.de-plugin/discussions)
- Security reports: see [SECURITY.md](SECURITY.md)

## Credits

- [media.ccc.de](https://media.ccc.de) for providing the API.
- [Jellyfin](https://jellyfin.org) for the media server platform.
