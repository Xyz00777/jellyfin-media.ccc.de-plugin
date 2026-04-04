using System;

namespace Jellyfin.Plugin.MediaCccDe.Models
{
    /// <summary>
    /// Represents a per-user watchlist library.
    /// </summary>
    public class UserLibrary
    {
        /// <summary>
        /// The unique identifier for the library.
        /// </summary>
        public Guid LibraryId { get; set; }

        /// <summary>
        /// The name of the library (e.g., "username's Watchlist").
        /// </summary>
        public string LibraryName { get; set; } = string.Empty;

        /// <summary>
        /// The path to the library's content directory.
        /// </summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// The user ID that owns this library.
        /// </summary>
        public Guid UserId { get; set; }
    }
}