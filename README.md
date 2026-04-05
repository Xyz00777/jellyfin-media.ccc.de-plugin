# Jellyfin Media.CCC.de Plugin

A Jellyfin plugin that integrates media.ccc.de conference recordings into your Jellyfin server.

## Features

- **Full Conference Library**: Browse and watch all media.ccc.de conference recordings
- **Automatic Metadata**: Conferences appear as TV Series with Days as Seasons and Talks as Episodes
- **Personal Watchlist**: Add talks to your personal watchlist for download and offline viewing
- **Per-User Language Preferences**: Set preferred audio and subtitle languages
- **Admin Sync Progress**: View sync history and status in the admin panel
- **Automatic Library Setup**: Creates Archive library automatically on startup

## Installation

### From Release

1. Download the latest release ZIP file
2. Go to Jellyfin Dashboard > Plugins
3. Click the gear icon > Repositories
4. Add repository and upload the ZIP
5. Restart Jellyfin

### From Source

```bash
git clone https://github.com/your-repo/jellyfin_ccc-media-de
cd jellyfin_ccc-media-de
dotnet build --configuration Release
```

Copy the built DLL to your Jellyfin plugins directory:
- Linux: `/var/lib/jellyfin/plugins/`
- Windows: `C:\ProgramData\Jellyfin\Server\plugins\`
- macOS: `~/.local/share/jellyfin/plugins/`

## Configuration

After installation, configure the plugin in Jellyfin Dashboard > Plugins > MediaCCC.de:

| Setting | Description | Default |
|---------|-------------|---------|
| Watchlist Path | Directory for downloaded watchlist videos | `/config/plugins/ccc-media/watchlists/` |
| Preferred Quality | Video quality preference | HD |
| Sync Interval | How often to check for new content | 6 hours |

## Usage

### Browse Conferences

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

- .NET 9.0 SDK
- Jellyfin 10.11+

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

- **.NET 9.0** - Target framework
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

- Issues: [GitHub Issues](https://github.com/your-repo/jellyfin_ccc-media-de/issues)
- Discussions: [GitHub Discussions](https://github.com/your-repo/jellyfin_ccc-media-de/discussions)

## Credits

- [media.ccc.de](https://media.ccc.de) for providing the API
- [Jellyfin](https://jellyfin.org) for the media server platform