using System;

namespace Jellyfin.Plugin.MediaCccDe.Services
{
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
}
