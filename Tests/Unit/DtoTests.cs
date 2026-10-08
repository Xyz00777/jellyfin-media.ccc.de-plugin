using System;
using System.Collections.Generic;
using System.Text.Json;
using Jellyfin.Plugin.MediaCccDe.Models;
using Xunit;

namespace Jellyfin.Plugin.MediaCccDe.Tests.Unit
{
    /// <summary>
    /// Tests for DTOs representing media.ccc.de API responses.
    /// RED Phase: These tests will fail until models are implemented.
    /// </summary>
    public class DtoTests
    {
        #region ConferenceDto Tests

        [Fact]
        public void ConferenceDto_deserializes_from_json()
        {
            // Arrange - Real API response structure
            var json = @"{
                ""title"": ""37C3"",
                ""acronym"": ""37c3"",
                ""slug"": ""37c3"",
                ""aspect_ratio"": ""16:9"",
                ""updated_at"": ""2024-01-01T00:00:00Z""
            }";

            // Act
            var conference = JsonSerializer.Deserialize<ConferenceDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            // Assert
            Assert.NotNull(conference);
            Assert.Equal("37C3", conference.Title);
            Assert.Equal("37c3", conference.Acronym);
            Assert.Equal("37c3", conference.Slug);
        }

        [Fact]
        public void ConferenceDto_has_required_properties()
        {
            // Arrange
            var json = @"{
                ""title"": ""36C3"",
                ""acronym"": ""36c3"",
                ""slug"": ""36c3""
            }";

            // Act
            var conference = JsonSerializer.Deserialize<ConferenceDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            // Assert
            Assert.NotNull(conference);
            Assert.NotNull(conference.Title);
            Assert.NotNull(conference.Acronym);
            Assert.NotNull(conference.Slug);
            Assert.False(string.IsNullOrEmpty(conference.Title));
            Assert.False(string.IsNullOrEmpty(conference.Acronym));
            Assert.False(string.IsNullOrEmpty(conference.Slug));
        }

        [Fact]
        public void ConferenceDto_has_optional_properties()
        {
            // Arrange - Conference with all optional fields
            var json = @"{
                ""title"": ""DEFCON 32"",
                ""acronym"": ""defcon32"",
                ""slug"": ""defcon32"",
                ""aspect_ratio"": ""16:9"",
                ""updated_at"": ""2024-08-15T12:30:45Z"",
                ""url"": ""https://media.ccc.de/c/defcon32"",
                ""schedule_url"": ""https://defcon.org/schedule.html""
            }";

            // Act
            var conference = JsonSerializer.Deserialize<ConferenceDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            // Assert
            Assert.NotNull(conference);
            Assert.Equal("16:9", conference.AspectRatio);
            Assert.NotNull(conference.UpdatedAt);
            Assert.Equal(new DateTime(2024, 8, 15, 12, 30, 45, DateTimeKind.Utc), conference.UpdatedAt.Value.ToUniversalTime());
            Assert.Equal("https://media.ccc.de/c/defcon32", conference.Url);
            Assert.Equal("https://defcon.org/schedule.html", conference.ScheduleUrl);
        }

        [Fact]
        public void ConferenceDto_handles_null_optional_properties()
        {
            // Arrange - Minimal conference without optional fields
            var json = @"{
                ""title"": ""Congress"",
                ""acronym"": ""congress"",
                ""slug"": ""congress""
            }";

            // Act
            var conference = JsonSerializer.Deserialize<ConferenceDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            // Assert
            Assert.NotNull(conference);
            Assert.Null(conference.AspectRatio);
            Assert.Null(conference.UpdatedAt);
            Assert.Null(conference.Url);
            Assert.Null(conference.ScheduleUrl);
        }

        [Fact]
        public void ConferenceDto_serializes_to_json()
        {
            // Arrange
            var conference = new ConferenceDto
            {
                Title = "GPN22",
                Acronym = "gpn22",
                Slug = "gpn22",
                AspectRatio = "16:9"
            };

            // Act
            var json = JsonSerializer.Serialize(conference, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            // Assert - Should contain camelCase property names
            Assert.Contains("\"title\":", json);
            Assert.Contains("\"acronym\":", json);
            Assert.Contains("\"slug\":", json);
            Assert.Contains("\"aspect_ratio\":", json);
        }

        #endregion

        #region EventDto Tests

        [Fact]
        public void EventDto_deserializes_from_json()
        {
            // Arrange - Real API response structure
            var json = @"{
                ""guid"": ""abc123-def456"",
                ""title"": ""Opening Event"",
                ""slug"": ""37c3-12746-opening_event"",
                ""link"": ""https://media.ccc.de/v/37c3-12746-opening_event"",
                ""description"": ""The opening ceremony..."",
                ""date"": ""2023-12-27"",
                ""length"": 3600,
                ""conference_id"": 123
            }";

            // Act
            var evt = JsonSerializer.Deserialize<EventDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            // Assert
            Assert.NotNull(evt);
            Assert.Equal("abc123-def456", evt.Guid);
            Assert.Equal("Opening Event", evt.Title);
            Assert.Equal("37c3-12746-opening_event", evt.Slug);
        }

        [Fact]
        public void EventDto_has_conference_reference()
        {
            // Arrange
            var json = @"{
                ""guid"": ""event-1"",
                ""title"": ""Keynote"",
                ""slug"": ""37c3-keynote"",
                ""conference_id"": 42
            }";

            // Act
            var evt = JsonSerializer.Deserialize<EventDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            // Assert
            Assert.NotNull(evt);
            Assert.Equal(42, evt.ConferenceId);
        }

        [Fact]
        public void EventDto_has_recordings_array()
        {
            // Arrange - Event with nested recordings
            var json = @"{
                ""guid"": ""event-2"",
                ""title"": ""Security Workshop"",
                ""slug"": ""37c3-security"",
                ""conference_id"": 1,
                ""recordings"": [
                    {
                        ""language"": ""en"",
                        ""format"": ""mp4"",
                        ""high_quality"": true,
                        ""url"": ""https://cdn.media.ccc.de/37c3/video.mp4""
                    },
                    {
                        ""language"": ""en"",
                        ""format"": ""webm"",
                        ""high_quality"": true,
                        ""url"": ""https://cdn.media.ccc.de/37c3/video.webm""
                    }
                ]
            }";

            // Act
            var evt = JsonSerializer.Deserialize<EventDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            // Assert
            Assert.NotNull(evt);
            Assert.NotNull(evt.Recordings);
            Assert.Equal(2, evt.Recordings.Count);
            Assert.Equal("mp4", evt.Recordings[0].Format);
            Assert.Equal("webm", evt.Recordings[1].Format);
        }

        [Fact]
        public void EventDto_has_title_description_length()
        {
            // Arrange
            var json = @"{
                ""guid"": ""event-3"",
                ""title"": ""Privacy in the Digital Age"",
                ""description"": ""An in-depth look at surveillance technologies..."",
                ""length"": 7200
            }";

            // Act
            var evt = JsonSerializer.Deserialize<EventDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            // Assert
            Assert.NotNull(evt);
            Assert.Equal("Privacy in the Digital Age", evt.Title);
            Assert.Equal("An in-depth look at surveillance technologies...", evt.Description);
            Assert.Equal(7200, evt.Length);
        }

        [Fact]
        public void EventDto_handles_minimal_data()
        {
            // Arrange - Event with only required fields
            var json = @"{
                ""guid"": ""minimal-event"",
                ""title"": ""Untitled"",
                ""slug"": ""untitled""
            }";

            // Act
            var evt = JsonSerializer.Deserialize<EventDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            // Assert
            Assert.NotNull(evt);
            Assert.Equal("minimal-event", evt.Guid);
            Assert.Equal("Untitled", evt.Title);
            Assert.Equal("untitled", evt.Slug);
            Assert.Null(evt.Description);
            Assert.Null(evt.Link);
            Assert.Null(evt.Date);
            Assert.Equal(0, evt.Length);
            Assert.Equal(0, evt.ConferenceId);
            Assert.Null(evt.Recordings);
        }

        [Fact]
        public void EventDto_parses_date_correctly()
        {
            // Arrange
            var json = @"{
                ""guid"": ""dated-event"",
                ""title"": ""Historical Talk"",
                ""date"": ""2024-03-15""
            }";

            // Act
            var evt = JsonSerializer.Deserialize<EventDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            // Assert
            Assert.NotNull(evt);
            Assert.NotNull(evt.Date);
            // Date should be stored as string or DateTime
            Assert.Equal("2024-03-15", evt.Date);
        }

        #endregion

        #region Serialization Bug Tests

        [Fact]
        public void RecordingDto_deserializes_mimetype_from_API()
        {
            // The actual media.ccc.de API sends "mimetype" (no underscore)
            var json = @"{""mimetype"":""video/mp4"",""language"":""en"",""url"":""https://cdn.media.ccc.de/v.mp4""}";
            var recording = JsonSerializer.Deserialize<RecordingDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            Assert.NotNull(recording);
            Assert.Equal("video/mp4", recording.MimeType);
        }

        [Fact]
        public void Recording_HighQuality_preserves_null_unknown_state()
        {
            // null = "unknown", should NOT be coerced to false
            var recording = new Recording { HighQuality = null };
            Assert.Null(recording.HighQuality);
        }

        [Fact]
        public void Recording_deserialization_does_not_throw()
        {
            // Verify Recording can be deserialized without InvalidOperationException
            // from duplicate [JsonPropertyName] attributes
            var json = @"{""id"":1,""high_quality"":true,""url"":""https://example.com/v.mp4""}";
            var ex = Record.Exception(() => JsonSerializer.Deserialize<Recording>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }));
            Assert.Null(ex);
        }

        [Fact]
        public void Recording_roundtrip_preserves_HighQuality_null()
        {
            var recording = new Recording { Id = 1, HighQuality = null, Url = "https://example.com/v.mp4" };
            var json = JsonSerializer.Serialize(recording);
            var deserialized = JsonSerializer.Deserialize<Recording>(json);
            Assert.NotNull(deserialized);
            Assert.Null(deserialized.HighQuality);
        }

        #endregion

        #region RecordingDto Tests

        [Fact]
        public void RecordingDto_deserializes_from_json()
        {
            // Arrange - Real API response structure
            var json = @"{
                ""language"": ""en"",
                ""format"": ""mp4"",
                ""high_quality"": true,
                ""width"": 1920,
                ""height"": 1080,
                ""size"": 1500000000,
                ""url"": ""https://cdn.media.ccc.de/37c3/hd/video.mp4""
            }";

            // Act
            var recording = JsonSerializer.Deserialize<RecordingDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            // Assert
            Assert.NotNull(recording);
            Assert.Equal("en", recording.Language);
            Assert.Equal("mp4", recording.Format);
            Assert.True(recording.HighQuality);
        }

        [Fact]
        public void RecordingDto_has_language_quality_format()
        {
            // Arrange - Multiple quality levels
            var json = @"{
                ""language"": ""de"",
                ""format"": ""webm"",
                ""high_quality"": false,
                ""url"": ""https://cdn.media.ccc.de/37c3/low/video.webm""
            }";

            // Act
            var recording = JsonSerializer.Deserialize<RecordingDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            // Assert
            Assert.NotNull(recording);
            Assert.Equal("de", recording.Language);
            Assert.Equal("webm", recording.Format);
            Assert.False(recording.HighQuality);
        }

        [Fact]
        public void RecordingDto_has_url_and_size()
        {
            // Arrange
            var json = @"{
                ""language"": ""en"",
                ""format"": ""mp4"",
                ""url"": ""https://cdn.media.ccc.de/congress/video.mp4"",
                ""size"": 2457600000
            }";

            // Act
            var recording = JsonSerializer.Deserialize<RecordingDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            // Assert
            Assert.NotNull(recording);
            Assert.Equal("https://cdn.media.ccc.de/congress/video.mp4", recording.Url);
            Assert.Equal(2457600000L, recording.Size);
        }

        [Fact]
        public void RecordingDto_has_multiple_formats()
        {
            // Test different common recording formats
            var formats = new[] { "mp4", "webm", "mp3", "ogg", "opus" };

            foreach (var format in formats)
            {
                // Arrange
                var json = $@"{{
                    ""language"": ""en"",
                    ""format"": ""{format}"",
                    ""url"": ""https://cdn.media.ccc.de/video.{format}""
                }}";

                // Act
                var recording = JsonSerializer.Deserialize<RecordingDto>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                // Assert
                Assert.NotNull(recording);
                Assert.Equal(format, recording.Format);
            }
        }

        [Fact]
        public void RecordingDto_has_dimensions_for_video()
        {
            // Arrange - HD video recording
            var json = @"{
                ""language"": ""en"",
                ""format"": ""mp4"",
                ""high_quality"": true,
                ""width"": 1920,
                ""height"": 1080,
                ""url"": ""https://cdn.media.ccc.de/hd/video.mp4""
            }";

            // Act
            var recording = JsonSerializer.Deserialize<RecordingDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            // Assert
            Assert.NotNull(recording);
            Assert.Equal(1920, recording.Width);
            Assert.Equal(1080, recording.Height);
        }

        [Fact]
        public void RecordingDto_handles_audio_only()
        {
            // Arrange - Audio recording without dimensions
            var json = @"{
                ""language"": ""en"",
                ""format"": ""mp3"",
                ""url"": ""https://cdn.media.ccc.de/audio/talk.mp3"",
                ""size"": 50000000
            }";

            // Act
            var recording = JsonSerializer.Deserialize<RecordingDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            // Assert
            Assert.NotNull(recording);
            Assert.Equal("mp3", recording.Format);
            Assert.Null(recording.Width);
            Assert.Null(recording.Height);
            Assert.Equal(50000000L, recording.Size);
        }

        [Fact]
        public void RecordingDto_handles_multiple_languages()
        {
            // Test various language codes
            var languages = new[] { "en", "de", "fr", "es", "pt" };

            foreach (var lang in languages)
            {
                // Arrange
                var json = $@"{{
                    ""language"": ""{lang}"",
                    ""format"": ""mp4"",
                    ""url"": ""https://cdn.media.ccc.de/video.{lang}.mp4""
                }}";

                // Act
                var recording = JsonSerializer.Deserialize<RecordingDto>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                // Assert
                Assert.NotNull(recording);
                Assert.Equal(lang, recording.Language);
            }
        }

        #endregion

        [Fact]
        public void RecordingDto_EffectiveUrl_prefers_the_playable_recording_url()
        {
            var dto = new RecordingDto
            {
                Url = "https://api.media.ccc.de/public/recordings/71930",
                RecordingUrl = "https://cdn.media.ccc.de/events/camp2023/h264-hd/talk_hd.mp4"
            };

            Assert.Equal(dto.RecordingUrl, dto.EffectiveUrl);
        }

        [Fact]
        public void RecordingDto_EffectiveUrl_falls_back_to_url_when_recording_url_missing()
        {
            var dto = new RecordingDto
            {
                Url = "https://api.media.ccc.de/public/recordings/1",
                RecordingUrl = string.Empty
            };

            Assert.Equal(dto.Url, dto.EffectiveUrl);
        }
    }
}
