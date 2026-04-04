using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MediaCccDe.Models
{
    /// <summary>
    /// Represents a recording from the media.ccc.de API.
    /// </summary>
    public class Recording
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("length")]
        public int Length { get; set; }

        [JsonPropertyName("mimetype")]
        public string MimeType { get; set; } = string.Empty;

        [JsonPropertyName("language")]
        public string Language { get; set; } = string.Empty;

        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("format")]
        public string? Format { get; set; }

        [JsonPropertyName("high_quality")]
        private bool? _highQuality = false;
        [JsonPropertyName("high_quality")]
        public bool? HighQuality 
        { 
            get => _highQuality;
            set => _highQuality = value ?? false;
        }

        [JsonPropertyName("width")]
        public int? Width { get; set; }

        [JsonPropertyName("height")]
        public int? Height { get; set; }

        /// <summary>
        /// File size in bytes (alias for Size).
        /// </summary>
        public long? FileSize { get; set; }

        /// <summary>
        /// Bitrate in kbps.
        /// </summary>
        public int? Bitrate { get; set; }
    }
}