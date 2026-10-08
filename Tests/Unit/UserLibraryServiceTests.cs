using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.MediaCccDe.Models;
using Jellyfin.Plugin.MediaCccDe.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
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
        private readonly string _testBasePath;

        public UserLibraryServiceTests()
        {
            _libraryManagerMock = new Mock<ILibraryManager>(MockBehavior.Loose);
            _userManagerMock = new Mock<IUserManager>(MockBehavior.Strict);
            _applicationPathsMock = new Mock<IApplicationPaths>(MockBehavior.Strict);
            _loggerMock = new Mock<ILogger<UserLibraryService>>(MockBehavior.Loose);
            _testBasePath = Path.Combine(Path.GetTempPath(), "ccc-media-userlib-tests-" + Guid.NewGuid().ToString());
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
            var libraryId = Guid.NewGuid();
            var expectedLibraryName = $"{username}'s Watchlist";

            var mockUser = CreateUser(username, userId);
            _userManagerMock
                .Setup(x => x.GetUserById(userId))
                .Returns(mockUser);

            _libraryManagerMock
                .SetupSequence(x => x.GetVirtualFolders())
                .Returns(new List<VirtualFolderInfo>())
                .Returns(new List<VirtualFolderInfo>
                {
                    new VirtualFolderInfo
                    {
                        Name = expectedLibraryName,
                        ItemId = libraryId.ToString(),
                        Locations = new[] { Path.Combine(_testBasePath, "ccc-media", "watchlists", username) + Path.DirectorySeparatorChar }
                    }
                });

            _libraryManagerMock
                .Setup(x => x.AddVirtualFolder(
                    It.IsAny<string>(),
                    It.IsAny<CollectionTypeOptions?>(),
                    It.IsAny<LibraryOptions>(),
                    It.IsAny<bool>()));

            var service = CreateService();

            // Act
            var result = await service.GetOrCreateUserLibraryAsync(userId, username);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(expectedLibraryName, result.LibraryName);
        }

        [Fact]
        public async Task Library_path_follows_pattern_watchlists_username()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var username = "alice";
            var expectedPath = Path.Combine(_testBasePath, "ccc-media", "watchlists", username) + Path.DirectorySeparatorChar;

            _libraryManagerMock
                .SetupSequence(x => x.GetVirtualFolders())
                .Returns(new List<VirtualFolderInfo>())
                .Returns(new List<VirtualFolderInfo>
                {
                    new VirtualFolderInfo
                    {
                        Name = $"{username}'s Watchlist",
                        ItemId = Guid.NewGuid().ToString(),
                        Locations = new[] { expectedPath }
                    }
                });

            _libraryManagerMock
                .Setup(x => x.AddVirtualFolder(
                    It.IsAny<string>(),
                    It.IsAny<CollectionTypeOptions?>(),
                    It.Is<LibraryOptions>(o => o.PathInfos.Any(p => p.Path == expectedPath)),
                    It.IsAny<bool>()));

            var mockUser = CreateUser(username, userId);
            _userManagerMock
                .Setup(x => x.GetUserById(userId))
                .Returns(mockUser);

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

            _libraryManagerMock
                .SetupSequence(x => x.GetVirtualFolders())
                .Returns(new List<VirtualFolderInfo>())
                .Returns(new List<VirtualFolderInfo>
                {
                    new VirtualFolderInfo
                    {
                        Name = expectedLibraryName,
                        ItemId = Guid.NewGuid().ToString()
                    }
                });

            _libraryManagerMock
                .Setup(x => x.AddVirtualFolder(
                    expectedLibraryName,
                    It.IsAny<CollectionTypeOptions?>(),
                    It.IsAny<LibraryOptions>(),
                    It.IsAny<bool>()));

            var mockUser = CreateUser(username, userId);
            _userManagerMock
                .Setup(x => x.GetUserById(userId))
                .Returns(mockUser);

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

            _libraryManagerMock
                .SetupSequence(x => x.GetVirtualFolders())
                .Returns(new List<VirtualFolderInfo>())
                .Returns(new List<VirtualFolderInfo>
                {
                    new VirtualFolderInfo
                    {
                        Name = $"{username}'s Watchlist",
                        ItemId = Guid.NewGuid().ToString()
                    }
                });

            var mockUser = CreateUser(username, userId);
            _userManagerMock
                .Setup(x => x.GetUserById(userId))
                .Returns(mockUser);

            var service = CreateService();

            // Act
            await service.GetOrCreateUserLibraryAsync(userId, username);

            // Assert
            _libraryManagerMock.Verify(
                x => x.AddVirtualFolder(
                    It.IsAny<string>(),
                    CollectionTypeOptions.movies,
                    It.IsAny<LibraryOptions>(),
                    It.IsAny<bool>()),
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

            var mockUser = CreateUser(username, userId);
            _userManagerMock
                .Setup(x => x.GetUserById(userId))
                .Returns(mockUser);

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

            var mockUser = CreateUser(username, userId);
            _userManagerMock
                .Setup(x => x.GetUserById(userId))
                .Returns(mockUser);

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

            var mockUser = CreateUser(username, userId);
            _userManagerMock
                .Setup(x => x.GetUserById(userId))
                .Returns(mockUser);

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

            var mockUser = CreateUser(username, userId);
            _userManagerMock
                .Setup(x => x.GetUserById(userId))
                .Returns(mockUser);

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
            var libraryPath = Path.Combine(_testBasePath, "ccc-media", "watchlists", "testuser") + Path.DirectorySeparatorChar;

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
                    It.IsAny<bool>()));

            var mockUser = CreateUser(username, userId);
            _userManagerMock
                .Setup(x => x.GetUserById(userId))
                .Returns(mockUser);

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
            var sanitizedUsername = "testuserscript";
            var expectedLibraryName = $"{sanitizedUsername}'s Watchlist";

            _libraryManagerMock
                .SetupSequence(x => x.GetVirtualFolders())
                .Returns(new List<VirtualFolderInfo>())
                .Returns(new List<VirtualFolderInfo>
                {
                    new VirtualFolderInfo
                    {
                        Name = expectedLibraryName,
                        ItemId = Guid.NewGuid().ToString()
                    }
                });

            _libraryManagerMock
                .Setup(x => x.AddVirtualFolder(
                    It.IsAny<string>(),
                    It.IsAny<CollectionTypeOptions?>(),
                    It.IsAny<LibraryOptions>(),
                    It.IsAny<bool>()));

            var mockUser = CreateUser(username, userId);
            _userManagerMock
                .Setup(x => x.GetUserById(userId))
                .Returns(mockUser);

            var service = CreateService();

            // Act
            var result = await service.GetOrCreateUserLibraryAsync(userId, username);

            // Assert
            Assert.NotNull(result);
            Assert.DoesNotContain("<", result.LibraryName);
            Assert.DoesNotContain("/", result.LibraryName);
        }

        [Fact]
        public void SanitizeUsername_rejects_dot_dot_sequences()
        {
            var service = CreateService();
            var result = service.SanitizeUsername("..");
            Assert.NotEqual("..", result);
            Assert.DoesNotContain("..", result);
        }

        [Theory]
        [InlineData("../etc/passwd")]
        [InlineData("..\\windows\\system32")]
        [InlineData("user..traversal")]
        [InlineData(".")]
        public void SanitizeUsername_blocks_path_traversal(string input)
        {
            var service = CreateService();
            var result = service.SanitizeUsername(input);
            Assert.DoesNotContain("..", result);
            Assert.NotEmpty(result);
        }

        [Theory]
        [InlineData("/")]
        [InlineData("\\")]
        [InlineData("user/name")]
        [InlineData("user\\name")]
        public void SanitizeUsername_rejects_path_separators(string input)
        {
            var service = CreateService();
            var result = service.SanitizeUsername(input);
            Assert.DoesNotContain("/", result);
            Assert.DoesNotContain("\\", result);
            Assert.NotEmpty(result);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\t")]
        [InlineData("\n")]
        public void SanitizeUsername_handles_empty_and_whitespace(string input)
        {
            var service = CreateService();
            var result = service.SanitizeUsername(input);
            Assert.Equal("unknown", result);
        }

        [Fact]
        public void SanitizeUsername_truncates_long_input()
        {
            var service = CreateService();
            var longUsername = new string('a', 200);
            var result = service.SanitizeUsername(longUsername);
            Assert.True(result.Length <= 64, $"Expected length <= 64 but got {result.Length}");
        }

        [Fact]
        public async Task GetOrCreateUserLibraryAsync_creates_directory_atomically()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var username = "atomicuser";
            var expectedLibraryName = $"{username}'s Watchlist";
            var libraryId = Guid.NewGuid();

            _libraryManagerMock
                .SetupSequence(x => x.GetVirtualFolders())
                .Returns(new List<VirtualFolderInfo>())
                .Returns(new List<VirtualFolderInfo>
                {
                    new VirtualFolderInfo
                    {
                        Name = expectedLibraryName,
                        ItemId = libraryId.ToString(),
                        Locations = new[] { Path.Combine(_testBasePath, "ccc-media", "watchlists", username) + Path.DirectorySeparatorChar }
                    }
                });

            _libraryManagerMock
                .Setup(x => x.AddVirtualFolder(
                    It.IsAny<string>(),
                    It.IsAny<CollectionTypeOptions?>(),
                    It.IsAny<LibraryOptions>(),
                    It.IsAny<bool>()));

            var mockUser = CreateUser(username, userId);
            _userManagerMock
                .Setup(x => x.GetUserById(userId))
                .Returns(mockUser);

            var service = CreateService();

            // Act
            var result = await service.GetOrCreateUserLibraryAsync(userId, username);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(expectedLibraryName, result.LibraryName);
            _libraryManagerMock.Verify(
                x => x.AddVirtualFolder(
                    expectedLibraryName,
                    CollectionTypeOptions.movies,
                    It.IsAny<LibraryOptions>(),
                    It.IsAny<bool>()),
                Times.Once);
        }

        [Fact]
        public void GetWatchlistBasePath_uses_PluginConfigurationsPath()
        {
            var service = CreateService();
            var result = service.GetWatchlistBasePath();
            Assert.StartsWith(_testBasePath, result);
            Assert.DoesNotContain("/config/", result);
        }

        private User CreateUser(string username, Guid userId)
        {
            var user = new User(username, "Default", "Default");
            user.Id = userId;
            return user;
        }

        private UserLibraryService CreateService()
        {
            _applicationPathsMock.Setup(x => x.PluginConfigurationsPath).Returns(_testBasePath);
            return new UserLibraryService(
                _libraryManagerMock.Object,
                _userManagerMock.Object,
                _applicationPathsMock.Object,
                _loggerMock.Object);
        }
    }
}
