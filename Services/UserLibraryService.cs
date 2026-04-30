using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MediaCccDe.Models;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediaCccDe.Services
{
    public class UserLibraryService : IUserLibraryService
    {
        private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars()
            .Concat(new[] { '<', '>', '"', '|', '\0', '\u0001', '\u0002', '\u0003', '\u0004', '\u0005', '\u0006', '\u0007', '\u0008', '\u0009', '\u000a', '\u000b', '\u000c', '\u000d', '\u000e', '\u000f' })
            .ToArray();

        private readonly ILibraryManager _libraryManager;
        private readonly IUserManager _userManager;
        private readonly IApplicationPaths _applicationPaths;
        private readonly ILogger<UserLibraryService> _logger;
        private readonly object _lock = new object();

        public UserLibraryService(
            ILibraryManager libraryManager,
            IUserManager userManager,
            IApplicationPaths applicationPaths,
            ILogger<UserLibraryService> logger)
        {
            _libraryManager = libraryManager;
            _userManager = userManager;
            _applicationPaths = applicationPaths;
            _logger = logger;
        }

        public async Task<UserLibrary> GetOrCreateUserLibraryAsync(Guid userId, string username)
        {
            var user = _userManager.GetUserById(userId);
            if (user == null)
            {
                throw new ArgumentException($"User with ID {userId} not found.", nameof(userId));
            }

            var sanitizedUsername = SanitizeUsername(username);
            var libraryName = $"{sanitizedUsername}'s Watchlist";
            var watchlistBasePath = GetWatchlistBasePath();
            var libraryPath = Path.Combine(watchlistBasePath, sanitizedUsername) + Path.DirectorySeparatorChar;

            lock (_lock)
            {
                var existingLibrary = FindUserLibrary(userId, sanitizedUsername);
                if (existingLibrary != null)
                {
                    return existingLibrary;
                }

                Directory.CreateDirectory(libraryPath);

                var libraryOptions = new LibraryOptions
                {
                    EnablePhotos = false,
                    EnableRealtimeMonitor = false,
                    PathInfos = new[]
                    {
                        new MediaPathInfo
                        {
                            Path = libraryPath
                        }
                    }
                };

                _libraryManager.AddVirtualFolder(libraryName, CollectionTypeOptions.movies, libraryOptions, false);

                var newLibrary = FindUserLibrary(userId, sanitizedUsername);
                if (newLibrary != null)
                {
                    return newLibrary;
                }
            }

            var finalLibrary = FindUserLibrary(userId, sanitizedUsername);
            if (finalLibrary == null)
            {
                throw new InvalidOperationException($"Failed to create library for user {username}");
            }

            await Task.CompletedTask.ConfigureAwait(false);
            return finalLibrary;
        }

        public Task<bool> UserLibraryExistsAsync(Guid userId)
        {
            var user = _userManager.GetUserById(userId);
            if (user == null)
            {
                return Task.FromResult(false);
            }

            var sanitizedUsername = SanitizeUsername(user.Username);
            var libraryName = $"{sanitizedUsername}'s Watchlist";
            var virtualFolders = _libraryManager.GetVirtualFolders();

            var exists = virtualFolders.Any(vf =>
                vf.Name != null &&
                vf.Name.Equals(libraryName, StringComparison.OrdinalIgnoreCase));

            return Task.FromResult(exists);
        }

        public async Task RemoveUserLibraryAsync(Guid userId)
        {
            var user = _userManager.GetUserById(userId);
            if (user == null)
            {
                return;
            }

            var sanitizedUsername = SanitizeUsername(user.Username);
            var libraryName = $"{sanitizedUsername}'s Watchlist";
            var virtualFolders = _libraryManager.GetVirtualFolders();

            var library = virtualFolders.FirstOrDefault(vf =>
                vf.Name != null &&
                vf.Name.Equals(libraryName, StringComparison.OrdinalIgnoreCase));

            if (library != null)
            {
                await _libraryManager.RemoveVirtualFolder(libraryName, false).ConfigureAwait(false);
            }

            await Task.CompletedTask.ConfigureAwait(false);
        }

        internal string SanitizeUsername(string username)
        {
            const int MaxLength = 64;

            if (string.IsNullOrWhiteSpace(username))
            {
                return "unknown";
            }

            var sanitized = new string(username
                .Where(c => !InvalidFileNameChars.Contains(c) && c != '/' && c != '\\')
                .ToArray());

            sanitized = new string(sanitized.Where(c =>
                !char.IsControl(c) &&
                c != '<' &&
                c != '>' &&
                c != '"' &&
                c != '|' &&
                c != '?' &&
                c != '*')
                .ToArray());

            // Strip parent-directory traversal sequences (..) repeatedly
            while (sanitized.Contains(".."))
            {
                sanitized = sanitized.Replace("..", string.Empty);
            }

            // Handle dot-only input (e.g., "." after .. stripping becomes empty)
            if (string.IsNullOrWhiteSpace(sanitized) || sanitized.Trim('.').Length == 0)
            {
                return "unknown";
            }

            // Truncate to maximum length
            if (sanitized.Length > MaxLength)
            {
                sanitized = sanitized.Substring(0, MaxLength);
            }

            return sanitized;
        }

        internal string GetWatchlistBasePath()
        {
            return Path.Combine(_applicationPaths.PluginConfigurationsPath, "ccc-media", "watchlists");
        }

        private UserLibrary? FindUserLibrary(Guid userId, string sanitizedUsername)
        {
            var libraryName = $"{sanitizedUsername}'s Watchlist";
            var virtualFolders = _libraryManager.GetVirtualFolders();

            var virtualFolder = virtualFolders.FirstOrDefault(vf =>
                vf.Name != null &&
                vf.Name.Equals(libraryName, StringComparison.OrdinalIgnoreCase));

            if (virtualFolder == null || string.IsNullOrEmpty(virtualFolder.ItemId))
            {
                return null;
            }

            if (!Guid.TryParse(virtualFolder.ItemId, out var libraryId))
            {
                return null;
            }

            var libraryPath = virtualFolder.Locations?.FirstOrDefault() ?? string.Empty;

            return new UserLibrary
            {
                LibraryId = libraryId,
                LibraryName = libraryName,
                Path = libraryPath,
                UserId = userId
            };
        }
    }
}