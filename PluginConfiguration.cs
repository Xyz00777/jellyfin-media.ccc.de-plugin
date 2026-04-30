using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.MediaCccDe
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public string WatchlistPath { get; set; } = string.Empty;
        public string PreferredQuality { get; set; } = "hd";
        public List<string> PreferredAudioLanguages { get; set; } = new List<string>();
        public List<string> PreferredSubtitleLanguages { get; set; } = new List<string>();
        public int SyncIntervalHours { get; set; } = 6;
    }
}