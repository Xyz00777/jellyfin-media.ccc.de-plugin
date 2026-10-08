using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.MediaCccDe.Services
{
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
}
