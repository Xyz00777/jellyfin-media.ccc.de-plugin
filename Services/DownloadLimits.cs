namespace Jellyfin.Plugin.MediaCccDe.Services
{
    /// <summary>
    /// Bounds what authenticated users, individually and together, can make the server
    /// store. Any Jellyfin account can queue a watchlist download for any catalog
    /// recording, so without these bounds an ordinary non-administrator account could
    /// fill the volume and grow durable queue state until the server stopped working.
    /// </summary>
    public static class DownloadLimits
    {
        /// <summary>
        /// Maximum number of unfinished downloads one user may have queued at once.
        /// </summary>
        public const int MaxActiveItemsPerUser = 250;

        /// <summary>
        /// Maximum number of unfinished downloads across all users at once.
        /// </summary>
        public const int MaxActiveItemsTotal = 500;

        /// <summary>
        /// Maximum number of finished queue items kept for the watchlist display.
        /// Older finished items are dropped so history cannot grow without bound.
        /// </summary>
        public const int MaxRetainedFinishedItems = 500;

        /// <summary>
        /// Maximum size of a single downloaded recording.
        /// </summary>
        public const long MaxRecordingBytes = 32L * 1024 * 1024 * 1024;

        /// <summary>
        /// Maximum number of bytes one user's watchlist library may occupy.
        /// </summary>
        public const long MaxUserLibraryBytes = 100L * 1024 * 1024 * 1024;

        /// <summary>
        /// Free space kept available on the download volume, so a download cannot leave
        /// the host with no room to start.
        /// </summary>
        public const long ReservedFreeSpaceBytes = 512L * 1024 * 1024;
    }
}
