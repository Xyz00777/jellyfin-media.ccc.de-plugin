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
