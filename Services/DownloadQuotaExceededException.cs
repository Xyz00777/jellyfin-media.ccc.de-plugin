using System;

namespace Jellyfin.Plugin.MediaCccDe.Services
{
    /// <summary>
    /// Raised when a download would exceed one of the configured download limits. This is
    /// a refusal the caller reports to the user, not a failure of the plugin, so it is
    /// surfaced as a normal response rather than a server error.
    /// </summary>
    public sealed class DownloadQuotaExceededException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DownloadQuotaExceededException"/> class.
        /// </summary>
        /// <param name="message">The reason the download was refused.</param>
        public DownloadQuotaExceededException(string message)
            : base(message)
        {
        }
    }
}
