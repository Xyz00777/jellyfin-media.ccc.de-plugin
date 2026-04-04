using System;

namespace Jellyfin.Plugin.MediaCccDe.Models
{
    /// <summary>
    /// Represents an item in the download queue.
    /// </summary>
    public class DownloadQueueItem
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string EventGuid { get; set; } = string.Empty;
        public string EventTitle { get; set; } = string.Empty;
        public string ConferenceAcronym { get; set; } = string.Empty;
        public string RecordingUrl { get; set; } = string.Empty;
        public string DestinationPath { get; set; } = string.Empty;
        public DownloadStatus Status { get; set; } = DownloadStatus.Pending;
        public double Progress { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public int Priority { get; set; }
    }
}