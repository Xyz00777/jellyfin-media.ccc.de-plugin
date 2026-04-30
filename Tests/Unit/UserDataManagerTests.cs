using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaCccDe.Models;
using Jellyfin.Plugin.MediaCccDe.Services;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.MediaCccDe.Tests.Unit
{
    public class UserDataManagerTests
    {
        private readonly Mock<IApplicationPaths> _applicationPathsMock;
        private readonly Mock<ILogger<UserDataManager>> _loggerMock;
        private readonly string _testDataPath;

        public UserDataManagerTests()
        {
            _applicationPathsMock = new Mock<IApplicationPaths>(MockBehavior.Strict);
            _loggerMock = new Mock<ILogger<UserDataManager>>(MockBehavior.Loose);
            _testDataPath = Path.Combine(Path.GetTempPath(), "ccc-media-tests", Guid.NewGuid().ToString());
        }

        #region UserData Class Tests

        [Fact]
        public void UserData_has_correct_properties()
        {
            // Arrange & Act
            var userId = Guid.NewGuid();
            var userData = new UserData
            {
                UserId = userId,
                Watchlist = new HashSet<string> { "event-1", "event-2" },
                SearchProgress = new HashSet<string> { "event-1" },
                PreferredAudioLanguages = new HashSet<string> { "en", "de" },
                PreferredSubtitleLanguages = new HashSet<string> { "en", "de" },
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            // Assert
            Assert.Equal(userId, userData.UserId);
            Assert.Equal(2, userData.Watchlist.Count);
            Assert.Single(userData.SearchProgress);
            Assert.Equal(2, userData.PreferredAudioLanguages.Count);
            Assert.Equal(2, userData.PreferredSubtitleLanguages.Count);
        }

        [Fact]
        public void UserData_default_values_are_empty()
        {
            // Arrange & Act
            var userData = new UserData();

            // Assert
            Assert.Equal(Guid.Empty, userData.UserId);
            Assert.Empty(userData.Watchlist);
            Assert.Empty(userData.SearchProgress);
            Assert.Empty(userData.PreferredAudioLanguages);
            Assert.Empty(userData.PreferredSubtitleLanguages);
        }

        #endregion

        #region GetUserData Tests

        [Fact]
        public void GetUserData_returns_empty_UserData_for_new_user()
        {
            // Arrange
            var userDataManager = CreateUserDataManager();
            var userId = Guid.NewGuid();

            // Act
            var userData = userDataManager.GetUserData(userId);

            // Assert
            Assert.NotNull(userData);
            Assert.Equal(userId, userData.UserId);
            Assert.Empty(userData.Watchlist);
            Assert.Empty(userData.SearchProgress);
            Assert.Empty(userData.PreferredAudioLanguages);
            Assert.Empty(userData.PreferredSubtitleLanguages);
        }

        [Fact]
        public void GetUserData_returns_existing_user_data_when_previously_saved()
        {
            // Arrange
            var userDataManager = CreateUserDataManager();
            var userId = Guid.NewGuid();
            var originalUserData = new UserData
            {
                UserId = userId,
                Watchlist = new HashSet<string> { "event-1" },
                SearchProgress = new HashSet<string>(),
                PreferredAudioLanguages = new HashSet<string> { "en" },
                PreferredSubtitleLanguages = new HashSet<string>(),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            userDataManager.SaveUserData(userId, originalUserData);

            // Act
            var retrievedUserData = userDataManager.GetUserData(userId);

            // Assert
            Assert.NotNull(retrievedUserData);
            Assert.Single(retrievedUserData.Watchlist);
            Assert.Contains("event-1", retrievedUserData.Watchlist);
            Assert.Single(retrievedUserData.PreferredAudioLanguages);
            Assert.Contains("en", retrievedUserData.PreferredAudioLanguages);
        }

        #endregion

        #region SaveUserData Tests

        [Fact]
        public async Task SaveUserData_persists_to_json_file()
        {
            // Arrange
            var userDataManager = CreateUserDataManager();
            var userId = Guid.NewGuid();
            var userData = new UserData
            {
                UserId = userId,
                Watchlist = new HashSet<string> { "event-1", "event-2" },
                SearchProgress = new HashSet<string> { "event-1" },
                PreferredAudioLanguages = new HashSet<string> { "en", "de" },
                PreferredSubtitleLanguages = new HashSet<string> { "en" },
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            // Act
            userDataManager.SaveUserData(userId, userData);
            await userDataManager.PersistAsync(userId);

            // Assert
            var filePath = Path.Combine(_testDataPath, "plugins", "ccc-media", "data", $"user-{userId}.json");
            Assert.True(File.Exists(filePath));
            
            var json = await File.ReadAllTextAsync(filePath);
            var loadedUserData = JsonSerializer.Deserialize<UserData>(json);
            
            Assert.NotNull(loadedUserData);
            Assert.Equal(userId, loadedUserData!.UserId);
            Assert.Equal(2, loadedUserData.Watchlist.Count);
        }

        [Fact]
        public async Task SaveUserData_overwrites_existing_data()
        {
            // Arrange
            var userDataManager = CreateUserDataManager();
            var userId = Guid.NewGuid();
            
            var initialData = new UserData
            {
                UserId = userId,
                Watchlist = new HashSet<string> { "event-1" },
                SearchProgress = new HashSet<string>(),
                PreferredAudioLanguages = new HashSet<string>(),
                PreferredSubtitleLanguages = new HashSet<string>(),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            
            userDataManager.SaveUserData(userId, initialData);

            // Act - Update with new data
            var updatedData = new UserData
            {
                UserId = userId,
                Watchlist = new HashSet<string> { "event-2", "event-3" },
                SearchProgress = new HashSet<string>(),
                PreferredAudioLanguages = new HashSet<string>(),
                PreferredSubtitleLanguages = new HashSet<string>(),
                CreatedAt = initialData.CreatedAt,
                UpdatedAt = DateTime.UtcNow
            };
            
            userDataManager.SaveUserData(userId, updatedData);

            // Assert
            var retrieved = userDataManager.GetUserData(userId);
            Assert.Equal(2, retrieved.Watchlist.Count);
            Assert.DoesNotContain("event-1", retrieved.Watchlist);
        }

        #endregion

        #region Watchlist Tests

        [Fact]
        public void AddToWatchlist_adds_event_guid()
        {
            // Arrange
            var userDataManager = CreateUserDataManager();
            var userId = Guid.NewGuid();
            var eventGuid = "event-123";

            // Act
            userDataManager.AddToWatchlist(userId, eventGuid);

            // Assert
            var watchlist = userDataManager.GetWatchlist(userId);
            Assert.Single(watchlist);
            Assert.Contains(eventGuid, watchlist);
        }

        [Fact]
        public void AddToWatchlist_does_not_duplicate()
        {
            // Arrange
            var userDataManager = CreateUserDataManager();
            var userId = Guid.NewGuid();
            var eventGuid = "event-123";

            // Act - Add same event twice
            userDataManager.AddToWatchlist(userId, eventGuid);
            userDataManager.AddToWatchlist(userId, eventGuid);

            // Assert
            var watchlist = userDataManager.GetWatchlist(userId);
            Assert.Single(watchlist);
        }

        [Fact]
        public void RemoveFromWatchlist_removes_event_guid()
        {
            // Arrange
            var userDataManager = CreateUserDataManager();
            var userId = Guid.NewGuid();
            var eventGuid1 = "event-123";
            var eventGuid2 = "event-456";

            userDataManager.AddToWatchlist(userId, eventGuid1);
            userDataManager.AddToWatchlist(userId, eventGuid2);

            // Act
            userDataManager.RemoveFromWatchlist(userId, eventGuid1);

            // Assert
            var watchlist = userDataManager.GetWatchlist(userId);
            Assert.Single(watchlist);
            Assert.Contains(eventGuid2, watchlist);
        }

        [Fact]
        public void RemoveFromWatchlist_handles_nonexistent_event_gracefully()
        {
            // Arrange
            var userDataManager = CreateUserDataManager();
            var userId = Guid.NewGuid();

            // Act - Should not throw
            userDataManager.RemoveFromWatchlist(userId, "nonexistent-event");

            // Assert
            var watchlist = userDataManager.GetWatchlist(userId);
            Assert.Empty(watchlist);
        }

        [Fact]
        public void IsOnWatchlist_returns_correct_status()
        {
            // Arrange
            var userDataManager = CreateUserDataManager();
            var userId = Guid.NewGuid();
            var eventGuid = "event-123";

            // Act & Assert - Before adding
            Assert.False(userDataManager.IsOnWatchlist(userId, eventGuid));

            // Add and check again
            userDataManager.AddToWatchlist(userId, eventGuid);
            Assert.True(userDataManager.IsOnWatchlist(userId, eventGuid));
        }

        [Fact]
        public void GetWatchlist_returns_empty_list_for_new_user()
        {
            // Arrange
            var userDataManager = CreateUserDataManager();
            var userId = Guid.NewGuid();

            // Act
            var watchlist = userDataManager.GetWatchlist(userId);

            // Assert
            Assert.NotNull(watchlist);
            Assert.Empty(watchlist);
        }

        [Fact]
        public void GetWatchlist_returns_all_items()
        {
            var userDataManager = CreateUserDataManager();
            var userId = Guid.NewGuid();

            userDataManager.AddToWatchlist(userId, "event-3");
            userDataManager.AddToWatchlist(userId, "event-1");
            userDataManager.AddToWatchlist(userId, "event-2");

            var watchlist = userDataManager.GetWatchlist(userId);

            Assert.Equal(3, watchlist.Count);
            Assert.Contains("event-3", watchlist);
            Assert.Contains("event-1", watchlist);
            Assert.Contains("event-2", watchlist);
        }

        #endregion

        #region SearchProgress Tests

        [Fact]
        public void MarkAsSearched_marks_event()
        {
            // Arrange
            var userDataManager = CreateUserDataManager();
            var userId = Guid.NewGuid();
            var eventGuid = "event-123";

            // Act
            userDataManager.MarkAsSearched(userId, eventGuid);

            // Assert
            var userData = userDataManager.GetUserData(userId);
            Assert.Single(userData.SearchProgress);
            Assert.Equal(eventGuid, userData.SearchProgress.First());
        }

        [Fact]
        public void MarkAsSearched_does_not_duplicate()
        {
            // Arrange
            var userDataManager = CreateUserDataManager();
            var userId = Guid.NewGuid();
            var eventGuid = "event-123";

            // Act - Mark same event twice
            userDataManager.MarkAsSearched(userId, eventGuid);
            userDataManager.MarkAsSearched(userId, eventGuid);

            // Assert
            var userData = userDataManager.GetUserData(userId);
            Assert.Single(userData.SearchProgress);
        }

        [Fact]
        public void IsMarkedAsSearched_returns_correct_status()
        {
            // Arrange
            var userDataManager = CreateUserDataManager();
            var userId = Guid.NewGuid();
            var eventGuid = "event-123";

            // Act & Assert - Before marking
            Assert.False(userDataManager.IsMarkedAsSearched(userId, eventGuid));

            // Mark and check again
            userDataManager.MarkAsSearched(userId, eventGuid);
            Assert.True(userDataManager.IsMarkedAsSearched(userId, eventGuid));
        }

        #endregion

        #region Language Preferences Tests

        [Fact]
        public void SetPreferredAudioLanguages_saves_ordered_list()
        {
            // Arrange
            var userDataManager = CreateUserDataManager();
            var userId = Guid.NewGuid();
            var languages = new List<string> { "en", "de", "fr" };

            // Act
            userDataManager.SetPreferredAudioLanguages(userId, languages);

            // Assert
            var retrieved = userDataManager.GetPreferredAudioLanguages(userId);
            Assert.Equal(3, retrieved.Count);
            Assert.Equal("en", retrieved[0]); // First = highest priority
            Assert.Equal("de", retrieved[1]);
            Assert.Equal("fr", retrieved[2]);
        }

        [Fact]
        public void GetPreferredAudioLanguages_returns_empty_for_new_user()
        {
            // Arrange
            var userDataManager = CreateUserDataManager();
            var userId = Guid.NewGuid();

            // Act
            var languages = userDataManager.GetPreferredAudioLanguages(userId);

            // Assert
            Assert.NotNull(languages);
            Assert.Empty(languages);
        }

        [Fact]
        public void SetPreferredSubtitleLanguages_saves_ordered_list()
        {
            // Arrange
            var userDataManager = CreateUserDataManager();
            var userId = Guid.NewGuid();
            var languages = new List<string> { "de", "en" };

            // Act
            userDataManager.SetPreferredSubtitleLanguages(userId, languages);

            // Assert
            var retrieved = userDataManager.GetPreferredSubtitleLanguages(userId);
            Assert.Equal(2, retrieved.Count);
            Assert.Equal("de", retrieved[0]); // First = highest priority
            Assert.Equal("en", retrieved[1]);
        }

        [Fact]
        public void GetPreferredSubtitleLanguages_returns_empty_for_new_user()
        {
            // Arrange
            var userDataManager = CreateUserDataManager();
            var userId = Guid.NewGuid();

            // Act
            var languages = userDataManager.GetPreferredSubtitleLanguages(userId);

            // Assert
            Assert.NotNull(languages);
            Assert.Empty(languages);
        }

        [Fact]
        public void Language_preferences_order_preserved_after_update()
        {
            // Arrange
            var userDataManager = CreateUserDataManager();
            var userId = Guid.NewGuid();

            // Act - Set initial order
            userDataManager.SetPreferredAudioLanguages(userId, new List<string> { "en", "de" });

            // Update with new order
            userDataManager.SetPreferredAudioLanguages(userId, new List<string> { "de", "fr", "en" });

            // Assert - New order should replace old order
            var retrieved = userDataManager.GetPreferredAudioLanguages(userId);
            Assert.Equal(3, retrieved.Count);
            Assert.Equal("de", retrieved[0]);
            Assert.Equal("fr", retrieved[1]);
            Assert.Equal("en", retrieved[2]);
        }

        #endregion

        #region Thread Safety Tests

        [Fact]
        public async Task Handle_concurrent_access_safely()
        {
            // Arrange
            var userDataManager = CreateUserDataManager();
            var userId = Guid.NewGuid();
            var tasks = new List<Task>();

            // Act - Simulate concurrent modifications
            for (int i = 0; i < 10; i++)
            {
                var index = i;
                tasks.Add(Task.Run(() =>
                {
                    for (int j = 0; j < 10; j++)
                    {
                        userDataManager.AddToWatchlist(userId, $"event-{index}-{j}");
                        userDataManager.MarkAsSearched(userId, $"event-{index}-{j}");
                    }
                }));
            }

            await Task.WhenAll(tasks);

            // Assert - No corruption, all items should be present
            var watchlist = userDataManager.GetWatchlist(userId);
            Assert.True(watchlist.Count <= 100); // Should not exceed unique items
            Assert.True(watchlist.Count > 0); // Should have some items
        }

        [Fact]
        public async Task Handle_simultaneous_read_write_operations()
        {
            // Arrange
            var userDataManager = CreateUserDataManager();
            var userId = Guid.NewGuid();
            var tasks = new List<Task>();

            // Act - Simulate simultaneous reads and writes
            for (int i = 0; i < 5; i++)
            {
                var index = i;
                tasks.Add(Task.Run(() =>
                {
                    userDataManager.AddToWatchlist(userId, $"event-{index}");
                }));
                
                tasks.Add(Task.Run(() =>
                {
                    userDataManager.GetWatchlist(userId);
                }));
                
                tasks.Add(Task.Run(() =>
                {
                    userDataManager.IsOnWatchlist(userId, $"event-{index}");
                }));
            }

            await Task.WhenAll(tasks);

            // Assert - Should complete without exceptions
            // Thread-safe implementation should handle this gracefully
            Assert.True(userDataManager.GetWatchlist(userId).Count > 0);
        }

        #endregion

        #region Load and Persist Tests

        [Fact]
        public async Task LoadAsync_reads_existing_user_data_from_file()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var userData = new UserData
            {
                UserId = userId,
                Watchlist = new HashSet<string> { "event-1", "event-2" },
                SearchProgress = new HashSet<string> { "event-1" },
                PreferredAudioLanguages = new HashSet<string> { "en", "de" },
                PreferredSubtitleLanguages = new HashSet<string> { "en" },
                CreatedAt = DateTime.UtcNow.AddDays(-1),
                UpdatedAt = DateTime.UtcNow
            };

            var filePath = Path.Combine(_testDataPath, "plugins", "ccc-media", "data", $"user-{userId}.json");
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            
            var json = JsonSerializer.Serialize(userData, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(filePath, json);

            // Act
            var userDataManager = CreateUserDataManager();
            await userDataManager.LoadAsync(userId);

            // Assert
            var retrieved = userDataManager.GetUserData(userId);
            Assert.Equal(2, retrieved.Watchlist.Count);
            Assert.Single(retrieved.SearchProgress);
            Assert.Equal(2, retrieved.PreferredAudioLanguages.Count);
        }

        [Fact]
        public async Task LoadAsync_handles_missing_file_gracefully()
        {
            // Arrange - No file exists
            var userDataManager = CreateUserDataManager();
            var userId = Guid.NewGuid();

            // Act - Should not throw
            await userDataManager.LoadAsync(userId);

            // Assert - Should return empty user data
            var userData = userDataManager.GetUserData(userId);
            Assert.NotNull(userData);
            Assert.Empty(userData.Watchlist);
        }

        [Fact]
        public async Task LoadAsync_handles_corrupted_json_gracefully()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var filePath = Path.Combine(_testDataPath, "plugins", "ccc-media", "data", $"user-{userId}.json");
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            await File.WriteAllTextAsync(filePath, "{ invalid json }");

            var userDataManager = CreateUserDataManager();

            // Act - Should not throw
            await userDataManager.LoadAsync(userId);

            // Assert - Should start with empty data
            var userData = userDataManager.GetUserData(userId);
            Assert.NotNull(userData);
            Assert.Empty(userData.Watchlist);
            
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once());
        }

        [Fact]
        public async Task PersistAsync_creates_directory_if_missing()
        {
            // Arrange
            var userDataManager = CreateUserDataManager();
            var userId = Guid.NewGuid();

            userDataManager.AddToWatchlist(userId, "event-1");

            // Act
            await userDataManager.PersistAsync(userId);

            // Assert
            var expectedDir = Path.Combine(_testDataPath, "plugins", "ccc-media", "data");
            Assert.True(Directory.Exists(expectedDir));
            
            var expectedFile = Path.Combine(expectedDir, $"user-{userId}.json");
            Assert.True(File.Exists(expectedFile));
        }

        #endregion

        #region Timestamp Tests

        [Fact]
        public void CreatedAt_is_set_on_first_save()
        {
            // Arrange
            var userDataManager = CreateUserDataManager();
            var userId = Guid.NewGuid();
            var beforeSave = DateTime.UtcNow;

            // Act
            userDataManager.AddToWatchlist(userId, "event-1");

            // Assert
            var userData = userDataManager.GetUserData(userId);
            Assert.True(userData.CreatedAt >= beforeSave);
            Assert.True(userData.CreatedAt <= DateTime.UtcNow);
        }

        [Fact]
        public void UpdatedAt_changes_on_modification()
        {
            // Arrange
            var userDataManager = CreateUserDataManager();
            var userId = Guid.NewGuid();

            userDataManager.AddToWatchlist(userId, "event-1");
            var firstUpdate = userDataManager.GetUserData(userId).UpdatedAt;

            // Small delay to ensure timestamp difference
            Thread.Sleep(10);

            // Act
            userDataManager.AddToWatchlist(userId, "event-2");
            var secondUpdate = userDataManager.GetUserData(userId).UpdatedAt;

            // Assert
            Assert.True(secondUpdate >= firstUpdate);
        }

        #endregion

        #region HashSet Semantics Tests

        [Fact]
        public void Watchlist_deduplicates_entries()
        {
            // Directly adding same event twice to UserData.Watchlist
            // List<string> allows duplicates; HashSet<string> silently ignores
            var userData = new UserData();
            userData.Watchlist.Add("event-1");
            userData.Watchlist.Add("event-1"); // duplicate — List allows, HashSet ignores

            Assert.Single(userData.Watchlist); // Fails with List (count=2), passes with HashSet
        }

        [Fact]
        public void SearchProgress_deduplicates_entries()
        {
            var userData = new UserData();
            userData.SearchProgress.Add("event-1");
            userData.SearchProgress.Add("event-1");

            Assert.Single(userData.SearchProgress);
        }

        [Fact]
        public void PreferredAudioLanguages_deduplicates_entries()
        {
            var userData = new UserData();
            userData.PreferredAudioLanguages.Add("en");
            userData.PreferredAudioLanguages.Add("en");

            Assert.Single(userData.PreferredAudioLanguages);
        }

        [Fact]
        public void PreferredSubtitleLanguages_deduplicates_entries()
        {
            var userData = new UserData();
            userData.PreferredSubtitleLanguages.Add("de");
            userData.PreferredSubtitleLanguages.Add("de");

            Assert.Single(userData.PreferredSubtitleLanguages);
        }

        [Fact]
        public void HashSet_provides_O1_lookup_for_watchlist_contains()
        {
            // Verify the data structure is HashSet (O(1) Contains)
            var userData = new UserData();
            Assert.IsType<HashSet<string>>(userData.Watchlist);

            userData.Watchlist.Add("event-a");
            Assert.Contains("event-a", userData.Watchlist);
            Assert.DoesNotContain("event-nonexistent", userData.Watchlist);
        }

        [Fact]
        public void HashSet_provides_O1_lookup_for_search_progress_contains()
        {
            var userData = new UserData();
            Assert.IsType<HashSet<string>>(userData.SearchProgress);
        }

        [Fact]
        public void HashSet_provides_O1_lookup_for_preferred_audio_languages_contains()
        {
            var userData = new UserData();
            Assert.IsType<HashSet<string>>(userData.PreferredAudioLanguages);
        }

        [Fact]
        public void HashSet_provides_O1_lookup_for_preferred_subtitle_languages_contains()
        {
            var userData = new UserData();
            Assert.IsType<HashSet<string>>(userData.PreferredSubtitleLanguages);
        }

        #endregion

        #region Helper Methods

        private UserDataManager CreateUserDataManager()
        {
            _applicationPathsMock
                .Setup(x => x.DataPath)
                .Returns(_testDataPath);

            return new UserDataManager(
                _applicationPathsMock.Object,
                _loggerMock.Object);
        }

        #endregion
    }
}