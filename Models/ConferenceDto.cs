using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MediaCccDe.Models
{
    public class ConferenceDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("acronym")]
        public string Acronym { get; set; } = string.Empty;

        [JsonPropertyName("slug")]
        public string Slug { get; set; } = string.Empty;

        [JsonPropertyName("aspect_ratio")]
        public string? AspectRatio { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTime? UpdatedAt { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("schedule_url")]
        public string? ScheduleUrl { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("logo_url")]
        public string? LogoUrl { get; set; }

        [JsonPropertyName("images")]
        public List<ConferenceImageDto>? Images { get; set; }

        [JsonPropertyName("events")]
        public List<EventDto>? Events { get; set; }
    }

    public class ConferenceImageDto
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("mime_type")]
        public string? MimeType { get; set; }
    }
}
