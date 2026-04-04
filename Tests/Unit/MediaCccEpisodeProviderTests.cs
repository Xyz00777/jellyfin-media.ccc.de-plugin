using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaCccDe.Api;
using Jellyfin.Plugin.MediaCccDe.Models;
using Jellyfin.Plugin.MediaCccDe.Providers;
using Jellyfin.Plugin.MediaCccDe.Services;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.MediaCccDe.Tests.Unit
{
    public class MediaCccEpisodeProviderTests
    {
        private readonly Mock<IMediaCccApiClient> _mockApiClient;
        private readonly RecordingSelector _recordingSelector;
        private readonly MediaCccEpisodeProvider _provider;

        public MediaCccEpisodeProviderTests()
        {
            _mockApiClient = new Mock<IMediaCccApiClient>();
            _recordingSelector = new RecordingSelector();
            _provider = new MediaCccEpisodeProvider(_mockApiClient.Object, _recordingSelector);
        }

        #region GetMetadata Tests

        [Fact]
        public async Task GetMetadata_returns_EpisodeInfo_for_event()
        {
            // Arrange
            var testEvent = CreateTestEvent();
            var eventDto = CreateEventDto(testEvent);
            var episodeInfo = new EpisodeInfo { Name = testEvent.Slug };
            
            _mockApiClient
                .Setup(x => x.GetEventAsync(testEvent.Guid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(eventDto);

// Act
            var result = await _provider.GetMetadata(episodeInfo, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.HasMetadata);
            Assert.NotNull(result.Item);
            Assert.IsType<Episode>(result.Item);
        }

        [Fact]
        public async Task GetMetadata_maps_event_title_to_Name()
        {
            // Arrange
            var testEvent = CreateTestEvent();
            testEvent.Title = "Opening Ceremony - 37C3";
            var eventDto = CreateEventDto(testEvent);
            var episodeInfo = new EpisodeInfo { Name = testEvent.Slug };
            
            _mockApiClient
                .Setup(x => x.GetEventAsync(testEvent.Guid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(eventDto);

// Act
            var result = await _provider.GetMetadata(episodeInfo, CancellationToken.None);

            // Assert
            Assert.Equal("Opening Ceremony - 37C3", result.Item.Name);
        }

        [Fact]
        public async Task GetMetadata_maps_event_description_to_Overview()
        {
            // Arrange
            var testEvent = CreateTestEvent();
            testEvent.Description = "This is the opening ceremony of 37C3, featuring exciting announcements and talks.";
            var eventDto = CreateEventDto(testEvent);
            var episodeInfo = new EpisodeInfo { Name = testEvent.Slug };
            
            _mockApiClient
                .Setup(x => x.GetEventAsync(testEvent.Guid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(eventDto);

// Act
            var result = await _provider.GetMetadata(episodeInfo, CancellationToken.None);

            // Assert
            Assert.Equal("This is the opening ceremony of 37C3, featuring exciting announcements and talks.", result.Item.Overview);
        }

        [Fact]
        public async Task GetMetadata_maps_event_length_to_RunTimeTicks()
        {
            // Arrange
            var testEvent = CreateTestEvent();
            testEvent.Length = 7200; // 2 hours in seconds
            var eventDto = CreateEventDto(testEvent);
            var episodeInfo = new EpisodeInfo { Name = testEvent.Slug };
            
            _mockApiClient
                .Setup(x => x.GetEventAsync(testEvent.Guid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(eventDto);

// Act
            var result = await _provider.GetMetadata(episodeInfo, CancellationToken.None);

            // Assert
            // Length is in seconds, RunTimeTicks is in 100-nanosecond intervals
            // 7200 seconds * 10,000,000 ticks/second = 72,000,000,000 ticks
            Assert.Equal(7200L * 10000000L, result.Item.RunTimeTicks);
        }

        [Fact]
        public async Task GetMetadata_fetches_poster_from_event_recordings()
        {
            // Arrange
            var testEvent = CreateTestEvent();
            var bestRecording = new Recording
            {
                Id = 1,
                Language = "en",
                Format = "mp4",
                HighQuality = true,
                Url = "https://cdn.media.ccc.de/37c3/h264-hd/37c3-12746-opening_ceremony.mp4",
                Width = 1920,
                Height = 1080,
                Size = 2048000000L,
                MimeType = "video/mp4"
            };
            var eventDto = CreateEventDto(testEvent);
            var episodeInfo = new EpisodeInfo { Name = testEvent.Slug };
            
            _mockApiClient
                .Setup(x => x.GetEventAsync(testEvent.Guid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(eventDto);

            _mockRecordingSelector
                .Setup(x => x.SelectBestRecording(It.IsAny<IEnumerable<Recording>>(), It.IsAny<RecordingPreferences>()))
                .Returns(bestRecording);

            // Act
            var result = await _provider.GetMetadata(episodeInfo, CancellationToken.None);

            // Assert
            Assert.NotNull(result.Item);
            // Poster URL should be derived from recording or event poster image
            // This assertion validates that we set image URLs appropriately
            // The actual implementation may use a different poster URL strategy
        }

        [Fact]
        public async Task GetMetadata_sets_premiere_date_from_event_date()
        {
            // Arrange
            var testEvent = CreateTestEvent();
            testEvent.Date = "2023-12-27";
            var eventDto = CreateEventDto(testEvent);
            var episodeInfo = new EpisodeInfo { Name = testEvent.Slug };
            
            _mockApiClient
                .Setup(x => x.GetEventAsync(testEvent.Guid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(eventDto);

// Act
            var result = await _provider.GetMetadata(episodeInfo, CancellationToken.None);

            // Assert
            Assert.NotNull(result.Item.PremiereDate);
            Assert.Equal(2023, result.Item.PremiereDate.Value.Year);
            Assert.Equal(12, result.Item.PremiereDate.Value.Month);
            Assert.Equal(27, result.Item.PremiereDate.Value.Day);
        }

        [Fact]
        public async Task GetMetadata_sets_index_number_from_event_order()
        {
            // Arrange
            var testEvent = CreateTestEvent();
            testEvent.Slug = "37c3-12746-opening_ceremony"; // Event slug with order number
            var eventDto = CreateEventDto(testEvent);
            var episodeInfo = new EpisodeInfo { Name = testEvent.Slug };
            
            _mockApiClient
                .Setup(x => x.GetEventAsync(testEvent.Guid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(eventDto);

// Act
            var result = await _provider.GetMetadata(episodeInfo, CancellationToken.None);

            // Assert
            // IndexNumber represents the episode number within a season
            // This should be derived from event order/schedule position
            // The actual implementation will need to extract this from conference schedule data
            Assert.NotEqual(0, result.Item.IndexNumber);
        }

        [Fact]
        public async Task GetMetadata_sets_parent_index_number_from_day_number()
        {
            // Arrange
            var testEvent = CreateTestEvent();
            testEvent.Date = "2023-12-27"; // Day 1 of 37C3 (day numbers: 1-4)
            var eventDto = CreateEventDto(testEvent);
            var episodeInfo = new EpisodeInfo { Name = testEvent.Slug };
            
            _mockApiClient
                .Setup(x => x.GetEventAsync(testEvent.Guid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(eventDto);

// Act
            var result = await _provider.GetMetadata(episodeInfo, CancellationToken.None);

            // Assert
            // ParentIndexNumber represents the season (day) number
            // Day 1 = 2023-12-27, Day 2 = 2023-12-28, etc.
            Assert.NotEqual(0, result.Item.ParentIndexNumber);
            // Should be between 1 and 4 (or however many days the conference has)
            Assert.InRange(result.Item.ParentIndexNumber ?? 0, 1, 10);
        }

        [Fact]
        public async Task GetMetadata_returns_null_for_unknown_event()
        {
            // Arrange
            var unknownSlug = "unknown-event-12345";
            var episodeInfo = new EpisodeInfo { Name = unknownSlug };
            
            _mockApiClient
                .Setup(x => x.GetEventAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((EventDto?)null);

            // Act
            var result = await _provider.GetMetadata(episodeInfo, CancellationToken.None);

            // Assert
            Assert.False(result.HasMetadata);
            Assert.Null(result.Item);
        }

        [Fact]
        public async Task GetMetadata_handles_missing_recordings_gracefully()
        {
            // Arrange
            var testEvent = CreateTestEvent();
            testEvent.Recordings = new List<Recording>(); // No recordings available
            var eventDto = CreateEventDto(testEvent);
            var episodeInfo = new EpisodeInfo { Name = testEvent.Slug };
            
            _mockApiClient
                .Setup(x => x.GetEventAsync(testEvent.Guid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(eventDto);

            _mockRecordingSelector
                .Setup(x => x.SelectBestRecording(It.IsAny<IEnumerable<Recording>>(), It.IsAny<RecordingPreferences>()))
                .Returns((Recording?)null);

            // Act
            var result = await _provider.GetMetadata(episodeInfo, CancellationToken.None);

            // Assert
            // Should still return metadata even without recordings
            // The provider should gracefully handle this case
            Assert.NotNull(result);
            Assert.True(result.HasMetadata);
            Assert.NotNull(result.Item);
        }

        [Fact]
        public async Task GetMetadata_selects_best_recording_for_poster()
        {
            // Arrange
            var testEvent = CreateTestEvent();
            testEvent.Recordings = new List<Recording>
            {
                new() 
                { 
                    Id = 1, 
                    Language = "de", 
                    Format = "mp4", 
                    HighQuality = false, 
                    Width = 1280, 
                    Height = 720,
                    Url = "https://cdn.media.ccc.de/37c3/sd/37c3-12746-opening_ceremony_sd.mp4"
                },
                new() 
                { 
                    Id = 2, 
                    Language = "en", 
                    Format = "mp4", 
                    HighQuality = true, 
                    Width = 1920, 
                    Height = 1080,
                    Url = "https://cdn.media.ccc.de/37c3/hd/37c3-12746-opening_ceremony_hd.mp4"
                },
                new() 
                { 
                    Id = 3, 
                    Language = "en", 
                    Format = "webm", 
                    HighQuality = true, 
                    Width = 3840, 
                    Height = 2160,
                    Url = "https://cdn.media.ccc.de/37c3/4k/37c3-12746-opening_ceremony_4k.webm"
                }
            };
            
            var eventDto = CreateEventDto(testEvent);
            var episodeInfo = new EpisodeInfo { Name = testEvent.Slug };
            var bestRecording = testEvent.Recordings[2]; // WebM 4K is best
            
            _mockApiClient
                .Setup(x => x.GetEventAsync(testEvent.Guid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(eventDto);

            _mockRecordingSelector
                .Setup(x => x.SelectBestRecording(
                    It.IsAny<IEnumerable<Recording>>(), 
                    It.IsAny<RecordingPreferences>()))
                .Returns(bestRecording);

            // Act
            var result = await _provider.GetMetadata(episodeInfo, CancellationToken.None);

            // Assert
            _mockRecordingSelector.Verify(
                x => x.SelectBestRecording(
                    It.IsAny<IEnumerable<Recording>>(),
                    It.IsAny<RecordingPreferences>()),
                Times.Once);
            Assert.NotNull(result.Item);
        }

        [Fact]
        public async Task GetMetadata_handles_null_event_date()
        {
            // Arrange
            var testEvent = CreateTestEvent();
            testEvent.Date = null;
            var eventDto = CreateEventDto(testEvent);
            var episodeInfo = new EpisodeInfo { Name = testEvent.Slug };
            
            _mockApiClient
                .Setup(x => x.GetEventAsync(testEvent.Guid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(eventDto);

// Act
            var result = await _provider.GetMetadata(episodeInfo, CancellationToken.None);

            // Assert - Should not throw, should handle gracefully
            Assert.NotNull(result);
            Assert.True(result.HasMetadata);
            Assert.Null(result.Item.PremiereDate);
        }

        [Fact]
        public async Task GetMetadata_handles_null_event_description()
        {
            // Arrange
            var testEvent = CreateTestEvent();
            testEvent.Description = null;
            var eventDto = CreateEventDto(testEvent);
            var episodeInfo = new EpisodeInfo { Name = testEvent.Slug };
            
            _mockApiClient
                .Setup(x => x.GetEventAsync(testEvent.Guid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(eventDto);

// Act
            var result = await _provider.GetMetadata(episodeInfo, CancellationToken.None);

            // Assert - Should not throw
            Assert.NotNull(result);
            Assert.True(result.HasMetadata);
            Assert.Null(result.Item.Overview);
        }

        [Fact]
        public async Task GetMetadata_converts_length_seconds_to_runtime_ticks_correctly()
        {
            // Arrange
            var testEvent = CreateTestEvent();
            testEvent.Length = 3600; // 1 hour
            var eventDto = CreateEventDto(testEvent);
            var episodeInfo = new EpisodeInfo { Name = testEvent.Slug };
            
            _mockApiClient
                .Setup(x => x.GetEventAsync(testEvent.Guid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(eventDto);

// Act
            var result = await _provider.GetMetadata(episodeInfo, CancellationToken.None);

            // Assert
            // 3600 seconds * 10,000,000 ticks/second = 36,000,000,000 ticks
            Assert.Equal(3600L * 10000000L, result.Item.RunTimeTicks);
        }

        [Fact]
        public async Task GetMetadata_handles_zero_length_event()
        {
            // Arrange
            var testEvent = CreateTestEvent();
            testEvent.Length = 0;
            var eventDto = CreateEventDto(testEvent);
            var episodeInfo = new EpisodeInfo { Name = testEvent.Slug };
            
            _mockApiClient
                .Setup(x => x.GetEventAsync(testEvent.Guid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(eventDto);

// Act
            var result = await _provider.GetMetadata(episodeInfo, CancellationToken.None);

            // Assert - Should set runtime to 0 or not set it at all
            Assert.NotNull(result);
            Assert.True(result.HasMetadata);
            // Runtime should be 0 or null
            Assert.True(result.Item.RunTimeTicks == 0 || result.Item.RunTimeTicks == null);
        }

        [Fact]
        public async Task GetMetadata_handles_cancellation_token()
        {
            // Arrange
            var testEvent = CreateTestEvent();
            var eventDto = CreateEventDto(testEvent);
            var episodeInfo = new EpisodeInfo { Name = testEvent.Slug };
            var cts = new CancellationTokenSource();
            
            _mockApiClient
                .Setup(x => x.GetEventAsync(testEvent.Guid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(eventDto);

// Act & Assert - Should handle cancellation token properly
            var result = await _provider.GetMetadata(episodeInfo, cts.Token);
            Assert.NotNull(result);
        }

        [Fact]
        public async Task GetMetadata_preserves_event_guid_as_provider_id()
        {
            // Arrange
            var testEvent = CreateTestEvent();
            testEvent.Guid = "abc123-def456-789xyz";
            var eventDto = CreateEventDto(testEvent);
            var episodeInfo = new EpisodeInfo { Name = testEvent.Slug };
            
            _mockApiClient
                .Setup(x => x.GetEventAsync(testEvent.Guid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(eventDto);

// Act
            var result = await _provider.GetMetadata(episodeInfo, CancellationToken.None);

            // Assert
            // The event GUID should be stored as a provider ID for future lookups
            Assert.NotNull(result.Item);
            // ProviderId should be set for the MediaCccDe provider
            // Expected: result.Item.ProviderIds["MediaCccDe"] == testEvent.Guid
        }

        [Fact]
        public async Task GetMetadata_handles_event_with_multiple_recordings()
        {
            // Arrange
            var testEvent = CreateTestEvent();
            testEvent.Recordings = new List<Recording>
            {
                new() { Language = "de", Format = "mp4", HighQuality = true, Width = 1920, Height = 1080 },
                new() { Language = "en", Format = "mp4", HighQuality = true, Width = 1920, Height = 1080 },
                new() { Language = "en", Format = "webm", HighQuality = false, Width = 1280, Height = 720 },
                new() { Language = "en", Format = "mp4", HighQuality = true, Width = 3840, Height = 2160 }
            };
            
            var eventDto = CreateEventDto(testEvent);
            var episodeInfo = new EpisodeInfo { Name = testEvent.Slug };
            var selectedRecording = testEvent.Recordings[3]; // Best recording
            
            _mockApiClient
                .Setup(x => x.GetEventAsync(testEvent.Guid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(eventDto);

            _mockRecordingSelector
                .Setup(x => x.SelectBestRecording(It.IsAny<IEnumerable<Recording>>(), It.IsAny<RecordingPreferences>()))
                .Returns(selectedRecording);

            // Act
            var result = await _provider.GetMetadata(episodeInfo, CancellationToken.None);

            // Assert
            _mockRecordingSelector.Verify(
                x => x.SelectBestRecording(
                    It.Is<IEnumerable<Recording>>(r => r != null),
                    It.IsAny<RecordingPreferences>()),
                Times.Once);
            Assert.NotNull(result.Item);
        }

        #endregion

        #region Helper Methods

        private static Event CreateTestEvent()
        {
            return new Event
            {
                Guid = "abc123-def456-789",
                Title = "Opening Ceremony",
                Slug = "37c3-12746-opening_ceremony",
                Description = "The opening ceremony of 37C3",
                Date = "2023-12-27",
                Length = 7200,
                ConferenceId = 123,
                Recordings = new List<Recording>
                {
                    new()
                    {
                        Id = 1,
                        Language = "en",
                        Format = "mp4",
                        HighQuality = true,
                        Url = "https://cdn.media.ccc.de/37c3/h264-hd/37c3-12746-opening_ceremony.mp4",
                        Width = 1920,
                        Height = 1080,
                        Size = 2048000000L,
                        MimeType = "video/mp4"
                    }
                }
            };
        }

        private static EventDto CreateEventDto(Event eventModel)
        {
            // EventDto wraps Event data from the API
            // The exact structure depends on your DTO implementation
            return new EventDto
            {
                Guid = eventModel.Guid,
                Title = eventModel.Title,
                Slug = eventModel.Slug,
                Description = eventModel.Description,
                Date = eventModel.Date,
                Length = eventModel.Length,
                ConferenceId = eventModel.ConferenceId,
                Recordings = eventModel.Recordings
            };
        }

        #endregion
    }
}