# Learnings - CCC Media Plugin TDD

## 2026-04-04 Architecture Decisions

### Conference-to-Series Mapping
- Conference = TV Series
- Day = Season (use ParentIndexNumber for day number)
- Talk = Episode (use IndexNumber for event order)

### File Structure for .strm Files
```
{archivePath}/{acronym}/Season {day_number}/{slug}.strm
```

### Library Paths
- Archive: `/config/plugins/ccc-media/archive/`
- Watchlists: `/config/plugins/ccc-media/watchlists/{username}/`
- User data: `/config/plugins/ccc-media/data/user-{guid}.json`
- Sync logs: `/config/plugins/ccc-media/data/sync-logs.json`

### API Client
- Base URL: `https://api.media.ccc.de/public/`
- JSON serialization uses snake_case_lower property naming
- All endpoints return DTOs that map to domain models

### Language Selection
- Fallback chain: preferred language → original language
- "und" (undefined) treated as valid fallback
- Subtitles return null if no match found (no fallback)

## Wave 2 Completed (v0.2.0)
- MediaCccApi HTTP client fully implemented
- DTOs created: ConferenceDto, EventDto, RecordingDto
- Domain models created: Conference, Event, Recording
- LanguageSelector service with language matching logic
- RecordingSelector service for quality/language selection
- 99+ tests passing

## Wave 3 GREEN Phase Progress

### 2026-04-04 Completed Tasks

#### Task 26: Series Metadata Provider
- Implemented `MediaCccSeriesProvider` in `Providers/MediaCccSeriesProvider.cs`
- Implements `IRemoteMetadataProvider<Series, SeriesInfo>`
- Maps Conference → Series metadata (Name, OriginalTitle, Overview, Genres)
- Caches conference list for performance
- Note: ConferenceDto needs Description property added

#### Task 28: Episode Metadata Provider
- Implemented `MediaCccEpisodeProvider` in `Providers/MediaCccEpisodeProvider.cs`
- Implements `IRemoteMetadataProvider<Episode, EpisodeInfo>`
- Maps Event → Episode metadata
- Note: Test file has `_mockRecordingSelector` bug - references undeclared field

#### Task 30: Strm Generator
- Created `IStrmGenerator` interface and `StrmGenerator` class
- Created `IRecordingSelector` interface in `Services/IRecordingSelector.cs`
- Path format: `{archivePath}/{acronym}/Season {day_number:D2}/{slug}.strm`
- Day number extracted from date: day_of_month - 27 (CCC convention)
- File sanitization for special characters

#### Task 36: Sync Logger
- Created `ISyncLogger` interface and `SyncLogger` class
- `SyncStatus` enum: Started, Completed, Failed
- `SyncLogEntry` class with all required properties
- Thread-safe implementation with JSON persistence
- Path: `{DataPath}/sync-logs.json`

### Key Interfaces Created
- `IRecordingSelector` - For recording selection (implemented by RecordingSelector)
- `IStrmGenerator` - For .strm file generation
- `ISyncLogger` - For sync operation logging

### Test File Notes
- StrmGeneratorTests and StrmTreeGeneratorTests both define `IStrmGenerator` with different signatures - potential namespace conflict
- Episode provider tests have mock field naming inconsistency
## Task 36: SyncLogger Implementation (2026-04-04)

### Model/Interface Pattern
- `SyncLogEntry` and `SyncStatus` are public models used across codebase
- `ISyncLogger` interface allows mocking in tests (used by SyncServiceTests)
- Constructor takes `IApplicationPaths`, `ILogger<SyncLogger>`, and optional `maxHistoryEntries`

### Thread-Safety Pattern
- All public methods use `lock(_lock)` for thread-safe access
- History stored in-memory as `List<SyncLogEntry>`
- Insert at position 0 (newest first) for date-descending order

### File Persistence
- Path: `{DataPath}/sync-logs.json` (not full path from decisions.md)
- Uses `System.Text.Json` with indented formatting
- Creates directory if missing
- Handles missing/corrupted JSON files gracefully (logs warning, starts empty)

## Task 28: Episode Metadata Provider Implementation

### Implementation Choices
- `IndexNumber` derived from event slug's numeric portion (e.g., "37c3-12746-opening" → 12746 % 100 + 1 = 47)
- `ParentIndexNumber` derived from date: Dec 27 = Day 1, Dec 28 = Day 2, etc.
- `RunTimeTicks` = event duration in seconds × 10,000,000 (100-ns ticks)
- `ProviderIds["MediaCccDe"]` = event GUID for future lookups

### Test Bug Identified
- MediaCccEpisodeProviderTests references `_mockRecordingSelector` field that is never declared
- Test constructor creates concrete `RecordingSelector` instance but some tests try to mock behavior
- This is a test file bug, not implementation bug

### Mapping Summary
- Episode = Talk from conference
- IndexNumber = Episode order within day
- ParentIndexNumber = Day number (season)
- RunTimeTicks = Length seconds × 10,000,000

## Task 26: Series Metadata Provider Implementation

### Interface Contract
- `IRemoteMetadataProvider<Series, SeriesInfo>` requires `Task<MetadataResult<Series>> GetMetadata(SeriesInfo, CancellationToken)`
- Tests initially expected `SeriesInfo?` but must return `MetadataResult<Series>`
- `MetadataResult<T>.HasMetadata` must be true for successful lookup
- `MetadataResult<T>.Item` is the `Series` entity with populated metadata

### Conference-to-Series Mapping
- `Conference.Title` → `Series.Name`
- `Conference.Slug` → `Series.OriginalTitle`  
- `Conference.Description` → `Series.Overview`
- `{ "Talk" }` → `Series.Genres`
- `Conference.Acronym` → `Series.ProviderIds["MediaCccDe"]`

### Conference Lookup Pattern
- Match by acronym or slug (case-insensitive)
- Cache conference list in instance field `_cachedConferences`
- Return `MetadataResult<Series> { HasMetadata = false }` for no match
- Return same on `HttpRequestException`

### Test Pattern
- Use `MetadataResult<Series>` return type, not `SeriesInfo?`
- Assert `result.HasMetadata` and `result.Item.Name`, etc.
- Tests must NOT reference interface members that don't exist (`SupportsRefresh`, `GetSupportedMediaTypes`)

### Description Property Added
- `ConferenceDto.Description` maps to `Series.Overview`
- `Conference.Description` domain model also has this property

## Task 32: SyncService Implementation (2026-04-04)

### IHostedService Pattern
- `SyncService` implements `IHostedService` for background task execution
- `StartAsync()` returns `Task.CompletedTask` immediately, spawns background task
- `StopAsync()` cancels via `CancellationTokenSource` and waits for completion
- Background loop uses `while (!cancellationToken.IsCancellationRequested)`

### Interface Extensions Required
- Tests expected `IStrmGenerator.CreateStrmFilesForConference(ConferenceDto, CancellationToken)` and `StrmFilesExistForConference(ConferenceDto)`
- Tests expected `ISyncLogger.LogSyncCompletion(int, int, DateTime)` method
- These were added to existing interfaces alongside original methods

### Sync Interval Pattern
- Configuration `SyncIntervalHours` controls loop delay
- When set to 0 or negative, service loops continuously without delay (for testing)
- `Task.Delay(TimeSpan.FromHours(intervalHours), cancellationToken)` respects cancellation

### Error Handling
- `HttpRequestException` caught separately for API failures
- General `Exception` caught to prevent loop from breaking
- `OperationCanceledException` breaks the loop gracefully

### Test Mock Pattern
- `Mock<PluginConfiguration>(MockBehavior.Loose)` for configuration
- `MockBehavior.Strict` for API client, services (verifies exact calls)
- Tests verify: start/completed logs, STRM creation, cancellation handling

## Wave 4 Research (Jellyfin APIs)

### User Data Persistence
- **IUserDataManager** - for per-item user data (watched status, favorites)
- **Plugin.DataFolderPath** - for custom JSON storage (per-user preferences, watchlists)
- **Custom DbContext** - for complex relational data (optional)
- Store user data at: `plugin.DataFolderPath/user-{guid}.json`

### Per-User Library Creation
- **ILibraryManager.AddVirtualFolder(name, collectionType, LibraryOptions, refreshLibrary)**
- **LibraryOptions** has: `Enabled`, `PathInfos`, `EnablePhotos`, etc.
- **IMPORTANT**: Jellyfin core has NO per-library ACL - need custom implementation
- Approaches for per-user visibility:
  1. Virtual folder naming: `user_{username}_watchlist`
  2. Filter libraries via custom API endpoint
  3. Use parental controls (MaxParentalRating)

### Background Download Patterns
- **IScheduledTask** - for periodic queue processing
- **IHostedService** - for persistent background services (already in use by SyncService)
- **IHttpClientFactory.CreateClient(NamedClient.Default)** - for HTTP requests
- **IProgress<double>** - for progress reporting to UI
- Download queue pattern: Store in database or JSON, process with IHostedService

### Key Namespaces
- `MediaBrowser.Controller.Library` - ILibraryManager, IUserDataManager
- `MediaBrowser.Model.Configuration` - LibraryOptions
- `MediaBrowser.Model.Plugins` - BasePluginConfiguration
- `Microsoft.Extensions.Hosting` - IHostedService

## Task 39: UserDataManager Implementation (2026-04-04)

### Model Created
- `Models/UserData.cs` - User data container with Watchlist, SearchProgress, PreferredAudioLanguages, PreferredSubtitleLanguages, CreatedAt, UpdatedAt

### Interface Created
- `Services/IUserDataManager.cs` - Full contract for user data management

### Implementation
- Thread-safe with `lock(_lock)` around all public methods
- In-memory cache `Dictionary<Guid, UserData>` for fast access
- JSON persistence at `{DataPath}/plugins/ccc-media/data/user-{guid}.json`
- Graceful handling of missing/corrupt JSON files (logs warning, returns empty data)
- Directory creation with `Directory.CreateDirectory()` before saving
- Timestamp management: CreatedAt set on first save, UpdatedAt updated on each modification

### Key Patterns
- `GetOrCreateUserData(userId)` - ensures user data exists in cache
- `GetUserFilePath(userId)` - standardized path for user JSON files
- Returning copies from getter methods to prevent external mutation

### Path Format
- `{DataPath}/plugins/ccc-media/data/user-{guid}.json`
- Following same pattern as SyncLogger (logs at `sync-logs.json`)

### Build Status
- Implementation compiles correctly
- Other test files (DownloadProcessing, FileService, DownloadQueue, UserLibraryService) have pre-existing errors from unfinished TDD tasks
- UserDataManager tests will run once other tasks are complete

## Wave 5: API Endpoints Tests (Task 60)

### RED Phase Tests Created
- File: `Tests/Unit/ApiEndpointsTests.cs`
- Tests define expected behavior for `MediaCccController` and `SyncController`
- Controllers do NOT exist yet - tests intentionally FAIL

### MediaCccController Endpoints (6 tests)
1. `GET /media_ccc/conferences` - Returns list of conferences
   - Happy path: 200 OK with conferences list
   - Empty list: 200 OK with empty list
   - API failure: 500 error

2. `GET /media_ccc/conferences/{id}/events` - Returns events for conference
   - Happy path: 200 OK with events array
   - Invalid ID: 400 Bad Request
   - No events: 200 OK with empty array

3. `GET /media_ccc/events/{guid}` - Returns single event
   - Happy path: 200 OK with event details
   - Not found: 404 NotFound
   - Empty GUID: 400 Bad Request

4. `POST /media_ccc/watchlist/add` - Add event to watchlist
   - Requires authentication (401 if not authenticated)
   - Validates event GUID (400 for empty)
   - Idempotent (no duplicates)

5. `POST /media_ccc/watchlist/remove` - Remove event from watchlist
   - Requires authentication
   - Validates event GUID
   - Idempotent (OK even if event not on list)

6. `GET /media_ccc/watchlist` - Get user's watchlist
   - Requires authentication
   - Returns empty list for new user

### SyncController Endpoints (3 tests)
1. `POST /media_ccc/sync/trigger` - Manually trigger sync
   - Requires admin (Elevation policy)
   - Returns 202 Accepted
   - 403 Forbidden for non-admin

2. `GET /media_ccc/sync/status` - Get sync status/progress
   - Requires admin
   - Returns sync history

3. `GET /media_ccc/sync/history` - Get sync history
   - Optional filter by conference acronym
   - Requires admin

### Controller Pattern
- Base route: `[Route("media_ccc")]` for MediaCccController
- Base route: `[Route("media_ccc/sync")]` for SyncController
- Admin endpoints use `[Authorize(Policy = "Elevation")]`
- User endpoints check `HttpContext.User` for `ClaimTypes.NameIdentifier`

### Test Helper Pattern
```csharp
private MediaCccController CreateMediaCccControllerWithUser(Guid userId)
{
    var controller = CreateBaseController();
    var claims = new List<Claim> { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
    var identity = new ClaimsIdentity(claims, "Test");
    controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(identity);
    return controller;
}
```

### Interfaces to Mock
- `IMediaCccApiClient` - Conference/event data fetch
- `IUserDataManager` - Watchlist operations
- `ISyncLogger` - Sync history/status
- `PluginConfiguration` - Sync settings

### Build Status (RED Phase)
- 10 expected compilation errors:
  - CS0234: `Controllers` namespace does not exist
  - CS0246: `MediaCccController` type not found
  - CS0246: `SyncController` type not found
- These errors confirm TDD RED phase - implementation needed in GREEN phase

## Task 49: Configuration Page Scaffold (2026-04-05)

### Jellyfin Configuration Page Pattern
- Uses `data-role="page"` with class `"page type-interior pluginConfigurationPage"`
- `data-require` attribute lists required Emby components: `emby-input,emby-button,emby-select,emby-checkbox`
- Plugin GUID: `e225c91a-ef11-41ca-b913-6491f15c2992`

### UI Structure
- `verticalSection` divs group related settings with `<h2>` headers
- `inputContainer` for text/number inputs with `emby-input` custom element
- `selectContainer` for dropdowns with `emby-select` custom element
- `fieldDescription` divs provide help text below inputs
- Submit button: `is="emby-button" type="submit" class="raised button-submit block"`

### JavaScript Pattern (Modern, No jQuery)
- `ApiClient.getPluginConfiguration(pluginId)` - load config
- `ApiClient.updatePluginConfiguration(pluginId, config)` - save config
- Event listener on 'pageshow' event (NOT jQuery's .on('pageshow'))
- IIFE pattern to avoid polluting global scope
- Handle arrays by joining with ', ' for display, splitting on save

### Configuration Properties Mapped
- `WatchlistPath` - text input
- `PreferredQuality` - select dropdown (hd/sd)
- `SyncIntervalHours` - number input (1-168)
- `PreferredAudioLanguages` - comma-separated text → array
- `PreferredSubtitleLanguages` - comma-separated text → array

### Design System Workflow Applied
1. Researched existing Jellyfin plugin configs (TMDb, Douban, others)
2. Identified standard components (emby-input, emby-select, etc.)
3. Matched existing patterns (verticalSection, inputContainer, fieldDescription)
4. Used proper event handler pattern (pageshow event listener)
5. Clean, minimal UI aligned with Jellyfin's design system
