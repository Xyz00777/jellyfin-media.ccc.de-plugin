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

## P1-2: SSRF & Path Traversal Security Fixes (FileService.cs)

### Changes Made
1. **SSRF Prevention - URL Scheme Validation**: `ValidateUrl()` rejects non-HTTPS schemes (http, ftp, file://, etc.)
2. **SSRF Prevention - Private IP Blocking**: `IsPrivateOrLoopbackHost()` + `IsPrivateOrLoopbackAddress()` blocks 127.0.0.0/8, 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16, 169.254.0.0/16, ::1, and "localhost"
3. **Path Traversal Prevention**: `ValidateDestinationPath()` rejects any destination path containing ".."
4. **Atomic File Writes**: Download now writes to `.tmp` file first, then `File.Move()` with overwrite — prevents partial/corrupt files on failure
5. **Temp File Cleanup**: On exception, `.tmp` file is deleted in catch block

### Key Decisions
- **`IFileService` interface unchanged**: All validation is internal implementation detail
- **Validation runs before HTTP call**: ArgumentException thrown immediately without creating HttpClient
- **`ValidateUrl()` shared by both `DownloadFileAsync` and `GetFileSizeAsync`**: Both methods get SSRF protection
- **Atomic write uses `destinationPath + ".tmp"`**: Simple suffix, cleaned up on failure, uses `File.Move` with overwrite
- **CIDR range comments preserved**: Byte-level IP range checks require CIDR annotation for readability (security-critical code)

### TDD Results
- 4 new test methods (16 total test cases with [Theory]):
  - `DownloadFileAsync_rejects_non_https_urls` (4 cases: http, ftp, file, empty)
  - `DownloadFileAsync_rejects_localhost_urls` (7 cases: 127.0.0.1, localhost, 10.x, 172.16.x, 192.168.x, 169.254.x, ::1)
  - `DownloadFileAsync_rejects_path_traversal_destination` (4 cases: ..\, ../, subdir/../.., windows traversal)
  - `DownloadFileAsync_writes_atomically` (verification: file exists, no .tmp leftover, correct content)
- All 457 tests pass (441 existing + 16 new)

## P1-4: Path Traversal Fix in UserLibraryService + WatchlistPath Platform Fix

### Changes Made
1. **SanitizeUsername path traversal fix**: Added `..` sequence stripping (repeated until no `..` remains), dot-only input detection (`.` → "unknown"), and max length truncation (64 chars).
2. **SanitizeUsername visibility**: Changed from `private` to `internal` with `[InternalsVisibleTo("MediaCccDe.Tests")]` in main csproj to enable direct unit testing.
3. **GetWatchlistBasePath visibility**: Changed from `private` to `internal` for testability.
4. **PluginConfiguration.WatchlistPath default**: Changed from hardcoded `"/config/plugins/ccc-media/watchlists/"` to `string.Empty`. The service's `GetWatchlistBasePath()` already uses `IApplicationPaths.PluginConfigurationsPath` which is platform-aware.

### Vulnerability Details
- **Original bug**: `SanitizeUsername` stripped `/` and `\` as individual characters but NOT the `..` sequence. Input like `"../etc/passwd"` → `"..etcpasswd"` — still contains `..`. Input like `".."` → `".."` — passed through completely unsanitized.
- **WatchlistPath**: Hardcoded Linux path `/config/plugins/ccc-media/watchlists/` doesn't exist on Windows/macOS. Fixed by making default empty and deriving from `IApplicationPaths.PluginConfigurationsPath`.

### Security Comments
- Added 3 inline comments in SanitizeUsername for security-critical logic: `..` stripping, dot-only check, length truncation. These are necessary because the defense-in-depth reasoning is not obvious from code alone.

### TDD Results
- 5 RED tests failed (3 path traversal cases, 1 `..` alone, 1 length truncation)
- All 458 tests pass after fixes (457 existing + 7 new: `SanitizeUsername_rejects_dot_dot_sequences`, `SanitizeUsername_blocks_path_traversal` [4 Theory cases], `SanitizeUsername_rejects_path_separators` [4 Theory cases], `SanitizeUsername_handles_empty_and_whitespace` [4 Theory cases], `SanitizeUsername_truncates_long_input`, `GetOrCreateUserLibraryAsync_creates_directory_atomically`, `GetWatchlistBasePath_uses_PluginConfigurationsPath`)
- Note: test count increased from 457 to 458 (total test cases including Theory expansions is higher)

### Potential Gotchas
- `[InternalsVisibleTo]` in csproj requires the test project assembly name to match exactly. The test project's assembly name is `MediaCccDe.Tests` (set via `AssemblyName` derived from project name `MediaCccDe.Tests`).
- The `while (sanitized.Contains(".."))` loop is intentional: `Replace("..", "")` on `"..."` → `"."`, but a single pass on `"...` would leave `"."`. Repeated replacement handles edge cases like `"...."`.
