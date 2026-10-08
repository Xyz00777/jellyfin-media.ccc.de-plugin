using System.Collections.Generic;
using Jellyfin.Plugin.MediaCccDe.Models;
using Jellyfin.Plugin.MediaCccDe.Services;
using Xunit;

namespace Jellyfin.Plugin.MediaCccDe.Tests.Unit
{
    public class RecordingSelectorTests
    {
        #region Quality Selection Tests

        [Fact]
        public void SelectBestRecording_prefer_hd_over_sd()
        {
            // Arrange
            var selector = new RecordingSelector();
            var recordings = new List<Recording>
            {
                CreateRecording(language: "en", format: "mp4", highQuality: false, width: 1280, height: 720),
                CreateRecording(language: "en", format: "mp4", highQuality: true, width: 1920, height: 1080)
            };
            var preferences = new RecordingPreferences
            {
                PreferredLanguages = new List<string> { "en" },
                QualityPreference = "hd",
                PreferredFormat = "mp4"
            };

            // Act
            var result = selector.SelectBestRecording(recordings, preferences);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.HighQuality);
            Assert.Equal(1920, result.Width);
        }

        [Fact]
        public void SelectBestRecording_returns_sd_if_only_sd_available()
        {
            // Arrange
            var selector = new RecordingSelector();
            var recordings = new List<Recording>
            {
                CreateRecording(language: "en", format: "mp4", highQuality: false, width: 1280, height: 720)
            };
            var preferences = new RecordingPreferences
            {
                PreferredLanguages = new List<string> { "en" },
                QualityPreference = "hd",
                PreferredFormat = "mp4"
            };

            // Act
            var result = selector.SelectBestRecording(recordings, preferences);

            // Assert
            Assert.NotNull(result);
            Assert.False(result.HighQuality);
        }

        [Fact]
        public void SelectBestRecording_respects_quality_preference()
        {
            // Arrange - User prefers SD
            var selector = new RecordingSelector();
            var recordings = new List<Recording>
            {
                CreateRecording(language: "en", format: "mp4", highQuality: true, width: 1920, height: 1080),
                CreateRecording(language: "en", format: "mp4", highQuality: false, width: 1280, height: 720)
            };
            var preferences = new RecordingPreferences
            {
                PreferredLanguages = new List<string> { "en" },
                QualityPreference = "sd",
                PreferredFormat = "mp4"
            };

            // Act
            var result = selector.SelectBestRecording(recordings, preferences);

            // Assert
            Assert.NotNull(result);
            Assert.False(result.HighQuality);
        }

        [Fact]
        public void SelectBestRecording_handles_no_quality_flag()
        {
            // Arrange - Recording without quality flag set
            var selector = new RecordingSelector();
            var recordings = new List<Recording>
            {
                CreateRecording(language: "en", format: "mp4", highQuality: null, width: 1280, height: 720)
            };
            var preferences = new RecordingPreferences
            {
                PreferredLanguages = new List<string> { "en" },
                QualityPreference = "hd",
                PreferredFormat = "mp4"
            };

            // Act
            var result = selector.SelectBestRecording(recordings, preferences);

            // Assert - Unknown quality (null) is preserved, not coerced to false
            Assert.NotNull(result);
            Assert.Null(result.HighQuality);
        }

        #endregion

        #region Format Selection Tests

        [Fact]
        public void SelectBestRecording_prefers_mp4_over_webm()
        {
            // Arrange
            var selector = new RecordingSelector();
            var recordings = new List<Recording>
            {
                CreateRecording(language: "en", format: "webm", highQuality: true, width: 1920, height: 1080),
                CreateRecording(language: "en", format: "mp4", highQuality: true, width: 1920, height: 1080)
            };
            var preferences = new RecordingPreferences
            {
                PreferredLanguages = new List<string> { "en" },
                QualityPreference = "hd",
                PreferredFormat = null // Should default to preferring mp4
            };

            // Act
            var result = selector.SelectBestRecording(recordings, preferences);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("mp4", result.Format);
        }

        [Fact]
        public void SelectBestRecording_respects_format_preference()
        {
            // Arrange - User prefers webm
            var selector = new RecordingSelector();
            var recordings = new List<Recording>
            {
                CreateRecording(language: "en", format: "mp4", highQuality: true, width: 1920, height: 1080),
                CreateRecording(language: "en", format: "webm", highQuality: true, width: 1920, height: 1080)
            };
            var preferences = new RecordingPreferences
            {
                PreferredLanguages = new List<string> { "en" },
                QualityPreference = "hd",
                PreferredFormat = "webm"
            };

            // Act
            var result = selector.SelectBestRecording(recordings, preferences);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("webm", result.Format);
        }

        [Fact]
        public void SelectBestRecording_falls_back_to_any_format()
        {
            // Arrange - Only webm available, but user prefers mp4
            var selector = new RecordingSelector();
            var recordings = new List<Recording>
            {
                CreateRecording(language: "en", format: "webm", highQuality: true, width: 1920, height: 1080)
            };
            var preferences = new RecordingPreferences
            {
                PreferredLanguages = new List<string> { "en" },
                QualityPreference = "hd",
                PreferredFormat = "mp4"
            };

            // Act
            var result = selector.SelectBestRecording(recordings, preferences);

            // Assert - Should still return a recording, just not in preferred format
            Assert.NotNull(result);
            Assert.Equal("webm", result.Format);
        }

        #endregion

        #region Language + Quality Combination Tests

        [Fact]
        public void SelectBestRecording_prefers_hd_in_preferred_language()
        {
            // Arrange
            var selector = new RecordingSelector();
            var recordings = new List<Recording>
            {
                CreateRecording(language: "de", format: "mp4", highQuality: true, width: 1920, height: 1080),
                CreateRecording(language: "en", format: "mp4", highQuality: true, width: 1920, height: 1080),
                CreateRecording(language: "en", format: "mp4", highQuality: false, width: 1280, height: 720)
            };
            var preferences = new RecordingPreferences
            {
                PreferredLanguages = new List<string> { "en", "de" },
                QualityPreference = "hd",
                PreferredFormat = "mp4"
            };

            // Act
            var result = selector.SelectBestRecording(recordings, preferences);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("en", result.Language);
            Assert.True(result.HighQuality);
        }

        [Fact]
        public void SelectBestRecording_falls_back_to_sd_in_preferred_language()
        {
            // Arrange - Only SD available in preferred language
            var selector = new RecordingSelector();
            var recordings = new List<Recording>
            {
                CreateRecording(language: "de", format: "mp4", highQuality: true, width: 1920, height: 1080),
                CreateRecording(language: "en", format: "mp4", highQuality: false, width: 1280, height: 720)
            };
            var preferences = new RecordingPreferences
            {
                PreferredLanguages = new List<string> { "en" },
                QualityPreference = "hd",
                PreferredFormat = "mp4"
            };

            // Act
            var result = selector.SelectBestRecording(recordings, preferences);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("en", result.Language);
            Assert.False(result.HighQuality);
        }

        [Fact]
        public void SelectBestRecording_falls_back_to_hd_in_fallback_language()
        {
            // Arrange - Preferred language not available, fallback language has HD
            var selector = new RecordingSelector();
            var recordings = new List<Recording>
            {
                CreateRecording(language: "de", format: "mp4", highQuality: true, width: 1920, height: 1080)
            };
            var preferences = new RecordingPreferences
            {
                PreferredLanguages = new List<string> { "en", "de" },
                QualityPreference = "hd",
                PreferredFormat = "mp4"
            };

            // Act
            var result = selector.SelectBestRecording(recordings, preferences);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("de", result.Language);
            Assert.True(result.HighQuality);
        }

        [Fact]
        public void SelectBestRecording_handles_original_language_fallback()
        {
            // Arrange - None of preferred languages available
            var selector = new RecordingSelector();
            var recordings = new List<Recording>
            {
                CreateRecording(language: "fr", format: "mp4", highQuality: true, width: 1920, height: 1080)
            };
            var preferences = new RecordingPreferences
            {
                PreferredLanguages = new List<string> { "en", "de" },
                QualityPreference = "hd",
                PreferredFormat = "mp4"
            };

            // Act
            var result = selector.SelectBestRecording(recordings, preferences);

            // Assert - Should fall back to the available recording (original language)
            Assert.NotNull(result);
            Assert.Equal("fr", result.Language);
        }

        #endregion

        #region Resolution Sorting Tests

        [Fact]
        public void SelectBestRecording_sorts_by_resolution_width()
        {
            // Arrange - Multiple HD recordings with different resolutions
            var selector = new RecordingSelector();
            var recordings = new List<Recording>
            {
                CreateRecording(language: "en", format: "mp4", highQuality: true, width: 1280, height: 720),
                CreateRecording(language: "en", format: "mp4", highQuality: true, width: 1920, height: 1080),
                CreateRecording(language: "en", format: "mp4", highQuality: true, width: 3840, height: 2160)
            };
            var preferences = new RecordingPreferences
            {
                PreferredLanguages = new List<string> { "en" },
                QualityPreference = "hd",
                PreferredFormat = "mp4"
            };

            // Act
            var result = selector.SelectBestRecording(recordings, preferences);

            // Assert - Should select highest resolution
            Assert.NotNull(result);
            Assert.Equal(3840, result.Width);
        }

        [Fact]
        public void SelectBestRecording_sorts_by_file_size()
        {
            // Arrange - Same resolution but different file sizes
            var selector = new RecordingSelector();
            var recordings = new List<Recording>
            {
                CreateRecording(language: "en", format: "mp4", highQuality: true, width: 1920, height: 1080, fileSize: 1024000000L),
                CreateRecording(language: "en", format: "mp4", highQuality: true, width: 1920, height: 1080, fileSize: 2048000000L),
                CreateRecording(language: "en", format: "mp4", highQuality: true, width: 1920, height: 1080, fileSize: 3072000000L)
            };
            var preferences = new RecordingPreferences
            {
                PreferredLanguages = new List<string> { "en" },
                QualityPreference = "hd",
                PreferredFormat = "mp4"
            };

            // Act
            var result = selector.SelectBestRecording(recordings, preferences);

            // Assert - Should select largest file (better quality)
            Assert.NotNull(result);
            Assert.Equal(3072000000L, result.FileSize);
        }

        [Fact]
        public void SelectBestRecording_prefers_higher_bitrate()
        {
            // Arrange - Same resolution and file size, different bitrates
            var selector = new RecordingSelector();
            var recordings = new List<Recording>
            {
                CreateRecording(language: "en", format: "mp4", highQuality: true, width: 1920, height: 1080, bitrate: 5000),
                CreateRecording(language: "en", format: "mp4", highQuality: true, width: 1920, height: 1080, bitrate: 8000)
            };
            var preferences = new RecordingPreferences
            {
                PreferredLanguages = new List<string> { "en" },
                QualityPreference = "hd",
                PreferredFormat = "mp4"
            };

            // Act
            var result = selector.SelectBestRecording(recordings, preferences);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(8000, result.Bitrate);
        }

        #endregion

        #region Edge Cases Tests

        [Fact]
        public void SelectBestRecording_returns_null_if_no_recordings()
        {
            // Arrange
            var selector = new RecordingSelector();
            var recordings = new List<Recording>();
            var preferences = new RecordingPreferences
            {
                PreferredLanguages = new List<string> { "en" },
                QualityPreference = "hd",
                PreferredFormat = "mp4"
            };

            // Act
            var result = selector.SelectBestRecording(recordings, preferences);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void SelectBestRecording_handles_multiple_matching_recordings()
        {
            // Arrange - Multiple identical recordings (pick first/best)
            var selector = new RecordingSelector();
            var recordings = new List<Recording>
            {
                CreateRecording(language: "en", format: "mp4", highQuality: true, width: 1920, height: 1080, url: "url1"),
                CreateRecording(language: "en", format: "mp4", highQuality: true, width: 1920, height: 1080, url: "url2")
            };
            var preferences = new RecordingPreferences
            {
                PreferredLanguages = new List<string> { "en" },
                QualityPreference = "hd",
                PreferredFormat = "mp4"
            };

            // Act
            var result = selector.SelectBestRecording(recordings, preferences);

            // Assert - Should return a recording (not null), implementation determines which
            Assert.NotNull(result);
            Assert.True(result.HighQuality);
            Assert.Equal("en", result.Language);
        }

        [Fact]
        public void SelectBestRecording_handles_null_preferences()
        {
            // Arrange
            var selector = new RecordingSelector();
            var recordings = new List<Recording>
            {
                CreateRecording(language: "en", format: "mp4", highQuality: true, width: 1920, height: 1080)
            };

            // Act
            var result = selector.SelectBestRecording(recordings, null);

            // Assert - Should still return a recording with default behavior
            Assert.NotNull(result);
        }

        [Fact]
        public void SelectBestRecording_handles_empty_language_preferences()
        {
            // Arrange
            var selector = new RecordingSelector();
            var recordings = new List<Recording>
            {
                CreateRecording(language: "en", format: "mp4", highQuality: true, width: 1920, height: 1080)
            };
            var preferences = new RecordingPreferences
            {
                PreferredLanguages = new List<string>(),
                QualityPreference = "hd",
                PreferredFormat = "mp4"
            };

            // Act
            var result = selector.SelectBestRecording(recordings, preferences);

            // Assert
            Assert.NotNull(result);
        }

        [Fact]
        public void SelectBestRecording_prioritizes_language_over_quality()
        {
            // Arrange - SD in preferred language vs HD in fallback language
            var selector = new RecordingSelector();
            var recordings = new List<Recording>
            {
                CreateRecording(language: "de", format: "mp4", highQuality: true, width: 1920, height: 1080),
                CreateRecording(language: "en", format: "mp4", highQuality: false, width: 1280, height: 720)
            };
            var preferences = new RecordingPreferences
            {
                PreferredLanguages = new List<string> { "en" },
                QualityPreference = "hd",
                PreferredFormat = "mp4"
            };

            // Act
            var result = selector.SelectBestRecording(recordings, preferences);

            // Assert - Language takes priority over quality
            Assert.NotNull(result);
            Assert.Equal("en", result.Language);
        }

        [Fact]
        public void SelectBestRecording_prioritizes_quality_over_format()
        {
            // Arrange - HD in webm vs SD in mp4
            var selector = new RecordingSelector();
            var recordings = new List<Recording>
            {
                CreateRecording(language: "en", format: "webm", highQuality: true, width: 1920, height: 1080),
                CreateRecording(language: "en", format: "mp4", highQuality: false, width: 1280, height: 720)
            };
            var preferences = new RecordingPreferences
            {
                PreferredLanguages = new List<string> { "en" },
                QualityPreference = "hd",
                PreferredFormat = "mp4"
            };

            // Act
            var result = selector.SelectBestRecording(recordings, preferences);

            // Assert - Quality takes priority over format
            Assert.NotNull(result);
            Assert.True(result.HighQuality);
            Assert.Equal("webm", result.Format);
        }

        #endregion

        #region Complex Scenarios

        [Fact]
        public void SelectBestRecording_complex_scenario_chooses_best_match()
        {
            // Arrange - Complex set of recordings
            var selector = new RecordingSelector();
            var recordings = new List<Recording>
            {
                // French recordings
                CreateRecording(language: "fr", format: "mp4", highQuality: true, width: 1920, height: 1080, fileSize: 2500000000L),
                // German recordings - HD and SD
                CreateRecording(language: "de", format: "mp4", highQuality: true, width: 1920, height: 1080, fileSize: 2000000000L),
                CreateRecording(language: "de", format: "webm", highQuality: true, width: 1920, height: 1080, fileSize: 2200000000L),
                CreateRecording(language: "de", format: "mp4", highQuality: false, width: 1280, height: 720, fileSize: 800000000L),
                // English recordings - multiple qualities
                CreateRecording(language: "en", format: "mp4", highQuality: false, width: 1280, height: 720, fileSize: 600000000L),
                CreateRecording(language: "en", format: "webm", highQuality: true, width: 1920, height: 1080, fileSize: 1800000000L),
                CreateRecording(language: "en", format: "mp4", highQuality: true, width: 3840, height: 2160, fileSize: 4000000000L)
            };
            var preferences = new RecordingPreferences
            {
                PreferredLanguages = new List<string> { "en", "de" },
                QualityPreference = "hd",
                PreferredFormat = "mp4"
            };

            // Act
            var result = selector.SelectBestRecording(recordings, preferences);

            // Assert - Should select English 4K mp4 HD recording
            Assert.NotNull(result);
            Assert.Equal("en", result.Language);
            Assert.True(result.HighQuality);
            Assert.Equal("mp4", result.Format);
            Assert.Equal(3840, result.Width);
        }

        #endregion

        #region Helper Methods

        private static Recording CreateRecording(
            string language,
            string format,
            bool? highQuality,
            int width,
            int height,
            long? fileSize = null,
            int? bitrate = null,
            string url = "http://example.com/recording")
        {
            return new Recording
            {
                Language = language,
                Format = format,
                HighQuality = highQuality,
                Width = width,
                Height = height,
                FileSize = fileSize,
                Bitrate = bitrate,
                Url = url
            };
        }

        #endregion

        private static Recording Sample(string language, string format, string mimeType, bool hq, int width, long size)
        {
            return new Recording
            {
                Language = language,
                Format = format,
                MimeType = mimeType,
                HighQuality = hq,
                Width = width,
                Height = width == 1920 ? 1080 : 576,
                FileSize = size,
                Url = $"https://cdn.media.ccc.de/{language}-{format}"
            };
        }

        [Fact]
        public void SelectBestRecording_ignores_audio_only_and_subtitle_files()
        {
            // Mirrors a real media.ccc.de payload: subtitle and audio renditions arrive in
            // the same list and some of them are flagged high quality.
            var recordings = new List<Recording>
            {
                Sample("eng", "mp3", "audio/mpeg", true, 0, 10),
                Sample("eng", "opus", "audio/opus", false, 0, 6),
                Sample("eng", "", "application/x-subrip", true, 0, 1),
                Sample("eng", "mp4", "video/mp4", true, 1920, 496)
            };

            var selected = new RecordingSelector().SelectBestRecording(recordings, null);

            Assert.NotNull(selected);
            Assert.Equal("video/mp4", selected!.MimeType);
        }

        [Fact]
        public void SelectBestRecording_returns_null_when_only_non_video_files_exist()
        {
            var recordings = new List<Recording>
            {
                Sample("eng", "mp3", "audio/mpeg", true, 0, 10),
                Sample("eng", "", "application/x-subrip", true, 0, 1)
            };

            Assert.Null(new RecordingSelector().SelectBestRecording(recordings, null));
        }

        [Fact]
        public void SelectBestRecording_excludes_non_video_when_mime_type_is_missing()
        {
            var recordings = new List<Recording>
            {
                Sample("eng", "mp3", string.Empty, true, 0, 10),
                Sample("eng", "mp4", string.Empty, true, 1920, 496)
            };

            var selected = new RecordingSelector().SelectBestRecording(recordings, null);

            Assert.NotNull(selected);
            Assert.Equal("mp4", selected!.Format);
        }

        [Fact]
        public void SelectBestRecording_prefers_combined_language_recording()
        {
            // A .strm holds a single URL, so the combined file is the only rendition that
            // lets Jellyfin expose more than one audio track.
            var recordings = new List<Recording>
            {
                Sample("deu", "mp4", "video/mp4", true, 1920, 496),
                Sample("eng", "mp4", "video/mp4", true, 1920, 500),
                Sample("deu-eng", "mp4", "video/mp4", true, 1920, 500)
            };

            var selected = new RecordingSelector()
                .SelectBestRecording(recordings, new RecordingPreferences());

            Assert.NotNull(selected);
            Assert.Equal("deu-eng", selected!.Language);
        }

    }
}
