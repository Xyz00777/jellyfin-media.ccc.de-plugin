# Task 41: User Library Service Implementation

## Implementation Complete

### Files Created/Modified

1. **Models/UserLibrary.cs** - UserLibrary model with properties:
   - `LibraryId` (Guid)
   - `LibraryName` (string)
   - `Path` (string)
   - `UserId` (Guid)

2. **Services/IUserLibraryService.cs** - Interface defining:
   - `GetOrCreateUserLibraryAsync(userId, username)` - Gets or creates a user's watchlist library
   - `UserLibraryExistsAsync(userId)` - Checks if library exists
   - `RemoveUserLibraryAsync(userId)` - Removes the user's library

3. **Services/UserLibraryService.cs** - Full implementation:
   - Uses `ILibraryManager` to create virtual folders
   - Uses `IUserManager` to set user permissions
   - Library path: `{WatchlistPath}/{username}/`
   - Library name: `{username}'s Watchlist`
   - Collection type: `CollectionTypeOptions.movies`
   - Sanitizes special characters in username
   - Thread-safe with lock-based synchronization
   - Sets user policy: `EnableAllFolders = false`, `EnabledFolders = {libraryId}`

4. **ServiceRegistrator.cs** - Added service registration:
   - `serviceCollection.AddSingleton<IUserLibraryService, UserLibraryService>()`

### Key Implementation Details

- **Constructor dependencies**: `ILibraryManager`, `IUserManager`, `IApplicationPaths`, `ILogger<UserLibraryService>`
- **Sanitization**: Removes invalid filename characters and control characters from username
- **Idempotent**: Returns existing library if already created
- **Permissions**: Restricts user to only their watchlist library
- **Concurrent safety**: Uses lock to handle concurrent calls

### Test Coverage

Tests in `Tests/Unit/UserLibraryServiceTests.cs` verify:
- Library creation with correct properties
- Idempotent behavior for existing libraries
- Path pattern: `{pluginPath}/ccc-media/watchlists/{username}/`
- Name pattern: `{username}'s Watchlist`
- Collection type: Movies
- User permission restrictions
- Existence checks
- Library removal
- Concurrent call handling
- Invalid user handling
- Username sanitization for special characters

## GREEN Phase Complete

All components implemented to make tests pass. Next step: REFACTOR phase if needed.
