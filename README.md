# Jellyfin Media.CCC.de Plugin

A Jellyfin plugin that integrates media.ccc.de conference recordings into your Jellyfin server.

## Features

- **Full Conference Library**: Browse and watch all media.ccc.de conference recordings
- **Automatic Metadata**: Conferences appear as TV Series with Days as Seasons and Talks as Episodes
- **Personal Watchlist**: Add talks to your personal watchlist for download and offline viewing
- **Per-User Language Preferences**: Set preferred audio and subtitle languages
- **Admin Sync Progress**: View sync history and status in the admin panel
- **Automatic Library Setup**: Creates Archive library automatically on startup

## Compatibility

| Plugin version | Jellyfin | .NET  |
|----------------|----------|-------|
| 1.1.0          | 12.2+    | 10.0  |

Plugin 1.1.0 is built against the Jellyfin 12 API and **requires Jellyfin 12.2 or
newer**. Jellyfin 10.11 is not supported by this version, because Jellyfin 12
requires .NET 10 and the plugin is compiled against the 12.x API surface. Older
servers will report the plugin as unsupported and refuse to load it. To stay on
Jellyfin 10.11 you must build the plugin against `Jellyfin.Controller` 10.11.x
with `TargetFramework` `net9.0`; no such build is published, so build it from
source.

`targetAbi` in `meta.json` must stay in sync with the `Jellyfin.Controller`
package version in the `.csproj`. The plugin is compiled against version
`12.2.0.0` of Jellyfin's shared assemblies, and a Jellyfin server cannot satisfy
a reference to a *newer* assembly than the one it ships: declaring a lower
`targetAbi` makes the server accept the plugin and then disable it at load time
with `Could not load file or assembly 'MediaBrowser.Controller'`. Supporting an
older 12.x server therefore requires building against that server's package
version, not just lowering `targetAbi`.

## Installation

### From Release

No release artifacts are published yet, so build the plugin from source (below)
and install the resulting ZIP.

1. Build the plugin and package it: `./build.sh release`
2. Go to Jellyfin Dashboard > Plugins
3. Click the gear icon > Repositories
4. Add repository and upload `dist/media-ccc-de-plugin-<version>.zip`
5. Restart Jellyfin

### From Source

```bash
git clone https://github.com/ncc1031/jellyfin_ccc-media-de
cd jellyfin_ccc-media-de
dotnet build --configuration Release
```

Copy both the built DLL and `meta.json` to a versioned Jellyfin plugin directory:
- Linux: `/var/lib/jellyfin/plugins/`
- Windows: `C:\ProgramData\Jellyfin\Server\plugins\`
- macOS: `~/.local/share/jellyfin/plugins/`

### Fully Automatic Podman Smoke Test

With Podman, `curl`, and `jq` installed, run:

```bash
bash scripts/podman-smoke-test.sh
```

The script builds the release package, starts a disposable Jellyfin `12.2`
container, installs the plugin into its mounted configuration, creates the
first administrator through Jellyfin's startup API, authenticates, verifies
the plugin is loaded, checks an authenticated addon endpoint, verifies
acronym-based event hydration with recordings, and fetches live conference
data from `media.ccc.de`. The container and temporary data are removed
automatically. Set `KEEP_TEST_DATA=1` to retain the data for failure inspection,
or `SKIP_BUILD=1` to reuse the existing release output.

## Configuration

After installation, configure the plugin in Jellyfin Dashboard > Plugins > MediaCCC.de:

| Setting | Description | Default |
|---------|-------------|---------|
| Watchlist Path | Directory for downloaded watchlist videos; empty uses Jellyfin's plugin configuration directory | empty |
| Preferred Quality | Video quality preference | HD |
| Preferred Audio Languages | Ordered ISO 639-1 codes; a file containing both of the top two is preferred over either single-language file | empty |
| Preferred Subtitle Languages | Ordered ISO 639-1 codes, used when downloading subtitles | empty |
| Sync Interval | How often to check for new content | 6 hours |
| Download Subtitles | Fetch each talk's subtitle beside its `.strm`. Off by default | off |

### Opening the settings on Jellyfin 12

Jellyfin 12's dashboard renders plugin pages but does not execute their scripts, so
this plugin serves the settings page itself instead:

```
http://<your-jellyfin>/media_ccc/settings
```

On first use, unlock it with the access token printed in the Jellyfin server log
when the plugin starts:

```
Media.CCC.de settings can be edited at /media_ccc/settings?token=<token> ...
```

Paste that token into the form once. It is stored in
`<plugin-config-dir>/settings-access.txt`, unlocks the page for 30 days, and
should be treated as a secret. Per-user language preferences are set through
`POST /media_ccc/languages/audio`, or with the server-wide default above.

## Usage

### Browse Conferences

The dashboard pages link to the plugin's own pages. On Jellyfin 12 they are
plain links rather than interactive forms, because that dashboard does not run
plugin page scripts.

1. Navigate to **Dashboard > MediaCCC.de > Browse**
2. Browse all available conferences
3. Click on a conference to see its talks
4. Talks appear in the Archive library as TV Shows

### Watchlist

1. Add talks to your watchlist from the Browse page
2. Navigate to **Dashboard > MediaCCC.de > Watchlist**
3. View download status and manage your list
4. Downloaded videos appear in your personal watchlist library

### Language Preferences

1. Go to **Dashboard > MediaCCC.de > Language Preferences**
2. Add and reorder your preferred audio languages
3. Add and reorder your preferred subtitle languages
4. Languages are tried in order when selecting recordings

### Sync Log (Admin Only)

1. Navigate to **Dashboard > MediaCCC.de > Sync Log**
2. View sync history with timestamps
3. See number of files created per sync
4. Trigger manual sync if needed

## Architecture

### Library Structure

```
Archive Library (streaming)
├── 37C3/
│   ├── Season 01/           # Day 1
│   │   ├── opening.strm
│   │   └── ...
│   ├── Season 02/           # Day 2
│   └── ...
├── 36C3/
└── ...

User Watchlist Libraries (downloaded)
├── username's Watchlist/
│   ├── conference-acronym/
│   │   └── talk-slug.mp4
│   └── ...
```

### Data Storage

All plugin data stored in JSON files under:
- `{JellyfinData}/plugins/ccc-media/data/`

Files:
- `user-{guid}.json` - Per-user preferences and watchlist
- `sync-logs.json` - Sync history for admins
- `download-queue.json` - Pending downloads

### API Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/media_ccc/conferences` | GET | List all conferences |
| `/media_ccc/conferences/{id}/events` | GET | Get events for conference |
| `/media_ccc/events/{guid}` | GET | Get single event |
| `/media_ccc/watchlist` | GET | Get user's watchlist |
| `/media_ccc/watchlist/add` | POST | Add to watchlist |
| `/media_ccc/watchlist/remove` | POST | Remove from watchlist |
| `/media_ccc/sync/trigger` | POST | Trigger manual sync |
| `/media_ccc/sync/history` | GET | Get sync history |

## Development

### Prerequisites

- .NET 10.0 SDK
- Jellyfin 12.2+

### Building

```bash
dotnet build --configuration Release
dotnet test
```

### Project Structure

```
├── Api/                    # HTTP client for media.ccc.de API
├── Controllers/            # ASP.NET API controllers
├── Models/                 # Data models and DTOs
├── Pages/                  # Embedded HTML UI pages
├── Providers/              # Jellyfin metadata providers
├── Services/               # Business logic services
├── Configuration/          # Plugin configuration
├── Plugin.cs               # Plugin entry point
└── ServiceRegistrator.cs   # DI setup
```

### Technology Stack

- **.NET 10.0** - Target framework (required by Jellyfin 12)
- **Jellyfin.Controller** - Plugin API
- **ASP.NET Core** - API controllers
- **xUnit + Moq** - Testing
- **System.Text.Json** - Serialization

## Limitations

By design, this plugin does NOT include:
- Video transcoding (uses original files)
- Custom video player (uses Jellyfin's built-in)
- Automatic content cleanup
- Metadata editing UI
- Download priority queue
- Bandwidth throttling
- Per-user quality settings
- Thumbnail caching
- Smart recommendations

## License

MIT License - See LICENSE file for details

## Contributing

1. Fork the repository
2. Create a feature branch
3. Follow TDD: write tests first
4. Submit a pull request

## Support

- Issues: [GitHub Issues](https://github.com/ncc1031/jellyfin_ccc-media-de/issues)
- Discussions: [GitHub Discussions](https://github.com/ncc1031/jellyfin_ccc-media-de/discussions)

## Credits

- [media.ccc.de](https://media.ccc.de) for providing the API
- [Jellyfin](https://jellyfin.org) for the media server platform
