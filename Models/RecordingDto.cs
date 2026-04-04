using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MediaCccDe.Models
{
    /// <summary>
    /// Data Transfer Object representing a recording from media.ccc.de API.
    /// </summary>
    public class RecordingDto
    {
        /// <summary>
        /// Language code for the recording (e.g., "en", "de").
        /// </summary>
        [JsonPropertyName("language")]
        public string Language { get; set; } = string.Empty;

        /// <summary>
        /// Format of the recording (e.g., "mp4", "webm", "mp3").
        /// </summary>
        [JsonPropertyName("format")]
        public string Format { get; set; } = string.Empty;

        /// <summary>
        /// Whether this is a high-quality recording.
        /// </summary>
        [JsonPropertyName("high_quality")]
        public bool HighQuality { get; set; }

        /// <summary>
        /// Width of video in pixels. Zero for audio-only recordings.
        /// </summary>
        [JsonPropertyName("width")]
        public int Width { get; set; }

        /// <summary>
        /// Height of video in pixels. Zero for audio-only recordings.
        /// </summary>
        [JsonPropertyName("height")]
        public int Height { get; set; }

        /// <summary>
        /// Size of the recording file in bytes.
        /// </summary>
        [JsonPropertyName("size")]
        public long Size { get; set; }

        /// <summary>
        /// URL to the recording file.
        /// </summary>
        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        /// <summary>
        /// MIME type of the recording.
        /// Optional field.
        /// </summary>
        [JsonPropertyName("mime_type")]
        public string? MimeType { get; set; }

        /// <summary>
        /// Recording URL (alternative URL field).
        /// Optional field.
        /// </summary>
        [JsonPropertyName("recording_url")]
        public string? RecordingUrl { get; set; }
    }
}