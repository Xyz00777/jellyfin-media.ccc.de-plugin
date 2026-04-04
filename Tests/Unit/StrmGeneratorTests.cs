using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Xunit;
using Jellyfin.Plugin.MediaCccDe.Api;
using Jellyfin.Plugin.MediaCccDe.Models;
using Jellyfin.Plugin.MediaCccDe.Services;

namespace Jellyfin.Plugin.MediaCccDe.Tests.Unit
{
    public class StrmGeneratorTests
    {
        private readonly Mock<IMediaCccApiClient> _apiClientMock;
        private readonly Mock<IRecordingSelector> _recordingSelectorMock;
        private readonly string _testArchivePath;
        private readonly StrmGenerator _strmGenerator;

        public StrmGeneratorTests()
        {
            _apiClientMock = new Mock<IMediaCccApiClient>();
            _recordingSelectorMock = new Mock<IRecordingSelector>();
            _testArchivePath = Path.Combine(Path.GetTempPath(), "test_strm_" + Guid.NewGuid().ToString());
            _strmGenerator = new StrmGenerator(
                _apiClientMock.Object,
                _recordingSelectorMock.Object,
                _testArchivePath);
        }

        #region GenerateStrm Tests

        [Fact]
        public async Task GenerateStrm_creates_file_with_recording_url()
        {
            // Arrange
            var conference = CreateConference("37c3", "37C3");
            var evt = CreateEvent("opening-ceremony", "Opening Ceremony");
            var recording = CreateRecording("https://example.com/video.mp4");

            _recordingSelectorMock
                .Setup(r => r.SelectBestRecording(It.IsAny<IEnumerable<Recording>>(), It.IsAny<RecordingPreferences>()))
                .Returns(recording);

            // Act
            var result = await _strmGenerator.GenerateStrmAsync(conference, evt, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.True(File.Exists(result.FilePath));
            var content = await File.ReadAllTextAsync(result.FilePath);
            Assert.Equal("https://example.com/video.mp4", content.Trim());
        }

        [Fact]
        public async Task GenerateStrm_uses_conference_name_for_directory()
        {
            // Arrange
            var conference = CreateConference("37c3", "37C3");
            var evt = CreateEvent("opening-ceremony", "Opening Ceremony");
            var recording = CreateRecording("https://example.com/video.mp4");

            _recordingSelectorMock
                .Setup(r => r.SelectBestRecording(It.IsAny<IEnumerable<Recording>>(), It.IsAny<RecordingPreferences>()))
                .Returns(recording);

            // Act
            var result = await _strmGenerator.GenerateStrmAsync(conference, evt, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.Contains("37c3", result.FilePath);
        }

        [Fact]
        public async Task GenerateStrm_uses_event_slug_for_filename()
        {
            // Arrange
            var conference = CreateConference("37c3", "37C3");
            var evt = CreateEvent("opening-ceremony", "Opening Ceremony");
            var recording = CreateRecording("https://example.com/video.mp4");

            _recordingSelectorMock
                .Setup(r => r.SelectBestRecording(It.IsAny<IEnumerable<Recording>>(), It.IsAny<RecordingPreferences>()))
                .Returns(recording);

            // Act
            var result = await _strmGenerator.GenerateStrmAsync(conference, evt, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.EndsWith("opening-ceremony.strm", result.FilePath);
        }

        [Fact]
        public async Task GenerateStrm_sanitizes_special_characters()
        {
            // Arrange
            var conference = CreateConference("37c3", "37C3");
            var evt = CreateEvent("event-with-special/chars:test", "Event with Special/Chars:Test");
            var recording = CreateRecording("https://example.com/video.mp4");

            _recordingSelectorMock
                .Setup(r => r.SelectBestRecording(It.IsAny<IEnumerable<Recording>>(), It.IsAny<RecordingPreferences>()))
                .Returns(recording);

            // Act
            var result = await _strmGenerator.GenerateStrmAsync(conference, evt, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.DoesNotContain("/", result.FilePath.Substring(result.FilePath.LastIndexOf(Path.DirectorySeparatorChar)));
            Assert.DoesNotContain(":", result.FilePath);
        }

        [Fact]
        public async Task GenerateStrm_creates_nested_directory_structure()
        {
            // Arrange
            var conference = CreateConference("37c3", "37C3");
            var evt = CreateEvent("opening-ceremony", "Opening Ceremony", day: 1);
            var recording = CreateRecording("https://example.com/video.mp4");

            _recordingSelectorMock
                .Setup(r => r.SelectBestRecording(It.IsAny<IEnumerable<Recording>>(), It.IsAny<RecordingPreferences>()))
                .Returns(recording);

            // Act
            var result = await _strmGenerator.GenerateStrmAsync(conference, evt, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            var expectedPath = Path.Combine(_testArchivePath, "37c3", "Season 01", "opening-ceremony.strm");
            Assert.Equal(expectedPath, result.FilePath);
        }

        [Fact]
        public async Task GenerateStrm_handles_concurrent_events_same_day()
        {
            // Arrange
            var conference = CreateConference("37c3", "37C3");
            var evt1 = CreateEvent("opening-ceremony", "Opening Ceremony", day: 1);
            var evt2 = CreateEvent("closing-ceremony", "Closing Ceremony", day: 1);
            var recording = CreateRecording("https://example.com/video.mp4");

            _recordingSelectorMock
                .Setup(r => r.SelectBestRecording(It.IsAny<IEnumerable<Recording>>(), It.IsAny<RecordingPreferences>()))
                .Returns(recording);

            // Act
            var result1 = await _strmGenerator.GenerateStrmAsync(conference, evt1, CancellationToken.None);
            var result2 = await _strmGenerator.GenerateStrmAsync(conference, evt2, CancellationToken.None);

            // Assert
            Assert.NotNull(result1);
            Assert.NotNull(result2);
            Assert.Equal(
                Path.GetDirectoryName(result1.FilePath),
                Path.GetDirectoryName(result2.FilePath));
        }

        [Fact]
        public async Task GenerateStrm_selects_best_recording_url()
        {
            // Arrange
            var conference = CreateConference("37c3", "37C3");
            var evt = CreateEvent("test-event", "Test Event");
            var recordings = new List<Recording>
            {
                CreateRecording("https://example.com/low.mp4", width: 720, height: 480),
                CreateRecording("https://example.com/high.mp4", width: 1920, height: 1080)
            };
            evt.Recordings = recordings;

            _recordingSelectorMock
                .Setup(r => r.SelectBestRecording(recordings, It.IsAny<RecordingPreferences>()))
                .Returns(recordings[1]);

            // Act
            var result = await _strmGenerator.GenerateStrmAsync(conference, evt, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            var content = await File.ReadAllTextAsync(result.FilePath);
            Assert.Equal("https://example.com/high.mp4", content.Trim());
        }

        [Fact]
        public async Task GenerateStrm_overwrites_existing_file()
        {
            // Arrange
            var conference = CreateConference("37c3", "37C3");
            var evt = CreateEvent("opening-ceremony", "Opening Ceremony");
            var recording = CreateRecording("https://example.com/video.mp4");

            _recordingSelectorMock
                .Setup(r => r.SelectBestRecording(It.IsAny<IEnumerable<Recording>>(), It.IsAny<RecordingPreferences>()))
                .Returns(recording);

            // Act - First call
            var result1 = await _strmGenerator.GenerateStrmAsync(conference, evt, CancellationToken.None);

            // Modify recording URL
            var newRecording = CreateRecording("https://example.com/new-video.mp4");
            _recordingSelectorMock
                .Setup(r => r.SelectBestRecording(It.IsAny<IEnumerable<Recording>>(), It.IsAny<RecordingPreferences>()))
                .Returns(newRecording);

            // Act - Second call with same event
            var result2 = await _strmGenerator.GenerateStrmAsync(conference, evt, CancellationToken.None);

            // Assert
            Assert.Equal(result1.FilePath, result2.FilePath);
            var content = await File.ReadAllTextAsync(result2.FilePath);
            Assert.Equal("https://example.com/new-video.mp4", content.Trim());
        }

        [Fact]
        public async Task GenerateStrm_creates_parent_directories()
        {
            // Arrange
            var conference = CreateConference("37c3", "37C3");
            var evt = CreateEvent("opening-ceremony", "Opening Ceremony", day: 3);
            var recording = CreateRecording("https://example.com/video.mp4");

            // Ensure the Season 03 directory doesn't exist
            var seasonPath = Path.Combine(_testArchivePath, "37c3", "Season 03");
            if (Directory.Exists(seasonPath))
            {
                Directory.Delete(seasonPath, recursive: true);
            }

            _recordingSelectorMock
                .Setup(r => r.SelectBestRecording(It.IsAny<IEnumerable<Recording>>(), It.IsAny<RecordingPreferences>()))
                .Returns(recording);

            // Act
            var result = await _strmGenerator.GenerateStrmAsync(conference, evt, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.True(Directory.Exists(Path.GetDirectoryName(result.FilePath)));
        }

        [Fact]
        public async Task GenerateStrm_returns_null_when_no_recordings_available()
        {
            // Arrange
            var conference = CreateConference("37c3", "37C3");
            var evt = CreateEvent("no-recordings", "No Recordings");
            evt.Recordings = new List<Recording>();

            _recordingSelectorMock
                .Setup(r => r.SelectBestRecording(It.IsAny<IEnumerable<Recording>>(), It.IsAny<RecordingPreferences>()))
                .Returns((Recording?)null);

            // Act
            var result = await _strmGenerator.GenerateStrmAsync(conference, evt, CancellationToken.None);

            // Assert
            Assert.Null(result);
        }

        #endregion

        #region GenerateSeriesStrmTree Tests

        [Fact]
        public async Task GenerateSeriesStrmTree_creates_all_conferences()
        {
            // Arrange
            var conferences = new List<ConferenceDto>
            {
                CreateConferenceDto("37c3", "37C3", id: 1),
                CreateConferenceDto("36c3", "36C3", id: 2)
            };

            var events = new[]
            {
                CreateEventDto("event1", "Event 1", conferenceId: 1),
                CreateEventDto("event2", "Event 2", conferenceId: 1),
                CreateEventDto("event3", "Event 3", conferenceId: 2)
            };

            var recording = CreateRecording("https://example.com/video.mp4");

            _apiClientMock
                .Setup(a => a.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(conferences);

            _apiClientMock
                .Setup(a => a.GetEventsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(events);

            _recordingSelectorMock
                .Setup(r => r.SelectBestRecording(It.IsAny<IEnumerable<Recording>>(), It.IsAny<RecordingPreferences>()))
                .Returns(recording);

            // Act
            var result = await _strmGenerator.GenerateSeriesStrmTreeAsync(CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Count >= 2);
            Assert.True(Directory.Exists(Path.Combine(_testArchivePath, "37c3")));
            Assert.True(Directory.Exists(Path.Combine(_testArchivePath, "36c3")));
        }

        [Fact]
        public async Task GenerateSeriesStrmTree_skips_existing_files()
        {
            // Arrange
            var conferenceDto = CreateConferenceDto("37c3", "37C3");
            var eventDto = CreateEventDto("existing-event", "Existing Event", conferenceId: 1, day: 1);
            var recording = CreateRecording("https://example.com/video.mp4");

            // Pre-create the .strm file
            var existingPath = Path.Combine(_testArchivePath, "37c3", "Season 01", "existing-event.strm");
            Directory.CreateDirectory(Path.GetDirectoryName(existingPath)!);
            await File.WriteAllTextAsync(existingPath, "https://example.com/old.mp4");

            _apiClientMock
                .Setup(a => a.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ConferenceDto> { conferenceDto });

            _apiClientMock
                .Setup(a => a.GetEventsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { eventDto });

            _recordingSelectorMock
                .Setup(r => r.SelectBestRecording(It.IsAny<IEnumerable<Recording>>(), It.IsAny<RecordingPreferences>()))
                .Returns(recording);

            // Act
            await _strmGenerator.GenerateSeriesStrmTreeAsync(CancellationToken.None);

            // Assert
            var content = await File.ReadAllTextAsync(existingPath);
            Assert.Equal("https://example.com/old.mp4", content.Trim()); // File was not overwritten
        }

        [Fact]
        public async Task GenerateSeriesStrmTree_organizes_by_day()
        {
            // Arrange
            var conferenceDto = CreateConferenceDto("37c3", "37C3");
            var events = new[]
            {
                CreateEventDto("day1-morning", "Day 1 Morning", conferenceId: 1, day: 1),
                CreateEventDto("day1-evening", "Day 1 Evening", conferenceId: 1, day: 1),
                CreateEventDto("day2-morning", "Day 2 Morning", conferenceId: 1, day: 2)
            };

            var recording = CreateRecording("https://example.com/video.mp4");

            _apiClientMock
                .Setup(a => a.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ConferenceDto> { conferenceDto });

            _apiClientMock
                .Setup(a => a.GetEventsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(events);

            _recordingSelectorMock
                .Setup(r => r.SelectBestRecording(It.IsAny<IEnumerable<Recording>>(), It.IsAny<RecordingPreferences>()))
                .Returns(recording);

            // Act
            await _strmGenerator.GenerateSeriesStrmTreeAsync(CancellationToken.None);

            // Assert
            Assert.True(Directory.Exists(Path.Combine(_testArchivePath, "37c3", "Season 01")));
            Assert.True(Directory.Exists(Path.Combine(_testArchivePath, "37c3", "Season 02")));
        }

        [Fact]
        public async Task GenerateSeriesStrmTree_handles_empty_conferences()
        {
            // Arrange
            _apiClientMock
                .Setup(a => a.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ConferenceDto>());

            // Act
            var result = await _strmGenerator.GenerateSeriesStrmTreeAsync(CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public async Task GenerateSeriesStrmTree_handles_cancellation()
        {
            // Arrange
            var cts = new CancellationTokenSource();
            cts.Cancel();

            _apiClientMock
                .Setup(a => a.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException());

            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                _strmGenerator.GenerateSeriesStrmTreeAsync(cts.Token));
        }

        #endregion

        #region Edge Cases

        [Fact]
        public async Task GenerateStrm_handles_events_without_day_info()
        {
            // Arrange
            var conference = CreateConference("37c3", "37C3");
            var evt = CreateEvent("no-day-event", "No Day Event");
            evt.Date = null; // No date = cannot determine day

            var recording = CreateRecording("https://example.com/video.mp4");

            _recordingSelectorMock
                .Setup(r => r.SelectBestRecording(It.IsAny<IEnumerable<Recording>>(), It.IsAny<RecordingPreferences>()))
                .Returns(recording);

            // Act
            var result = await _strmGenerator.GenerateStrmAsync(conference, evt, CancellationToken.None);

            // Assert - Should default to Season 01 or no season folder
            Assert.NotNull(result);
        }

        [Fact]
        public async Task GenerateStrm_sanitizes_conference_acronyms()
        {
            // Arrange
            var conference = CreateConference("conf/test:name", "Conf/Test:Name");
            var evt = CreateEvent("event", "Event");
            var recording = CreateRecording("https://example.com/video.mp4");

            _recordingSelectorMock
                .Setup(r => r.SelectBestRecording(It.IsAny<IEnumerable<Recording>>(), It.IsAny<RecordingPreferences>()))
                .Returns(recording);

            // Act
            var result = await _strmGenerator.GenerateStrmAsync(conference, evt, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.DoesNotContain("/", Path.GetFileName(Path.GetDirectoryName(result.FilePath)!));
            Assert.DoesNotContain(":", result.FilePath);
        }

        [Fact]
        public async Task GenerateStrm_preserves_unicode_characters()
        {
            // Arrange
            var conference = CreateConference("37c3", "37C3");
            var evt = CreateEvent("über-keynote", "Über Keynote");
            var recording = CreateRecording("https://example.com/video.mp4");

            _recordingSelectorMock
                .Setup(r => r.SelectBestRecording(It.IsAny<IEnumerable<Recording>>(), It.IsAny<RecordingPreferences>()))
                .Returns(recording);

            // Act
            var result = await _strmGenerator.GenerateStrmAsync(conference, evt, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.Contains("über-keynote", result.FilePath);
        }

        [Fact]
        public async Task GenerateStrm_handles_multiple_recordings_same_event()
        {
            // Arrange
            var conference = CreateConference("37c3", "37C3");
            var evt = CreateEvent("multi-rec", "Multiple Recordings");
            var recordings = new List<Recording>
            {
                CreateRecording("https://example.com/low.mp4", language: "en", width: 640),
                CreateRecording("https://example.com/hd-en.mp4", language: "en", width: 1920),
                CreateRecording("https://example.com/hd-de.mp4", language: "de", width: 1920)
            };
            evt.Recordings = recordings;

            _recordingSelectorMock
                .Setup(r => r.SelectBestRecording(recordings, It.IsAny<RecordingPreferences>()))
                .Returns(recordings[1]);

            // Act
            var result = await _strmGenerator.GenerateStrmAsync(conference, evt, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            var content = await File.ReadAllTextAsync(result.FilePath);
            Assert.Contains("example.com/", content);
        }

        #endregion

        #region Helper Methods

        private static Conference CreateConference(string acronym, string title)
        {
            return new Conference
            {
                Acronym = acronym,
                Title = title,
                Slug = acronym.ToLower(),
                AspectRatio = "16:9"
            };
        }

        private static ConferenceDto CreateConferenceDto(string acronym, string title, int id = 1)
        {
            return new ConferenceDto
            {
                Id = id,
                Acronym = acronym,
                Title = title,
                Slug = acronym.ToLower(),
                AspectRatio = "16:9"
            };
        }

        private static Event CreateEvent(string slug, string title, int conferenceId = 1, int? day = null)
        {
            return new Event
            {
                Guid = Guid.NewGuid().ToString(),
                Slug = slug,
                Title = title,
                ConferenceId = conferenceId,
                Date = day.HasValue ? $"2023-12-{27 + day.Value:D2}" : null,
                Recordings = new List<Recording>()
            };
        }

        private static EventDto CreateEventDto(string slug, string title, int conferenceId = 1, int? day = null)
        {
            return new EventDto
            {
                Guid = Guid.NewGuid().ToString(),
                Slug = slug,
                Title = title,
                ConferenceId = conferenceId,
                Date = day.HasValue ? $"2023-12-{27 + day.Value:D2}" : null,
                Recordings = new List<RecordingDto>()
            };
        }

        private static Recording CreateRecording(
            string url,
            string language = "en",
            int width = 1920,
            int height = 1080)
        {
            return new Recording
            {
                Url = url,
                Language = language,
                Width = width,
                Height = height,
                HighQuality = width >= 1920,
                Format = "mp4"
            };
        }

        private static RecordingDto CreateRecordingDto(
            string url,
            string language = "en",
            int width = 1920,
            int height = 1080)
        {
            return new RecordingDto
            {
                Url = url,
                Language = language,
                Width = width,
                Height = height,
                HighQuality = width >= 1920,
                Format = "mp4"
            };
        }

        #endregion
    }
}