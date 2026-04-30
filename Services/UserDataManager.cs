using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaCccDe.Models;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediaCccDe.Services
{
    public class UserDataManager : IUserDataManager
    {
        private readonly IApplicationPaths _applicationPaths;
        private readonly ILogger<UserDataManager> _logger;
        private readonly object _lock = new object();
        private readonly Dictionary<Guid, UserData> _cache = new Dictionary<Guid, UserData>();

        public UserDataManager(IApplicationPaths applicationPaths, ILogger<UserDataManager> logger)
        {
            _applicationPaths = applicationPaths;
            _logger = logger;
        }

        public UserData GetUserData(Guid userId)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(userId, out var cachedData))
                {
                    return cachedData;
                }

                var emptyData = new UserData
                {
                    UserId = userId,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _cache[userId] = emptyData;
                return emptyData;
            }
        }

        public void SaveUserData(Guid userId, UserData data)
        {
            lock (_lock)
            {
                data.UserId = userId;
                data.UpdatedAt = DateTime.UtcNow;

                if (data.CreatedAt == default)
                {
                    data.CreatedAt = DateTime.UtcNow;
                }

                _cache[userId] = data;
            }
        }

        public void AddToWatchlist(Guid userId, string eventGuid)
        {
            lock (_lock)
            {
                var userData = GetOrCreateUserData(userId);

                if (userData.Watchlist.Add(eventGuid))
                {
                    userData.UpdatedAt = DateTime.UtcNow;
                }
            }
        }

        public void RemoveFromWatchlist(Guid userId, string eventGuid)
        {
            lock (_lock)
            {
                var userData = GetOrCreateUserData(userId);
                userData.Watchlist.Remove(eventGuid);
                userData.UpdatedAt = DateTime.UtcNow;
            }
        }

        public bool IsOnWatchlist(Guid userId, string eventGuid)
        {
            lock (_lock)
            {
                var userData = GetOrCreateUserData(userId);
                return userData.Watchlist.Contains(eventGuid);
            }
        }

        public void MarkAsSearched(Guid userId, string eventGuid)
        {
            lock (_lock)
            {
                var userData = GetOrCreateUserData(userId);

                if (userData.SearchProgress.Add(eventGuid))
                {
                    userData.UpdatedAt = DateTime.UtcNow;
                }
            }
        }

        public bool IsMarkedAsSearched(Guid userId, string eventGuid)
        {
            lock (_lock)
            {
                var userData = GetOrCreateUserData(userId);
                return userData.SearchProgress.Contains(eventGuid);
            }
        }

        public List<string> GetWatchlist(Guid userId)
        {
            lock (_lock)
            {
                var userData = GetOrCreateUserData(userId);
                return userData.Watchlist.ToList();
            }
        }

        public void SetPreferredAudioLanguages(Guid userId, List<string> languages)
        {
            lock (_lock)
            {
                var userData = GetOrCreateUserData(userId);
                userData.PreferredAudioLanguages = new HashSet<string>(languages);
                userData.UpdatedAt = DateTime.UtcNow;
            }
        }

        public List<string> GetPreferredAudioLanguages(Guid userId)
        {
            lock (_lock)
            {
                var userData = GetOrCreateUserData(userId);
                return userData.PreferredAudioLanguages.ToList();
            }
        }

        public void SetPreferredSubtitleLanguages(Guid userId, List<string> languages)
        {
            lock (_lock)
            {
                var userData = GetOrCreateUserData(userId);
                userData.PreferredSubtitleLanguages = new HashSet<string>(languages);
                userData.UpdatedAt = DateTime.UtcNow;
            }
        }

        public List<string> GetPreferredSubtitleLanguages(Guid userId)
        {
            lock (_lock)
            {
                var userData = GetOrCreateUserData(userId);
                return userData.PreferredSubtitleLanguages.ToList();
            }
        }

        public async Task PersistAsync(Guid userId)
        {
            string filePath;
            UserData? dataToSave;

            lock (_lock)
            {
                filePath = GetUserFilePath(userId);
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                _cache.TryGetValue(userId, out dataToSave);
            }

            if (dataToSave != null)
            {
                var json = JsonSerializer.Serialize(dataToSave, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                await File.WriteAllTextAsync(filePath, json).ConfigureAwait(false);
            }
        }

        public async Task LoadAsync(Guid userId)
        {
            var filePath = GetUserFilePath(userId);

            if (!File.Exists(filePath))
            {
                lock (_lock)
                {
                    if (!_cache.ContainsKey(userId))
                    {
                        _cache[userId] = GetOrCreateUserData(userId);
                    }
                }
                return;
            }

            try
            {
                var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
                var userData = JsonSerializer.Deserialize<UserData>(json);

                lock (_lock)
                {
                    if (userData != null)
                    {
                        _cache[userId] = userData;
                    }
                    else
                    {
                        _cache[userId] = GetOrCreateUserData(userId);
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse user data from {FilePath}. Starting with empty data.", filePath);

                lock (_lock)
                {
                    _cache[userId] = GetOrCreateUserData(userId);
                }
            }
        }

        private UserData GetOrCreateUserData(Guid userId)
        {
            if (_cache.TryGetValue(userId, out var data))
            {
                return data;
            }

            var newData = new UserData
            {
                UserId = userId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _cache[userId] = newData;
            return newData;
        }

        private string GetUserFilePath(Guid userId)
        {
            return Path.Combine(_applicationPaths.DataPath, "plugins", "ccc-media", "data", $"user-{userId}.json");
        }
    }
}