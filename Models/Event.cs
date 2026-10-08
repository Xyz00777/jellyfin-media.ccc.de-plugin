using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MediaCccDe.Models
{
    /// <summary>
    /// Represents an event from the media.ccc.de API.
    /// </summary>
    public class Event
    {
        [JsonPropertyName("guid")]
        public string Guid { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("slug")]
        public string Slug { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("link")]
        public string? Link { get; set; }

        [JsonPropertyName("date")]
        public string? Date { get; set; }

        [JsonPropertyName("length")]
        public int Length { get; set; }

        [JsonPropertyName("conference_id")]
        public int ConferenceId { get; set; }

        [JsonPropertyName("recordings")]
        public List<Recording> Recordings { get; set; } = new List<Recording>();
    }
}
