using System.Collections.Generic;

namespace Jellyfin.Plugin.MediaCccDe.Models
{
    /// <summary>
    /// User preferences for recording selection.
    /// </summary>
    public class RecordingPreferences
    {
        /// <summary>
        /// Ordered list of preferred language codes (e.g., "en", "de").
        /// </summary>
        public List<string> PreferredLanguages { get; set; } = new List<string>();

        /// <summary>
        /// Quality preference: "hd" or "sd".
        /// </summary>
        public string QualityPreference { get; set; } = "hd";

        /// <summary>
        /// Preferred format (e.g., "mp4", "webm").
        /// </summary>
        public string? PreferredFormat { get; set; }
    }
}