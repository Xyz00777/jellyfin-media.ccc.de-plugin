# Code Review Remediation Plan

> **Source**: Consolidated 6-agent deep-dive code quality audit  
> **Total findings**: 10 CRITICAL · 13 HIGH · 24 MEDIUM · 5 LOW  
> **Plan created**: 2026-04-30

---

## Phase 0 — Bootstrap (Blockers for all other work)

- [x] **P0-1: Fix ServiceRegistrator DI to allow plugin startup**
  - C1 (`StrmGenerator` requires unresolvable `string archivePath`)
  - C2 (`StrmTreeGenerator` requires unresolvable `IStrmFileGenerator`)
  - H10 (Singleton `MediaCccApi` with raw `HttpClient` → DNS staleness)
  - C10 (`DownloadQueue` sync-over-async in constructor)
  - H6 (`FileService` global `SemaphoreSlim(1,1)` killing download concurrency)
  - Acceptance: `dotnet build` succeeds, DI resolution smoke test passes
  - _Files_: `ServiceRegistrator.cs`, `MediaCccApi.cs`, `DownloadQueue.cs`, `FileService.cs`

---

## Phase 1 — Data Model Integrity (fix models before services that depend on them)

- [x] **P1-1: Fix Recording.cs + RecordingDto.cs serialization bugs**
  - C5 (Duplicate `[JsonPropertyName("high_quality")]` on private field + property)
  - C5 (`mime_type` vs `mimetype` mismatch between `RecordingDto` and `Recording`)
  - `HighQuality` setter coercing null → false (losing "unknown" state)
  - Nullability mismatches between DTO ↔ domain model pairs
  - Acceptance: DTO round-trip deserialization tests pass, no `InvalidOperationException` from JSON
  - _Files_: `Recording.cs`, `RecordingDto.cs`, `DtoTests.cs`

- [ ] **P1-2: Add validation & immutability to all Models**
  - `required` modifier on identity properties (Guid, Id, Title, Acronym, etc.)
  - `[Required]`, `[StringLength]`, `[Range]` attributes where appropriate
  - Change exposed `List<T>` → `IReadOnlyList<T>` with `init` (Event, UserData, RecordingPreferences)
  - Add `IEquatable<T>` / `Equals` / `GetHashCode` to key models
  - Rename `UserLibrary.Path` → `LibraryPath` (shadows `System.IO.Path`)
  - Rename `Recording.Guid` → `EventGuid` (shadows `System.Guid`)
  - Acceptance: All model tests pass, no invalid default states possible
  - _Files_: All 11 model/DTO files, `DtoTests.cs` updates

- [x] **P1-3: Replace `List<string>` with `HashSet<string>` in UserData**
  - `Watchlist`, `SearchProgress`, `PreferredAudioLanguages`, `PreferredSubtitleLanguages`
  - O(n) → O(1) lookup performance
  - Acceptance: All `UserDataManagerTests` pass with HashSet, no behavioral change
  - _Files_: `UserData.cs`, `UserDataManager.cs`, `UserDataManagerTests.cs`

---

## Phase 2 — Security Fixes (critical exploits)

- [x] **P2-1: URL & path validation in FileService**
  - C6 (SSRF — no URL validation on downloads)
  - C6 (Path traversal — no `destinationPath` sanitization)
  - H13 (Non-atomic file persistence — temp file + rename pattern)
  - Acceptance: URL whitelist enforced (https://cdn.media.ccc.de only), `..` blocked, atomic writes
  - _Files_: `FileService.cs`, `FileServiceTests.cs`

- [x] **P2-2: Path traversal fix in UserLibraryService**
  - C7 (`SanitizeUsername` does not strip `..` → parent-directory writes)
  - H11 (Lock held during slow synchronous `AddVirtualFolder`)
  - H8 (Hardcoded Linux-only default `WatchlistPath` in `PluginConfiguration`)
  - Acceptance: Username `..` rejected, path validation tests pass, cross-platform path defaults
  - _Files_: `UserLibraryService.cs`, `UserLibraryServiceTests.cs`, `PluginConfiguration.cs`

- [x] **P2-3: Add authentication to MediaCccController read endpoints**
  - C8 (Browse `/conferences`, `/events`, `/events/recent` — no `[Authorize]`)
  - C8 (Server usable as open proxy to media.ccc.de API)
  - H12 (Inconsistent `elevation` policy vs `Admin` role in `SyncController`)
  - Acceptance: Anonymous requests to read endpoints return 401; admin sync endpoints consistent
  - _Files_: `MediaCccController.cs`, `SyncController.cs`, `ApiEndpointsTests.cs`

---

## Phase 3 — Core Feature Fixes (things that are simply broken)

- [x] **P3-1: Fix MediaCccEpisodeProvider GUID lookup**
  - C3 (`info.Name` used instead of `info.ProviderIds["MediaCccDe"]`)
  - C4 (Non-deterministic `GetHashCode()` in `DeriveIndexNumber`)
  - C5 (Hardcoded Dec-27 convention in `DeriveParentIndexNumber`)
  - `GetSearchResults` returns empty (breaks "Identify" feature)
  - `GetImageResponse` throws `NotImplementedException`
  - DI violation: depends on `RecordingSelector` (concrete) not `IRecordingSelector`
  - Acceptance: Episode metadata loads correctly via ProviderIds, stable episode numbers, Identify dialog works
  - _Files_: `MediaCccEpisodeProvider.cs`, `MediaCccEpisodeProviderTests.cs`, `ServiceRegistrator.cs`

- [x] **P3-2: Fix TriggerSync (currently a no-op)**
  - C9 (`TriggerSync` logs but never invokes sync)
  - Acceptance: POST to `/media_ccc/sync/trigger` actually triggers a sync
  - _Files_: `SyncController.cs`, `SyncService.cs`, `SyncServiceTests.cs`

- [x] **P3-3: Fix MediaCccSeriesProvider cache & identify**
  - H1 (`_cachedConferences` never expires, no thread safety)
  - H2 (`GetSearchResults` empty → breaks Identify)
  - Dead code at lines 62-65 (Overview set twice)
  - `OriginalTitle` = `Slug` (semantically wrong)
  - `Genres` replaces all existing instead of appending
  - Acceptance: Cache expires after N minutes, concurrent access safe, Identify dialog functional
  - _Files_: `MediaCccSeriesProvider.cs`, `MediaCccSeriesProviderTests.cs`

---

## Phase 4 — STRM Generation Consistency

- [x] **P4-1: Unify StrmGenerator & StrmTreeGenerator**
  - H4 (Hardcoded `date.Day - 27` in `ExtractDayNumber` → only works for December CCC)
  - H5 (Directory name mismatch: lowercase vs original-case acronym)
  - Extract duplicate `SanitizeFileName` and `ExtractDayNumber` into shared utility
  - Non-atomic stale file deletion with TOCTOU in `StrmTreeGenerator`
  - Sequential N+1 API calls in `GenerateSeriesStrmTreeAsync`
  - Acceptance: Both generators produce identical directory/file names for same conference; date logic works for any month
  - _Files_: `StrmGenerator.cs`, `StrmTreeGenerator.cs`, new shared utility, `StrmGeneratorTests.cs`, `StrmTreeGeneratorTests.cs`

---

## Phase 5 — Runtime Robustness

- [x] **P5-1: Fix configuration change detection**
  - H9 (SyncService captures `PluginConfiguration` snapshot → runtime changes ignored)
  - H10 (`SyncIntervalHours <= 0` → infinite busy loop)
  - H13 (No retry backoff on transient errors in `SyncService`)
  - Acceptance: Admin panel config changes take effect without restart; sync respects interval; retry with backoff
  - _Files_: `SyncService.cs`, `SyncServiceTests.cs`, `SyncServiceIntegrationTests.cs`

- [ ] **P5-2: Fix persistence consistency**
  - H12 (`SyncLogger.PersistAsync` reads `_history` outside lock)
  - H14 (`DownloadQueue.PersistAsync` non-atomic writes, lock held during IO)
  - H11 (Controller `PersistAsync` failures leave inconsistent in-memory/disk state)
  - `UserDataManager.PersistAsync` no deep copy → concurrent mutation during serialization
  - `UserDataManager.LoadAsync` only catches `JsonException`, not `IOException`
  - Inconsistent `JsonStringEnumConverter` usage across persistence classes
  - Acceptance: All persist operations are atomic and crash-safe; consistent serialization options
  - _Files_: `SyncLogger.cs`, `DownloadQueue.cs`, `UserDataManager.cs`, `MediaCccController.cs`

- [ ] **P5-3: Fix async/await anti-patterns**
  - Fire-and-forget progress update in `DownloadService` (silent exception swallowing)
  - `DownloadService` CancellationTokenSource never disposed
  - `MediaCccController` generic `catch (Exception)` swallows `OperationCanceledException`
  - `FileService` obsolete `ReadAsync(buffer, offset, count)` overloads
  - `SyncService` and `DownloadService` busy-wait loops
  - Acceptance: No fire-and-forget tasks, proper CancellationToken disposal, correct exception routing
  - _Files_: `DownloadService.cs`, `FileService.cs`, `MediaCccController.cs`, `SyncService.cs`

---

## Phase 6 — Test Quality (fix tests before verifying fixes)

- [ ] **P6-1: Fix flaky tests — replace Task.Delay with signals**
  - `SyncServiceTests` — 4 tests using `Task.Delay(100-500ms)`
  - `DownloadProcessingTests` — 3 tests using `Thread.Sleep` / `Task.Delay`
  - `SyncServiceIntegrationTests` — 6 tests using `Task.Delay`
  - Use `TaskCompletionSource<bool>` or `SemaphoreSlim.WaitAsync` for coordination
  - Acceptance: Zero `Task.Delay` or `Thread.Sleep` in test code; tests pass reliably on slow CI

- [ ] **P6-2: Add resource cleanup to all test classes**
  - `StrmGeneratorTests` — temp archive path leak
  - `FileServiceTests` — temp download path leak
  - `SyncLoggerTests` — temp data path leak
  - `UserDataManagerTests` — temp data path leak
  - `DownloadQueueTests` — temp data path leak
  - `StrmTreeGeneratorTests` — hardcoded `/tmp/test_archive` path
  - Implement `IDisposable` / `IAsyncLifetime` with cleanup
  - Acceptance: Zero temp directories remain after test runs

- [ ] **P6-3: Fix trivial/worthless assertions**
  - 4 × `Assert.True(true)` / always-true conditions
  - `DownloadQueueTests` 150 lines of property-setter tests (test nothing useful)
  - `LibrarySetupServiceTests` near-zero coverage (only 3 trivial tests)
  - `ServiceRegistratorTests` string-matching on type names
  - `PluginTests` fragile reflection-based construction
  - Acceptance: Every assertion tests meaningful behavior; deleted tests replaced with real coverage

- [ ] **P6-4: Make SyncServiceIntegrationTests real integration tests**
  - Currently uses `Mock<IMediaCccApiClient>` — it's a "wider unit test," not an integration test
  - Options: test against real test endpoint, use HTTP recording/replay (WireMock), or rename
  - Acceptance: Either real integration tests or honestly renamed to avoid false confidence

---

## Phase 7 — Project Hygiene & Packaging

- [ ] **P7-1: Move interfaces out of implementation files**
  - `ILanguageSelector` → own file
  - `IStrmGenerator` + `StrmResult` → own files
  - `ISyncLogger` + `SyncLogEntry` + `SyncStatus` → own files
  - `IStrmFileGenerator` + `TreeGenerationResult` → own files
  - Acceptance: Each interface/type in its own file, matching project convention

- [ ] **P7-2: Add version & packaging**
  - Add `<Version>1.0.0</Version>` to `.csproj`
  - Add `<GenerateDocumentationFile>true</GenerateDocumentationFile>`
  - Add `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`
  - Create `meta.json` for Jellyfin plugin system
  - Add packaging step to `build.sh` (ZIP with DLL + meta.json)
  - Remove `Jellyfin.Database.Implementations` from test project (unnecessary)
  - Add `coverlet.collector` for code coverage
  - Acceptance: `build.sh release` produces distributable plugin ZIP

- [ ] **P7-3: Code hygiene fixes**
  - Break up `GlobalUsings.cs` — remove Services namespace, keep only truly global usings
  - Fix broken HTML `</</div>` in `sync-log.js`
  - Fix `formatDuration` misnaming + incorrect "Duration" label
  - Consider `.sisyphus/` in `.gitignore` (or explicitly document that it's tracked)
  - Remove stale `TestResults/` from disk
  - Acceptance: Clean build, no dead/residual files on disk

---

## Final Verification Wave

- [ ] **F1: Build & Static Analysis** — `dotnet build --configuration Release` zero warnings, lsp_diagnostics clean across all `.cs` files
- [ ] **F2: Full Test Suite** — All unit + integration tests pass, zero flaky tests, clean test results
- [ ] **F3: Security Audit Re-check** — Verify SSRF protections, path traversal guards, auth on all endpoints
- [ ] **F4: Integration Smoke Test** — Plugin loads in Jellyfin, conferences appear, episode metadata resolves, watchlist works

---

## Task Dependency Graph

```
P0-1 (DI/bootstrap)
 ├── P1-1 (Recording serialization)
 ├── P1-2 (Model validation)
 └── P1-3 (HashSet<UserData>)
      │
      ├── P2-1 (FileService security) ──┐
      ├── P2-2 (UserLibraryService path) ─┤ Parallel
      └── P2-3 (Controller auth) ────────┘
           │
           ├── P3-1 (EpisodeProvider GUID) ──┐
           ├── P3-2 (TriggerSync) ───────────┤ Parallel
           ├── P3-3 (SeriesProvider cache) ──┘
           │
           ├── P4-1 (Strm unification)
           │
           ├── P5-1 (Config detection) ──────┐
           ├── P5-2 (Persistence consistency) ┤ Parallel
           └── P5-3 (Async anti-patterns) ───┘
                │
                ├── P6-1 (Flaky tests) ──────┐
                ├── P6-2 (Test cleanup) ──────┤ Parallel
                ├── P6-3 (Trivial assertions) ┤
                └── P6-4 (Integration tests) ─┘
                     │
                     ├── P7-1 (Interfaces) ──┐
                     ├── P7-2 (Packaging) ────┤ Parallel
                     └── P7-3 (Hygiene) ─────┘
                          │
                          └── F1 → F2 → F3 → F4 (Final Wave)
```

---

## Summary

| Phase | Tasks | CRITICAL Fixed | HIGH Fixed | MEDIUM Fixed |
|-------|-------|---------------|------------|--------------|
| 0 | 1 | 4 | 3 | 1 |
| 1 | 3 | 1 | 0 | 6 |
| 2 | 3 | 3 | 3 | 0 |
| 3 | 3 | 3 | 2 | 3 |
| 4 | 1 | 0 | 2 | 3 |
| 5 | 3 | 0 | 4 | 4 |
| 6 | 4 | 0 | 0 | 5 |
| 7 | 3 | 0 | 0 | 2 |
| **Total** | **21** | **11** | **14** | **24** |
