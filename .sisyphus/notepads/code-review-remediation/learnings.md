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
