using System;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MediaCccDe.Models
{
    /// <summary>
    /// Data Transfer Object representing a conference from media.ccc.de API.
    /// </summary>
    public class ConferenceDto
    {
        /// <summary>
        /// Title of the conference (e.g., "37C3", "36C3").
        /// </summary>
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Acronym/short identifier for the conference (e.g., "37c3").
        /// </summary>
        [JsonPropertyName("acronym")]
        public string Acronym { get; set; } = string.Empty;

        /// <summary>
        /// URL slug for the conference.
        /// </summary>
        [JsonPropertyName("slug")]
        public string Slug { get; set; } = string.Empty;

        /// <summary>
        /// Aspect ratio for video recordings (e.g., "16:9").
        /// Optional field.
        /// </summary>
        [JsonPropertyName("aspect_ratio")]
        public string? AspectRatio { get; set; }

        /// <summary>
        /// Last update timestamp for the conference data.
        /// Optional field.
        /// </summary>
        [JsonPropertyName("updated_at")]
        public DateTime? UpdatedAt { get; set; }

        /// <summary>
        /// URL to the conference page on media.ccc.de.
        /// Optional field.
        /// </summary>
        [JsonPropertyName("url")]
        public string? Url { get; set; }

        /// <summary>
        /// URL to the conference schedule.
        /// Optional field.
        /// </summary>
        [JsonPropertyName("schedule_url")]
        public string? ScheduleUrl { get; set; }
    }
}