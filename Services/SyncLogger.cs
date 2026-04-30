using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediaCccDe.Services
{
    /// <summary>
    /// Represents the status of a sync operation.
    /// </summary>
    public enum SyncStatus
    {
        Started,
        Completed,
        Failed
    }

    /// <summary>
    /// Represents a log entry for a sync operation.
    /// </summary>
    public class SyncLogEntry
    {
        public DateTime Timestamp { get; set; }
        public string ConferenceAcronym { get; set; } = string.Empty;
        public SyncStatus Status { get; set; }
        public int EventsProcessed { get; set; }
        public int FilesCreated { get; set; }
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Service for logging sync operations to JSON file for admin visibility.
    /// </summary>
    public interface ISyncLogger
    {
        /// <summary>
        /// Logs the start of a sync operation.
        /// </summary>
        void LogSyncStart(string conferenceAcronym, DateTime timestamp);

        /// <summary>
        /// Logs the completion of a sync operation.
        /// </summary>
        void LogSyncComplete(string conferenceAcronym, int eventsProcessed, int filesCreated);

        /// <summary>
        /// Logs the failure of a sync operation.
        /// </summary>
        void LogSyncFailure(string conferenceAcronym, string errorMessage, int eventsProcessed, int filesCreated);

        /// <summary>
        /// Gets sync history, optionally filtered by conference.
        /// </summary>
        IReadOnlyList<SyncLogEntry> GetSyncHistory(string? conferenceAcronym = null);

        /// <summary>
        /// Clears all sync history.
        /// </summary>
        void ClearHistory();

        /// <summary>
        /// Persists sync history to JSON file.
        /// </summary>
        Task PersistAsync();

        /// <summary>
        /// Loads sync history from JSON file.
        /// </summary>
        Task LoadAsync();

        /// <summary>
        /// Logs the completion of a sync operation with timestamp.
        /// </summary>
        Task LogSyncCompletion(int conferencesProcessed, int filesCreated, DateTime timestamp);
    }

    /// <summary>
    /// Default implementation of sync logging.
    /// Thread-safe for concurrent access.
    /// </summary>
    public class SyncLogger : ISyncLogger
    {
        private readonly IApplicationPaths _applicationPaths;
        private readonly ILogger<SyncLogger> _logger;
        private readonly int _maxHistoryEntries;
        private readonly object _lock = new object();
        private readonly SemaphoreSlim _persistLock = new SemaphoreSlim(1, 1);
        private List<SyncLogEntry> _history = new List<SyncLogEntry>();

        public SyncLogger(IApplicationPaths applicationPaths, ILogger<SyncLogger> logger, int maxHistoryEntries = 100)
        {
            _applicationPaths = applicationPaths;
            _logger = logger;
            _maxHistoryEntries = maxHistoryEntries;
        }

        public void LogSyncStart(string conferenceAcronym, DateTime timestamp)
        {
            lock (_lock)
            {
                var entry = new SyncLogEntry
                {
                    ConferenceAcronym = conferenceAcronym,
                    Timestamp = timestamp,
                    Status = SyncStatus.Started
                };

                _history.Insert(0, entry);
                TrimHistory();
            }
        }

        public void LogSyncComplete(string conferenceAcronym, int eventsProcessed, int filesCreated)
        {
            lock (_lock)
            {
                var entry = _history.FirstOrDefault(e => 
                    e.ConferenceAcronym == conferenceAcronym && 
                    e.Status == SyncStatus.Started);

                if (entry != null)
                {
                    entry.Status = SyncStatus.Completed;
                    entry.EventsProcessed = eventsProcessed;
                    entry.FilesCreated = filesCreated;
                }
            }
        }

        public void LogSyncFailure(string conferenceAcronym, string errorMessage, int eventsProcessed, int filesCreated)
        {
            lock (_lock)
            {
                var entry = _history.FirstOrDefault(e => 
                    e.ConferenceAcronym == conferenceAcronym && 
                    e.Status == SyncStatus.Started);

                if (entry != null)
                {
                    entry.Status = SyncStatus.Failed;
                    entry.ErrorMessage = errorMessage;
                    entry.EventsProcessed = eventsProcessed;
                    entry.FilesCreated = filesCreated;
                }
            }
        }

        public IReadOnlyList<SyncLogEntry> GetSyncHistory(string? conferenceAcronym = null)
        {
            lock (_lock)
            {
                if (conferenceAcronym == null)
                {
                    return _history.ToList().AsReadOnly();
                }

                return _history
                    .Where(e => e.ConferenceAcronym == conferenceAcronym)
                    .ToList()
                    .AsReadOnly();
            }
        }

        public void ClearHistory()
        {
            lock (_lock)
            {
                _history.Clear();
            }
        }

        public async Task PersistAsync()
        {
            await _persistLock.WaitAsync().ConfigureAwait(false);
            try
            {
                string filePath;
                List<SyncLogEntry> snapshot;
                lock (_lock)
                {
                    filePath = GetFilePath();
                    var directory = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    snapshot = _history.ToList();
                }

                var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                });

                await File.WriteAllTextAsync(filePath, json).ConfigureAwait(false);
            }
            finally
            {
                _persistLock.Release();
            }
        }

        public async Task LoadAsync()
        {
            var filePath = GetFilePath();

            if (!File.Exists(filePath))
            {
                lock (_lock)
                {
                    _history.Clear();
                }
                return;
            }

            try
            {
                var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
                var entries = JsonSerializer.Deserialize<List<SyncLogEntry>>(json, new JsonSerializerOptions
                {
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                });

                lock (_lock)
                {
                    _history = entries ?? new List<SyncLogEntry>();
                    TrimHistory();
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse sync logs from {FilePath}. Starting with empty history.", filePath);
                
                lock (_lock)
                {
                    _history.Clear();
                }
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Failed to read sync logs from {FilePath}. Starting with empty history.", filePath);

                lock (_lock)
                {
                    _history.Clear();
                }
            }
        }

        private string GetFilePath()
        {
            return Path.Combine(_applicationPaths.DataPath, "sync-logs.json");
        }

        private void TrimHistory()
        {
            // Assumes lock is already held
            if (_history.Count > _maxHistoryEntries)
            {
                _history = _history.Take(_maxHistoryEntries).ToList();
            }
        }

        public async Task LogSyncCompletion(int conferencesProcessed, int filesCreated, DateTime timestamp)
        {
            var entry = new SyncLogEntry
            {
                Timestamp = timestamp,
                ConferenceAcronym = "all",
                Status = SyncStatus.Completed,
                EventsProcessed = conferencesProcessed,
                FilesCreated = filesCreated
            };

            lock (_lock)
            {
                _history.Insert(0, entry);
                TrimHistory();
            }

            await PersistAsync().ConfigureAwait(false);
        }
    }
}