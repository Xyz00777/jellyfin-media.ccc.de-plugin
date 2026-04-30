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
    public class DownloadQueue : IDownloadQueue
    {
        private readonly IApplicationPaths _applicationPaths;
        private readonly ILogger<DownloadQueue> _logger;
        private readonly object _lock = new object();
        private readonly Dictionary<Guid, DownloadQueueItem> _queue = new Dictionary<Guid, DownloadQueueItem>();
        private volatile bool _initialized;
        private readonly SemaphoreSlim _initLock = new SemaphoreSlim(1, 1);

        public DownloadQueue(IApplicationPaths applicationPaths, ILogger<DownloadQueue> logger)
        {
            _applicationPaths = applicationPaths;
            _logger = logger;
        }

        private async Task EnsureInitializedAsync()
        {
            if (_initialized) return;

            await _initLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_initialized) return;
                await LoadAsync().ConfigureAwait(false);
                _initialized = true;
            }
            finally
            {
                _initLock.Release();
            }
        }

        public async Task EnqueueAsync(DownloadQueueItem item)
        {
            await EnsureInitializedAsync().ConfigureAwait(false);
            lock (_lock)
            {
                var existingItem = _queue.Values.FirstOrDefault(i =>
                    i.EventGuid == item.EventGuid && i.UserId == item.UserId);

                if (existingItem != null)
                {
                    return;
                }

                if (item.CreatedAt == default)
                {
                    item.CreatedAt = DateTime.UtcNow;
                }

                if (item.UpdatedAt == default)
                {
                    item.UpdatedAt = DateTime.UtcNow;
                }

                _queue[item.Id] = item;
            }

            await PersistAsync().ConfigureAwait(false);
        }

        public async Task<DownloadQueueItem?> DequeueAsync()
        {
            await EnsureInitializedAsync().ConfigureAwait(false);
            DownloadQueueItem? nextItem;
            lock (_lock)
            {
                nextItem = _queue.Values
                    .Where(i => i.Status == DownloadStatus.Pending)
                    .OrderBy(i => i.Priority)
                    .ThenBy(i => i.CreatedAt)
                    .FirstOrDefault();

                if (nextItem == null)
                {
                    return null;
                }

                nextItem.Status = DownloadStatus.InProgress;
                nextItem.UpdatedAt = DateTime.UtcNow;
            }

            await PersistAsync().ConfigureAwait(false);
            return nextItem;
        }

        public async Task<DownloadQueueItem?> GetItemAsync(Guid id)
        {
            await EnsureInitializedAsync().ConfigureAwait(false);
            DownloadQueueItem? item;
            lock (_lock)
            {
                _queue.TryGetValue(id, out item);
            }
            return item;
        }

        public async Task<IEnumerable<DownloadQueueItem>> GetUserQueueAsync(Guid userId)
        {
            await EnsureInitializedAsync().ConfigureAwait(false);
            List<DownloadQueueItem> items;
            lock (_lock)
            {
                items = _queue.Values.Where(i => i.UserId == userId).ToList();
            }
            return items;
        }

        public async Task MarkInProgressAsync(Guid id)
        {
            await EnsureInitializedAsync().ConfigureAwait(false);
            lock (_lock)
            {
                if (_queue.TryGetValue(id, out var item))
                {
                    item.Status = DownloadStatus.InProgress;
                    item.UpdatedAt = DateTime.UtcNow;
                }
            }

            await PersistAsync().ConfigureAwait(false);
        }

        public async Task MarkCompletedAsync(Guid id)
        {
            await EnsureInitializedAsync().ConfigureAwait(false);
            lock (_lock)
            {
                if (_queue.TryGetValue(id, out var item))
                {
                    item.Status = DownloadStatus.Completed;
                    item.UpdatedAt = DateTime.UtcNow;
                }
            }

            await PersistAsync().ConfigureAwait(false);
        }

        public async Task MarkFailedAsync(Guid id, string errorMessage)
        {
            await EnsureInitializedAsync().ConfigureAwait(false);
            lock (_lock)
            {
                if (_queue.TryGetValue(id, out var item))
                {
                    item.Status = DownloadStatus.Failed;
                    item.ErrorMessage = errorMessage;
                    item.UpdatedAt = DateTime.UtcNow;
                }
            }

            await PersistAsync().ConfigureAwait(false);
        }

        public async Task UpdateProgressAsync(Guid id, double progress)
        {
            await EnsureInitializedAsync().ConfigureAwait(false);
            lock (_lock)
            {
                if (_queue.TryGetValue(id, out var item))
                {
                    item.Progress = progress;
                    item.UpdatedAt = DateTime.UtcNow;
                }
            }

            await PersistAsync().ConfigureAwait(false);
        }

        public async Task<int> GetQueueLengthAsync()
        {
            await EnsureInitializedAsync().ConfigureAwait(false);
            int count;
            lock (_lock)
            {
                count = _queue.Values.Count(i => i.Status == DownloadStatus.Pending);
            }
            return count;
        }

        public async Task RemoveAsync(Guid id)
        {
            await EnsureInitializedAsync().ConfigureAwait(false);
            lock (_lock)
            {
                _queue.Remove(id);
            }

            await PersistAsync().ConfigureAwait(false);
        }

        private readonly SemaphoreSlim _persistLock = new SemaphoreSlim(1, 1);

        private async Task PersistAsync()
        {
            string filePath;
            string json;
            lock (_lock)
            {
                filePath = GetFilePath();
                var itemsToSave = _queue.Values.ToList();
                json = JsonSerializer.Serialize(itemsToSave, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                });
            }

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await _persistLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var tempPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await File.WriteAllTextAsync(tempPath, json).ConfigureAwait(false);
                    File.Move(tempPath, filePath, overwrite: true);
                }
                catch
                {
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                    throw;
                }
            }
            finally
            {
                _persistLock.Release();
            }
        }

        private async Task LoadAsync()
        {
            var filePath = GetFilePath();

            if (!File.Exists(filePath))
            {
                lock (_lock)
                {
                    _queue.Clear();
                }
                return;
            }

            try
            {
                var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
                var items = JsonSerializer.Deserialize<List<DownloadQueueItem>>(json, new JsonSerializerOptions
                {
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                });

                lock (_lock)
                {
                    _queue.Clear();
                    if (items != null)
                    {
                        foreach (var item in items)
                        {
                            _queue[item.Id] = item;
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse download queue from {FilePath}. Starting with empty queue.", filePath);

                lock (_lock)
                {
                    _queue.Clear();
                }
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Failed to read download queue from {FilePath}. Starting with empty queue.", filePath);

                lock (_lock)
                {
                    _queue.Clear();
                }
            }
        }

        private string GetFilePath()
        {
            return Path.Combine(_applicationPaths.DataPath, "plugins", "ccc-media", "data", "download-queue.json");
        }
    }
}