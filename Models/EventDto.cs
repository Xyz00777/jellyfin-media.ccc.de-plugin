using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MediaCccDe.Models
{
    /// <summary>
    /// Data Transfer Object representing an event/talk from media.ccc.de API.
    /// </summary>
    public class EventDto
    {
        /// <summary>
        /// Unique identifier for the event.
        /// </summary>
        [JsonPropertyName("guid")]
        public string Guid { get; set; } = string.Empty;

        /// <summary>
        /// Title of the event/talk.
        /// </summary>
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// URL slug for the event.
        /// </summary>
        [JsonPropertyName("slug")]
        public string Slug { get; set; } = string.Empty;

        /// <summary>
        /// Link to the event page.
        /// Optional field.
        /// </summary>
        [JsonPropertyName("link")]
        public string? Link { get; set; }

        /// <summary>
        /// Description of the event/talk.
        /// Optional field.
        /// </summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        /// <summary>
        /// Date of the event (stored as string from API).
        /// Optional field.
        /// </summary>
        [JsonPropertyName("date")]
        public string? Date { get; set; }

        /// <summary>
        /// Duration of the event in seconds.
        /// </summary>
        [JsonPropertyName("length")]
        public int Length { get; set; }

        /// <summary>
        /// ID of the conference this event belongs to.
        /// </summary>
        [JsonPropertyName("conference_id")]
        public int ConferenceId { get; set; }

        /// <summary>
        /// Poster/thumbnail image for the event.
        /// Optional field.
        /// </summary>
        [JsonPropertyName("poster_url")]
        public string? PosterUrl { get; set; }

        /// <summary>
        /// Thumbnail image for the event.
        /// Optional field.
        /// </summary>
        [JsonPropertyName("thumb_url")]
        public string? ThumbUrl { get; set; }

        /// <summary>
        /// List of available recordings for this event.
        /// Optional field.
        /// </summary>
        [JsonPropertyName("recordings")]
        public List<RecordingDto>? Recordings { get; set; }

        /// <summary>
        /// List of speakers/persons for this event.
        /// Optional field.
        /// </summary>
        [JsonPropertyName("persons")]
        public List<string>? Persons { get; set; }

        /// <summary>
        /// List of tags for this event.
        /// Optional field.
        /// </summary>
        [JsonPropertyName("tags")]
        public List<string>? Tags { get; set; }
    }
}
