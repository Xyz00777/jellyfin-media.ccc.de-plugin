namespace Jellyfin.Plugin.MediaCccDe.Services
{
    public class StrmResult
    {
        public string FilePath { get; set; } = string.Empty;
        public string RecordingUrl { get; set; } = string.Empty;
        public string ConferenceAcronym { get; set; } = string.Empty;
        public string EventSlug { get; set; } = string.Empty;
    }
}
