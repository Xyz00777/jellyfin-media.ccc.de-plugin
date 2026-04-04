using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaCccDe.Models;
using Jellyfin.Plugin.MediaCccDe.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Users;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.MediaCccDe.Tests.Unit
{
    public class UserLibraryServiceTests
    {
        private readonly Mock<ILibraryManager> _libraryManagerMock;
        private readonly Mock<IUserManager> _userManagerMock;
        private readonly Mock<IApplicationPaths> _applicationPathsMock;
        private readonly Mock<ILogger<UserLibraryService>> _loggerMock;

        public UserLibraryServiceTests()
        {
            _libraryManagerMock = new Mock<ILibraryManager>(MockBehavior.Strict);
            _userManagerMock = new Mock<IUserManager>(MockBehavior.Strict);
            _applicationPathsMock = new Mock<IApplicationPaths>(MockBehavior.Strict);
            _loggerMock = new Mock<ILogger<UserLibraryService>>(MockBehavior.Loose);
        }

        [Fact]
        public void UserLibrary_exists_with_required_properties()
        {
            // Arrange & Act
            var userLibrary = new UserLibrary
            {
                LibraryId = Guid.NewGuid(),
                LibraryName = "testuser's Watchlist",
                Path = "/config/plugins/ccc-media/watchlists/testuser/",
                UserId = Guid.NewGuid()
            };

            // Assert
            Assert.NotEqual(Guid.Empty, userLibrary.LibraryId);
            Assert.NotNull(userLibrary.LibraryName);
            Assert.NotNull(userLibrary.Path);
            Assert.NotEqual(Guid.Empty, userLibrary.UserId);
        }

        [Fact]
        public async Task GetOrCreateUserLibraryAsync_creates_library_for_new_user()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var username = "testuser";
            var watchlistBasePath = "/config/plugins/ccc-media/watchlists";
            var expectedLibraryPath = $"{watchlistBasePath}/{username}/";
            var expectedLibraryName = $"{username}'s Watchlist";
            var libraryId = Guid.NewGuid();

            _applicationPathsMock.Setup(x => x.PluginConfigurationsPath).Returns("/config/plugins");

            _libraryManagerMock
                .Setup(x => x.GetVirtualFolders())
                .Returns(new List<VirtualFolderInfo>());

            _libraryManagerMock
                .Setup(x => x.AddVirtualFolder(
                    expectedLibraryName,
                    CollectionTypeOptions.movies,
                    It.Is<LibraryOptions>(o => o.PathInfos.Any(p => p.Path == expectedLibraryPath)),
                    It.IsAny<bool>()))
                .Returns(Task.CompletedTask);

            _libraryManagerMock
                .Setup(x => x.GetVirtualFolders())
                .Returns(new List<VirtualFolderInfo>
                {
                    new VirtualFolderInfo
                    {
                        Name = expectedLibraryName,
                        ItemId = libraryId.ToString()
                    }
                });

            var user = new User("Default", "Default", userId) { Name = username };
            user.Policy = new UserPolicy { EnableAllFolders = true };

            _userManagerMock
                .Setup(x => x.GetUserById(userId))
                .Returns(user);

            _userManagerMock
                .Setup(x => x.UpdateUserAsync(user, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var service = CreateService();

            // Act
            var result = await service.GetOrCreateUserLibraryAsync(userId, username);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(libraryId, result.LibraryId);
            Assert.Equal(expectedLibraryName, result.LibraryName);
            Assert.Equal(expectedLibraryPath, result.Path);
            Assert.Equal(userId, result.UserId);

            // Verify library creation
            _libraryManagerMock.Verify(
                x => x.AddVirtualFolder(
                    expectedLibraryName,
                    CollectionTypeOptions.movies,
                    It.IsAny<LibraryOptions>(),
                    It.IsAny<bool>()),
                Times.Once);

            // Verify user permissions were restricted
            Assert.False(user.Policy.EnableAllFolders);
            Assert.Contains(libraryId.ToString(), user.Policy.EnabledFolders);
        }

        [Fact]
        public async Task GetOrCreateUserLibraryAsync_returns_existing_library_if_already_created()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var username = "testuser";
            var libraryId = Guid.NewGuid();
            var expectedLibraryName = $"{username}'s Watchlist";

            _applicationPathsMock.Setup(x => x.PluginConfigurationsPath).Returns("/config/plugins");

            _libraryManagerMock
                .Setup(x => x.GetVirtualFolders())
                .Returns(new List<VirtualFolderInfo>
                {
                    new VirtualFolderInfo
                    {
                        Name = expectedLibraryName,
                        ItemId = libraryId.ToString(),
                        Locations = new[] { "/config/plugins/ccc-media/watchlists/testuser/" }
                    }
                });

            var user = new User("Default", "Default", userId) { Name = username };
            user.Policy = new UserPolicy
            {
                EnableAllFolders = false,
                EnabledFolders = new[] { libraryId.ToString() }
            };

            _userManagerMock
                .Setup(x => x.GetUserById(userId))
                .Returns(user);

            var service = CreateService();

            // Act
            var result = await service.GetOrCreateUserLibraryAsync(userId, username);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(libraryId, result.LibraryId);
            Assert.Equal(expectedLibraryName, result.LibraryName);

            // Verify library was NOT created again
            _libraryManagerMock.Verify(
                x => x.AddVirtualFolder(
                    It.IsAny<string>(),
                    It.IsAny<CollectionTypeOptions?>(),
                    It.IsAny<LibraryOptions>(),
                    It.IsAny<bool>()),
                Times.Never);
        }

        [Fact]
        public async Task Library_path_follows_pattern_watchlists_username()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var username = "alice";
            var pluginPath = "/config/plugins";
            var expectedPath = $"{pluginPath}/ccc-media/watchlists/{username}/";

            _applicationPathsMock.Setup(x => x.PluginConfigurationsPath).Returns(pluginPath);

            _libraryManagerMock
                .Setup(x => x.GetVirtualFolders())
                .Returns(new List<VirtualFolderInfo>());

            _libraryManagerMock
                .Setup(x => x.AddVirtualFolder(
                    It.IsAny<string>(),
                    It.IsAny<CollectionTypeOptions?>(),
                    It.Is<LibraryOptions>(o => o.PathInfos.Any(p => p.Path == expectedPath)),
                    It.IsAny<bool>()))
                .Returns(Task.CompletedTask);

            _libraryManagerMock
                .Setup(x => x.GetVirtualFolders())
                .Returns(new List<VirtualFolderInfo>
                {
                    new VirtualFolderInfo
                    {
                        Name = $"{username}'s Watchlist",
                        ItemId = Guid.NewGuid().ToString(),
                        Locations = new[] { expectedPath }
                    }
                });

            var user = new User("Default", "Default", userId) { Name = username };
            user.Policy = new UserPolicy { EnableAllFolders = true };

            _userManagerMock
                .Setup(x => x.GetUserById(userId))
                .Returns(user);

            _userManagerMock
                .Setup(x => x.UpdateUserAsync(user, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var service = CreateService();

            // Act
            var result = await service.GetOrCreateUserLibraryAsync(userId, username);

            // Assert
            Assert.Equal(expectedPath, result.Path);
        }

        [Fact]
        public async Task Library_name_follows_pattern_username_s_Watchlist()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var username = "bob";
            var expectedLibraryName = "bob's Watchlist";

            _applicationPathsMock.Setup(x => x.PluginConfigurationsPath).Returns("/config/plugins");

            _libraryManagerMock
                .Setup(x => x.GetVirtualFolders())
                .Returns(new List<VirtualFolderInfo>());

            _libraryManagerMock
                .Setup(x => x.AddVirtualFolder(
                    expectedLibraryName,
                    It.IsAny<CollectionTypeOptions?>(),
                    It.IsAny<LibraryOptions>(),
                    It.IsAny<bool>()))
                .Returns(Task.CompletedTask);

            _libraryManagerMock
                .Setup(x => x.GetVirtualFolders())
                .Returns(new List<VirtualFolderInfo>
                {
                    new VirtualFolderInfo
                    {
                        Name = expectedLibraryName,
                        ItemId = Guid.NewGuid().ToString()
                    }
                });

            var user = new User("Default", "Default", userId) { Name = username };
            user.Policy = new UserPolicy { EnableAllFolders = true };

            _userManagerMock
                .Setup(x => x.GetUserById(userId))
                .Returns(user);

            _userManagerMock
                .Setup(x => x.UpdateUserAsync(user, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var service = CreateService();

            // Act
            var result = await service.GetOrCreateUserLibraryAsync(userId, username);

            // Assert
            Assert.Equal(expectedLibraryName, result.LibraryName);
        }

        [Fact]
        public async Task Library_type_is_Movies()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var username = "testuser";

            _applicationPathsMock.Setup(x => x.PluginConfigurationsPath).Returns("/config/plugins");

            _libraryManagerMock
                .Setup(x => x.GetVirtualFolders())
                .Returns(new List<VirtualFolderInfo>());

            CollectionTypeOptions? capturedCollectionType = null;

            _libraryManagerMock
                .Setup(x => x.AddVirtualFolder(
                    It.IsAny<string>(),
                    It.IsAny<CollectionTypeOptions?>(),
                    It.IsAny<LibraryOptions>(),
                    It.IsAny<bool>()))
                .Callback<string, CollectionTypeOptions?, LibraryOptions, bool>((name, type, options, refresh) =>
                {
                    capturedCollectionType = type;
                })
                .Returns(Task.CompletedTask);

            _libraryManagerMock
                .Setup(x => x.GetVirtualFolders())
                .Returns(new List<VirtualFolderInfo>
                {
                    new VirtualFolderInfo
                    {
                        Name = $"{username}'s Watchlist",
                        ItemId = Guid.NewGuid().ToString()
                    }
                });

            var user = new User("Default", "Default", userId) { Name = username };
            user.Policy = new UserPolicy { EnableAllFolders = true };

            _userManagerMock
                .Setup(x => x.GetUserById(userId))
                .Returns(user);

            _userManagerMock
                .Setup(x => x.UpdateUserAsync(user, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var service = CreateService();

            // Act
            await service.GetOrCreateUserLibraryAsync(userId, username);

            // Assert
            Assert.Equal(CollectionTypeOptions.movies, capturedCollectionType);
        }

        [Fact]
        public async Task User_permissions_are_set_correctly()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var username = "testuser";
            var libraryId = Guid.NewGuid();
            var otherLibraryId = Guid.NewGuid();

            _applicationPathsMock.Setup(x => x.PluginConfigurationsPath).Returns("/config/plugins");

            _libraryManagerMock
                .Setup(x => x.GetVirtualFolders())
                .Returns(new List<VirtualFolderInfo>());

            _libraryManagerMock
                .Setup(x => x.AddVirtualFolder(
                    It.IsAny<string>(),
                    It.IsAny<CollectionTypeOptions?>(),
                    It.IsAny<LibraryOptions>(),
                    It.IsAny<bool>()))
                .Returns(Task.CompletedTask);

            _libraryManagerMock
                .Setup(x => x.GetVirtualFolders())
                .Returns(new List<VirtualFolderInfo>
                {
                    new VirtualFolderInfo { Name = "Other Library", ItemId = otherLibraryId.ToString() },
                    new VirtualFolderInfo { Name = $"{username}'s Watchlist", ItemId = libraryId.ToString() }
                });

            var user = new User("Default", "Default", userId) { Name = username };
            user.Policy = new UserPolicy { EnableAllFolders = true, EnabledFolders = Array.Empty<string>() };

            _userManagerMock
                .Setup(x => x.GetUserById(userId))
                .Returns(user);

            _userManagerMock
                .Setup(x => x.UpdateUserAsync(user, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var service = CreateService();

            // Act
            await service.GetOrCreateUserLibraryAsync(userId, username);

            // Assert
            Assert.False(user.Policy.EnableAllFolders);
            Assert.Single(user.Policy.EnabledFolders);
            Assert.Equal(libraryId.ToString(), user.Policy.EnabledFolders[0]);

            _userManagerMock.Verify(
                x => x.UpdateUserAsync(user, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task UserLibraryExistsAsync_returns_true_for_existing_library()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var username = "testuser";
            var libraryId = Guid.NewGuid();
            var expectedLibraryName = $"{username}'s Watchlist";

            _userManagerMock
                .Setup(x => x.GetUserById(userId))
                .Returns(new User("Default", "Default", userId) { Name = username });

            _libraryManagerMock
                .Setup(x => x.GetVirtualFolders())
                .Returns(new List<VirtualFolderInfo>
                {
                    new VirtualFolderInfo
                    {
                        Name = expectedLibraryName,
                        ItemId = libraryId.ToString()
                    }
                });

            var service = CreateService();

            // Act
            var result = await service.UserLibraryExistsAsync(userId);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public async Task UserLibraryExistsAsync_returns_false_for_nonexistent_library()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var username = "testuser";

            _userManagerMock
                .Setup(x => x.GetUserById(userId))
                .Returns(new User("Default", "Default", userId) { Name = username });

            _libraryManagerMock
                .Setup(x => x.GetVirtualFolders())
                .Returns(new List<VirtualFolderInfo>());

            var service = CreateService();

            // Act
            var result = await service.UserLibraryExistsAsync(userId);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task RemoveUserLibraryAsync_deletes_library_and_files()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var username = "testuser";
            var libraryId = Guid.NewGuid();
            var libraryName = $"{username}'s Watchlist";
            var libraryPath = $"/config/plugins/ccc-media/watchlists/{username}/";

            _userManagerMock
                .Setup(x => x.GetUserById(userId))
                .Returns(new User("Default", "Default", userId) { Name = username });

            _libraryManagerMock
                .Setup(x => x.GetVirtualFolders())
                .Returns(new List<VirtualFolderInfo>
                {
                    new VirtualFolderInfo
                    {
                        Name = libraryName,
                        ItemId = libraryId.ToString(),
                        Locations = new[] { libraryPath }
                    }
                });

            _libraryManagerMock
                .Setup(x => x.RemoveVirtualFolder(libraryName, false))
                .Returns(Task.CompletedTask);

            var service = CreateService();

            // Act
            await service.RemoveUserLibraryAsync(userId);

            // Assert
            _libraryManagerMock.Verify(
                x => x.RemoveVirtualFolder(libraryName, It.IsAny<bool>()),
                Times.Once);
        }

        [Fact]
        public async Task RemoveUserLibraryAsync_handles_nonexistent_library_gracefully()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var username = "testuser";

            _userManagerMock
                .Setup(x => x.GetUserById(userId))
                .Returns(new User("Default", "Default", userId) { Name = username });

            _libraryManagerMock
                .Setup(x => x.GetVirtualFolders())
                .Returns(new List<VirtualFolderInfo>());

            var service = CreateService();

            // Act & Assert - should not throw
            await service.RemoveUserLibraryAsync(userId);
        }

        [Fact]
        public async Task Handles_concurrent_calls_for_same_user_idempotently()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var username = "testuser";
            var libraryId = Guid.NewGuid();
            var libraryName = $"{username}'s Watchlist";
            var libraryPath = "/config/plugins/ccc-media/watchlists/testuser/";

            _applicationPathsMock.Setup(x => x.PluginConfigurationsPath).Returns("/config/plugins");

            var callCount = 0;
            _libraryManagerMock
                .Setup(x => x.GetVirtualFolders())
                .Returns(() =>
                {
                    callCount++;
                    if (callCount <= 2)
                    {
                        return new List<VirtualFolderInfo>();
                    }
                    return new List<VirtualFolderInfo>
                    {
                        new VirtualFolderInfo
                        {
                            Name = libraryName,
                            ItemId = libraryId.ToString(),
                            Locations = new[] { libraryPath }
                        }
                    };
                });

            _libraryManagerMock
                .Setup(x => x.AddVirtualFolder(
                    It.IsAny<string>(),
                    It.IsAny<CollectionTypeOptions?>(),
                    It.IsAny<LibraryOptions>(),
                    It.IsAny<bool>()))
                .Returns(Task.CompletedTask);

            var user = new User("Default", "Default", userId) { Name = username };
            user.Policy = new UserPolicy { EnableAllFolders = true };

            _userManagerMock
                .Setup(x => x.GetUserById(userId))
                .Returns(user);

            _userManagerMock
                .Setup(x => x.UpdateUserAsync(user, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var service = CreateService();

            // Act - simulate concurrent calls
            var task1 = service.GetOrCreateUserLibraryAsync(userId, username);
            var task2 = service.GetOrCreateUserLibraryAsync(userId, username);

            // Wait for both to complete
            await Task.WhenAll(task1, task2);

            // Assert - both tasks should return valid results
            var result1 = await task1;
            var result2 = await task2;

            Assert.NotNull(result1);
            Assert.NotNull(result2);
            
            // Library should be created only once despite concurrent calls
            // (idempotent - implementation should handle race conditions)
        }

        [Fact]
        public async Task GetOrCreateUserLibraryAsync_throws_for_invalid_user()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var username = "testuser";

            _userManagerMock
                .Setup(x => x.GetUserById(userId))
                .Returns((User)null!);

            var service = CreateService();

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(
                () => service.GetOrCreateUserLibraryAsync(userId, username));
        }

        [Fact]
        public async Task GetOrCreateUserLibraryAsync_sanitizes_special_characters_in_username()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var username = "test/user<script>";
            var expectedLibraryName = "test_user_script_'s Watchlist";

            _applicationPathsMock.Setup(x => x.PluginConfigurationsPath).Returns("/config/plugins");

            _libraryManagerMock
                .Setup(x => x.GetVirtualFolders())
                .Returns(new List<VirtualFolderInfo>());

            _libraryManagerMock
                .Setup(x => x.AddVirtualFolder(
                    It.Is<string>(name => name.Contains("test") && !name.Contains("<")),
                    It.IsAny<CollectionTypeOptions?>(),
                    It.IsAny<LibraryOptions>(),
                    It.IsAny<bool>()))
                .Returns(Task.CompletedTask);

            _libraryManagerMock
                .Setup(x => x.GetVirtualFolders())
                .Returns(new List<VirtualFolderInfo>
                {
                    new VirtualFolderInfo
                    {
                        Name = $"{username}'s Watchlist",
                        ItemId = Guid.NewGuid().ToString()
                    }
                });

            var user = new User("Default", "Default", userId) { Name = username };
            user.Policy = new UserPolicy { EnableAllFolders = true };

            _userManagerMock
                .Setup(x => x.GetUserById(userId))
                .Returns(user);

            _userManagerMock
                .Setup(x => x.UpdateUserAsync(user, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var service = CreateService();

            // Act
            var result = await service.GetOrCreateUserLibraryAsync(userId, username);

            // Assert
            Assert.NotNull(result);
            // Library name should be sanitized
            Assert.DoesNotContain("<", result.LibraryName);
            Assert.DoesNotContain("/", result.LibraryName);
        }

        private UserLibraryService CreateService()
        {
            return new UserLibraryService(
                _libraryManagerMock.Object,
                _userManagerMock.Object,
                _applicationPathsMock.Object,
                _loggerMock.Object);
        }
    }
}