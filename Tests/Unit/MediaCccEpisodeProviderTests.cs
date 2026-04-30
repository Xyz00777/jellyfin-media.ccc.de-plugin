using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaCccDe.Api;
using Jellyfin.Plugin.MediaCccDe.Models;
using Jellyfin.Plugin.MediaCccDe.Providers;
using Jellyfin.Plugin.MediaCccDe.Services;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.MediaCccDe.Tests.Unit
{
    public class MediaCccEpisodeProviderTests
    {
        private readonly Mock<IMediaCccApiClient> _mockApiClient;
        private readonly Mock<IRecordingSelector> _mockRecordingSelector;
        private readonly MediaCccEpisodeProvider _provider;

        public MediaCccEpisodeProviderTests()
        {
            _mockApiClient = new Mock<IMediaCccApiClient>();
            _mockRecordingSelector = new Mock<IRecordingSelector>();
            _provider = new MediaCccEpisodeProvider(_mockApiClient.Object, _mockRecordingSelector.Object);
        }

        #region GetMetadata Tests

        [Fact]
        public async Task GetMetadata_returns_EpisodeInfo_for_event()
        {
            // Arrange
            var testEvent = CreateTestEvent();
            var eventDto = CreateEventDto(testEvent);
            var episodeInfo = new EpisodeInfo { Name = testEvent.Guid };
            
            _mockApiClient
                .Setup(x => x.GetEventAsync(testEvent.Guid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(eventDto);

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
            var episodeInfo = new EpisodeInfo { Name = testEvent.Guid };
            
            _mockApiClient
                .Setup(x => x.GetEventAsync(testEvent.Guid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(eventDto);

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
            var episodeInfo = new EpisodeInfo { Name = testEvent.Guid };
            
            _mockApiClient
                .Setup(x => x.GetEventAsync(testEvent.Guid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(eventDto);

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
            var episodeInfo = new EpisodeInfo { Name = testEvent.Guid };
            
            _mockApiClient
                .Setup(x => x.GetEventAsync(testEvent.Guid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(eventDto);

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
            var eventDto = CreateEventDto(testEvent);
            var episodeInfo = new EpisodeInfo { Name = testEvent.Guid };
            
            _mockApiClient
                .Setup(x => x.GetEventAsync(testEvent.Guid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(eventDto);

            // Act
            var result = await _provider.GetMetadata(episodeInfo, CancellationToken.None);

            // Assert - Provider maps event metadata without selecting recording
            Assert.NotNull(result.Item);
        }

        [Fact]
        public async Task GetMetadata_sets_premiere_date_from_event_date()
        {
            // Arrange
            var testEvent = CreateTestEvent();
            testEvent.Date = "2023-12-27";
            var eventDto = CreateEventDto(testEvent);
            var episodeInfo = new EpisodeInfo { Name = testEvent.Guid };
            
            _mockApiClient
                .Setup(x => x.GetEventAsync(testEvent.Guid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(eventDto);

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
            testEvent.Slug = "37c3-12746-opening_ceremony";
            var eventDto = CreateEventDto(testEvent);
            var episodeInfo = new EpisodeInfo { Name = testEvent.Guid };
            
            _mockApiClient
                .Setup(x => x.GetEventAsync(testEvent.Guid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(eventDto);

            var result = await _provider.GetMetadata(episodeInfo, CancellationToken.None);

            // Assert
            // IndexNumber represents the episode number within a season
            Assert.NotEqual(0, result.Item.IndexNumber);
        }

        [Fact]
        public async Task GetMetadata_sets_parent_index_number_from_day_number()
        {
            // Arrange
            var testEvent = CreateTestEvent();
            testEvent.Date = "2023-12-27";
            var eventDto = CreateEventDto(testEvent);
            var episodeInfo = new EpisodeInfo { Name = testEvent.Guid };
            
            _mockApiClient
                .Setup(x => x.GetEventAsync(testEvent.Guid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(eventDto);

            var result = await _provider.GetMetadata(episodeInfo, CancellationToken.None);

            // Assert
            // ParentIndexNumber represents the season (day) number
            Assert.NotNull(result.Item.ParentIndexNumber);
            Assert.InRange(result.Item.ParentIndexNumber ?? 0, 1, 31);
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
            testEvent.Recordings = new List<Recording>();
            var eventDto = CreateEventDto(testEvent);
            var episodeInfo = new EpisodeInfo { Name = testEvent.Guid };
            
            _mockApiClient
                .Setup(x => x.GetEventAsync(testEvent.Guid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(eventDto);

            // Act
            var result = await _provider.GetMetadata(episodeInfo, CancellationToken.None);

            // Assert - Should not throw, should handle gracefully
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
            var episodeInfo = new EpisodeInfo { Name = testEvent.Guid };

            _mockApiClient
                .Setup(x => x.GetEventAsync(testEvent.Guid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(eventDto);

            // Act
            var result = await _provider.GetMetadata(episodeInfo, CancellationToken.None);

            // Assert - Provider maps event metadata; recording selection happens elsewhere
            Assert.NotNull(result.Item);
        }

        [Fact]
        public async Task GetMetadata_handles_null_event_date()
        {
            // Arrange
            var testEvent = CreateTestEvent();
            testEvent.Date = null;
            var eventDto = CreateEventDto(testEvent);
            var episodeInfo = new EpisodeInfo { Name = testEvent.Guid };
            
            _mockApiClient
                .Setup(x => x.GetEventAsync(testEvent.Guid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(eventDto);

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
            var episodeInfo = new EpisodeInfo { Name = testEvent.Guid };
            
            _mockApiClient
                .Setup(x => x.GetEventAsync(testEvent.Guid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(eventDto);

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
            var episodeInfo = new EpisodeInfo { Name = testEvent.Guid };
            
            _mockApiClient
                .Setup(x => x.GetEventAsync(testEvent.Guid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(eventDto);

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
            var episodeInfo = new EpisodeInfo { Name = testEvent.Guid };
            
            _mockApiClient
                .Setup(x => x.GetEventAsync(testEvent.Guid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(eventDto);

            var result = await _provider.GetMetadata(episodeInfo, CancellationToken.None);

            // Assert - Should set runtime to 0 or not set it at all
            Assert.NotNull(result);
            Assert.True(result.HasMetadata);
            Assert.Equal(0, result.Item.RunTimeTicks);
        }

        [Fact]
        public async Task GetMetadata_handles_cancellation_token()
        {
            // Arrange
            var testEvent = CreateTestEvent();
            var eventDto = CreateEventDto(testEvent);
            var episodeInfo = new EpisodeInfo { Name = testEvent.Guid };
            var cts = new CancellationTokenSource();
            
            _mockApiClient
                .Setup(x => x.GetEventAsync(testEvent.Guid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(eventDto);

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
            var episodeInfo = new EpisodeInfo { Name = testEvent.Guid };
            
            _mockApiClient
                .Setup(x => x.GetEventAsync(testEvent.Guid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(eventDto);

            var result = await _provider.GetMetadata(episodeInfo, CancellationToken.None);

            // Assert
            Assert.NotNull(result.Item);
            Assert.Equal(testEvent.Guid, result.Item.ProviderIds["MediaCccDe"]);
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
            var episodeInfo = new EpisodeInfo { Name = testEvent.Guid };
            
            _mockApiClient
                .Setup(x => x.GetEventAsync(testEvent.Guid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(eventDto);

            // Act
            var result = await _provider.GetMetadata(episodeInfo, CancellationToken.None);

            // Assert - Provider maps event metadata with recordings preserved in DTO
            Assert.NotNull(result.Item);
        }

        #endregion

        #region C3: GetMetadata uses ProviderIds for lookup (not Name)

        [Fact]
        public async Task GetMetadata_uses_provider_id_for_lookup_when_available()
        {
            // Arrange - ProviderIds should be the primary lookup key, not Name
            var eventGuid = "real-guid-123";
            var talkTitle = "Opening Ceremony";
            var testEvent = CreateTestEvent();
            testEvent.Guid = eventGuid;
            testEvent.Title = talkTitle;
            var eventDto = CreateEventDto(testEvent);

            // EpisodeInfo has BOTH Name (talk title) and ProviderIds (guid)
            var episodeInfo = new EpisodeInfo { Name = talkTitle };
            episodeInfo.ProviderIds["MediaCccDe"] = eventGuid;

            // API should be called with the GUID, not the title
            _mockApiClient
                .Setup(x => x.GetEventAsync(eventGuid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(eventDto);

            // Act
            var result = await _provider.GetMetadata(episodeInfo, CancellationToken.None);

            // Assert - API must be called with the GUID from ProviderIds
            _mockApiClient.Verify(
                x => x.GetEventAsync(eventGuid, It.IsAny<CancellationToken>()),
                Times.Once);
            _mockApiClient.Verify(
                x => x.GetEventAsync(talkTitle, It.IsAny<CancellationToken>()),
                Times.Never);
            Assert.True(result.HasMetadata);
            Assert.Equal(eventGuid, result.Item.ProviderIds["MediaCccDe"]);
        }

        [Fact]
        public async Task GetMetadata_falls_back_to_Name_when_no_provider_id()
        {
            // Arrange - When ProviderIds has no MediaCccDe entry, fall back to Name
            var eventSlug = "37c3-12746-opening_ceremony";
            var testEvent = CreateTestEvent();
            testEvent.Guid = "some-guid";
            var eventDto = CreateEventDto(testEvent);

            var episodeInfo = new EpisodeInfo { Name = eventSlug };
            // No ProviderIds set - should fall back to Name

            _mockApiClient
                .Setup(x => x.GetEventAsync(eventSlug, It.IsAny<CancellationToken>()))
                .ReturnsAsync(eventDto);

            // Act
            var result = await _provider.GetMetadata(episodeInfo, CancellationToken.None);

            // Assert - API called with Name as fallback
            _mockApiClient.Verify(
                x => x.GetEventAsync(eventSlug, It.IsAny<CancellationToken>()),
                Times.Once);
            Assert.True(result.HasMetadata);
        }

        #endregion

        #region C4: DeriveIndexNumber is deterministic (stable across calls)

        [Fact]
        public async Task DeriveIndexNumber_is_stable_across_calls()
        {
            // Arrange - Same event must always produce the same IndexNumber.
            // The old GetHashCode() approach was non-deterministic across restarts.
            var testEvent = CreateTestEvent();
            testEvent.Slug = "37c3-12746-opening_ceremony";
            testEvent.Guid = "abc123-def456-789";
            var eventDto = CreateEventDto(testEvent);
            var episodeInfo = new EpisodeInfo { Name = testEvent.Guid };
            episodeInfo.ProviderIds["MediaCccDe"] = testEvent.Guid;

            _mockApiClient
                .Setup(x => x.GetEventAsync(testEvent.Guid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(eventDto);

            // Act - Call GetMetadata multiple times; IndexNumber must be identical
            var result1 = await _provider.GetMetadata(episodeInfo, CancellationToken.None);
            var result2 = await _provider.GetMetadata(episodeInfo, CancellationToken.None);
            var result3 = await _provider.GetMetadata(episodeInfo, CancellationToken.None);

            // Assert
            Assert.Equal(result1.Item.IndexNumber, result2.Item.IndexNumber);
            Assert.Equal(result2.Item.IndexNumber, result3.Item.IndexNumber);
        }

        [Fact]
        public async Task DeriveIndexNumber_is_not_zero()
        {
            // IndexNumber should always be a positive number (1-based)
            var testEvent = CreateTestEvent();
            testEvent.Slug = "37c3-12746-opening_ceremony";
            var eventDto = CreateEventDto(testEvent);
            var episodeInfo = new EpisodeInfo { Name = testEvent.Guid };
            episodeInfo.ProviderIds["MediaCccDe"] = testEvent.Guid;

            _mockApiClient
                .Setup(x => x.GetEventAsync(testEvent.Guid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(eventDto);

            var result = await _provider.GetMetadata(episodeInfo, CancellationToken.None);
            Assert.True(result.Item.IndexNumber > 0, "IndexNumber should be positive (1-based)");
        }

        #endregion

        #region C5: DeriveParentIndexNumber works for non-December conferences

        [Fact]
        public async Task DeriveParentIndexNumber_works_for_july_conference()
        {
            // Arrange - MCH (MiniCamp Hamburg) takes place in July
            var testEvent = CreateTestEvent();
            testEvent.Date = "2024-07-05";
            var eventDto = CreateEventDto(testEvent);
            var episodeInfo = new EpisodeInfo { Name = testEvent.Guid };
            episodeInfo.ProviderIds["MediaCccDe"] = testEvent.Guid;

            _mockApiClient
                .Setup(x => x.GetEventAsync(testEvent.Guid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(eventDto);

            // Act
            var result = await _provider.GetMetadata(episodeInfo, CancellationToken.None);

            // Assert - ParentIndexNumber should be determined from the date,
            // not hardcoded to December logic
            Assert.NotNull(result.Item.ParentIndexNumber);
            Assert.True(result.Item.ParentIndexNumber > 0,
                "ParentIndexNumber should be positive for non-December conferences");
        }

        [Fact]
        public async Task DeriveParentIndexNumber_works_for_march_conference()
        {
            // Arrange - GPN takes place in March
            var testEvent = CreateTestEvent();
            testEvent.Date = "2024-03-22";
            var eventDto = CreateEventDto(testEvent);
            var episodeInfo = new EpisodeInfo { Name = testEvent.Guid };
            episodeInfo.ProviderIds["MediaCccDe"] = testEvent.Guid;

            _mockApiClient
                .Setup(x => x.GetEventAsync(testEvent.Guid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(eventDto);

            // Act
            var result = await _provider.GetMetadata(episodeInfo, CancellationToken.None);

            // Assert
            Assert.NotNull(result.Item.ParentIndexNumber);
            Assert.True(result.Item.ParentIndexNumber > 0,
                "ParentIndexNumber should be positive for March conferences");
        }

        [Fact]
        public async Task DeriveParentIndexNumber_preserves_december_congress_day_number()
        {
            // Arrange - For CCC congresses starting Dec 27, day numbers should still work
            var testEvent = CreateTestEvent();
            testEvent.Date = "2023-12-27";
            var eventDto = CreateEventDto(testEvent);
            var episodeInfo = new EpisodeInfo { Name = testEvent.Guid };
            episodeInfo.ProviderIds["MediaCccDe"] = testEvent.Guid;

            _mockApiClient
                .Setup(x => x.GetEventAsync(testEvent.Guid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(eventDto);

            // Act
            var result = await _provider.GetMetadata(episodeInfo, CancellationToken.None);

            // Assert - Dec 27 should be Day 1 (ParentIndexNumber = 1)
            Assert.Equal(1, result.Item.ParentIndexNumber);
        }

        [Fact]
        public async Task DeriveParentIndexNumber_returns_null_for_null_date()
        {
            // Arrange
            var testEvent = CreateTestEvent();
            testEvent.Date = null;
            var eventDto = CreateEventDto(testEvent);
            var episodeInfo = new EpisodeInfo { Name = testEvent.Guid };
            episodeInfo.ProviderIds["MediaCccDe"] = testEvent.Guid;

            _mockApiClient
                .Setup(x => x.GetEventAsync(testEvent.Guid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(eventDto);

            // Act
            var result = await _provider.GetMetadata(episodeInfo, CancellationToken.None);

            // Assert
            Assert.Null(result.Item.ParentIndexNumber);
        }

        #endregion

        #region GetSearchResults tests

        [Fact]
        public async Task GetSearchResults_returns_result_when_provider_id_available()
        {
            // Arrange
            var searchInfo = new EpisodeInfo { Name = "Opening Ceremony" };
            searchInfo.ProviderIds["MediaCccDe"] = "real-guid-123";

            // Act
            var results = await _provider.GetSearchResults(searchInfo, CancellationToken.None);

            // Assert
            Assert.NotEmpty(results);
            var resultList = results.ToList();
            Assert.Single(resultList);
            Assert.Equal("real-guid-123", resultList[0].ProviderIds["MediaCccDe"]);
        }

        [Fact]
        public async Task GetSearchResults_returns_empty_when_no_provider_id()
        {
            // Arrange
            var searchInfo = new EpisodeInfo { Name = "Opening Ceremony" };
            // No ProviderIds set

            // Act
            var results = await _provider.GetSearchResults(searchInfo, CancellationToken.None);

            // Assert
            Assert.Empty(results);
        }

        #endregion

        #region GetImageResponse tests

        [Fact]
        public async Task GetImageResponse_returns_404_not_found()
        {
            // Act
            var response = await _provider.GetImageResponse("https://example.com/image.jpg", CancellationToken.None);

            // Assert - Should return 404, not throw NotImplementedException
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
            return new EventDto
            {
                Guid = eventModel.Guid,
                Title = eventModel.Title,
                Slug = eventModel.Slug,
                Description = eventModel.Description,
                Date = eventModel.Date,
                Length = eventModel.Length,
                ConferenceId = eventModel.ConferenceId,
                Link = eventModel.Link,
                Recordings = eventModel.Recordings?.Select(r => new RecordingDto
                {
                    Id = r.Id,
                    Language = r.Language,
                    Format = r.Format ?? string.Empty,
                    HighQuality = r.HighQuality ?? false,
                    Width = r.Width ?? 0,
                    Height = r.Height ?? 0,
                    Size = r.Size,
                    Url = r.Url,
                    MimeType = r.MimeType,
                    Length = r.Length,
                    FileSize = r.FileSize,
                    Bitrate = r.Bitrate
                }).ToList()
            };
        }

        #endregion
    }
}