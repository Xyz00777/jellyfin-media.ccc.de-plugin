# Work Plan: Jellyfin media.ccc.de Plugin

## TL;DR

> **Quick Summary**: Build a Jellyfin plugin integrating media.ccc.de conference recordings with two-library architecture: streaming archive (Conference-as-TV-Series) + per-user download watchlist. Users browse conferences, stream instantly, or add talks to personal offline-ready watchlists.
 
> **Deliverables**: 
> - Jellyfin plugin (.NET 9.0): `Jellyfin.Plugin.MediaCccDe.dll`
> - Archive library: All conferences as TV Shows (.strm streaming)
> - Watchlist system: Per-user downloaded files (offline-capable)
> - Configuration UI: Browse + Manage watchlist + Search progress
> - Background services: Sync + Download queue
> 
> **Estimated Effort**: Large (multi-component plugin with metadata providers, background services, UI)
> **Parallel Execution**: YES - 5 waves (Foundation → Archive → Watchlist → UI → Polish)
> **Critical Path**: API Client → Metadata Providers → Sync Service → Download Service → UI Integration

---

## Context

### Original Request
User wants to browse and watch media.ccc.de videos in Jellyfin with personal organization:
- Browse all conferences (full archive)
- Personal watchlist with offline downloads
- Search progress tracking ("already searched through" markers)
- Multi-user support (separate watchlists)

### Interview Summary

**Key Discussions**:
- **Organization**: Conference = TV Series, Day = Season, Talk = Episode (Jellyfin TV Show model)
- **Libraries**: Two separate libraries - Archive (streaming) + Watchlist (downloaded)
- **Multi-user**: Per-user isolation with UUID-prefixed folders, admin-configurable path
- **Downloads**: Unlimited bandwidth, manual storage management, immediate file deletion on remove
- **Quality**: Global HD/SD setting (admin-configured)
- **Search tracking**: Manual "mark as searched" markers (independent from "played" status)

**Technical Decisions**:
- .strm files for Archive (streaming references to media.ccc.de CDN)
- Actual MP4/WebM downloads for Watchlist (offline-capable)
- Metadata providers: IRemoteMetadataProvider<Series> + IRemoteMetadataProvider<Episode>
- Background services: SyncService (scheduled) + DownloadService (continuous queue)
- Config page: Embedded HTML/JS with tabs (Browse/Watchlist/SearchProgress/Settings)

### Research Findings

**From explore/librarian agents**:
- **Jellyfin Plugin Architecture**: .NET 9.0, BasePlugin<TConfig>, IPluginServiceRegistrator, IHasWebPages
- **Existing similar plugins**: jellyfin-youtube-feed (.strm pattern), jellyfin-plugin-thetvapp (metadata providers)
- **API**: media.ccc.de JSON API stable (api.media.ccc.de/public/), GraphQL available
- **Data structure**: Conferences → Events → Recordings (HD/SD/WebM/audio)
- **Metadata**: Persons (speakers), tags, thumbnails, posters, duration, view count
- **URLs**: Direct CDN links (cdn.media.ccc.de), stable for streaming

### Metis Review

**Identified Gaps** (addressed in plan):

**CRITICAL Questions Researched**:
1. **User Identity in Plugins**: ✅ Jellyfin provides `IUserManager` and HttpContext.User for identity
2. **Library Refresh API**: ✅ `ILibraryManager.QueueLibraryScan()` available
3. **Per-User Storage**: ✅ Plugin manages own JSON files in `/data/plugins/{plugin-guid}/`
4. **URL Stability**: ⚠ ASSUMPTION: URLs are stable. Need validation task to test TTL/auth requirements
5. **Search Progress**: ✅ CONFIRMED: Manual "mark as searched" markers (not browse history)
6. **Conference Sync**: ✅ Full sync every 6h: refresh URLs + detect new events

**Guardrails Applied** (from Metis review):
- NO transcoding logic (Jellyfin handles)
- NO custom player UI (config page MANAGEMENT only)
- NO auto-cleanup (user said "manual only")
- NO metadata editing (read-only from API)
- NO bandwidth throttling (user said "unlimited")
- NO priority queue (FIFO only)
- NO recommendations (browse by conference tree)
- NO thumbnail caching (direct URLs)
- NO external API for watchlists (Jellyfin UI only)
- NO offline mode over-engineering (minimal error handling)

**Edge Cases Covered**:
- Conference with no recordings → Skip in .strm generation, log warning
- Multiple quality options → Use global quality setting for .strm, download picks based on setting
- Interrupted downloads → Resume from byte position on plugin restart
- Disk full → Log error, mark download as failed, show in watchlist UI
- User deleted → Cleanup orphaned folder on next plugin startup
- URL changes → SyncService refreshes .strm files every 6h
- Event removed upstream → .strm breaks, user sees "file not found", manual resync needed
- Add 50 talks at once → FIFO queue processes sequentially
- Watch downloaded then streaming → Separate progress tracking per library
- Missing poster → Use placeholder image from plugin resources

---

## Work Objectives

### Core Objective

Build a production-ready Jellyfin plugin that:
1. Integrates media.ccc.de's full conference archive as a browsable TV Show library
2. Enables per-user personal watchlists with background downloads
3. Provides search progress tracking independent from playback status
4. Operates entirely within Jellyfin (no external dependencies)

### Concrete Deliverables

**Plugin Assembly**: `Jellyfin.Plugin.MediaCccDe.dll`
- Entry point: `Plugin.cs` (BasePlugin<PluginConfiguration>)
- Service registration: `ServiceRegistrator.cs`
- Configuration: `PluginConfiguration.cs` + configPage.html

**API Client**: `MediaCccApi.cs`
- HTTP client for api.media.ccc.de
- DTOs: ConferenceDto, EventDto, RecordingDto
- Methods: GetConferences(), GetEvents(), GetEvent(guid), GetRecent()

**Metadata Providers**:
- `MediaCccSeriesProvider.cs` (IRemoteMetadataProvider<Series>)
- `MediaCccEpisodeProvider.cs` (IRemoteMetadataProvider<Episode>)

**Background Services**:
- `SyncService.cs` (IHostedService): Generate .strm files (every 6h)
- `DownloadService.cs` (IHostedService): Process download queue (continuous)

**Storage Management**:
- `UserDataManager.cs`: Per-user watchlist + search progress JSON
- `FileService.cs`: .strm creation, download management, cleanup

**Configuration UI**:
- `configPage.html` (embedded resource):
  - Tab 1: Browse archive (conference tree + "Add to Watchlist" buttons)
  - Tab 2: My Watchlist (downloads + progress + remove buttons)
  - Tab 3: Search Progress (marked events + mark/unmark)
  - Tab 4: Settings (admin: download path, quality; user: view storage usage)

**Tests**:
- `MediaCccApiTests.cs`: Unit tests with mocked HTTP
- `MetadataProviderTests.cs`: Unit tests for mapping logic
- `DownloadServiceTests.cs`: Unit tests for queue + file management
- `IntegrationTests.cs`: End-to-end with recorded API responses

### Definition of Done

- [ ] All NuGet dependencies version-locked in .csproj
- [ ] Plugin loads in Jellyfin 10.11+ without errors
- [ ] Archive library shows all conferences as TV Shows
- [ ] Metadata (title, description, posters, runtime) displays correctly
- [ ] Streaming playback works for .strm files
- [ ] User A's watchlist invisible to User B
- [ ] Downloads start within 30s of adding to watchlist
- [ ] Download progress visible in config page
- [ ] Removing from watchlist deletes file immediately
- [ ] Search progress markers persist across restarts
- [ ] Unit tests pass: `dotnet test --no-build --verbosity normal` (80%+ coverage)
- [ ] Manual QA: Browse → Add → Download → Watch → Remove flow works
- [ ] Logs: All major actions logged with `[MediaCcc]` prefix

### Must Have

- ✅ All conferences from media.ccc.de API synced as TV Series
- ✅ Two libraries: Archive (streaming) + Watchlist (downloaded)
- ✅ Per-user watchlist with UUID isolation
- ✅ **Per-user language preferences** (audio + subtitle, ordered priority list)
- ✅ Admin-configurable download path
- ✅ Global quality setting (HD/SD)
- ✅ Background download queue with progress tracking
- ✅ **Immediate file deletion on watchlist remove** (automatic cleanup when user manually removes)
- ✅ Search progress markers (manual "mark as searched")
- ✅ **Sync status logging** (admin-visible sync history with success/failure per conference)
- ✅ Configuration page with Browse/Watchlist/Progress/Settings/SyncLog tabs
- ✅ **Language fallback logic** (preferred language → original language if unavailable)

### Must NOT Have (Guardrails)

- ❌ Transcoding/encoding logic (Jellyfin handles)
- ❌ Custom video player UI (config page management only)
- ❌ **Age-based automatic cleanup** (no "delete files older than X days")
- ❌ **Storage-based automatic cleanup** (no "delete when disk 90% full")
- ❌ **Periodic automatic cleanup** (no "cleanup every Sunday at 3am")
- ❌ **User deletion file cleanup** (orphaned files from deleted users - manual only)
- ❌ Metadata editing UI (read-only from API)
- ❌ Download priority queue (FIFO only)
- ❌ Bandwidth throttling (unlimited per user)
- ❌ Per-user quality settings (global admin setting)
- ❌ User permission/role system (all users = same capabilities)
- ❌ Thumbnail caching (use direct URLs)
- ❌ "Smart" recommendations (browse by conference tree only)
- ❌ External API for watchlist management (Jellyfin UI only)
- ❌ Offline mode detection logic (minimal error handling)
- ❌ **Admin language defaults** (per-user only, no global fallback)

---

## Verification Strategy (MANDATORY)

> **ZERO HUMAN INTERVENTION** — ALL verification is agent-executed. No exceptions.
> Acceptance criteria requiring "user manually tests/confirms" are FORBIDDEN.

### Test Decision

- **Infrastructure exists**: NO (new plugin project)
- **Automated tests**: YES (TDD) 
- **Framework**: xUnit + Moq (C# standard)
- **TDD Flow**: Each task follows RED (failing test) → GREEN (minimal impl) → REFACTOR

### QA Policy

Every task MUST include agent-executed QA scenarios (see TODO template below).
Evidence saved to `.sisyphus/evidence/task-{N}-{scenario-slug}.{ext}`.

- **C# Library**: Use `dotnet test` — Run tests, check coverage, assert outputs
- **API Calls**: Use `curl` (Bash) — Send requests to Jellyfin API, assert status + JSON fields
- **UI/Config Page**: Use Playwright — Navigate, click buttons, assert DOM, screenshot
- **Background Services**: Use `Bash` — Check log files, verify file creation, monitor processes

---

## Execution Strategy

### Parallel Execution Waves

> Maximize throughput by grouping independent tasks into parallel waves.
> Each wave completes before the next begins.
> Target: 5-8 tasks per wave. Fewer than 3 per wave (except final) = under-splitting.

```
Wave 1 (Foundation - Start Immediately):
├── Task 1: Project scaffolding + .csproj setup [quick]
├── Task 2: Plugin entry point + configuration [quick]
├── Task 3: Service registrator + DI setup [quick]
├── Task 4: API client - core HTTP wrapper [quick]
├── Task 5: Data models (DTOs for API) [quick]
├── Task 6: User identity context research [quick]
└── Task 7: Directory structure + build verification [quick]

Wave 2 (API Client + Tests - After Wave 1):
├── Task 8: API client - GetConferences() [unspecified-low]
├── Task 9: API client - GetEvents(acronym) [unspecified-low]
├── Task 10: API client - GetEvent(guid) [unspecified-low]
├── Task 11: API client - GetRecent() + Search() [unspecified-low]
├── Task 12: API client tests (mocked HTTP) [unspecified-low]
├── Task 13: Recording URL selection logic [quick]
└── Task 14: API client integration test (recorded responses) [unspecified-high]

Wave 3 (Metadata + Archive - After Wave 2):
├── Task 15: Series metadata provider (scaffold) [unspecified-low]
├── Task 16: Series metadata provider (implementation) [unspecified-high]
├── Task 17: Episode metadata provider (scaffold) [unspecified-low]
├── Task 18: Episode metadata provider (implementation) [unspecified-high]
├── Task 19: Metadata provider tests [unspecified-low]
├── Task 20: .strm file generation logic [quick]
├── Task 21: Sync service - scaffold + scheduling [unspecified-low]
├── Task 22: Sync service - .strm tree generation [unspecified-high]
└── Task 23: Sync service tests [unspecified-low]

Wave 4 (Watchlist + Downloads - After Wave 3):
├── Task 24: User data manager - scaffold [quick]
├── Task 25: User data manager - watchlist JSON [unspecified-low]
├── Task 26: User data manager - search progress [unspecified-low]
├── Task 26.5: Language selection service [quick]
├── Task 27: File service - scaffold [quick]
├── Task 28: File service - download queue [unspecified-low]
├── Task 29: File service - download processing [unspecified-high]
├── Task 30: Download service - scaffold + queue [unspecified-low]
├── Task 31: Download service - processing + progress [unspecified-high]
├── Task 32: Download service - cancellation + cleanup [unspecified-low]
└── Task 33: Download service tests [unspecified-low]

Wave 5 (UI + Integration - After Wave 4):
├── Task 34: Config page HTML - scaffold [visual-engineering]
├── Task 35: Config page - Browse tab (JS logic) [visual-engineering]
├── Task 36: Config page - Watchlist tab + progress [visual-engineering]
├── Task 37: Config page - Search Progress tab [visual-engineering]
├── Task 38: Config page - Settings tab [visual-engineering]
├── Task 38.5: Language preferences UI in Settings tab [visual-engineering]
├── Task 39: API endpoints for config page [unspecified-low]
└── Task 40: End-to-end integration test [unspecified-high]
├── Task 35: Config page - Browse tab (JS logic) [visual-engineering]
├── Task 36: Config page - Watchlist tab + progress [visual-engineering]
├── Task 37: Config page - Search Progress tab [visual-engineering]
├── Task 38: Config page - Settings tab [visual-engineering]
├── Task 39: API endpoints for config page [unspecified-low]
└── Task 40: End-to-end integration test [unspecified-high]

Wave FINAL (Verification - After Wave 5):
├── Task F1: Plan compliance audit (oracle)
├── Task F2: Code quality review (unspecified-high)
├── Task F3: Real manual QA (unspecified-high + playwright)
└── Task F4: Scope fidelity check (deep)
-> Present results -> Get explicit user okay

Critical Path: T1 → T4 → T8 → T15 → T21 → T28 → T30 → T34 → T40 → F1-F4
Parallel Speedup: ~75% faster than sequential
Max Concurrent: 7 (Wave 1 & 2)
```

### Dependency Matrix (abbreviated — show ALL tasks in your generated plan)

- **1-7**: — — 8-14, 1
- **8-14**: 1-7 — 15-23, 2
- **15-23**: 8-14 — 24-33, 3
- **24-33**: 15-23 — 34-40, 4
- **34-40**: 24-33 — F1-F4, 5
- **F1-F4**: 34-40 — —, FINAL

> This is abbreviated for reference. YOUR generated plan must include the FULL matrix for ALL tasks.

### Agent Dispatch Summary

- **1**: **7** — T1-T4 → `quick`, T5-T7 → `quick` (foundation)
- **2**: **7** — T8-T14 → `unspecified-low` / `unspecified-high` (API client)
- **3**: **9** — T15-T23 → `unspecified-low` / `unspecified-high` (metadata + archive)
- **4**: **10** — T24-T33 → `quick` / `unspecified-low` / `unspecified-high` (watchlist)
- **5**: **7** — T34-T40 → `visual-engineering` / `unspecified-low` / `unspecified-high` (UI)
- **FINAL**: **4** — F1 → `oracle`, F2 → `unspecified-high`, F3 → `unspecified-high`, F4 → `deep`

---

## TODOs

> Implementation + Test = ONE Task. Never separate.
> EVERY task MUST have: Recommended Agent Profile + Parallelization info + QA Scenarios.
> **A task WITHOUT QA Scenarios is INCOMPLETE. No exceptions.**

- [ ] 1. **Project Scaffolding and .csproj Setup**

  **What to do**:
  - Create .NET 9.0 class library project: `Jellyfin.Plugin.MediaCccDe.csproj`
  - Add NuGet packages: Jellyfin.Controller (10.11.3), Jellyfin.Model (10.11.3), xUnit, Moq
  - Use `<ExcludeAssets>runtime</ExcludeAssets>` for Jellyfin packages
  - Configure project structure: Plugin.cs, Configuration/, Api/, Services/, Providers/, Models/, Tests/
  - Create .sln file for solution
  - Add .gitignore for .NET projects

  **Must NOT do**:
  - Do NOT pin exact versions (use `10.*` range for Jellyfin packages)
  - Do NOT add unnecessary dependencies (Newtonsoft.Json included in Jellyfin.Model)
  - Do NOT target wrong framework (must be `net9.0` not `net8.0`)

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: Simple project setup, boilerplate .csproj configuration
  - **Skills**: [] (no special skills needed)
  - **Skills Evaluated but Omitted**:
    - `git-master`: Project scaffolding doesn't require git expertise beyond basic commit

  **Parallelization**:
  - **Can Run In Parallel**: YES (with T2-T7)
  - **Parallel Group**: Wave 1 (with Tasks 2-7)
  - **Blocks**: T8-T14 (API client needs project structure)
  - **Blocked By**: None (can start immediately)

  **References**:

  **Pattern References** (existing code to follow):
  - `https://raw.githubusercontent.com/jellyfin/jellyfin-plugin-template/master/Jellyfin.Plugin.Template/Jellyfin.Plugin.Template.csproj` - Correct .csproj structure with ExcludeAssets

  **External References**:
  - Official docs: `https://jellyfin-jellyfin.mintlify.app/development/plugin-development` - Getting started, prerequisites

  **WHY Each Reference Matters**:
  - .csproj template shows proper package reference syntax with ExcludeAssets (critical for plugin loading)
  - Official docs confirm .NET 9.0 requirement and package versioning strategy

  **Acceptance Criteria**:

  **If TDD (tests enabled)**:
  - [ ] Test file created: Tests/ProjectScaffoldingTests.cs
  - [ ] `dotnet build` → SUCCESS (0 errors)
  - [ ] `dotnet test` → PASS (compilation test)

  **QA Scenarios (MANDATORY)**:

  ```
  Scenario: Project builds successfully
    Tool: Bash
    Preconditions: .csproj file exists at project root
    Steps:
      1. cd /opt/syncthing/sync/ncc1031/git/jellyfin_ccc-media-de
      2. dotnet build --configuration Release
      3. Assert exit code = 0
    Expected Result: Build succeeded. 0 Warning(s). 0 Error(s).
    Failure Indicators: Error messages, missing packages, wrong framework
    Evidence: .sisyphus/evidence/task-01-build-success.log

  Scenario: Project targets correct framework
    Tool: Bash
    Preconditions: .csproj exists
    Steps:
      1. grep -o "<TargetFramework>net[0-9.]*</TargetFramework>" Jellyfin.Plugin.MediaCccDe.csproj
      2. Assert output = "<TargetFramework>net9.0</TargetFramework>"
    Expected Result: net9.0 framework targeted
    Failure Indicators: net8.0 or other versions
    Evidence: .sisyphus/evidence/task-01-framework-check.log
  ```

  **Evidence to Capture**:
  - [ ] Build output log
  - [ ] .csproj file content (showing correct packages)

  **Commit**: YES (groups with T1-T7)
  - Message: `feat(scaffold): initial plugin project structure`
  - Files: `Jellyfin.Plugin.MediaCccDe.csproj`, `.gitignore`, `.sln`
  - Pre-commit: `dotnet build`

---

- [ ] 2. **Plugin Entry Point and Configuration**

  **What to do**:
  - Create `Plugin.cs` extending `BasePlugin<PluginConfiguration>`
  - Generate unique GUID (never reuse existing one)
  - Implement `IHasWebPages` for config page route
  - Create `PluginConfiguration.cs` extending `BasePluginConfiguration`
  - Add settings: `WatchlistPath`, `PreferredQuality` (hd/sd), `PreferredFormat` (mp4/webm), `SyncIntervalHours`
  - Add default constructor for DI injection
  - Create `Instance` static property for singleton access

  **Must NOT do**:
  - Do NOT use hardcoded GUID from examples (generate new one)
  - Do NOT make Plugin implement IHostedService (use separate service classes)
  - Do NOT add complex configuration validation here (validate in UI)

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: Standard plugin boilerplate, follows documented pattern
  - **Skills**: []
  - **Skills Evaluated but Omitted**: None applicable

  **Parallelization**:
  - **Can Run In Parallel**: YES (with T1, T3-T7)
  - **Parallel Group**: Wave 1
  - **Blocks**: T3 (service registrator needs Plugin class)
  - **Blocked By**: None

  **References**:

  **Pattern References**:
  - `https://raw.githubusercontent.com/jellyfin/jellyfin-plugin-template/master/Jellyfin.Plugin.Template/Plugin.cs` - Plugin entry point pattern, GUID usage, IHasWebPages implementation
  - `https://raw.githubusercontent.com/jellyfin/jellyfin-plugin-template/master/Jellyfin.Plugin.Template/Configuration/PluginConfiguration.cs` - Configuration model pattern

  **WHY Each Reference Matters**:
  - Plugin.cs shows exact pattern: BasePlugin<T>, constructor with IApplicationPaths + IXmlSerializer, static Instance
  - Configuration shows BasePluginConfiguration inheritance and property declaration pattern

  **Acceptance Criteria**:
  - [ ] Plugin.cs exists with BasePlugin<PluginConfiguration>
  - [ ] Unique GUID generated (not from template)
  - [ ] PluginConfiguration has: WatchlistPath, PreferredQuality, PreferredFormat, SyncIntervalHours
  - [ ] `dotnet build` → SUCCESS

  **QA Scenarios**:

  ```
  Scenario: Plugin has unique GUID
    Tool: Bash
    Preconditions: Plugin.cs exists
    Steps:
      1. grep "public override Guid Id" Plugin.cs
      2. Extract GUID value
      3. Assert GUID != "4a5b6c7d-8e9f-0a1b-2c3d-4e5f6a7b8c9d" (template GUID)
      4. Assert GUID format correct: [0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}
    Expected Result: Unique valid GUID
    Failure Indicators: Template GUID reused, invalid format
    Evidence: .sisyphus/evidence/task-02-guid-check.log

  Scenario: Configuration properties exist
    Tool: Bash
    Preconditions: PluginConfiguration.cs exists
    Steps:
      1. grep "public string WatchlistPath" PluginConfiguration.cs
      2. grep "public string PreferredQuality" PluginConfiguration.cs
      3. Assert both lines found
    Expected Result: All required properties present
    Failure Indicators: Missing properties
    Evidence: .sisyphus/evidence/task-02-config-properties.log
  ```

  **Commit**: YES (part of Wave 1 commit)
  - Message: Included in T1 commit
  - Files: `Plugin.cs`, `PluginConfiguration.cs`

---

- [ ] 3. **Service Registrator and DI Setup**

  **What to do**:
  - Create `ServiceRegistrator.cs` implementing `IPluginServiceRegistrator`
  - Register services in DI container:
    - `MediaCccApi` as singleton
    - `SyncService` as IHostedService (background scheduled task)
    - `DownloadService` as IHostedService (continuous queue processor)
    - `UserDataManager` as singleton
    - `FileService` as singleton
  - Add HttpClient registration with base address: `https://api.media.ccc.de`
  - Configure HttpClient timeout: 30 seconds

  **Must NOT do**:
  - Do NOT register Plugin class itself as service (it's created by Jellyfin core)
  - Do NOT register controllers/services that don't exist yet (add placeholder comments)

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: Standard DI registration pattern, minimal logic
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: YES (with T1-T2, T4-T7)
  - **Parallel Group**: Wave 1
  - **Blocks**: T8 (API client needs MediaCccApi registration)
  - **Blocked By**: T2 (needs Plugin class to confirm project structure)

  **References**:
  - `https://raw.githubusercontent.com/BingleP/jellyfin-youtube-feed/master/ServiceRegistrator.cs` - DI registration pattern, AddHostedService usage
  - `https://github.com/jellyfin/jellyfin/blob/master/MediaBrowser.Controller/Plugins/IPluginServiceRegistrator.cs` - Interface definition

  **Acceptance Criteria**:
  - [ ] ServiceRegistrator.cs exists with IPluginServiceRegistrator
  - [ ] HttpClient registered with base address
  - [ ] Placeholder comments for services not yet implemented
  - [ ] `dotnet build` → SUCCESS

  **QA Scenarios**:
  ```
  Scenario: ServiceRegistrator exists and compiles
    Tool: Bash
    Steps:
      1. test -f ServiceRegistrator.cs && echo "File exists"
      2. dotnet build --no-restore
      3. Assert exit code = 0
    Expected Result: File exists and builds
    Evidence: .sisyphus/evidence/task-03-service-registrator.log
  ```

  **Commit**: Part of Wave 1 commit

---

- [ ] 4. **API Client - Core HTTP Wrapper**

  **What to do**:
  - Create `Api/MediaCccApi.cs`
  - Implement constructor: Inject `IHttpClientFactory`, logger
  - Create base method: `GetJsonAsync<T>(string endpoint)` with:
    - HTTP GET request
    - JSON deserialization (using System.Text.Json or Newtonsoft.Json)
    - Error handling: Log non-success status codes
    - Cancellation token support
  - Configure retry policy: 3 retries with exponential backoff (Polly or manual)
  - Add logging: Request start, response received, errors

  **Must NOT do**:
  - Do NOT hardcode base URL (use from HttpClient registration in T3)
  - Do NOT add API-specific methods yet (only generic GetJsonAsync)
  - Do NOT sync-over-async (use async/await properly)

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: Standard HTTP wrapper pattern, minimal business logic
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: YES (with T1-T3, T5-T7)
  - **Parallel Group**: Wave 1
  - **Blocks**: T8-T11 (API methods use this wrapper)
  - **Blocked By**: T3 (needs HttpClient registration)

  **References**:
  - Jellyfin HTTP pattern: `IHttpClientFactory.CreateClient()` usage in existing plugins
  - System.Text.Json docs: `JsonSerializer.DeserializeAsync<T>()` pattern

  **Acceptance Criteria**:
  - [ ] MediaCccApi.cs exists
  - [ ] GetJsonAsync<T> method implemented
  - [ ] Error handling + logging present
  - [ ] `dotnet build` → SUCCESS

  **QA Scenarios**:
  ```
  Scenario: HTTP wrapper compiles
    Tool: Bash
    Steps:
      1. grep "GetJsonAsync<T>" Api/MediaCccApi.cs
      2. dotnet build
      3. Assert exit code = 0
    Expected Result: Method exists and compiles
    Evidence: .sisyphus/evidence/task-04-http-wrapper.log
  ```

  **Commit**: Part of Wave 1 commit

---

- [ ] 5. **Data Models (DTOs for API)**

  **What to do**:
  - Create `Models/ConferenceDto.cs`:
    - Properties: Acronym, Title, Slug, AspectRatio, ScheduleUrl, Link, Description, LogoUrl, ImagesUrl, RecordingsUrl, WebgenLocation, EventLastReleasedAt, UpdatedAt
  - Create `Models/EventDto.cs`:
    - Properties: Guid, Title, Subtitle, Slug, Link, Description, OriginalLanguage, Persons (array), Tags (array), ViewCount, Promoted, Date, ReleaseDate, Length, Duration, ThumbUrl, PosterUrl, TimelineUrl, ThumbnailsUrl, FrontendLink
  - Create `Models/RecordingDto.cs`:
    - Properties: Size, Length, MimeType, Language, Filename, Folder, HighQuality, Width, Height, State, RecordingUrl, UpdatedAt
  - Create `Models/ApiResponse.cs`:
    - Wrapper for paginated responses if needed
  - Use System.Text.Json attributes: `[JsonPropertyName("acronym")]` for snake_case mapping

  **Must NOT do**:
  - Do NOT add business logic to DTOs (they're simple data containers)
  - Do NOT use different property names than API returns (match snake_case exactly except PascalCase conversion)
  - Do NOT add validation attributes (validate in service layer)

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: Simple data models, straightforward mapping
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: YES (with T1-T4, T6-T7)
  - **Parallel Group**: Wave 1
  - **Blocks**: T8-T11 (API methods return these DTOs)
  - **Blocked By**: None

  **References**:
  - media.ccc.de API response: `https://api.media.ccc.de/public/conferences` - Shows exact JSON structure
  - media.ccc.de API response: `https://api.media.ccc.de/public/events/50757de3-a863-59fa-9cb0-479951b1a288` - Event structure

  **Acceptance Criteria**:
  - [ ] All DTOs created with correct property names
  - [ ] JsonPropertyName attributes applied
  - [ ] `dotnet build` → SUCCESS

  **QA Scenarios**:
  ```
  Scenario: DTOs serialize/deserialize correctly
    Tool: Bash
    Steps:
      1. Create test: Models/DtoTests.cs
      2. Test JSON roundtrip: ConferenceDto → JSON → ConferenceDto
      3. dotnet test --filter "FullyQualifiedName~DtoTests"
      4. Assert all tests pass
    Expected Result: Roundtrip serialization works
    Evidence: .sisyphus/evidence/task-05-dto-serialization.log
  ```

  **Commit**: Part of Wave 1 commit

---

- [ ] 6. **User Identity Context Research**

  **What to do**:
  - Research: How Jellyfin plugins access current user identity in config page requests
  - Check: `IUserManager`, `HttpContext.User`, `IAuthorizationContext`
  - Document findings in code comments or markdown
  - Create helper method: `GetCurrentUserId(HttpContext)` or equivalent
  - Test: Can plugin determine user ID from HTTP request context?
  - Result: Document approach for T24-T33 (watchlist service) to use

  **Must NOT do**:
  - Do NOT implement full authentication (use Jellyfin's existing system)
  - Do NOT hardcode user IDs (must be dynamic per request)

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: Research task, documentation-focused
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: YES (with T1-T5, T7)
  - **Parallel Group**: Wave 1
  - **Blocks**: T24 (needs user identity approach)
  - **Blocked By**: None

  **References**:
  - Jellyfin source: `MediaBrowser.Controller/Net/IAuthorizationContext.cs`
  - Existing plugins: Check how other plugins handle user context

  **Acceptance Criteria**:
  - [ ] Research documented in code comments or markdown
  - [ ] Helper method created (even if placeholder)
  - [ ] Approach confirmed for user identity access

  **QA Scenarios**:
  ```
  Scenario: Research documented
    Tool: Bash
    Steps:
      1. grep -r "user identity" --include="*.md" --include="*.cs"
      2. Assert documentation found
    Expected Result: Approach documented
    Evidence: .sisyphus/evidence/task-06-user-identity-research.md
  ```

  **Commit**: Part of Wave 1 commit

---

- [ ] 7. **Directory Structure and Build Verification**

  **What to do**:
  - Create directory structure: `Api/`, `Models/`, `Services/`, `Providers/`, `Configuration/`, `Tests/`
  - Verify build output includes all dependencies
  - Create placeholder files for upcoming tasks (empty classes with TODOs)
  - Setup test project structure: `Tests/Unit/`, `Tests/Integration/`
  - Verify plugin can be loaded in Jellyfin (even if non-functional)
  - Check: .csproj output settings (`<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>`)

  **Must NOT do**:
  - Do NOT add actual implementations yet (just placeholders)
  - Do NOT skip build verification (critical foundation)

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: File organization, build verification
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: YES (with T1-T6)
  - **Parallel Group**: Wave 1
  - **Blocks**: T8-T14 (needs structure)
  - **Blocked By**: None

  **References**:
  - Template structure: `https://github.com/jellyfin/jellyfin-plugin-template/tree/master/Jellyfin.Plugin.Template`

  **Acceptance Criteria**:
  - [ ] Directory structure matches plan
  - [ ] `dotnet build --configuration Release` → SUCCESS
  - [ ] Output DLL: `bin/Release/net9.0/Jellyfin.Plugin.MediaCccDe.dll`

  **QA Scenarios**:
  ```
  Scenario: Directory structure correct
    Tool: Bash
    Steps:
      1. test -d Api && test -d Models && test -d Services && test -d Providers && test -d Configuration && test -d Tests
      2. Assert all directories exist
    Expected Result: All directories present
    Evidence: .sisyphus/evidence/task-07-directory-structure.log

  Scenario: Build output correct
    Tool: Bash
    Steps:
      1. dotnet build --configuration Release --no-incremental
      2. test -f bin/Release/net9.0/Jellyfin.Plugin.MediaCccDe.dll
      3. Assert file size > 0
    Expected Result: DLL generated
    Evidence: .sisyphus/evidence/task-07-build-output.log
  ```

  **Commit**: YES (final Wave 1 commit)
  - Message: `feat(scaffold): initial plugin project structure`
  - Files: All Wave 1 files
  - Pre-commit: `dotnet build && dotnet test`

---

## Final Verification Wave (MANDATORY — after ALL implementation tasks)

> 4 review agents run in PARALLEL. ALL must APPROVE. Present consolidated results to user and get explicit "okay" before completing.

- [ ] F1. **Plan Compliance Audit** — `oracle`
  Read the plan end-to-end. For each "Must Have": verify implementation exists (read file, curl endpoint, run command). For each "Must NOT Have": search codebase for forbidden patterns — reject with file:line if found. Check evidence files exist in .sisyphus/evidence/. Compare deliverables against plan.
  Output: `Must Have [N/N] | Must NOT Have [N/N] | Tasks [N/N] | VERDICT: APPROVE/REJECT`

- [ ] F2. **Code Quality Review** — `unspecified-high`
  Run `dotnet build` + `dotnet test`. Review all changed files for: `as any`/`@ts-ignore` equivalents, empty catches, console.log in prod, commented-out code, unused imports. Check AI slop: excessive comments, over-abstraction, generic names.
  Output: `Build [PASS/FAIL] | Tests [N pass/N fail] | Files [N clean/N issues] | VERDICT`

- [ ] F3. **Real Manual QA** — `unspecified-high` (+ `playwright` skill)
  Start from clean Jellyfin instance. Execute EVERY QA scenario from EVERY task — follow exact steps, capture evidence. Test cross-task integration (browse → add to watchlist → download → watch → remove). Test edge cases: disk full, network fail, multi-user. Save to `.sisyphus/evidence/final-qa/`.
  Output: `Scenarios [N/N pass] | Integration [N/N] | Edge Cases [N tested] | VERDICT`

- [ ] F4. **Scope Fidelity Check** — `deep`
  For each task: read "What to do", read actual diff (git log/diff). Verify 1:1 — everything in spec was built (no missing), nothing beyond spec was built (no creep). Check "Must NOT do" compliance. Detect cross-task contamination: Task N touching Task M's unauthorized files. Flag unaccounted changes.
  Output: `Tasks [N/N compliant] | Contamination [CLEAN/N issues] | Unaccounted [CLEAN/N files] | VERDICT`

---

## Commit Strategy

- **1 (Wave 1)**: `feat(scaffold): initial plugin project structure` — Jellyfin.Plugin.MediaCccDe.csproj, solution files
- **2 (Wave 2)**: `feat(api): add media.ccc.de API client` — MediaCccApi.cs, DTOs, tests
- **3 (Wave 3)**: `feat(metadata): add series and episode providers` — MetadataProviders, tests
- **4 (Wave 3)**: `feat(sync): add archive sync service` — SyncService, .strm generation, tests
- **5 (Wave 4)**: `feat(watchlist): add user data management` — UserDataManager, FileService, tests
- **6 (Wave 4)**: `feat(download): add download service` — DownloadService, queue, tests
- **7 (Wave 5)**: `feat(ui): add configuration page` — configPage.html, JS, API endpoints
- **8 (FINAL)**: `feat(integration): end-to-end integration` — Integration tests, final polish

---

## Success Criteria

### Verification Commands
```bash
# 1. Build succeeds
dotnet build --configuration Release
# Expected: Build succeeded. 0 Warning(s). 0 Error(s).

# 2. Tests pass with coverage
dotnet test --configuration Release --collect:"XPlat Code Coverage"
# Expected: Total tests: N. Passed: N. Failed: 0. Skipped: 0. Coverage: 80%+

# 3. Plugin loads in Jellyfin
cp bin/Release/net9.0/Jellyfin.Plugin.MediaCccDe.dll /path/to/jellyfin/plugins/MediaCccDe/
# Expected: No errors in Jellyfin logs, plugin appears in dashboard

# 4. Archive library shows conferences
curl "http://localhost:8096/Items?ParentId=<archive-lib-id>&api_key=<key>" | jq '.Items[].Name'
# Expected: ["37C3 - Chaos Communication Congress", "36C3 - ...", "eh23 - EasterHegg 2023", ...]

# 5. Episode metadata fetched
curl "http://localhost:8096/Users/<user-id>/Items/<episode-id>?api_key=<key>" | jq '.Name, .Overview, .RunTimeTicks'
# Expected: {"Name": "Opening Ceremony", "Overview": "...", "RunTimeTicks": 123456789}

# 6. Watchlist isolation (User A vs B)
curl "http://localhost:8096/Users/<user-a-id>/Items?ParentId=<watchlist-lib-id>" | jq '.Items | length'
# Expected: 3 (User A has 3 items)
curl "http://localhost:8096/Users/<user-b-id>/Items?ParentId=<watchlist-lib-id>" | jq '.Items | length'
# Expected: 0 (User B sees none of User A's items)

# 7. Download completes and file exists
ls -lh /config/plugins/ccc-media/watchlists/<user-guid>/opening-ceremony.mp4
# Expected: -rw-r--r-- 1 jellyfin jellyfin 650M ... opening-ceremony.mp4

# 8. Remove from watchlist deletes file
# Trigger via config page: Click "Remove" button
ls -lh /config/plugins/ccc-media/watchlists/<user-guid>/opening-ceremony.mp4
# Expected: ls: cannot access: No such file or directory

# 9. Logs show sync events
grep "\[MediaCcc\]" /var/log/jellyfin/log*
# Expected: [MediaCcc] Sync started..., [MediaCcc] Created 245 .strm files..., [MediaCcc] Sync completed in 12s

# 10. Search progress persisted
cat /config/plugins/ccc-media/data/user-<guid>.json | jq '.searched[] | .eventGuid'
# Expected: ["50757de3-a863-59fa-9cb0-479951b1a288", ...]
```

### Final Checklist
- [ ] All "Must Have" present
- [ ] All "Must NOT Have" absent
- [ ] All tests pass (dotnet test)
- [ ] Coverage = 100%
- [ ] Plugin loads without errors
- [ ] Archive library displays all conferences
- [ ] Metadata displays correctly (titles, descriptions, posters)
- [ ] Streaming playback works
- [ ] Multi-user isolation works (User A ≠ User B)
- [ ] Downloads start + complete + progress visible
- [ ] File deletion on remove works
- [ ] Search progress persists
- [ ] Config page UI functional
- [ ] Logs have [MediaCcc] prefix
- [ ] No AI slop patterns (checked by F2)
- [ ] No scope creep (checked by F4)
---

### Wave 2: API Client + Tests

- [ ] 8. **API Client - GetConferences()**

  **What to do**:
  - Add method to MediaCccApi: `Task<List<ConferenceDto>> GetConferences(CancellationToken cancellationToken)`
  - Call: `https://api.media.ccc.de/public/conferences`
  - Deserialize JSON response to `List<ConferenceDto>`
  - Log: Number of conferences retrieved
  - Handle: Empty response, API errors, network timeouts
  - Add unit test with mocked HTTP response

  **Must NOT do**:
  - Do NOT cache results (caching is separate concern)
  - Do NOT filter conferences (return all from API)
  - Do NOT add conference-specific logic (just fetch and return)

  **Recommended Agent Profile**: `unspecified-low` - Standard API method, straightforward implementation
  **Skills**: []
  **Parallelization**: Wave 2, parallel with T9-T14, blocks T15-23
  **References**: `https://api.media.ccc.de/public/conferences` shows response format
  **Acceptance Criteria**: Method returns List<ConferenceDto>, unit test passes
  **QA Scenarios**:
  ```
  Scenario: GetConferences returns valid data
    Tool: xUnit test
    Steps:
      1. Mock HTTP response with sample conferences JSON
      2. Call GetConferences()
      3. Assert result.Count > 0
      4. Assert all conferences have non-empty Acronym and Title
    Expected Result: 300+ conferences returned, all valid
    Evidence: .sisyphus/evidence/task-08-getconferences-test.log
  ```
  **Commit**: Part of Wave 2 commit

---

- [ ] 9. **API Client - GetEvents(acronym)**

  **What to do**:
  - Add method: `Task<List<EventDto>> GetEvents(string conferenceAcronym, CancellationToken cancellationToken)`
  - Call: `https://api.media.ccc.de/public/conferences/{acronym}/events`
  - Handle: Conference not found (404), return empty list
  - Deserialize to `List<EventDto>`
  - Add unit test

  **Recommended Agent Profile**: `unspecified-low`
  **Skills**: []
  **Parallelization**: Wave 2, parallel with T8, T10-T14
  **References**: `https://api.media.ccc.de/public/conferences/37c3/events`
  **Acceptance Criteria**: Method returns events for given conference
  **QA Scenarios**:
  ```
  Scenario: GetEvents returns events for conference
    Tool: xUnit test
    Steps:
      1. Mock HTTP response with events JSON
      2. Call GetEvents("37c3")
      3. Assert result contains events
      4. Assert all events have Guid, Title, Length
  ```
  **Commit**: Part of Wave 2 commit

---

- [ ] 10. **API Client - GetEvent(guid)**

  **What to do**:
  - Add method: `Task<EventDto> GetEvent(Guid eventGuid, CancellationToken cancellationToken)`
  - Call: `https://api.media.ccc.de/public/events/{guid}`
  - Handle: Event not found (404), return null or throw custom exception
  - Include recordings array in response
  - Add unit test

  **Recommended Agent Profile**: `unspecified-low`
  **Skills**: []
  **Parallelization**: Wave 2
  **References**: Event response includes recordings array
  **Acceptance Criteria**: Method returns EventDto with recordings populated
  **QA Scenarios**:
  ```
  Scenario: GetEvent returns event details
    Tool: xUnit test
    Steps:
      1. Mock HTTP with specific event GUID
      2. Call GetEvent(guid)
      3. Assert event.Guid matches
      4. Assert event.Recordings not empty
  ```
  **Commit**: Part of Wave 2 commit

---

- [ ] 11. **API Client - GetRecent() + Search()**

  **What to do**:
  - Add method: `Task<List<EventDto>> GetRecent(CancellationToken cancellationToken)` - last 100 events
  - Call: `https://api.media.ccc.de/public/events/recent`
  - Add method: `Task<List<EventDto>> Search(string query, CancellationToken cancellationToken)`
  - Call: `https://api.media.ccc.de/public/events/search?q={query}`
  - Add unit tests for both

  **Recommended Agent Profile**: `unspecified-low`
  **Skills**: []
  **Parallelization**: Wave 2
  **References**: Recent: `/public/events/recent`, Search: `/public/events/search?q=`
  **Acceptance Criteria**: Both methods return events, search accepts query parameter
  **QA Scenarios**:
  ```
  Scenario: GetRecent returns recent events
    Tool: xUnit test
    Steps: Mock recent response, call GetRecent(), assert result.Count <= 100
  Scenario: Search returns matching events
    Tool: xUnit test
    Steps: Mock search response for "security", call Search("security"), assert results contain query
  ```
  **Commit**: Part of Wave 2 commit

---

- [ ] 12. **API Client Tests (Mocked HTTP)**

  **What to do**:
  - Create `Tests/Unit/MediaCccApiTests.cs`
  - Use Moq to mock `IHttpClientFactory` and `HttpMessageHandler`
  - Test all methods: GetConferences, GetEvents, GetEvent, GetRecent, Search
  - Test error handling: 404, 500, timeout
  - Test retry logic: Fail first request, succeed second
  - Achieve 80% code coverage on MediaCccApi class

  **Recommended Agent Profile**: `unspecified-low` - Standard unit testing
  **Skills**: []
  **Parallelization**: Wave 2
  **References**: xUnit + Moq patterns for HttpClient mocking
  **Acceptance Criteria**: All tests pass, coverage ≥ 80%
  **QA Scenarios**:
  ```
  Scenario: All API methods tested
    Tool: Bash
    Steps: dotnet test --filter "FullyQualifiedName~MediaCccApiTests" --collect:"XPlat Code Coverage", Assert all pass
  Scenario: Error handling tested
    Tool: xUnit test
    Steps: Mock 500 response, call GetConferences(), assert exception thrown or empty result
  ```
  **Commit**: Part of Wave 2 commit

---

- [ ] 13. **Recording URL Selection Logic**

  **What to do**:
  - Create `Services/RecordingSelector.cs`
  - Implement method: `RecordingDto SelectBestRecording(EventDto event, string quality, string format, List<string> preferredLanguages, string languageType)`
    - Quality: "hd" (prefer high_quality=true) or "sd"
    - Format: "mp4" (mime_type: video/mp4) or "webm" (mime_type: video/webm)
    - PreferredLanguages: Ordered list like ["en", "de"] (user preference)
    - LanguageType: "audio" or "subtitle"
  - Logic:
    - Filter recordings by format
    - Filter by quality preference
    - **Select language in priority order**:
      - Iterate preferredLanguages list
      - Match recording.Language or check recording.Translated flag
      - For audio: Prefer user's first preferred language, then second, etc.
      - If no match: Use recording with `"original"` language
    - Select highest resolution matching all criteria
    - Fallback if exact combination not available
  - Language code mapping: Use ISO 639-1 codes (en, de, es, etc.)
  - Add unit tests for selection logic including language fallback

  **Recommended Agent Profile**: `quick` - Selection logic with priority matching
  **Skills**: []
  **Parallelization**: Wave 2
  **References**: Recording has Language, Translated fields. Event.OriginalLanguage for fallback.
  **Acceptance Criteria**: Selects correct recording based on quality, format, and language preferences. Falls back to original language if preferred unavailable.
  **QA Scenarios**:
  ```
  Scenario: HD MP4 with preferred language selected
    Tool: xUnit test
    Steps: Create event with multiple recordings (en/hd/mp4, de/hd/mp4, es/hd/mp4), call SelectBestRecording(event, "hd", "mp4", ["en", "de"], "audio"), assert en recording selected
  Scenario: Fallback to second preferred language
    Tool: xUnit test
    Steps: Create event with de/hd/mp4 and es/hd/mp4 (no en), call SelectBestRecording(event, "hd", "mp4", ["en", "de"], "audio"), assert de recording selected
  Scenario: Fallback to original language
    Tool: xUnit test
    Steps: Create event with recordings in es/hd/mp4 only (user prefers en, de), call SelectBestRecording(event, "hd", "mp4", ["en", "de"], "audio"), assert original language recording selected
  Scenario: SD fallback when HD unavailable
    Tool: xUnit test
    Steps: Create event with only SD/MP4, call SelectBestRecording(event, "hd", "mp4", ["en"], "audio"), assert SD selected
  ```
  **Commit**: Part of Wave 2 commit

---

- [ ] 14. **API Client Integration Test (Recorded Responses)**

  **What to do**:
  - Create `Tests/Integration/MediaCccApiIntegrationTests.cs`
  - Record real API responses (JSON files in `Tests/Data/`):
    - `conferences.json` - Actual /public/conferences response
    - `events-37c3.json` - Actual /public/conferences/37c3/events response
    - `event-guid.json` - Actual /public/events/{guid} response
  - Use recorded responses instead of hitting real API
  - Test end-to-end deserialization
  - Verify all DTO properties map correctly

  **Recommended Agent Profile**: `unspecified-high` - Requires real API interaction
  **Skills**: []
  **Parallelization**: Wave 2
  **References**: Use curl to fetch real responses
  **Acceptance Criteria**: Integration tests pass with recorded data
  **QA Scenarios**:
  ```
  Scenario: Integration test with real response structure
    Tool: Bash
    Steps: curl https://api.media.ccc.de/public/conferences > Tests/Data/conferences.json, run integration tests, assert all properties deserialize
  ```
  **Commit**: YES (separate commit for integration tests)
  - Message: `test(api): add integration tests with recorded responses`
  - Files: `Tests/Integration/`, `Tests/Data/`


---

### Wave 3: Metadata Providers + Archive Sync

- [ ] 15. **Series Metadata Provider (Scaffold)**

  **What to do**:
  - Create `Providers/MediaCccSeriesProvider.cs`
  - Implement `IRemoteMetadataProvider<Series, SeriesInfo>`
  - Add required properties: `Name`, `SupportedFields`
  - Scaffold methods: `GetMetadata()`, `GetSearchResults()`
  - Inject MediaCccApi dependency via constructor
  - Add placeholder returns (will implement in T16)
  - Register in ServiceRegistrator

  **Must NOT do**:
  - Do NOT implement full logic yet (just scaffold)
  - Do NOT add custom fields (use standard Jellyfin Series fields)

  **Recommended Agent Profile**: `unspecified-low` - Provider scaffold following pattern
  **Skills**: []
  **Parallelization**: Wave 3, parallel with T16-T23
  **References**: Jellyfin IRemoteMetadataProvider interface, existing series providers
  **Acceptance Criteria**: Provider compiles, implements interface correctly
  **QA Scenarios**:
  ```
  Scenario: Series provider implements interface
    Tool: Bash
    Steps: grep "IRemoteMetadataProvider<Series" Providers/MediaCccSeriesProvider.cs, assert found
  Scenario: Provider registered in DI
    Tool: Bash
    Steps: grep "MediaCccSeriesProvider" ServiceRegistrator.cs, assert found
  ```
  **Commit**: Part of Wave 3 commit

---

- [ ] 16. **Series Metadata Provider (Implementation)**

  **What to do**:
  - Implement `GetMetadata(SeriesInfo info, CancellationToken)`
  - Match conference by folder name (info.Name or info.Path)
  - Call MediaCccApi.GetConferences() to find matching conference
  - Map ConferenceDto → Series:
    - `Title` → `Name`
    - `Description` → `Overview`
    - `LogoUrl` → `ImageUrl` (poster)
    - `ImagesUrl` → `BackdropImageUrl` (fanart)
    - `Acronym` → `SortName`
  - Create `MetadataResult<Series>` with Item and HasMetadata=true
  - Handle: Conference not found (return empty result)
  - Add unit test with mocked API

  **Must NOT do**:
  - Do NOT fetch images (return URLs only, Jellyfin downloads)
  - Do NOT add custom metadata fields beyond standard Series properties

  **Recommended Agent Profile**: `unspecified-high` - Core business logic for metadata mapping
  **Skills**: []
  **Parallelization**: Wave 3, depends on T15 scaffold
  **References**: ConferenceDto fields, Series properties in Jellyfin.Model
  **Acceptance Criteria**: Maps conference to series correctly, handles missing conference
  **QA Scenarios**:
  ```
  Scenario: Series metadata fetched for 37c3
    Tool: xUnit test
    Steps:
      1. Create SeriesInfo with Name="37c3"
      2. Mock GetConferences() to return 37c3 conference
      3. Call GetMetadata()
      4. Assert result.Item.Name == "37C3 - Chaos Communication Congress"
      5. Assert result.Item.ImageUrl contains logo
  Scenario: Missing conference returns empty
    Tool: xUnit test
    Steps: Create SeriesInfo with unknown name, call GetMetadata(), assert HasMetadata=false
  ```
  **Commit**: Part of Wave 3 commit

---

- [ ] 17. **Episode Metadata Provider (Scaffold)**

  **What to do**:
  - Create `Providers/MediaCccEpisodeProvider.cs`
  - Implement `IRemoteMetadataProvider<Episode, EpisodeInfo>`
  - Scaffold methods similar to T15
  - Register in ServiceRegistrator

  **Recommended Agent Profile**: `unspecified-low`
  **Skills**: []
  **Parallelization**: Wave 3, parallel with T15-T16, T18-T23
  **References**: Episode provider pattern from existing plugins
  **Acceptance Criteria**: Provider compiles, implements interface
  **QA Scenarios**:
  ```
  Scenario: Episode provider implements interface
    Tool: Bash
    Steps: grep "IRemoteMetadataProvider<Episode" Providers/MediaCccEpisodeProvider.cs
  ```
  **Commit**: Part of Wave 3 commit

---

- [ ] 18. **Episode Metadata Provider (Implementation)**

  **What to do**:
  - Implement `GetMetadata(EpisodeInfo info, CancellationToken)`
  - Match event by .strm filename pattern: `{conference}-S{season}E{episode}-{slug}.strm`
  - Alternative: Extract event GUID from .strm file content or metadata
  - Call MediaCccApi.GetEvent(guid)
  - Map EventDto → Episode:
    - `Title` → `Name`
    - `Description` → `Overview`
    - `Persons` → `Writers` (speakers)
    - `Date` → `PremiereDate`
    - `Length` (seconds) → `RunTimeTicks` (TimeSpan conversion)
    - `ThumbUrl` → `ImageUrl` (episode thumbnail)
    - `Tags` → `Genres` or `Tags` collection
  - Derive Season/Episode numbers from event metadata or file naming
  - Handle: Event not found
  - Add unit tests

  **Recommended Agent Profile**: `unspecified-high` - Complex mapping with season/episode logic
  **Skills**: []
  **Parallelization**: Wave 3, depends on T17 scaffold
  **References**: EventDto structure, Episode properties, season/episode extraction
  **Acceptance Criteria**: Maps event to episode, derives season/episode numbers correctly
  **QA Scenarios**:
  ```
  Scenario: Episode metadata fetched from .strm filename
    Tool: xUnit test
    Steps:
      1. Create EpisodeInfo with path="37c3/S01/37c3 - S01E02 - Bahnmining.strm"
      2. Mock GetEvent() with event GUID extracted
      3. Call GetMetadata()
      4. Assert result.Item.Name == "Bahnmining - Punktlichkeit ist eine Zier"
      5. Assert result.Item.Writers contains speaker names
      6. Assert result.Item.RunTimeTicks matches event.Length in ticks
  ```
  **Commit**: Part of Wave 3 commit

---

- [ ] 19. **Metadata Provider Tests**

  **What to do**:
  - Create `Tests/Unit/MetadataProvidersTests.cs`
  - Test SeriesProvider: conference matching, metadata mapping, missing conference
  - Test EpisodeProvider: filename parsing, event matching, season/episode derivation, missing event
  - Test edge cases: Conference with no events, Event with no recordings, Missing poster
  - Achieve 80% coverage on both providers
  - Use mocked MediaCccApi

  **Recommended Agent Profile**: `unspecified-low` - Standard unit testing
  **Skills**: []
  **Parallelization**: Wave 3, after T16 and T18 complete
  **References**: xUnit + Moq patterns
  **Acceptance Criteria**: All tests pass, coverage ≥ 80%
  **QA Scenarios**:
  ```
  Scenario: Provider coverage meets threshold
    Tool: Bash
    Steps: dotnet test --collect:"XPlat Code Coverage", grep "MediaCccSeriesProvider" coverage report, assert ≥ 80%
  ```
  **Commit**: Part of Wave 3 commit

---

- [ ] 20. **.strm File Generation Logic**

  **What to do**:
  - Create `Services/StrmGenerator.cs`
  - Implement method: `void GenerateStrmFile(string conferenceAcronym, int dayNumber, int episodeNumber, string eventSlug, string recordingUrl, string outputPath)`
  - Generate filename: `{conferenceAcronym} - S{day:D2}E{episode:D2} - {eventSlug}.strm`
  - Write recordingUrl as file content (plain text)
  - Create directory structure: `{conferenceAcronym}/Season {day:D2}/`
  - Handle: Directory creation, file write errors
  - Add unit tests: Verify filename format, file content

  **Must NOT do**:
  - Do NOT add metadata to .strm file (just URL)
  - Do NOT overwrite existing files without checking
  - Do NOT create directories outside plugin scope

  **Recommended Agent Profile**: `quick` - Simple file generation
  **Skills**: []
  **Parallelization**: Wave 3, parallel with T15-T19, T21-T23
  **References**: .strm file format (plain text with URL)
  **Acceptance Criteria**: Generates correct .strm files with proper naming
  **QA Scenarios**:
  ```
  Scenario: .strm file generated correctly
    Tool: xUnit test
    Steps:
      1. Call GenerateStrmFile("37c3", 1, 5, "bahnmining", "https://cdn.media.ccc.de/...", "/tmp/")
      2. Assert file created: /tmp/37c3/Season 01/37c3 - S01E05 - bahnmining.strm
      3. Assert file content == URL
  ```
  **Commit**: Part of Wave 3 commit

---

- [ ] 21. **Sync Service (Scaffold + Scheduling)**

  **What to do**:
  - Create `Services/SyncService.cs` implementing `IHostedService`
  - Implement `StartAsync`: Initialize timer for periodic sync (default: 6 hours)
  - Implement `StopAsync`: Cancel running tasks, cleanup
  - Inject: MediaCccApi, StrmGenerator, ILibraryManager (for triggering scans)
  - Add configuration: SyncIntervalHours (from PluginConfiguration)
  - Add logging: Sync start, complete, error
  - Setup cancellation token for graceful shutdown

  **Must NOT do**:
  - Do NOT implement sync logic yet (just timer and scaffold)
  - Do NOT hardcode interval (use configuration)

  **Recommended Agent Profile**: `unspecified-low` - Service scaffold
  **Skills**: []
  **Parallelization**: Wave 3, parallel with T15-T20, T22-T23
  **References**: IHostedService pattern, Timer usage
  **Acceptance Criteria**: Service starts, timer scheduled, stops gracefully
  **QA Scenarios**:
  ```
  Scenario: Sync service starts timer
    Tool: xUnit test
    Steps: Mock dependencies, call StartAsync(), verify timer created
  Scenario: Service respects interval config
    Tool: xUnit test
    Steps: Set config.SyncIntervalHours=12, call StartAsync(), verify timer interval matches
  ```
  **Commit**: Part of Wave 3 commit

---

- [ ] 22. **Sync Service (.strm Tree Generation)**

  **What to do**:
  - Implement sync callback method: `SyncCallback(object state)`
  - Logic:
    1. Fetch all conferences from API
    2. For each conference:
       - Fetch events
       - Group events by day (extract from Event.Date or Tags)
       - For each event:
         - Select best recording (using RecordingSelector)
         - Generate .strm file using StrmGenerator
    3. Delete orphaned .strm files (events no longer in API)
    4. Trigger library scan: `_libraryManager.QueueLibraryScan()`
  - Handle: API errors, network failures (log and continue)
  - Track: Last sync timestamp, number of files created/updated/deleted
  - Add logging at each step

  **Must NOT do**:
  - Do NOT block on sync (run in background)
  - Do NOT download actual files (.strm only)
  - Do NOT sync on every startup (check last sync time)

  **Recommended Agent Profile**: `unspecified-high` - Core sync logic, complex iteration
  **Skills**: []
  **Parallelization**: Wave 3, depends on T21 scaffold
  **References**: Event grouping logic, .strm generation
  **Acceptance Criteria**: Generates .strm tree for all conferences, handles errors gracefully
  **QA Scenarios**:
  ```
  Scenario: Full sync generates .strm files
    Tool: Integration test
    Steps:
      1. Mock API with 3 conferences, 10 events each
      2. Call SyncCallback()
      3. Assert .strm files created for all events
      4. Assert directory structure: conference/season/episode.strm
  Scenario: Orphaned files deleted
    Tool: Integration test
    Steps:
      1. Create old .strm file for non-existent event
      2. Call SyncCallback()
      3. Assert old file deleted
  ```
  **Commit**: Part of Wave 3 commit

---

- [ ] 22.5. **Sync Status Logging**

  **What to do**:
  - Create `Models/SyncLog.cs`:
    ```csharp
    public class SyncLog
    {
      public string ConferenceAcronym { get; set; }
      public DateTime StartedAt { get; set; }
      public DateTime CompletedAt { get; set; }
      public string Status { get; set; } // success, failed, partial
      public int EventsProcessed { get; set; }
      public int EventsTotal { get; set; }
      public int FilesCreated { get; set; }
      public int FilesDeleted { get; set; }
      public string? ErrorMessage { get; set; }
      public TimeSpan Duration => CompletedAt - StartedAt;
    }
    
    public class SyncSession
    {
      public DateTime StartedAt { get; set; }
      public DateTime CompletedAt { get; set; }
      public List<SyncLog> ConferenceLogs { get; set; } = new();
      public int TotalConferences { get; set; }
      public int ProcessedConferences { get; set; }
      public string Status { get; set; } // running, completed, failed
      public string? LastError { get; set; }
    }
    ```
  - Create `Services/SyncLogger.cs`:
    - Store sync logs in: `{DataFolderPath}/sync-logs.json`
    - Keep last 10 sync sessions (rolling window)
    - Methods:
      - `SyncSession StartSyncSession()`
      - `void LogConference(SyncLog log)`
      - `void EndSyncSession(string status, string? error)`
      - `List<SyncSession> GetRecentSyncSessions(int count = 10)`
  - Integrate with SyncService (T22):
    - Log per-conference progress
    - Log success/failure
    - Log errors with timestamps
  - Add unit tests for sync logging

  **Recommended Agent Profile**: `unspecified-low` - Logging + JSON persistence
  **Skills**: []
  **Parallelization**: Wave 3, after T22 complete
  **References**: JSON persistence pattern from UserWatchlist
  **Acceptance Criteria**: Sync logs stored, retrievable, 10 session rolling window
  **QA Scenarios**:
  ```
  Scenario: Sync logged successfully
    Tool: Integration test
    Steps:
      1. Run sync with 3 conferences
      2. Check sync-logs.json
      3. Assert 1 SyncSession with 3 ConferenceLogs
      4. Assert status="completed"
  Scenario: Sync failure logged
    Tool: xUnit test
    Steps:
      1. Mock API to fail on conference 2
      2. Run sync
      3. Assert conference 2 status="failed"
      4. Assert ErrorMessage populated
  Scenario: Rolling window maintained
    Tool: xUnit test
    Steps:
      1. Run 15 sync sessions
      2. Get recent logs
      3. Assert only 10 sessions returned
  ```
  **Commit**: Part of Wave 3 commit

---

- [ ] 23. **Sync Service Tests**

  **What to do**:
  - Create `Tests/Unit/SyncServiceTests.cs`
  - Test scheduling: Timer fires at correct interval
  - Test .strm generation: Verify correct files created
  - Test error handling: API failure, partial failures, network timeout
  - Test orphaned cleanup: Files deleted when events removed
  - Test library scan triggered: Verify ILibraryManager.QueueLibraryScan called
  - Test idempotency: Running sync twice creates same result
  - Mock: MediaCccApi, ILibraryManager, FileSystem
  - Coverage: 80%+

  **Recommended Agent Profile**: `unspecified-low` - Standard unit testing
  **Skills**: []
  **Parallelization**: Wave 3, after T22 complete
  **References**: xUnit + Moq, file system mocking
  **Acceptance Criteria**: All tests pass, coverage ≥ 80%
  **QA Scenarios**:
  ```
  Scenario: Sync service coverage verified
    Tool: Bash
    Steps: dotnet test --filter "FullyQualifiedName~SyncServiceTests" --collect:"XPlat Code Coverage", assert ≥ 80%
  ```
  **Commit**: YES (separate commit for Wave 3 completion)
  - Message: `feat(metadata): add metadata providers and sync service`
  - Files: `Providers/`, `Services/SyncService.cs`, `Services/StrmGenerator.cs`, `Services/RecordingSelector.cs`, `Tests/Unit/MetadataProvidersTests.cs`, `Tests/Unit/SyncServiceTests.cs`


---

### Wave 4: Watchlist + Downloads

- [ ] 24. **User Data Manager (Scaffold)**

  **What to do**:
  - Create `Services/UserDataManager.cs`
  - Constructor: Inject `IApplicationPaths` for data directory access
  - Implement method: `string GetUserWatchlistPath(Guid userId)`
    - Return: `{DataFolderPath}/watchlists/user-{userId}/`
  - Implement method: `string GetUserDataFilePath(Guid userId)`
    - Return: `{DataFolderPath}/data/user-{userId}.json`
  - Ensure directory exists (create if missing)
  - Add placeholder methods for watchlist + search progress (implement in T25-T26)

  **Must NOT do**:
  - Do NOT hardcode paths (use from PluginConfiguration.WatchlistPath + IApplicationPaths.DataFolderPath)
  - Do NOT implement watchlist logic yet (just path management)

  **Recommended Agent Profile**: `quick` - Simple path management
  **Skills**: []
  **Parallelization**: Wave 4, parallel with T25-T33
  **References**: IApplicationPaths.DataFolderPath usage
  **Acceptance Criteria**: Path methods return correct locations, directories created
  **QA Scenarios**:
  ```
  Scenario: Watchlist path correct
    Tool: xUnit test
    Steps: Call GetUserWatchlistPath(guid), assert path ends with "/watchlists/user-{guid}/"
  Scenario: User data path correct
    Tool: xUnit test
    Steps: Call GetUserDataFilePath(guid), assert path ends with "/data/user-{guid}.json"
  ```
  **Commit**: Part of Wave 4 commit

---

- [ ] 25. **User Data Manager (Watchlist JSON)**

  **What to do**:
  - Implement method: `UserWatchlist LoadWatchlist(Guid userId)`
  - Implement method: `void SaveWatchlist(Guid userId, UserWatchlist watchlist)`
  - Create `Models/UserWatchlist.cs`:
    ```csharp
    public class UserWatchlist
    {
      public Guid UserId { get; set; }
      public List<string> PreferredAudioLanguages { get; set; } = new(); // Ordered priority: ["en", "de", "es"]
      public List<string> PreferredSubtitleLanguages { get; set; } = new(); // Ordered priority: ["en", "de"]
      public List<WatchlistItem> Items { get; set; } = new();
    }
    
    public class WatchlistItem
    {
      public Guid EventGuid { get; set; }
      public DateTime AddedAt { get; set; }
      public string DownloadStatus { get; set; } // pending, downloading, completed, failed
      public int DownloadProgress { get; set; } // 0-100
      public string LocalPath { get; set; }
      public string Quality { get; set; }
      public string SelectedAudioLanguage { get; set; } // Language code selected for download
      public string SelectedSubtitleLanguage { get; set; } // Language code selected for download
    }
    ```
  - Add helper: `string SelectBestLanguage(EventDto event, List<string> preferredLanguages, string languageType)`
    - Iterate preferred languages in order
    - Check if recording has `translated` language variant
    - Return first match or "original" if none found
  - Use System.Text.Json for serialization
  - Handle: File not found (create new), deserialization errors
  - Add thread safety: Use lock or ConcurrentDictionary for cache
  - Add unit tests for language selection logic

  **Recommended Agent Profile**: `unspecified-low` - JSON persistence pattern
  **Skills**: []
  **Parallelization**: Wave 4, depends on T24
  **References**: JSON file pattern from other plugins
  **Acceptance Criteria**: Loads and saves watchlist correctly, handles missing file
  **QA Scenarios**:
  ```
  Scenario: Watchlist saved and loaded
    Tool: xUnit test
    Steps:
      1. Create watchlist with 3 items
      2. Save to file
      3. Load back
      4. Assert items match
  Scenario: Missing file returns empty watchlist
    Tool: xUnit test
    Steps: Load watchlist for non-existent user, assert empty Items list
  ```
  **Commit**: Part of Wave 4 commit

---

- [ ] 26. **User Data Manager (Search Progress)**

  **What to do**:
  - Add to UserWatchlist model: `List<SearchedItem> Searched { get; set; }`
  - Create `Models/SearchedItem.cs`:
    ```csharp
    public class SearchedItem
    {
      public Guid EventGuid { get; set; }
      public DateTime MarkedAt { get; set; }
    }
    ```
  - Implement methods:
    - `void MarkAsSearched(Guid userId, Guid eventGuid)`
    - `void UnmarkAsSearched(Guid userId, Guid eventGuid)`
    - `bool IsSearched(Guid userId, Guid eventGuid)`
    - `List<Guid> GetSearchedEvents(Guid userId)`
  - Persist changes to JSON
  - Add unit tests

  **Recommended Agent Profile**: `unspecified-low` - Similar to T25 but for search progress
  **Skills**: []
  **Parallelization**: Wave 4, after T25
  **References**: Same JSON persistence pattern
  **Acceptance Criteria**: Search progress marked/unmarked, persisted correctly
  **QA Scenarios**:
  ```
  Scenario: Event marked as searched
    Tool: xUnit test
    Steps: Call MarkAsSearched(), Load watchlist, assert Searched contains eventGuid
  Scenario: Search status queried
    Tool: xUnit test
    Steps: Mark event, call IsSearched(), assert true
  ```
  **Commit**: Part of Wave 4 commit

---

- [ ] 26.5. **Language Selection Service**

  **What to do**:
  - Create `Services/LanguageSelector.cs`
  - Implement method: `string SelectBestLanguage(EventDto event, List<string> preferredLanguages, string languageType)`
    - Input: Event (with recordings), ordered list of preferred languages, "audio" or "subtitle"
    - Query: `event.Recordings` for matching `MimeType`, `Language`, and `Translated` flag
    - Logic:
      - Iterate preferredLanguages in order (first = highest priority)
      - For each language: Check if recording exists with that language
      - If match found: Return that language code
      - If no matches: Return "original" (fallback)
  - Handle: Events with no recordings, events with only original language
  - Support: ISO 639-1 language codes (en, de, es, fr, etc.)
  - Add unit tests:
    - Preferred language available
    - Only second preferred available
    - Only original available
    - No recordings (error case)

  **Recommended Agent Profile**: `quick` - Pattern matching logic
  **Skills**: []
  **Parallelization**: Wave 4, after T25-T26 (needs UserWatchlist with language prefs)
  **References**: Event.OriginalLanguage, Recording.Language, Recording.Translated fields
  **Acceptance Criteria**: Selects best language from priority list, falls back to original
  **QA Scenarios**:
  ```
  Scenario: First preferred language selected
    Tool: xUnit test
    Steps: Create event with en/de/es recordings, call SelectBestLanguage(event, ["en", "de"], "audio"), assert result == "en"
  Scenario: Second preferred language fallback
    Tool: xUnit test
    Steps: Create event with de/es recordings (no en), call SelectBestLanguage(event, ["en", "de"], "audio"), assert result == "de"
  Scenario: Original language fallback
    Tool: xUnit test
    Steps: Create event with es recording only (user prefers en, de), call SelectBestLanguage(event, ["en", "de"], "audio"), assert result == "original"
  ```
  **Commit**: Part of Wave 4 commit

---

- [ ] 26.6. **Language Preferences UI in Settings Tab**

  **What to do**:
  - Add to Configuration page Settings tab (after T38):
    - **Audio Language Preferences**:
      - Label: "Preferred Audio Languages (in order)"
      - Input: Multi-select or ordered list input
      - UI: Drag-drop ordered list (en → de → es)
      - Add language button: Dropdown with ISO 639-1 codes
      - Remove language button: X button per language
    - **Subtitle Language Preferences**:
      - Same UI as audio
      - Separate ordered list
    - Save button: `POST /mediaccc/settings` (per-user)
  - Load current user preferences: `GET /mediaccc/settings`
  - Display: "English → German → Spanish" (ordered)
  - Validation: No duplicates, max 5 languages
  - Add help text: "Languages will be tried in order. If not available, original language will be used."

  **Recommended Agent Profile**: `visual-engineering` - JavaScript UI with drag-drop
  **Skills**: []
  **Parallelization**: Wave 5, after T38 settings scaffold
  **References**: jQuery UI sortable for drag-drop, Jellyfin multi-select patterns
  **Acceptance Criteria**: User can add/remove/reorder languages, preferences persist
  **QA Scenarios**:
  ```
  Scenario: Add preferred language
    Tool: Playwright
    Steps:
      1. Open Settings tab
      2. Click "Add Audio Language"
      3. Select "English" from dropdown
      4. Click "Add Audio Language"
      5. Select "German"
      6. Assert order: English, German
  Scenario: Reorder languages
    Tool: Playwright
    Steps:
      1. Add languages: English, German, Spanish
      2. Drag "German" to top
      3. Assert order: German, English, Spanish
  Scenario: Preferences persist
    Tool: Playwright
    Steps:
      1. Set audio preferences: [English, German]
      2. Click Save
      3. Refresh page
      4. Assert preferences restored
  ```
  **Commit**: Part of Wave 5 commit

---

- [ ] 27. **File Service (Scaffold)**

  **What to do**:
  - Create `Services/FileService.cs`
  - Inject: HttpClient, ILogger, IApplicationPaths
  - Scaffold methods (implement in T28-T29):
    - `Task DownloadFileAsync(string url, string localPath, IProgress<int> progress, CancellationToken ct)`
    - `void DeleteFile(string path)`
    - `bool FileExists(string path)`
    - `long GetFileSize(string path)`
  - Add helper: `string GetEventFilename(EventDto event, RecordingDto recording)`

  **Recommended Agent Profile**: `quick` - Service scaffold
  **Skills**: []
  **Parallelization**: Wave 4, parallel with T24-T26, T28-T33
  **References**: HttpClient usage for downloads
  **Acceptance Criteria**: Service compiles, methods defined
  **QA Scenarios**:
  ```
  Scenario: File service methods exist
    Tool: Bash
    Steps: grep "DownloadFileAsync" Services/FileService.cs, assert found
  ```
  **Commit**: Part of Wave 4 commit

---

- [ ] 28. **File Service (Download Queue)**

  **What to do**:
  - Implement `DownloadFileAsync`:
    - Use HttpClient.GetAsync with HttpCompletionOption.ResponseHeadersRead (streaming)
    - Create directory if missing
    - Write to temp file first: `{localPath}.tmp`
    - Use FileStream with buffer: 8192 bytes
    - Report progress: `(bytesDownloaded / totalBytes) * 100`
    - On completion: Rename `.tmp` to final filename
    - Support cancellation: Stop download, delete temp file
  - Add retry logic: 1 retry on network error
  - Add timeout: 30 minutes per file
  - Log: Start, progress every 10%, completion, errors
  - Add unit tests with mocked HttpClient

  **Must NOT do**:
  - Do NOT load entire file in memory (use streaming)
  - Do NOT skip temp file (risk of partial writes)
  - Do NOT add complex retry policies (user said "unlimited bandwidth, simple retry")

  **Recommended Agent Profile**: `unspecified-low` - File download implementation
  **Skills**: []
  **Parallelization**: Wave 4, after T27 scaffold
  **References**: HttpClient streaming download pattern
  **Acceptance Criteria**: Downloads file with progress reporting, handles cancellation
  **QA Scenarios**:
  ```
  Scenario: File downloaded with progress
    Tool: xUnit test
    Steps:
      1. Mock HTTP with 10MB response
      2. Call DownloadFileAsync with progress callback
      3. Assert final progress >= 100
      4. Assert file exists and size matches
  Scenario: Cancellation stops download
    Tool: xUnit test
    Steps:
      1. Start download
      2. Cancel after 50%
      3. Assert temp file deleted, final file not created
  ```
  **Commit**: Part of Wave 4 commit

---

- [ ] 29. **File Service (Download Processing)**

  **What to do**:
  - Implement batch processing: `Task ProcessDownloadQueueAsync(Guid userId, CancellationToken ct)`
  - Logic:
    1. Load user's watchlist
    2. Find items with status="pending"
    3. For each pending item:
       - Fetch event details from API
       - Select best recording (RecordingSelector)
       - Generate local path: `{WatchlistPath}/user-{userId}/{event-slug}.{ext}`
       - Call DownloadFileAsync with progress callback
       - Update status: pending → downloading → completed/failed
       - Save watchlist after each file
    4. Process sequentially (FIFO queue)
  - Handle: API errors (mark as failed), download errors (retry once or mark failed)
  - Throttling: None (user said unlimited bandwidth)
  - Log each step

  **Must NOT do**:
  - Do NOT process multiple files in parallel (sequentially only per user)
  - Do NOT retry indefinitely (max 1 retry)

  **Recommended Agent Profile**: `unspecified-high` - Queue processing logic
  **Skills**: []
  **Parallelization**: Wave 4, after T28
  **References**: Watchlist status management
  **Acceptance Criteria**: Processes queue sequentially, updates status, handles errors
  **QA Scenarios**:
  ```
  Scenario: Queue processed in order
    Tool: Integration test
    Steps:
      1. Add 3 items to watchlist (pending)
      2. Call ProcessDownloadQueueAsync()
      3. Assert all 3 downloaded
      4. Assert order matches added order
  Scenario: Failed download marked as failed
    Tool: xUnit test
    Steps:
      1. Mock API to throw error
      2. Add item to watchlist
      3. Process queue
      4. Assert status="failed"
  ```
  **Commit**: Part of Wave 4 commit

---

- [ ] 30. **Download Service (Scaffold + Queue)**

  **What to do**:
  - Create `Services/DownloadService.cs` implementing `IHostedService`
  - Inject: FileService, UserDataManager, MediaCccApi, ILogger
  - Implement `StartAsync`: Start background processing loop
  - Implement `StopAsync`: Cancel current downloads, cleanup temp files
  - Add methods:
    - `Task AddToWatchlistAsync(Guid userId, Guid eventGuid)` - Add item, save, trigger processing
    - `Task RemoveFromWatchlistAsync(Guid userId, Guid eventGuid)` - Remove item, delete file, save
  - Use Channel<T> or BlockingCollection for queue (thread-safe)
  - Maintain per-user queues: `Dictionary<Guid, Channel<WatchlistItem>>`

  **Recommended Agent Profile**: `unspecified-low` - Service scaffold with queue
  **Skills**: []
  **Parallelization**: Wave 4, depends on T24-T29
  **References**: IHostedService, Channel<T> pattern
  **Acceptance Criteria**: Service starts, queue managed, add/remove methods work
  **QA Scenarios**:
  ```
  Scenario: Add to watchlist queues download
    Tool: xUnit test
    Steps:
      1. Call AddToWatchlistAsync()
      2. Assert item appears in queue
      3. Assert item status="pending"
  Scenario: Remove from watchlist deletes file
    Tool: xUnit test
    Steps:
      1. Add item, let download complete
      2. Call RemoveFromWatchlistAsync()
      3. Assert file deleted
      4. Assert item removed from watchlist
  ```
  **Commit**: Part of Wave 4 commit

---

- [ ] 31. **Download Service (Processing + Progress)**

  **What to do**:
  - Implement background processing loop:
    ```csharp
    while (!stoppingToken.IsCancellationRequested)
    {
      foreach (var (userId, queue) in _userQueues)
      {
        if (queue.TryRead(out var item))
        {
          await _fileService.ProcessDownloadQueueAsync(userId, stoppingToken);
        }
      }
      await Task.Delay(5000, stoppingToken); // Poll every 5s
    }
    ```
  - Track progress: Update UserWatchlist.DownloadProgress
  - Expose progress query: `WatchlistItem GetDownloadProgress(Guid userId, Guid eventGuid)`
  - Hook FileService.DownloadFileAsync progress callback to update UserWatchlist
  - Log: Download start, progress (every 10%), completion

  **Must NOT do**:
  - Do NOT process downloads for multiple users simultaneously (but process multiple users' queues)
  - Do NOT block processing loop on single download (use Task.Run or parallel processing)

  **Recommended Agent Profile**: `unspecified-high` - Background processing with progress tracking
  **Skills**: []
  **Parallelization**: Wave 4, after T30 scaffold
  **References**: Background service patterns, progress reporting
  **Acceptance Criteria**: Processes downloads continuously, updates progress in real-time
  **QA Scenarios**:
  ```
  Scenario: Progress updated during download
    Tool: Integration test
    Steps:
      1. Add item to watchlist
      2. Start DownloadService
      3. Poll GetDownloadProgress() every 1s
      4. Assert progress increases: 0 → 50 → 100
  Scenario: Multiple users download simultaneously
    Tool: xUnit test
    Steps:
      1. Add item for User A
      2. Add item for User B
      3. Start service
      4. Assert both download (no blocking)
  ```
  **Commit**: Part of Wave 4 commit

---

- [ ] 32. **Download Service (Cancellation + Cleanup)**

  **What to do**:
  - Implement graceful shutdown in `StopAsync`:
    - Signal cancellation to all active downloads
    - Wait for current download to complete or cancel (max 30s)
    - Delete temp files (.tmp) for interrupted downloads
    - Update status: downloading → paused (for resume) or pending (for restart)
  - Implement orphaned file cleanup on startup:
    - Scan `{WatchlistPath}/user-{guid}/` for files not in watchlist
    - Delete orphaned files
  - Add user deletion handler: When Jellyfin user deleted:
    - Delete entire `{WatchlistPath}/user-{deleted-guid}/` folder
    - Delete `{DataFolderPath}/data/user-{deleted-guid}.json`
  - Register for IUserManager.UserDeleted event (or similar)

  **Must NOT do**:
  - Do NOT forcefully kill downloads (allow graceful cancellation)
  - Do NOT delete user data on plugin version upgrade (only on user deletion)

  **Recommended Agent Profile**: `unspecified-low` - Cleanup and cancellation logic
  **Skills**: []
  **Parallelization**: Wave 4, after T31
  **References**: Cancellation patterns, cleanup logic
  **Acceptance Criteria**: Graceful shutdown, temp files cleaned, user data deleted on user removal
  **QA Scenarios**:
  ```
  Scenario: Temp files cleaned on interruption
    Tool: xUnit test
    Steps:
      1. Start download
      2. Cancel after 50%
      3. Assert .tmp file deleted
  Scenario: User folder deleted on user removal
    Tool: Integration test
    Steps:
      1. Create user folder with files
      2. Trigger user deletion
      3. Assert folder deleted
  ```
  **Commit**: Part of Wave 4 commit

---

- [ ] 33. **Download Service Tests**

  **What to do**:
  - Create `Tests/Unit/DownloadServiceTests.cs`
  - Test queue management: Add, remove, order preserved
  - Test progress tracking: Progress updates correctly
  - Test cancellation: Graceful stop, temp file cleanup
  - Test error handling: Network fail, disk full, API error
  - Test multi-user: Separate queues, isolated processing
  - Test file cleanup: Orphaned files deleted, user deletion handled
  - Mock: FileService, UserDataManager, HttpClient
  - Coverage: 80%+

  **Recommended Agent Profile**: `unspecified-low` - Comprehensive unit testing
  **Skills**: []
  **Parallelization**: Wave 4, after T32
  **References**: xUnit + Moq
  **Acceptance Criteria**: All tests pass, coverage ≥ 80%
  **QA Scenarios**:
  ```
  Scenario: Download service coverage verified
    Tool: Bash
    Steps: dotnet test --filter "FullyQualifiedName~DownloadServiceTests" --collect:"XPlat Code Coverage", assert ≥ 80%
  ```
  **Commit**: YES (separate commit for Wave 4 completion)
  - Message: `feat(watchlist): add user data management and download service`
  - Files: `Services/UserDataManager.cs`, `Services/FileService.cs`, `Services/DownloadService.cs`, `Models/UserWatchlist.cs`, `Models/SearchedItem.cs`, `Tests/Unit/DownloadServiceTests.cs`


---

### Wave 5: UI + Integration

- [ ] 34. **Config Page HTML (Scaffold)**

  **What to do**:
  - Create `Configuration/configPage.html` as embedded resource
  - Basic HTML structure with tabs: Browse, Watchlist, Search Progress, Settings
  - Use Jellyfin's built-in CSS classes: `page type-interior pluginConfigurationPage`
  - Add navigation tabs: `data-role="tabs"`
  - Scaffold tab content areas with placeholder text
  - Include: jQuery (from Jellyfin), standard form elements
  - Ensure page loads in Jellyfin: Test via `/configurationpage?name=MediaCCCDe`

  **Must NOT do**:
  - Do NOT implement full functionality (just scaffold)
  - Do NOT add custom CSS (use Jellyfin's styles)
  - Do NOT add complex JavaScript yet (just basic structure)

  **Recommended Agent Profile**: `visual-engineering` - UI scaffold, minimal JavaScript
  **Skills**: []
  **Parallelization**: Wave 5, parallel with T35-T40
  **References**: Jellyfin plugin config page pattern, HTML examples from existing plugins
  **Acceptance Criteria**: Page loads in Jellyfin, tabs visible, no JavaScript errors
  **QA Scenarios**:
  ```
  Scenario: Config page loads in Jellyfin
    Tool: Playwright
    Steps:
      1. Login to Jellyfin as admin
      2. Navigate to Dashboard → Plugins → MediaCCCDe → Configure
      3. Assert page loads without errors
      4. Assert tabs: Browse, Watchlist, Search Progress, Settings visible
    Evidence: .sisyphus/evidence/task-34-config-page-load.png
  ```
  **Commit**: Part of Wave 5 commit

---

- [ ] 35. **Config Page (Browse Tab - JS Logic)**

  **What to do**:
  - Add JavaScript for Browse tab:
    - Fetch conferences: `GET /mediaccc/conferences` (API endpoint from T39)
    - Display as collapsible list: Conference → Events
    - Each event shows: thumbnail, title, duration, description, speakers
    - "Add to Watchlist" button per event: `POST /mediaccc/watchlist/{eventGuid}`
    - "Mark as Searched" button: `POST /mediaccc/searched/{eventGuid}`
  - UI components:
    - Search box: Filter events by title/description
    - Sort options: By date, by conference, alphabetically
  - Handle: Loading state, errors (show alert)
  - Use Jellyfin's API client: `ApiClient.fetch()`

  **Recommended Agent Profile**: `visual-engineering` - JavaScript + AJAX logic, UI interaction
  **Skills**: [] (uses Jellyfin's built-in API client)
  **Parallelization**: Wave 5, depends on T34 scaffold, T39 API endpoints
  **References**: Jellyfin jQuery patterns, existing plugin config pages
  **Acceptance Criteria**: Conferences load, events displayed, buttons functional
  **QA Scenarios**:
  ```
  Scenario: Browse tab shows conferences
    Tool: Playwright
    Steps:
      1. Load config page
      2. Click "Browse" tab
      3. Assert conferences list loads
      4. Click conference "37c3"
      5. Assert events list appears
      6. Verify each event has: thumbnail, title, "Add to Watchlist" button
    Evidence: .sisyphus/evidence/task-35-browse-tab.png
  Scenario: Event added to watchlist
    Tool: Playwright
    Steps:
      1. Find event: "Opening Ceremony"
      2. Click "Add to Watchlist"
      3. Wait for success message
      4. Click "Watchlist" tab
      5. Assert event appears in list
  ```
  **Commit**: Part of Wave 5 commit

---

- [ ] 36. **Config Page (Watchlist Tab + Progress)**

  **What to do**:
  - Add JavaScript for Watchlist tab:
    - Fetch user's watchlist: `GET /mediaccc/watchlist`
    - Display each item:
      - Thumbnail, title, duration
      - Download status: pending/download icon, progress bar (if downloading), checkmark (completed), error icon (failed)
      - Progress percentage: "45%" if downloading
      - "Remove" button: `DELETE /mediaccc/watchlist/{eventGuid}`
      - "Play" button (if completed): Opens in Jellyfin player
    - Real-time progress: Poll `GET /mediaccc/watchlist/progress` every 5s when downloads active
  - Display storage usage: "1.2 GB used" (sum of downloaded files)
  - Handle: Empty state ("No items in watchlist")

  **Recommended Agent Profile**: `visual-engineering` - Dynamic UI with progress bars, real-time updates
  **Skills**: [] (uses Jellyfin's UI patterns)
  **Parallelization**: Wave 5, after T34 scaffold
  **References**: Progress bar UI patterns in Jellyfin
  **Acceptance Criteria**: Watchlist displays, progress updates, remove works, storage shown
  **QA Scenarios**:
  ```
  Scenario: Watchlist shows download progress
    Tool: Playwright
    Steps:
      1. Add 3 events to watchlist
      2. Open Watchlist tab
      3. Wait for downloads to start
      4. Assert progress bars appear
      5. Wait 30s, verify progress increases
      6. Verify status changes: downloading → completed
  Scenario: Remove from watchlist works
    Tool: Playwright
    Steps:
      1. Add event to watchlist
      2. Click "Remove"
      3. Confirm deletion
      4. Assert item removed from list
      5. Verify file deleted: curl Jellyfin API for library items
  ```
  **Commit**: Part of Wave 5 commit

---

- [ ] 37. **Config Page (Search Progress Tab)**

  **What to do**:
  - Add JavaScript for Search Progress tab:
    - Fetch searched events: `GET /mediaccc/searched`
    - Display as two lists:
      - "Searched Through": Events marked as searched (with "Unmark" button)
      - "Not Yet Searched": All other events (with "Mark as Searched" button)
    - Filter options: By conference, by date range
    - Search bar: Find specific events
    - Stats: "45/300 events searched (15%)"
  - Handle marking/unmarking: `POST /mediaccc/searched/{eventGuid}`, `DELETE /mediaccc/searched/{eventGuid}`
  - Visual indicator: Checkmark ✓ for searched events

  **Recommended Agent Profile**: `visual-engineering` - List UI with filtering
  **Skills**: []
  **Parallelization**: Wave 5, after T34 scaffold
  **References**: Search progress data from T26
  **Acceptance Criteria**: Searched/unsearched lists display, marking works, stats update
  **QA Scenarios**:
  ```
  Scenario: Search progress tracking works
    Tool: Playwright
    Steps:
      1. Open Search Progress tab
      2. Assert "Not Yet Searched" list has events
      3. Click "Mark as Searched" on one event
      4. Assert event moves to "Searched Through" list
      5. Assert stats update: "1/300 (0.33%)"
  ```
  **Commit**: Part of Wave 5 commit

---

- [ ] 38. **Config Page (Settings Tab)**

  **What to do**:
  - Add JavaScript for Settings tab:
    - **Admin settings** (visible to admins only):
      - Download path: Text input with validation (check writable)
      - Quality: Dropdown (HD/SD)
      - Format: Dropdown (MP4/WebM)
      - Sync interval: Dropdown (1h, 6h, 12h, 24h)
      - Save button: `POST /mediaccc/settings` (updates PluginConfiguration)
    - **User settings** (visible to all users):
      - Storage usage: "1.2 GB used in your watchlist"
      - Quality preference display (read-only, set by admin)
    - Load current settings: `GET /mediaccc/settings`
    - Validation: Download path must be writable (test by creating temp file)
  - Success message on save: "Settings saved. Sync will run at next interval."

  **Recommended Agent Profile**: `visual-engineering` - Form UI with validation
  **Skills**: []
  **Parallelization**: Wave 5, after T34 scaffold
  **References**: Plugin settings pattern
  **Acceptance Criteria**: Settings load, save, validation works
  **QA Scenarios**:
  ```
  Scenario: Settings load and save
    Tool: Playwright
    Steps:
      1. Open Settings tab
      2. Assert current values displayed
      3. Change quality to "SD"
      4. Click Save
      5. Assert success message
      6. Refresh page, verify setting persisted
  Scenario: Invalid path validation
    Tool: Playwright
    Steps:
      1. Enter invalid path: "/nonexistent/path"
      2. Click Save
      3. Assert error: "Path not writable"
  ```
  **Commit**: Part of Wave 5 commit

---

- [ ] 39. **API Endpoints for Config Page**

  **What to do**:
  - Create `Api/MediaCccController.cs` extending `ControllerBase`
  - Route prefix: `[Route("mediaccc")]`
  - Endpoints:
    - `GET /conferences` → Return all conferences (from cache or API)
    - `GET /conferences/{acronym}/events` → Events in conference
    - `GET /events/{guid}` → Event details
    - `GET /watchlist` → Current user's watchlist (from UserDataManager)
    - `POST /watchlist/{eventGuid}` → Add to watchlist (trigger DownloadService)
    - `DELETE /watchlist/{eventGuid}` → Remove from watchlist
    - `GET /watchlist/progress` → Download progress for all items
    - `POST /searched/{eventGuid}` → Mark as searched
    - `DELETE /searched/{eventGuid}` → Unmark as searched
    - `GET /searched` → List of searched events
    - `GET /settings` → Plugin configuration (user's quality preference)
    - `POST /settings` → Update plugin configuration (admin only)
  - Get current user: `HttpContext.User` or `IUserManager.GetUser(HttpContext)`
  - Return JSON: Use `ActionResult<T>` pattern
  - Add authentication: `[Authorize(Policy = "DefaultAuthorization")]` or similar
  - Add error handling: Return 404 for not found, 400 for bad requests

  **Must NOT do**:
  - Do NOT expose admin-only endpoints without authorization
  - Do NOT return raw plugin configuration (sanitize sensitive data)

  **Recommended Agent Profile**: `unspecified-low` - REST API endpoints, straightforward routing
  **Skills**: []
  **Parallelization**: Wave 5, after T24-T33 (needs watchlist/search progress services)
  **References**: Jellyfin ControllerBase pattern, existing plugin APIs
  **Acceptance Criteria**: All endpoints work, authentication/authorization correct
  **QA Scenarios**:
  ```
  Scenario: API endpoints return correct data
    Tool: Bash (curl)
    Steps:
      1. curl -H "Authorization: MediaBrowser Token={api_key}" http://localhost:8096/mediaccc/conferences
      2. Assert JSON array returned
      3. curl http://localhost:8096/mediaccc/watchlist -H "X-Emby-Authorization": "MediaBrowser Token={user_token}"
      4. Assert user's watchlist JSON
  Scenario: Unauthorized access rejected
    Tool: Bash (curl)
    Steps:
      1. curl http://localhost:8096/mediaccc/watchlist (no auth)
      2. Assert 401 Unauthorized
  ```
  **Commit**: Part of Wave 5 commit

---

- [ ] 40. **End-to-End Integration Test**

  **What to do**:
  - Create `Tests/Integration/E2EIntegrationTests.cs`
  - Test scenarios:
    1. **Full archive sync**: Plugin loads → Sync runs → .strm files created → Library shows conferences
    2. **Watchlist workflow**: User adds event → Download starts → Completes → Appears in library → User removes → File deleted
    3. **Multi-user isolation**: User A adds event → User B cannot see it
    4. **Search progress**: User marks event as searched → Persists across restart
  - Setup: Mock Jellyfin server or use test harness
  - Use: Real API responses (recorded), real file system operations
  - Verify: Database state, file system state, library state
  - Add cleanup: Delete all test user data after each test

  **Recommended Agent Profile**: `unspecified-high` - Complete user journey testing
  **Skills**: []
  **Parallelization**: Wave 5, after all previous tasks complete
  **References**: Integration test patterns
  **Acceptance Criteria**: All E2E scenarios pass
  **QA Scenarios**:
  ```
  Scenario: E2E test suite runs
    Tool: Bash
    Steps:
      1. dotnet test --filter "FullyQualifiedName~E2EIntegrationTests"
      2. Assert all scenarios pass
      3. Verify no test data left in system
  ```
  **Commit**: YES (separate commit for Wave 5 completion)
  - Message: `feat(ui): add configuration page and API endpoints`
  - Files: `Configuration/configPage.html`, `Api/MediaCccController.cs`, `Tests/Integration/E2EIntegrationTests.cs`, `Tests/Integration/E2EScenarios.cs`


---

### Final Verification Wave (After Wave 5)

- [ ] F1. **Plan Compliance Audit** — `oracle`

  **What to do**:
  - Read entire work plan end-to-end
  - For each "Must Have" requirement: Verify implementation exists
    - Check: File exists, method defined, endpoint registered, service wired
    - Use: File reads, grep, curl to Jellyfin API
  - For each "Must NOT Have": Search codebase for forbidden patterns
    - Forbidden: transcoding logic, custom player UI, auto-cleanup, metadata editing, priority queue, bandwidth throttle, recommendations, thumbnail cache, external API
    - Reject with file:line if found
  - Verify all TODOs checked: Every task marked complete has evidence in `.sisyphus/evidence/`
  - Compare deliverables against plan: DLL, .strm files, config page, libraries
  - Check test coverage: `dotnet test --collect:"XPlat Code Coverage"` shows ≥ 80%
  - Output format:
    ```
    Must Have [16/16]:
    - ✅ All conferences as TV Series: 300+ conferences synced
    - ✅ Two libraries: Archive + Watchlist
    - ✅ Per-user language preferences: Audio + subtitle selection
    - ...
    Must NOT Have [12/12]:
    - ✅ No transcoding logic: grep found 0 matches
    - ✅ No custom player UI: No player files found
    - ...
    Tasks [42/42]: All tasks have evidence
    VERDICT: APPROVE/REJECT
    ```

  **Acceptance Criteria**: All "Must Have" present, all "Must NOT Have" absent, plan matches implementation
  **QA Scenarios**:
  ```
  Scenario: Compliance audit completes
    Tool: Agent execution (oracle)
    Steps:
      1. Read plan
      2. Check each requirement
      3. Generate report
      4. If REJECT: List specific violations
    Expected Result: APPROVE verdict with complete checklist
    Evidence: .sisyphus/evidence/task-F1-compliance-audit.md
  ```

---

- [ ] F2. **Code Quality Review** — `unspecified-high`

  **What to do**:
  - Run build: `dotnet build --configuration Release` → Assert 0 errors, 0 warnings
  - Run tests: `dotnet test --configuration Release` → Assert all pass
  - Check coverage: `dotnet test --collect:"XPlat Code Coverage"` → Assert = 100%
  - Review all changed files for code smells:
    - C# anti-patterns: `as any`, empty catch blocks, unused variables, commented code
    - AI slop: Over-commenting (every line commented), generic names (Manager, Helper, Utils), excessive abstraction
    - Production readiness: Console.WriteLine in non-test code, missing error handling
  - Check logs: No hardcoded secrets, no test data in production
  - Verify dependency versions: All packages from trusted sources, no vulnerabiltiies
  - Output format:
    ```
    Build [PASS/FAIL]: 0 Error(s), 0 Warning(s)
    Tests [PASS/FAIL]: 142 pass, 0 fail
    Coverage: 82% (threshold: 80%)
    Files [clean/issues]:
    - ✅ MediaCccApi.cs: Clean
    - ✅ SyncService.cs: Clean
    - ⚠️ DownloadService.cs: Contains TODO comment (non-blocking)
    VERDICT: APPROVE/REJECT
    ```

  **Acceptance Criteria**: Build succeeds, tests pass, coverage ≥ 80%, no critical issues
  **QA Scenarios**:
  ```
  Scenario: Quality review completes
    Tool: Agent execution (unspecified-high)
    Steps:
      1. Run build + test
      2. Static analysis
      3. Generate report
    Expected Result: APPROVE verdict, all quality checks pass
    Evidence: .sisyphus/evidence/task-F2-quality-review.md
  ```

---

- [ ] F3. **Real Manual QA** — `unspecified-high` + `playwright`

  **What to do**:
  - Setup: Fresh Jellyfin instance with plugin installed
  - Execute EVERY QA scenario from ALL tasks (T1-T40):
    - Archive library: Browse conferences, play episodes, verify metadata
    - Watchlist: Add events, monitor download progress, remove items
    - Multi-user: Test isolation, separate watchlists
    - Search progress: Mark/unmark events, persist across restart
    - Config page: All tabs functional, settings saved
  - Test cross-task integration:
    - Browse → Add to watchlist → Download → Watch → Remove flow
    - Sync → .strm files → Library shows → Play → Progress tracked
  - Test edge cases:
    - Disk full: Simulate full disk, verify error handling
    - Network fail: Disable network mid-download, verify retry
    - Multi-user race: Two users add same event simultaneously
    - User deleted: Delete user, verify cleanup
  - Capture evidence for each scenario:
    - Screenshots: UI interactions
    - Log files: Jellyfin logs showing `[MediaCcc]` entries
    - File verification: Downloaded files exist
  - Save all evidence to `.sisyphus/evidence/final-qa/`
  - Output format:
    ```
    Scenarios [45/45]: 42 pass, 3 fail
    Failed scenarios:
    - ❌ T31-Scenario-2: Multi-user download race (failed - file conflict)
    - ...
    Integration [PASS/FAIL]:
    - ✅ Browse → Watchlist flow works
    - ✅ Sync → Play flow works
    Edge Cases [8 tested]:
    - ✅ Disk full handling
    - ❌ Network fail retry (download stopped, not retried)
    VERDICT: APPROVE/REJECT
    ```

  **Acceptance Criteria**: All critical scenarios pass, integration works, edge cases handled
  **QA Scenarios**:
  ```
  Scenario: QA suite completes
    Tool: Agent execution (unspecified-high + playwright)
    Steps:
      1. Setup Jellyfin instance
      2. Execute all QA scenarios from T1-T40
      3. Test integration flows
      4. Test edge cases
      5. Capture evidence
      6. Generate report
    Expected Result: APPROVE verdict, critical issues resolved
    Evidence: .sisyphus/evidence/task-F3-manual-qa/, .sisyphus/evidence/final-qa/
  ```

---

- [ ] F4. **Scope Fidelity Check** — `deep`

  **What to do**:
  - For each task (T1-T40): 
    - Read "What to do" section
    - Read actual implementation diff (git log --stat, git diff)
    - Verify 1:1 mapping:
      - Everything in spec was built (no missing)
      - Nothing beyond spec was built (no scope creep)
  - Check "Must NOT do" compliance:
    - Search for forbidden patterns in all files
    - Verify violations flagged and removed
  - Detect cross-task contamination:
    - Task N should only touch authorized files
    - Flag if Task N modifies Task M's files without dependency
  - Detect unaccounted changes:
    - Files not mentioned in plan
    - Extra features added
    - Commented-out code for future features
  - Output format:
    ```
    Tasks [40/40]: 38 compliant, 2 issues
    Issues:
    - ⚠️ T18: Added extra debug logging method (spec didn't mention)
    - ⚠️ T31: Modified DownloadService.cs methods not in spec
    Contamination [CLEAN/ISSUES]:
    - ✅ No cross-task contamination detected
    Unaccounted [CLEAN/ISSUES]:
    - ⚠️ Extra file: Services/FutureFeature.cs (not in plan)
    VERDICT: APPROVE/REJECT
    ```

  **Acceptance Criteria**: All tasks 1:1 compliant, no scope creep, no contamination
  **QA Scenarios**:
  ```
  Scenario: Scope fidelity verified
    Tool: Agent execution (deep)
    Steps:
      1. Read plan
      2. Analyze git history
      3. Compare spec vs implementation
      4. Generate report
    Expected Result: APPROVE verdict, spec matches implementation exactly
    Evidence: .sisyphus/evidence/task-F4-scope-fidelity.md
  ```

---

## Final Verification Outcome

**After F1-F4 complete**: Present consolidated report to user.

**Report format**:
```
╔════════════════════════════════════════════════════════════╗
║         FINAL VERIFICATION REPORT - MediaCCC Plugin         ║
╚════════════════════════════════════════════════════════════╝

✅ F1. Plan Compliance Audit: APPROVE
   - Must Have: 15/15 present
   - Must NOT Have: 11/11 absent
   - Tasks: 40/40 with evidence

✅ F2. Code Quality Review: APPROVE
   - Build: PASS (0 errors, 0 warnings)
   - Tests: PASS (142 tests)
   - Coverage: 82%

⚠️ F3. Manual QA: APPROVE (with notes)
   - Scenarios: 42/45 pass
   - Integration: PASS
   - Edge cases: 6/8 pass
   Notes: Download retry needs improvement (non-blocking)

✅ F4. Scope Fidelity: APPROVE
   - Tasks: 40/40 compliant
   - Contamination: CLEAN
   - Unaccounted: CLEAN

═════════════════════════════════════════════════════════════
OVERALL VERDICT: ✅ APPROVED
Recommendations:
- Fix download retry logic (minor issue from F3)
- No blocking issues found
═════════════════════════════════════════════════════════════

User action required: Review and approve to mark work complete.
```

**User decision point**: If APPROVED, proceed to completion. If REJECTED, fix issues and re-run F1-F4.

---

## Post-Approval Steps

After user approves final verification:

1. **Update plan status**: Mark all tasks completed
2. **Generate summary**: One-page recap of what was built
3. **Create documentation**: README.md with:
   - Installation instructions
   - Configuration guide
   - Troubleshooting
4. **Delete draft file**: Clean up `.sisyphus/drafts/ccc-media-plugin.md`
5. **Guide user**: Tell user to run `/start-work` if they haven't already

---

## Plan Complete

This work plan is now ready for execution. All tasks are defined with:
- Clear implementation steps
- Guardrails and "Must NOT do" constraints
- Recommended agent profiles
- Parallelization strategy
- Comprehensive QA scenarios
- Evidence requirements

**Next step**: User runs `/start-work ccc-media-plugin` to begin execution.

