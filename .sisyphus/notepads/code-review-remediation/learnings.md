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
