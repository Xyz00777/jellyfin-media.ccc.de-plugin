# Learnings — Code Review Remediation

## Project Conventions
- Jellyfin Plugin 10.x pattern: `BasePlugin<T>`, `IPluginServiceRegistrator`, `IRemoteMetadataProvider<T>`
- .NET 9.0 target, xUnit + Moq for testing
- Interfaces in separate files, DI via ServiceRegistrator
- GlobalUsings.cs provides common namespace imports

## Key Patterns
- Domain models (Event, Conference, Recording) separate from API DTOs (EventDto, etc.)
- STRM files = streaming pointers, MP4 = downloaded watchlist files
- JSON persistence to `{JellyfinData}/plugins/ccc-media/data/`
- Named HttpClient registered via AddHttpClient but not consistently consumed

## Architectural Decisions
- SyncService, DownloadService, LibrarySetupService all run as IHostedService
- RecordingSelector and LanguageSelector are stateless strategy services
- Providers rely on Jellyfin assembly scanning for registration, not explicit DI

## P0-2: Serialization Bug Fixes (Recording.cs, RecordingDto.cs)

### Bugs Fixed
1. **Recording.HighQuality duplicate [JsonPropertyName]**: Private backing field `_highQuality` had its own `[JsonPropertyName("high_quality")]`, creating a duplicate attribute. System.Text.Json didn't throw but the setter coerced null→false, losing "unknown" state.
2. **Recording.HighQuality null coercion**: Setter `set => _highQuality = value ?? false` turned null (unknown) into false, losing semantic meaning. Fixed by making it a simple `bool?` auto-property.
3. **RecordingDto.MimeType wrong JSON key**: Used `mime_type` but media.ccc.de API returns `mimetype` (no underscore). Changed `[JsonPropertyName("mime_type")]` → `[JsonPropertyName("mimetype")]`.

### Downstream Impact Assessed
- `RecordingSelector.FilterByQuality`: Uses `r.HighQuality.GetValueOrDefault()` and `r.HighQuality ?? false` — both handle null correctly (defaults to false for comparison).
- `StrmGenerator.MapToRecording` and `StrmFileGeneratorAdapter.MapToRecording`: Both map `bool HighQuality` → `bool? HighQuality` via implicit conversion, safe.
- `RecordingSelectorTests.SelectBestRecording_handles_no_quality_flag`: Updated assertion from `Assert.False(result.HighQuality ?? true)` to `Assert.Null(result.HighQuality)` to reflect new semantics (null = unknown, preserved).

### TDD Results
- 3 of 4 new tests failed initially (RED confirmed)
- `Recording_deserialization_does_not_throw` passed even before fix (duplicate attribute doesn't throw in System.Text.Json)
- All 418 tests pass after fixes (414 existing + 4 new)

## P0-3: List<string> → HashSet<string> for UserData Collections

### Changes Made
1. **UserData.cs**: All 4 collection properties (`Watchlist`, `SearchProgress`, `PreferredAudioLanguages`, `PreferredSubtitleLanguages`) changed from `List<string>` to `HashSet<string>`.
2. **UserDataManager.cs**: Updated internal consumers — `HashSet.Add()` returns `bool` (replaced explicit `Contains` + `Add` pattern), `GetWatchlist`/`GetPreferredAudioLanguages`/`GetPreferredSubtitleLanguages` now return `.ToList()`, `SetPreferred*` now construct `new HashSet<string>(languages)`.
3. **IUserDataManager.cs**: No changes needed — interface methods still return `List<string>` and accept `List<string>` parameters. Internal conversion happens in the implementation.
4. **MediaCccController.cs**: No changes needed — only uses `IUserDataManager` interface methods.
5. **UserDataManagerTests.cs**: Updated `new List<string>` initializers to `new HashSet<string>`, replaced `.Watchlist[0]`/`.SearchProgress[0]` indexer asserts with `.Contains()`/`.First()`, renamed `GetWatchlist_returns_ordered_list` → `GetWatchlist_returns_all_items` (HashSet doesn't guarantee order).

### Key Decisions
- **IUserDataManager interface unchanged**: Return types remain `List<string>` for backward compatibility. The manager converts internally (`HashSet ↔ List`). This avoids breaking any consumer depending on `List<string>` from the interface.
- **HashSet doesn't preserve insertion order**: Tests that previously verified insertion order were updated to check membership instead. If ordering becomes important later, consider `LinkedHashSet`-equivalent or return `.OrderBy()`.
- **System.Text.Json serialization**: `HashSet<string>` serializes identically to `List<string>` (both become JSON arrays). Deserialization works because `System.Text.Json` populates via the setter, and `UserData` has public setters with initializers.
- **TDD Results**: 8 new tests written first (all RED with `List<string>` — 4 dedup tests failed showing duplicate count=2, 4 IsType tests failed showing `List<string>`). After switching to `HashSet<string>`, all 426 tests passed (418 existing + 8 new).

### Potential Gotchas
- HashSet deserialization: If deserializing a JSON with duplicate keys into `HashSet<string>`, `System.Text.Json` will call `Add()` which silently ignores duplicates. No data loss, but the dedup happens silently.
- HashSet order is implementation-dependent: While .NET Core's `HashSet<T>` currently preserves insertion order for non-removed items, this is NOT contractual. Don't rely on it.
