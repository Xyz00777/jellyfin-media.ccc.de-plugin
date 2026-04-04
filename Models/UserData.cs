using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.MediaCccDe.Models
{
    /// <summary>
    /// Represents user-specific data for the CCC Media plugin.
    /// </summary>
    public class UserData
    {
        public Guid UserId { get; set; }
        public List<string> Watchlist { get; set; } = new List<string>();
        public List<string> SearchProgress { get; set; } = new List<string>();
        public List<string> PreferredAudioLanguages { get; set; } = new List<string>();
        public List<string> PreferredSubtitleLanguages { get; set; } = new List<string>();
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}