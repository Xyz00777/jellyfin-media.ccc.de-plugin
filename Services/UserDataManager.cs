using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
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
        private readonly ConcurrentDictionary<Guid, Lazy<Task>> _loads = new ConcurrentDictionary<Guid, Lazy<Task>>();
        private readonly SemaphoreSlim _persistLock = new SemaphoreSlim(1, 1);

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

        public bool AddToWatchlistIfMissing(Guid userId, string eventGuid)
        {
            lock (_lock)
            {
                var userData = GetOrCreateUserData(userId);

                if (!userData.Watchlist.Add(eventGuid))
                {
                    return false;
                }

                userData.UpdatedAt = DateTime.UtcNow;
                return true;
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
                userData.PreferredAudioLanguageOrder = languages.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                userData.UpdatedAt = DateTime.UtcNow;
            }
        }

        public List<string> GetPreferredAudioLanguages(Guid userId)
        {
            lock (_lock)
            {
                var userData = GetOrCreateUserData(userId);
                return userData.PreferredAudioLanguageOrder.Count > 0
                    ? userData.PreferredAudioLanguageOrder.ToList()
                    : userData.PreferredAudioLanguages.ToList();
            }
        }

        public void SetPreferredSubtitleLanguages(Guid userId, List<string> languages)
        {
            lock (_lock)
            {
                var userData = GetOrCreateUserData(userId);
                userData.PreferredSubtitleLanguages = new HashSet<string>(languages);
                userData.PreferredSubtitleLanguageOrder = languages.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                userData.UpdatedAt = DateTime.UtcNow;
            }
        }

        public List<string> GetPreferredSubtitleLanguages(Guid userId)
        {
            lock (_lock)
            {
                var userData = GetOrCreateUserData(userId);
                return userData.PreferredSubtitleLanguageOrder.Count > 0
                    ? userData.PreferredSubtitleLanguageOrder.ToList()
                    : userData.PreferredSubtitleLanguages.ToList();
            }
        }

        public async Task PersistAsync(Guid userId)
        {
            await _persistLock.WaitAsync().ConfigureAwait(false);
            try
            {
                string filePath;
                UserData snapshot;

                lock (_lock)
                {
                    filePath = GetUserFilePath(userId);

                    if (!_cache.TryGetValue(userId, out var dataToSave))
                    {
                        return;
                    }

                    snapshot = new UserData
                    {
                        UserId = dataToSave.UserId,
                        Watchlist = new HashSet<string>(dataToSave.Watchlist),
                        SearchProgress = new HashSet<string>(dataToSave.SearchProgress),
                        PreferredAudioLanguages = new HashSet<string>(dataToSave.PreferredAudioLanguages),
                        PreferredSubtitleLanguages = new HashSet<string>(dataToSave.PreferredSubtitleLanguages),
                        PreferredAudioLanguageOrder = dataToSave.PreferredAudioLanguageOrder.ToList(),
                        PreferredSubtitleLanguageOrder = dataToSave.PreferredSubtitleLanguageOrder.ToList(),
                        CreatedAt = dataToSave.CreatedAt,
                        UpdatedAt = dataToSave.UpdatedAt
                    };
                }

                var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                });

                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var tempPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await File.WriteAllTextAsync(tempPath, json).ConfigureAwait(false);
                    File.Move(tempPath, filePath, overwrite: true);
                }
                finally
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
            }
            finally
            {
                _persistLock.Release();
            }
        }

        public async Task EnsureLoadedAsync(Guid userId)
        {
            // GetOrAdd can invoke its value factory more than once, so wrap the load in a
            // Lazy to guarantee a single execution; duplicate loads could otherwise
            // overwrite a concurrent mutation with stale file contents.
            var load = _loads
                .GetOrAdd(
                    userId,
                    id => new Lazy<Task>(() => LoadAsync(id), LazyThreadSafetyMode.ExecutionAndPublication))
                .Value;

            try
            {
                await load.ConfigureAwait(false);
            }
            catch
            {
                // Keep the single-flight task cached only while it is healthy; a faulted
                // task would otherwise be replayed for this user on every later request.
                _loads.TryRemove(userId, out _);
                throw;
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
                var userData = JsonSerializer.Deserialize<UserData>(json, new JsonSerializerOptions
                {
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                });

                lock (_lock)
                {
                    if (userData != null)
                    {
                        userData.UserId = userId;
                        if (userData.PreferredAudioLanguageOrder.Count == 0)
                        {
                            userData.PreferredAudioLanguageOrder = userData.PreferredAudioLanguages.ToList();
                        }

                        if (userData.PreferredSubtitleLanguageOrder.Count == 0)
                        {
                            userData.PreferredSubtitleLanguageOrder = userData.PreferredSubtitleLanguages.ToList();
                        }

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
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Failed to read user data from {FilePath}. Starting with empty data.", filePath);

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
