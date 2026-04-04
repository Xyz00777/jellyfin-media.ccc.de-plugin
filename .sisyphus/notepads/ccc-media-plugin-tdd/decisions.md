# Decisions - CCC Media Plugin TDD

## Key Architecture Decisions

### 1. Library Model
Two-library architecture:
- **Archive Library**: Auto-created on startup, shared, streaming-only (.strm files)
- **User Watchlist Libraries**: Per-user, auto-created when user adds first item, contains downloaded files

### 2. Metadata Provider Pattern
- `MediaCccSeriesProvider` implements `IRemoteMetadataProvider<Series, SeriesInfo>`
- `MediaCccEpisodeProvider` implements `IRemoteMetadataProvider<Episode, EpisodeInfo>`
- Both fetch data from MediaCccApi and map to Jellyfin metadata

### 3. Sync Service Pattern
- `IHostedService` that runs on schedule (default: every 6 hours)
- Generates .strm file tree for all conferences
- Logs sync results to JSON file for admin visibility

### 4. Per-User Isolation
- Each user's watchlist stored in separate library
- User data persisted as `/config/plugins/ccc-media/data/user-{guid}.json`
- Only the owning user has access to their watchlist library

### 5. Language Preference Model
- Per-user only (no admin defaults)
- Ordered priority list for both audio and subtitles
- "original" language as fallback when no preference matches

## Technical Decisions

### .NET Version
- Target: .NET 9.0 (Jellyfin 10.11+ requirement)

### Dependencies
- Jellyfin.Controller (Version="10.*")
- Jellyfin.Model (Version="10.*")
- ExcludeAssets=runtime for Jellyfin packages (plugin runs in Jellyfin context)

### Testing Strategy
- Unit tests with xUnit + Moq
- Integration tests with recorded API responses
- Minimum 80% coverage for core logic
- TDD: Tests MUST be written before implementation

### Versioning
- Semantic versioning starting at 0.x.x
- Wave completions get version bumps