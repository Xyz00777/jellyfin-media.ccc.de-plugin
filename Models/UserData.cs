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
        public HashSet<string> Watchlist { get; set; } = new HashSet<string>();
        public HashSet<string> SearchProgress { get; set; } = new HashSet<string>();
        public HashSet<string> PreferredAudioLanguages { get; set; } = new HashSet<string>();
        public HashSet<string> PreferredSubtitleLanguages { get; set; } = new HashSet<string>();
        public List<string> PreferredAudioLanguageOrder { get; set; } = new List<string>();
        public List<string> PreferredSubtitleLanguageOrder { get; set; } = new List<string>();
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
