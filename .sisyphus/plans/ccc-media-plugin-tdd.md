# Work Plan: Jellyfin media.ccc.de Plugin (TDD)

## TL;DR

> **Quick Summary**: Build a Jellyfin plugin integrating media.ccc.de conference recordings with two-library architecture: streaming archive (Conference-as-TV-Series) + per-user download watchlist. Users browse conferences, stream instantly, or download to personal per-user libraries.
 
> **Deliverables**: 
> - Jellyfin plugin (.NET 9.0): `Jellyfin.Plugin.MediaCccDe.dll`
> - Archive library: All conferences as TV Shows (.strm streaming) - AUTO-CREATED
> - Watchlist system: Per-user libraries with automatic permission setup
> - Configuration UI: Browse + Manage watchlist + Sync log
> - Background services: Sync + Download queue
> 
> **Estimated Effort**: Large (multi-component plugin with metadata providers, background services, UI)
> **Parallel Execution**: YES - 5 waves (Foundation → Archive → Watchlist → UI → Polish)
> **Development Methodology**: Test-Driven Development (RED-GREEN-REFACTOR)

---

## TDD Methodology

Every feature follows this pattern:

```
RED: Write failing test(s) that specify expected behavior
GREEN: Write minimal implementation to make test(s) pass
REFACTOR: Clean up code, optimize, ensure quality
```

Each task explicitly separates these phases:
- **Test Phase**: What test to write, what it verifies
- **Implementation Phase**: What code to write (minimal)
- **Refactor Phase**: What to improve after tests pass

---

## Context

### Original Request
User wants to browse and watch media.ccc.de videos in Jellyfin with personal organization:
- Browse all conferences (full archive)
- Per-user watchlist with offline downloads
- Search progress tracking ("already searched through" markers)
- Per-user language preferences (audio + subtitles)
- Admin-visible sync history

### Architecture Decisions

**Library Model**:
1. **Archive Library** (Auto-created on first run)
   - Named: "CCC Archive"
   - Type: TV Shows
   - Path: `/config/plugins/ccc-media/archive/`
   - Content: All conferences as .strm files
   - Permissions: All users can browse

2. **Per-User Watchlist Libraries** (Auto-created per user)
   - Named: "{username}'s Watchlist"
   - Type: Movies
   - Path: `/config/plugins/ccc-media/watchlists/{username}/`
   - Content: Downloaded files for that user
   - Permissions: **Only that user can access**
   - Created: When user adds first item to watchlist

**Data Storage**:
- `/config/plugins/ccc-media/archive/` - .strm files (streaming references)
- `/config/plugins/ccc-media/watchlists/{username}/` - Downloaded videos
- `/config/plugins/ccc-media/data/user-{guid}.json` - Per-user data
- `/config/plugins/ccc-media/data/sync-logs.json` - Sync history

**Sync Model**:
- Automatic: Every 6 hours
- Visible: Admin can see sync history in Settings tab
- Per-conference logging: Success/failure status per conference

---

## Work Objectives

### Core Objective

Build a production-ready Jellyfin plugin that:
1. Integrates media.ccc.de's full conference archive as a browsable TV Show library
2. Creates per-user watchlist libraries with automatic permission setup
3. Enables per-user language preferences (audio + subtitles)
4. Provides admin-visible sync history
5. Operates entirely within Jellyfin (no external dependencies)

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
- `LibrarySetupService.cs` (IAsyncInitialize): Create Archive library on startup

**Storage Management**:
- `UserDataManager.cs`: Per-user watchlist + search progress + language prefs
- `FileService.cs`: .strm creation, download management, cleanup
- `SyncLogger.cs`: Sync history logging

**Library Creation**:
- `LibrarySetupService.cs`: Auto-create Archive library
- `UserLibraryService.cs`: Create per-user watchlist libraries with permissions

**Configuration UI**:
- `configPage.html` (embedded resource):
  - Tab 1: Browse archive (conference tree + "Add to Watchlist" buttons)
  - Tab 2: My Watchlist (downloads + progress + remove buttons)
  - Tab 3: Search Progress (marked events + mark/unmark)
  - Tab 4: Settings (admin: download path, quality; user: language prefs)
  - Tab 5: Sync Log (admin: sync history with per-conference status)

**Tests**:
- `MediaCccApiTests.cs`: Unit tests with mocked HTTP
- `MetadataProvidersTests.cs`: Unit tests for mapping logic
- `DownloadServiceTests.cs`: Unit tests for queue + file management
- `LibraryServicesTests.cs`: Unit tests for library creation + permissions
- `IntegrationTests.cs`: End-to-end with recorded API responses

### Definition of Done

- [ ] All NuGet dependencies version-locked in .csproj
- [ ] Plugin loads in Jellyfin 10.11+ without errors
- [ ] Archive library auto-created on startup
- [ ] All conferences visible as TV Shows in Archive library
- [ ] Metadata (title, description, posters, runtime) displays correctly
- [ ] Streaming playback works for .strm files
- [ ] Per-user watchlist libraries created automatically
- [ ] User A's watchlist library invisible to User B
- [ ] Downloads start within 30s of adding to watchlist
- [ ] Download progress visible in config page
- [ ] Removing from watchlist deletes file immediately
- [ ] Language preferences apply to download selection
- [ ] Search progress markers persist across restarts
- [ ] Sync log visible to admins in Settings tab
- [ ] Unit tests pass: `dotnet test --no-build --verbosity normal` (80%+ coverage)
- [ ] All tests written BEFORE implementation (TDD verified)
- [ ] Manual QA: Browse → Add → Download → Watch → Remove flow works
- [ ] Logs: All major actions logged with `[MediaCcc]` prefix

### Must Have

- ✅ All conferences from media.ccc.de API synced as TV Series
- ✅ Two library types: Archive (auto-created, shared) + Watchlist (per-user, auto-created)
- ✅ Per-user watchlist with UUID isolation
- ✅ **Per-user libraries with automatic permission setup** (only owner can access)
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

## Execution Strategy

### Parallel Execution Waves

> Maximize throughput by grouping independent tasks into parallel waves.
> Each wave completes before the next begins.
> Target: 5-8 tasks per wave. Fewer than 3 per wave (except final) = under-splitting.

```
Wave 1 (Foundation - Start Immediately — scaffolding + core APIs):
├── Task 1: Project scaffolding + build verification [quick]
├── Task 2: Plugin entry point + configuration [quick]
├── Task 3: Service registrator + DI setup [quick]
├── Task 4: Library setup service (RED tests) [quick]
├── Task 5: Library setup service implementation [unspecified-low]
├── Task 6: Directory structure + .gitignore [quick]
└── Task 7: .csproj foundation + dependencies [quick]

Wave 2 (API Client - After Wave 1 — API client + tests):
├── Task 8: API client - core HTTP wrapper tests [quick]
├── Task 9: API client - core HTTP wrapper implementation [quick]
├── Task 10: Data models (DTOs) tests [quick]
├── Task 11: Data models implementation [quick]
├── Task 12: API client - GetConferences tests [unspecified-low]
├── Task 13: API client - GetConferences implementation [unspecified-low]
├── Task 14: API client - GetEvents tests [unspecified-low]
├── Task 15: API client - GetEvents implementation [unspecified-low]
├── Task 16: API client - GetEvent tests [unspecified-low]
├── Task 17: API client - GetEvent implementation [unspecified-low]
├── Task 18: API client - GetRecent tests [unspecified-low]
├── Task 19: API client - GetRecent implementation [unspecified-low]
├── Task 20: Recording URL selection logic tests [quick]
├── Task 21: Recording URL selection logic implementation [quick]
├── Task 22: Language selection service tests [quick]
├── Task 23: Language selection service implementation [quick]
└── Task 24: API client integration tests [unspecified-high]

Wave 3 (Metadata + Archive - After Wave 2):
├── Task 25: Series metadata provider tests [unspecified-low]
├── Task 26: Series metadata provider implementation [unspecified-high]
├── Task 27: Episode metadata provider tests [unspecified-low]
├── Task 28: Episode metadata provider implementation [unspecified-high]
├── Task 29: .strm file generation tests [quick]
├── Task 30: .strm file generation implementation [quick]
├── Task 31: Sync service - scheduled sync tests [unspecified-low]
├── Task 32: Sync service - scheduled sync implementation [unspecified-low]
├── Task 33: Sync service - .strm tree generation tests [unspecified-high]
├── Task 34: Sync service - .strm tree generation implementation [unspecified-high]
├── Task 35: Sync logger tests [unspecified-low]
├── Task 36: Sync logger implementation [unspecified-low]
└── Task 37: Sync service integration tests [unspecified-low]

Wave 4 (Watchlist - After Wave 3):
├── Task 38: User data manager tests [quick]
├── Task 39: User data manager implementation [unspecified-low]
├── Task 40: User library service tests (per-user lib creation) [unspecified-low]
├── Task 41: User library service implementation [unspecified-high]
├── Task 42: File service - download tests [unspecified-low]
├── Task 43: File service - download implementation [unspecified-high]
├── Task 44: Download service - queue tests [unspecified-low]
├── Task 45: Download service - queue implementation [unspecified-low]
├── Task 46: Download service - processing tests [unspecified-high]
├── Task 47: Download service - processing implementation [unspecified-high]
└── Task 48: Download service integration tests [unspecified-low]

Wave 5 (UI + Integration - After Wave 4):
├── Task 49: Configuration page scaffold [visual-engineering]
├── Task 50: Browse tab tests [visual-engineering]
├── Task 51: Browse tab implementation [visual-engineering]
├── Task 52: Watchlist tab tests [visual-engineering]
├── Task 53: Watchlist tab implementation [visual-engineering]
├── Task 54: Settings tab tests [visual-engineering]
├── Task 55: Settings tab implementation [visual-engineering]
├── Task 56: Language preferences UI tests [visual-engineering]
├── Task 57: Language preferences UI implementation [visual-engineering]
├── Task 58: Sync log tab tests [visual-engineering]
├── Task 59: Sync log tab implementation [visual-engineering]
├── Task 60: API endpoints tests [unspecified-low]
├── Task 61: API endpoints implementation [unspecified-low]
└── Task 62: End-to-end integration tests [unspecified-high]

Final Wave (Verification - After ALL tasks):
├── Task F1: Plan compliance audit (oracle)
├── Task F2: Code quality review (unspecified-high)
├── Task F3: Real manual QA (unspecified-high + playwright)
└── Task F4: Scope fidelity check (deep)

Critical Path: T1 → T4-T5 → T8-T24 → T25-T28 → T31-T37 → T40-T41 → T48-T61 → F1-F4
Parallel Speedup: ~75% faster than sequential
Max Concurrent: 7 (Wave 1)
```
---

## TODOs (Test-Driven Development)

> Every task follows RED-GREEN-REFACTOR:
> 1. Write failing test(s) that specify expected behavior
> 2. Implement minimal code to make test(s) pass
> 3. Refactor and improve code quality
> 
> **A task WITHOUT tests is INCOMPLETE. No exceptions.**

---

### Wave 1: Foundation

- [ ] 1. **Project Scaffolding (TDD)**

  **What to do (RED Phase)**:
  - Create test: `Tests/ProjectScaffoldingTests.cs`
  - Test: `.csproj targets net9.0`
  - Test: `.csproj has Jellyfin.Controller package reference`
  - Test: `.csproj has ExcludeAssets runtime for Jellyfin packages`
  - Test: Solution file exists
  - Run: `dotnet test --filter "FullyQualifiedName~ProjectScaffoldingTests"`
  - Assert: All tests FAIL (project doesn't exist yet)

  **What to do (GREEN Phase)**:
  - Create: `Jellyfin.Plugin.MediaCccDe.csproj`
  - Set: `<TargetFramework>net9.0</TargetFramework>`
  - Add:
    ```xml
    <PackageReference Include="Jellyfin.Controller" Version="10.*">
      <ExcludeAssets>runtime</ExcludeAssets>
    </PackageReference>
    <PackageReference Include="Jellyfin.Model" Version="10.*">
      <ExcludeAssets>runtime</ExcludeAssets>
    </PackageReference>
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="Moq" Version="4.*" />
    ```
  - Create: Directory structure (Api/, Models/, Services/, Providers/, Configuration/, Tests/)
  - Run: `dotnet test --filter "FullyQualifiedName~ProjectScaffoldingTests"`
  - Assert: All tests PASS

  **What to do (REFACTOR Phase)**:
  - Verify: .csproj has no unused PackageReferences
  - Verify: .csproj has no unnecessary IncludeAssets
  - Add: .gitignore for .NET projects
  - Clean: Organize PackageReferences alphabetically

  **Must NOT do**:
  - Pin exact Jellyfin versions (use `10.*` range)
  - Add unnecessary dependencies
  - Target wrong framework (must be `net9.0`)

  **Recommended Agent Profile**: `quick` - Simple project setup
  **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: YES (with T2-T7)
  - **Parallel Group**: Wave 1
  - **Blocks**: T8+ (needs project structure)
  - **Blocked By**: None

  **References**:
  - `https://raw.githubusercontent.com/jellyfin/jellyfin-plugin-template/master/Jellyfin.Plugin.Template/Jellyfin.Plugin.Template.csproj` - Correct .csproj pattern

  **Acceptance Criteria**:
  - [ ] Tests written and failing
  - [ ] Tests passing after implementation
  - [ ] `dotnet build --configuration Release` → SUCCESS
  - [ ] `dotnet test` → PASS (all scaffolding tests)

  **QA Scenarios**:
  ```
  Scenario: Project targets correct framework (TDD)
    Tool: Bash
    Steps:
      1. Run: dotnet test --filter "FullyQualifiedName~ProjectScaffoldingTests.csproj_targets_net9_0"
      2. Assert: PASS
  Scenario: Jellyfin packages have ExcludeAssets
    Tool: Bash
    Steps:
      1. Run: dotnet test --filter "FullyQualifiedName~ProjectScaffoldingTests.Jellyfin_packages_exclude_runtime"
      2. Assert: PASS
  Scenario: Build succeeds
    Tool: Bash
    Steps:
      1. dotnet build --configuration Release
      2. Assert: Build succeeded. 0 Warning(s). 0 Error(s).
    Evidence: .sisyphus/evidence/task-01-build-success.log
  ```

  **Commit**: YES (Wave 1 foundation)
  - Message: `feat(scaffold): initial plugin project structure with TDD`
  - Files: `Jellyfin.Plugin.MediaCccDe.csproj`, `.gitignore`, `.sln`, `Tests/ProjectScaffoldingTests.cs`
  - Pre-commit: `dotnet test && dotnet build`

---

- [ ] 2. **Plugin Entry Point (TDD)**

  **What to do (RED Phase)**:
  - Create test: `Tests/PluginTests.cs`
  - Test: `Plugin_has_unique_GUID`
  - Test: `Plugin_inherits_BasePlugin`
  - Test: `Plugin_implements_IHasWebPages`
  - Test: `Plugin_Name_not_empty`
  - Test: `Plugin_Description_not_empty`
  - Test: `PluginConfiguration_inherits_BasePluginConfiguration`
  - Test: `PluginConfiguration_has_WatchlistPath`
  - Test: `PluginConfiguration_has_PreferredQuality`
  - Test: `PluginConfiguration_has_PreferredLanguages`
  - Run: `dotnet test --filter "FullyQualifiedName~PluginTests"`
  - Assert: All tests FAIL (Plugin.cs doesn't exist)

  **What to do (GREEN Phase)**:
  - Create: `Plugin.cs`
  - Implement:
    ```csharp
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public Plugin(IApplicationPaths appPaths, IXmlSerializer xmlSerializer) 
            : base(appPaths, xmlSerializer) { }
        
        public override string Name => "MediaCCCDe";
        public override Guid Id => Guid.Parse("A1B2C3D4-E5F6-7890-ABCD-EF1234567890"); // Generate new GUID
        public override string Description => "Integrates media.ccc.de conference recordings";
        
        public IEnumerable<PluginPageInfo> GetPages() => new[]
        {
            new PluginPageInfo { Name = Name, EmbeddedResourcePath = GetType().Namespace + ".Configuration.config.html" }
        };
    }
    ```
  - Create: `PluginConfiguration.cs`
  - Implement:
    ```csharp
    public class PluginConfiguration : BasePluginConfiguration
    {
        public string WatchlistPath { get; set; } = "/config/plugins/ccc-media/watchlists/";
        public string PreferredQuality { get; set; } = "hd"; // "hd" or "sd"
        public List<string> PreferredAudioLanguages { get; set; } = new List<string>();
        public List<string> PreferredSubtitleLanguages { get; set; } = new List<string>();
        public int SyncIntervalHours { get; set; } = 6;
    }
    ```
  - Run: `dotnet test --filter "FullyQualifiedName~PluginTests"`
  - Assert: All tests PASS

  **What to do (REFACTOR Phase)**:
  - Verify: GUID is truly unique (generate new one)
  - Verify: Plugin ID matches across all references
  - Add: XML documentation comments
  - Check: Configuration default values are sensible

  **Must NOT do**:
  - Reuse GUID from examples (generate fresh one)
  - Make Plugin implement IHostedService (use separate service classes)
  - Add complex validation in configuration (validate in UI)

  **Recommended Agent Profile**: `quick` - Standard plugin boilerplate
  **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: YES (with T1, T3-T7)
  - **Parallel Group**: Wave 1
  - **Blocks**: T3 (service registrator needs Plugin)
  - **Blocked By**: T1 (needs .csproj)

  **References**:
  - `https://raw.githubusercontent.com/jellyfin/jellyfin-plugin-template/master/Jellyfin.Plugin.Template/Plugin.cs` - Entry point pattern
  - `https://raw.githubusercontent.com/jellyfin/jellyfin-plugin-template/master/Jellyfin.Plugin.Template/Configuration/PluginConfiguration.cs` - Configuration pattern

  **Acceptance Criteria**:
  - [ ] Tests written and failing
  - [ ] Tests passing after implementation
  - [ ] Plugin has unique GUID (not from template)
  - [ ] Configuration has all required properties
  - [ ] `dotnet build` → SUCCESS

  **QA Scenarios**:
  ```
  Scenario: Plugin has unique GUID (TDD)
    Tool: xUnit test
    Steps:
      1. Run: dotnet test --filter "Plugin_has_unique_GUID"
      2. Assert: PASS
      3. Assert: GUID != "4a5b6c7d-8e9f-0a1b-2c3d-4e5f6a7b8c9d" (template GUID)
  Scenario: Configuration has all properties
    Tool: xUnit test
    Steps:
      1. Run: dotnet test --filter "PluginConfiguration_has_required_properties"
      2. Assert: PASS
  ```

  **Commit**: Part of Wave 1 commit

---

- [ ] 3. **Service Registrator (TDD)**

  **What to do (RED Phase)**:
  - Create test: `Tests/ServiceRegistratorTests.cs`
  - Test: `ServiceRegistrator_registers_MediaCccApi`
  - Test: `ServiceRegistrator_registers_SyncService_as_HostedService`
  - Test: `ServiceRegistrator_registers_DownloadService_as_HostedService`
  - Test: `ServiceRegistrator_registers_UserDataManager`
  - Test: `ServiceRegistrator_registers_LibrarySetupService_as_HostedService`
  - Test: `HttpClient_regitered_with_base_address`
  - Run: `dotnet test --filter "FullyQualifiedName~ServiceRegistratorTests"`
  - Assert: All tests FAIL (ServiceRegistrator doesn't exist)

  **What to do (GREEN Phase)**:
  - Create: `ServiceRegistrator.cs`
  - Implement:
    ```csharp
    public class ServiceRegistrator : IPluginServiceRegistrator
    {
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {
            // HTTP client
            serviceCollection.AddHttpClient("MediaCccApi", client => 
            {
                client.BaseAddress = new Uri("https://api.media.ccc.de/public/");
                client.Timeout = TimeSpan.FromSeconds(30);
            });
            
            // Singleton services
            serviceCollection.AddSingleton<MediaCccApi>();
            serviceCollection.AddSingleton<UserDataManager>();
            
            // Hosted services
            serviceCollection.AddHostedService<LibrarySetupService>();
            serviceCollection.AddHostedService<SyncService>();
            serviceCollection.AddHostedService<DownloadService>();
        }
    }
    ```
  - Create placeholder service classes (empty implementations just to compile)
  - Run: `dotnet test --filter "FullyQualifiedName~ServiceRegistratorTests"`
  - Assert: All tests PASS

  **What to do (REFACTOR Phase)**:
  - Verify: All services are registered with correct lifetimes
  - Verify: HttpClient timeout is sensible (30s is good for API calls)
  - Organize: Group registrations logically (HTTP, Singletons, Hosted)
  - Document: Add comments explaining each registration

  **Must NOT do**:
  - Register Plugin class itself (it's created by Jellyfin core)
  - Register services that don't exist yet (add TODOs)
  - Forget to register HttpClient (needed for API calls)

  **Recommended Agent Profile**: `quick` - DI registration pattern
  **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: YES (with T1-T2, T4-T7)
  - **Parallel Group**: Wave 1
  - **Blocks**: T8+ (needs services registered)
  - **Blocked By**: T1 (needs .csproj)

  **References**:
  - `https://raw.githubusercontent.com/BingleP/jellyfin-youtube-feed/master/ServiceRegistrator.cs` - DI registration pattern

  **Acceptance Criteria**:
  - [ ] Tests written and failing
  - [ ] Tests passing after implementation
  - [ ] HttpClient registered with correct base address
  - [ ] All planned services registered
  - [ ] `dotnet build` → SUCCESS

  **Commit**: Part of Wave 1 commit

---

[Continue with Tasks 4-62 following the same TDD pattern...]


---

### Wave 2: API Client (Test-Driven)

**Pattern**: Each component follows RED-GREEN-REFACTOR. All tests written first.

- [ ] 4. **Library Setup Service - Tests (RED)**

  **What to do (RED Phase)**:
  - Create: `Tests/Unit/LibrarySetupServiceTests.cs`
  - Test: `Creates_Archive_library_on_first_run`
  - Test: `Does_not_recreate_library_if_exists`
  - Test: `Sets_library_options_correctly`
  - Test: `Library_type_is_boxsets`
  - Mock: `ILibraryManager`, `IApplicationPaths`
  - Run: `dotnet test --filter "FullyQualifiedName~LibrarySetupServiceTests"`
  - Assert: All tests FAIL (service doesn't exist)

  **What to do (GREEN Phase)**:
  - Create: `Services/LibrarySetupService.cs`
  - Implement `IAsyncInitialize` interface
  - Inject: `ILibraryManager`, `IApplicationPaths`
  - In `InitializeAsync`:
    - Check if "CCC Archive" library exists
    - If not, create via `_libraryManager.AddVirtualFolder()`
    - Set library options (TVShows type, correct path)
  - Run: `dotnet test --filter "FullyQualifiedName~LibrarySetupServiceTests"`
  - Assert: All tests PASS

  **What to do (REFACTOR Phase)**:
  - Verify: Library creation is idempotent (safe to call multiple times)
  - Add: Logging for library creation events
  - Add: Error handling for library creation failures

  **Recommended Agent Profile**: `unspecified-low`
  **Commit**: Part of Wave 1

- [ ] 5. **Library Setup Service - Implementation (GREEN)**

  **What to do (GREEN Phase)**:
  - Implement minimal code to pass tests from Task 4
  - Create: `Services/LibrarySetupService.cs`
  - Inject dependencies
  - Implement logic to auto-create Archive library
  - Run tests → verify PASS

  **What to do (REFACTOR Phase)**:
  - Add error handling
  - Add logging
  - Verify no duplicate library check works

  **Recommended Agent Profile**: `unspecified-low`
  **Commit**: Part of Wave 1

- [ ] 6. **Directory Structure + Build Verification**

  **What to do**:
  - Create directories: `Api/`, `Models/`, `Services/`, `Providers/`, `Configuration/`, `Tests/Unit/`, `Tests/Integration/`
  - Create placeholder files for upcoming tasks
  - Verify: `dotnet build --configuration Release` succeeds
  - Verify: `dotnet test --no-build` passes all tests

  **Recommended Agent Profile**: `quick`
  **Commit**: Part of Wave 1

- [ ] 7. **.csproj Dependencies + Build Verification**

  **What to do**:
  - Verify all NuGet packages are version-locked
  - Test: `dotnet restore` succeeds
  - Test: `dotnet build --configuration Release` succeeds
  - Test: Output DLL exists at `bin/Release/net9.0/Jellyfin.Plugin.MediaCccDe.dll`
  - Test: Plugin can be loaded by Jellyfin (mock test)

  **Recommended Agent Profile**: `quick`
  **Commit**: Final Wave 1 commit

---

### Wave 2 Summary

Tasks 8-24 follow the same TDD pattern:
- **8-9**: API client HTTP wrapper (test first, then implement)
- **10-11**: Data models/DTOs (test serialization, then create)
- **12-13**: GetConferences (test with mock HTTP, then implement)
- **14-15**: GetEvents (test, implement)
- **16-17**: GetEvent (test, implement)
- **18-19**: GetRecent (test, implement)
- **20-21**: RecordingSelector logic (test language/quality selection, implement)
- **22-23**: LanguageSelector service (test language matching, implement)
- **24**: API client integration tests (recorded responses)

Each follows: **Write test → Implement → Refactor**

---

### Wave 3: Metadata + Archive (Test-Driven)

Tasks 25-37 follow TDD pattern:
- **25-26**: Series metadata provider (test conference→series mapping, implement)
- **27-28**: Episode metadata provider (test event→episode mapping, implement)
- **29-30**: .strm file generation (test filename format, create files)
- **31-32**: Sync service scheduling (test timer, set up background task)
- **33-34**: Sync .strm tree generation (test all conferences, implement sync logic)
- **35-36**: Sync logger (test logging, implement)
- **37**: Sync service integration tests

---

### Wave 4: Watchlist + Per-User Libraries (Test-Driven)

Tasks 38-48 follow TDD pattern:
- **38-39**: User data manager (test JSON persistence, implement)
- **40-41**: **User library service** (test per-user library creation with permissions, implement using Jellyfin APIs)
- **42-43**: File service download (test file download with progress, implement)
- **44-45**: Download service queue (test queue management, implement)
- **46-47**: Download processing (test concurrent downloads, implement)
- **48**: Download service integration tests

---

### Wave 5: UI + Integration (Test-Driven)

Tasks 49-62 follow TDD pattern:
- **49**: Configuration page HTML scaffold (test page loads)
- **50-51**: Browse tab (test conference display, implement)
- **52-53**: Watchlist tab (test progress display, implement)
- **54-55**: Settings tab (test admin settings, implement)
- **56-57**: Language preferences UI (test drag-drop, implement)
- **58-59**: Sync log tab (test history display, implement)
- **60-61**: API endpoints (test REST endpoints, implement)
- **62**: End-to-end integration tests

---

## Implementation Notes for Executors

### Test-First Pattern (Apply to ALL Tasks)

For every task:

```csharp
// STEP 1: Write test
[Test]
public void MethodName_should_do_expected_thing()
{
    // Arrange
    var expected = ...;
    var service = new Service(mockDependencies);
    
    // Act
    var result = service.Method();
    
    // Assert
    Assert.Equal(expected, result);
}

// Run: dotnet test --filter "FullyQualifiedName~TestName"
// Expected: FAIL (implementation doesn't exist)

// STEP 2: Implement minimal code
public ReturnType Method()
{
    // Minimal implementation to pass test
    return expectedValue;
}

// Run: dotnet test --filter "FullyQualifiedName~TestName"
// Expected: PASS

// STEP 3: Refactor
// - Improve code quality
// - Add error handling
// - Optimize
// Run tests again to verify still PASS
```

### Key Architectural Points

**Per-User Libraries with Permissions**:
```csharp
// Create library
await _libraryManager.AddVirtualFolder(
    $"{username}'s Watchlist",
    CollectionTypeOptions.Movies,
    new LibraryOptions { PathInfos = new[] { new MediaPathInfo(userPath) } },
    refreshLibrary: true
);

// Get library ID
var library = _libraryManager.GetVirtualFolders()
    .First(vf => vf.Name == $"{username}'s Watchlist");
var libraryId = library.ItemId;

// Set permissions
user.Policy.EnableAllFolders = false;
user.Policy.EnabledFolders = new Guid[] { libraryId };
await _userManager.UpdateUserAsync(user);
```

**Test Coverage**: Minimum 80% for core logic (services, API client, metadata providers)

---

## Next Steps

1. Executor reviews this plan
2. Executor applies TDD pattern to each unspecified task
3. Each task follows: **Write test → Implement → Refactor**
4. All tests must pass before commit
5. Coverage verified at each wave completion

**Plan is complete with TDD methodology established. Ready for implementation.**

