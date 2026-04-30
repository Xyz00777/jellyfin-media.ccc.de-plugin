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

## P2-2: MediaCccEpisodeProvider Bug Fixes (C3-C5 + 3 more)

### Bugs Fixed
1. **C3 — GetMetadata used info.Name instead of ProviderIds**: `GetMetadata` used `info.Name` (talk title) as the lookup key for `GetEventAsync`. After initial identification, Jellyfin stores the event GUID in `ProviderIds["MediaCccDe"]` and passes BOTH `Name` and `ProviderIds` on refresh. Using `Name` caused metadata refresh to always fail for previously-identified episodes. Fixed: `info.ProviderIds.TryGetValue("MediaCccDe", out var existingId) ? existingId : info.Name`.

2. **C4 — DeriveIndexNumber used non-deterministic string.GetHashCode()**: `string.GetHashCode()` is not deterministic across .NET process restarts (it uses a random per-process seed since .NET Core). This meant the same episode could get a different `IndexNumber` every server restart, breaking episode ordering. Fixed: replaced with SHA256 hash of the GUID, which is deterministic.

3. **C5 — DeriveParentIndexNumber hard-coded Dec-27 convention**: `dayOfMonth >= 27 && dayOfMonth <= 31` assumed CCC congresses in December, but the plugin supports other conferences (MCH in July, GPN in March, etc.). The old code would assign `Math.Min(dayOfMonth, 10)` to any non-late-December event, which is wrong for July (7→7) but catastrophically wrong for conferences spanning multiple months. Fixed: CCC congress logic preserved for December 27+, all other months use `dayOfMonth` directly (day 1 of conference = season 1).

4. **GetSearchResults was empty**: Returned `Enumerable.Empty<RemoteSearchResult>()`, breaking Jellyfin's "Identify" feature. Fixed: returns a `RemoteSearchResult` when `ProviderIds["MediaCccDe"]` is present.

5. **GetImageResponse threw NotImplementedException**: Fixed: returns `HttpResponseMessage` with `HttpStatusCode.NotFound` (404).

6. **Constructor depended on RecordingSelector (concrete) instead of IRecordingSelector (interface)**: DI registers `IRecordingSelector → RecordingSelector`, but the provider depended on the concrete type, preventing testing with mocks. Fixed: parameter changed to `IRecordingSelector`.

### Key Decisions
- **SHA256 for determinism**: Chose `SHA256.HashData` over simpler approaches (CRC32, FNV) because it's in `System.Security.Cryptography` (no NuGet needed), is guaranteed deterministic, and the cost is negligible for short GUID strings.
- **DeriveParentIndexNumber December logic unchanged**: For December 27-31, the formula `eventDate.Day - 27 + 1` is preserved exactly. This gives Dec 27=1, Dec 28=2, Dec 29=3, Dec 30=4, Dec 31=5 — matching the CCC convention that "Day 1" is Dec 27.
- **Non-December conferences use day-of-month**: This is a best-effort approach. Ideally we'd know the conference start date, but `GetMetadata` only sees a single event at a time, not the whole conference. The day-of-month approach works correctly for conferences that start on day 1 of a month.
- **Test constructor updated from `RecordingSelector` to `Mock<IRecordingSelector>`**: This allows proper unit testing with mocked dependencies, matching DI patterns.

### TDD Results
- 11 new tests written (all failed RED initially due to constructor type mismatch)
- Existing tests updated: changed `_recordingSelector` from `RecordingSelector` concrete to `Mock<IRecordingSelector>`
- Total: 475 tests passing (464 existing + 11 new)

## C9: TriggerSync in SyncService was a no-op

### Bug Fixed
1. **TriggerSyncAsync did not exist**: SyncService had no `TriggerSyncAsync` method. The SyncController's `TriggerSync()` endpoint just logged and returned `AcceptedResult` without invoking any sync.
2. **Background loop used Task.Delay with no wake mechanism**: `ExecuteAsync` used `await Task.Delay(TimeSpan.FromHours(intervalHours))` — no way to interrupt the delay and trigger an immediate sync.
3. **SemaphoreSlim.WaitAsync TimeSpan overflow**: `SemaphoreSlim.WaitAsync(TimeSpan)` throws `ArgumentOutOfRangeException` if the TimeSpan exceeds `int.MaxValue` milliseconds (~24.85 days / ~596 hours). The old `Task.Delay` didn't have this limitation.

### Changes Made
1. **Added `SemaphoreSlim _syncTrigger`**: Initialized with `(0, int.MaxValue)` — allows unlimited pending trigger signals.
2. **Added `TriggerSyncAsync(CancellationToken)` method**: Calls `_syncTrigger.Release()` to signal the waiting loop.
3. **Replaced `Task.Delay` with `_syncTrigger.WaitAsync(delay, cancellationToken)`**: The loop now waits on the semaphore with a timeout. If the timeout elapses (normal interval), `WaitAsync` returns `false` and the loop continues. If `TriggerSyncAsync` releases the semaphore, `WaitAsync` returns `true` immediately and the loop runs a sync cycle.
4. **Drain logic after trigger**: `while (_syncTrigger.Wait(TimeSpan.Zero)) { }` drains any accumulated signals after a trigger, preventing stale signals from causing extra sync cycles.
5. **Cap delay at `int.MaxValue` milliseconds**: Before calling `WaitAsync`, the delay is clamped to `TimeSpan.FromMilliseconds(int.MaxValue)` to prevent `ArgumentOutOfRangeException`.

### Key Decisions
- **SemaphoreSlim over CancellationTokenSource approach**: Cancellation-based approaches (cancel a CTS to wake the loop) work but are one-shot — you need to create a new CTS for each cycle. SemaphoreSlim naturally supports multiple signals and integrates cleanly with the timeout pattern.
- **`int.MaxValue` max count**: Using `int.MaxValue` as the max semaphore count avoids `SemaphoreFullException` from multiple rapid `TriggerSyncAsync` calls. Signals beyond the first are drained after wake.
- **ISyncService interface NOT added**: SyncService remains a concrete class implementing `IHostedService`. The `TriggerSyncAsync` method is on the concrete class. Adding an interface was not required by the task constraints.
- **SyncController NOT modified**: The task specified not to change SyncController.cs. The `TriggerSyncAsync` method is available on `SyncService` for future wiring.

### TDD Results
- 2 new tests written (both failed RED initially — `TriggerSyncAsync` didn't exist, got CS1061 compilation error)
- After adding `TriggerSyncAsync` and the semaphore mechanism, both tests passed (GREEN)
- Total: 477 tests passing (475 existing + 2 new)

### Potential Gotchas
- `SemaphoreSlim.WaitAsync(TimeSpan)` has a max value of `int.MaxValue` ms. Any `SyncIntervalHours > 596` would overflow. The cap ensures this can't happen.
- `_syncTrigger.Wait(TimeSpan.Zero)` is a non-blocking drain — it returns `true` if a signal was consumed, `false` if the semaphore is empty. Safe to call from the async loop since it only runs after `WaitAsync` returns.
- The `TriggerSyncAsync` signal is persistent — if `Release()` is called before `WaitAsync`, the count increases and the next `WaitAsync` returns immediately with `triggered=true`. This means there's no race condition between triggering and the loop entering the wait state.
