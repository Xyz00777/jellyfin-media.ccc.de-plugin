namespace Jellyfin.Plugin.MediaCccDe.Models
{
    /// <summary>
    /// Represents the status of a download queue item.
    /// </summary>
    public enum DownloadStatus
    {
        Pending = 0,
        InProgress = 1,
        Completed = 2,
        Failed = 3
    }
}