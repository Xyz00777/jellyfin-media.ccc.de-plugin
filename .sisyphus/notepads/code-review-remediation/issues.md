# Issues & Gotchas — Code Review Remediation

## Cross-cutting patterns to watch for:
- **TOCTOU everywhere**: File.Exists → File.Write, File.Exists → File.Delete patterns are not atomic
- **Sync-over-async**: Several places call `.GetAwaiter().GetResult()` or do blocking IO in async paths
- **Lock scope inconsistency**: Some methods snapshot inside lock, some read outside lock, some hold lock during IO
- **Mutable defaults**: Constructors expose objects in invalid state (Guid.Empty, zero, empty strings)
- **Naming collisions**: `Event.Guid` shadows `System.Guid`, `UserLibrary.Path` shadows `System.IO.Path`

## Known blockers:
- P0-1 must be solved first — plugin cannot build/start without DI fixes
- Rename cascades (Path→LibraryPath, Guid→EventGuid) affect many files — plan carefully
