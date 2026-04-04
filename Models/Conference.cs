using System;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MediaCccDe.Models
{
    /// <summary>
    /// Represents a conference from the media.ccc.de API.
    /// </summary>
    public class Conference
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("acronym")]
        public string Acronym { get; set; } = string.Empty;

        [JsonPropertyName("slug")]
        public string Slug { get; set; } = string.Empty;

        [JsonPropertyName("aspect_ratio")]
        public string AspectRatio { get; set; } = string.Empty;

        [JsonPropertyName("updated_at")]
        public DateTime? UpdatedAt { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("schedule_url")]
        public string? ScheduleUrl { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }
}