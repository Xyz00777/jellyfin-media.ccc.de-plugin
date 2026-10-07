using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MediaCccDe.Models
{
    public class RecordingDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("language")]
        public string Language { get; set; } = string.Empty;

        [JsonPropertyName("format")]
        public string Format { get; set; } = string.Empty;

        [JsonPropertyName("folder")]
        public string Folder { get; set; } = string.Empty;

        [JsonPropertyName("high_quality")]
        public bool HighQuality { get; set; }

        [JsonPropertyName("width")]
        public int? Width { get; set; }

        [JsonPropertyName("height")]
        public int? Height { get; set; }

        [JsonPropertyName("size")]
        public long? Size { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("recording_url")]
        public string RecordingUrl { get; set; } = string.Empty;

        [JsonPropertyName("mimetype")]
        public string? MimeType { get; set; }

        [JsonPropertyName("mime_type")]
        public string? CurrentMimeType { get; set; }

        [JsonPropertyName("length")]
        public int? Length { get; set; }

        [JsonPropertyName("file_size")]
        public long? FileSize { get; set; }

        [JsonPropertyName("bitrate")]
        public int? Bitrate { get; set; }

        [JsonIgnore]
        public string EffectiveUrl => string.IsNullOrWhiteSpace(RecordingUrl) ? Url : RecordingUrl;

        [JsonIgnore]
        public string EffectiveFormat => string.IsNullOrWhiteSpace(Format) ? Folder : Format;

        [JsonIgnore]
        public string? EffectiveMimeType => string.IsNullOrWhiteSpace(MimeType) ? CurrentMimeType : MimeType;
    }
}
