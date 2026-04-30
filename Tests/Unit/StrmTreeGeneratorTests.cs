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
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediaCccDe.Tests.Unit
{
    public class StrmTreeGeneratorTests
    {
        private readonly Mock<IMediaCccApiClient> _apiClientMock;
        private readonly Mock<IStrmFileGenerator> _strmGeneratorMock;
        private readonly Mock<ILogger<StrmTreeGenerator>> _loggerMock;
        private readonly string _archivePath;

        public StrmTreeGeneratorTests()
        {
            _apiClientMock = new Mock<IMediaCccApiClient>();
            _strmGeneratorMock = new Mock<IStrmFileGenerator>();
            _loggerMock = new Mock<ILogger<StrmTreeGenerator>>();
            _archivePath = "/tmp/test_archive";
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_throws_on_null_api_client()
        {
            Assert.Throws<ArgumentNullException>(
                () => new StrmTreeGenerator(null!, _strmGeneratorMock.Object, _loggerMock.Object));
        }

        [Fact]
        public void Constructor_throws_on_null_strm_generator()
        {
            Assert.Throws<ArgumentNullException>(
                () => new StrmTreeGenerator(_apiClientMock.Object, null!, _loggerMock.Object));
        }

        [Fact]
        public void Constructor_throws_on_null_logger()
        {
            Assert.Throws<ArgumentNullException>(
                () => new StrmTreeGenerator(_apiClientMock.Object, _strmGeneratorMock.Object, null!));
        }

        #endregion

        #region GenerateTree Tests

        [Fact]
        public async Task GenerateTree_creates_series_folder_for_each_conference()
        {
            // Arrange
            var conferences = CreateTestConferences("37c3", "36c3");
            var events = new EventDto[]
            {
                CreateTestEvent(guid: "1", title: "Event 1", date: "2023-12-28")
            };

            _apiClientMock.Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(conferences);

            _apiClientMock.Setup(x => x.GetEventsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(events);

            var generator = CreateGenerator();

            // Act
            await generator.GenerateTreeAsync(_archivePath, CancellationToken.None);

            // Assert
            // Verify that series folders were created
            Assert.True(Directory.Exists(Path.Combine(_archivePath, "37c3")));
            Assert.True(Directory.Exists(Path.Combine(_archivePath, "36c3")));
        }

        [Fact]
        public async Task GenerateTree_creates_season_folder_for_each_day()
        {
            // Arrange
            var conferences = CreateTestConferences("37c3");
            // ExtractDayNumber: conferenceFirstDay=Dec 28, so Dec 28 -> Day 1 -> Season 01
            //                    Dec 29 -> Day 2 -> Season 02
            //                    Dec 30 -> Day 3 -> Season 03
            var events = new EventDto[]
            {
                CreateTestEvent(guid: "1", title: "Event 1", date: "2023-12-28"),
                CreateTestEvent(guid: "2", title: "Event 2", date: "2023-12-29"),
                CreateTestEvent(guid: "3", title: "Event 3", date: "2023-12-30")
            };

            _apiClientMock.Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(conferences);
            
            _apiClientMock.Setup(x => x.GetEventsAsync(37, It.IsAny<CancellationToken>()))
                .ReturnsAsync(events);

            var generator = CreateGenerator();

            // Act
            await generator.GenerateTreeAsync(_archivePath, CancellationToken.None);

            // Assert
            var conferencePath = Path.Combine(_archivePath, "37c3");
            Assert.True(Directory.Exists(Path.Combine(conferencePath, "Season 01"))); // Dec 28
            Assert.True(Directory.Exists(Path.Combine(conferencePath, "Season 02"))); // Dec 29
            Assert.True(Directory.Exists(Path.Combine(conferencePath, "Season 03"))); // Dec 30
        }

        [Fact]
        public async Task GenerateTree_creates_episode_strm_for_each_event()
        {
            // Arrange
            var conferences = CreateTestConferences("37c3");
            // Dec 28 -> day 1 -> Season 01, Dec 29 -> day 2 -> Season 02
            var events = new EventDto[]
            {
                CreateTestEvent(guid: "event-1", title: "Opening Ceremony", date: "2023-12-28"),
                CreateTestEvent(guid: "event-2", title: "Keynote", date: "2023-12-28"),
                CreateTestEvent(guid: "event-3", title: "Closing", date: "2023-12-29")
            };

            _apiClientMock.Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(conferences);
            
            _apiClientMock.Setup(x => x.GetEventsAsync(37, It.IsAny<CancellationToken>()))
                .ReturnsAsync(events);

            _strmGeneratorMock.Setup(x => x.GenerateStrmAsync(
                It.IsAny<string>(), It.IsAny<EventDto>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var generator = CreateGenerator();

            // Act
            await generator.GenerateTreeAsync(_archivePath, CancellationToken.None);

            // Assert
            _strmGeneratorMock.Verify(
                x => x.GenerateStrmAsync(
                    Path.Combine(_archivePath, "37c3", "Season 01"),
                    It.Is<EventDto>(e => e.Guid == "event-1"),
                    It.IsAny<CancellationToken>()),
                Times.Once);

            _strmGeneratorMock.Verify(
                x => x.GenerateStrmAsync(
                    Path.Combine(_archivePath, "37c3", "Season 01"),
                    It.Is<EventDto>(e => e.Guid == "event-2"),
                    It.IsAny<CancellationToken>()),
                Times.Once);

            _strmGeneratorMock.Verify(
                x => x.GenerateStrmAsync(
                    Path.Combine(_archivePath, "37c3", "Season 02"),
                    It.Is<EventDto>(e => e.Guid == "event-3"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task GenerateTree_uses_correct_naming_convention()
        {
            // Arrange
            var conferences = CreateTestConferences("37c3");
            // Dec 28 -> day 1 -> Season 01
            var events = new EventDto[]
            {
                CreateTestEvent(guid: "test-guid", title: "Test Event Title", date: "2023-12-28")
            };

            _apiClientMock.Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(conferences);
            
            _apiClientMock.Setup(x => x.GetEventsAsync(37, It.IsAny<CancellationToken>()))
                .ReturnsAsync(events);

            var generator = CreateGenerator();

            // Act
            await generator.GenerateTreeAsync(_archivePath, CancellationToken.None);

            // Assert
            // Series folder uses conference acronym (lowercase)
            Assert.True(Directory.Exists(Path.Combine(_archivePath, "37c3")));
            
            // Season folder uses "Season XX" format
            Assert.True(Directory.Exists(Path.Combine(_archivePath, "37c3", "Season 01")));
        }

        [Fact]
        public async Task GenerateTree_handles_multiple_conferences()
        {
            // Arrange
            var conferences = CreateTestConferences("37c3", "36c3", "35c3");
            
            var events37c3 = new EventDto[]
            {
                CreateTestEvent(guid: "37c3-1", title: "37C3 Event", date: "2023-12-28")
            };
            
            var events36c3 = new EventDto[]
            {
                CreateTestEvent(guid: "36c3-1", title: "36C3 Event", date: "2019-12-28")
            };
            
            var events35c3 = Array.Empty<EventDto>();

            _apiClientMock.Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(conferences);
            
            _apiClientMock.Setup(x => x.GetEventsAsync(37, It.IsAny<CancellationToken>()))
                .ReturnsAsync(events37c3);
            
            _apiClientMock.Setup(x => x.GetEventsAsync(36, It.IsAny<CancellationToken>()))
                .ReturnsAsync(events36c3);
            
            _apiClientMock.Setup(x => x.GetEventsAsync(35, It.IsAny<CancellationToken>()))
                .ReturnsAsync(events35c3);

            var generator = CreateGenerator();

            // Act
            var result = await generator.GenerateTreeAsync(_archivePath, CancellationToken.None);

            // Assert
            Assert.True(Directory.Exists(Path.Combine(_archivePath, "37c3")));
            Assert.True(Directory.Exists(Path.Combine(_archivePath, "36c3")));
            Assert.False(Directory.Exists(Path.Combine(_archivePath, "35c3"))); // No events = no folder
            
            Assert.Equal(2, result.ConferencesProcessed);
            Assert.Equal(2, result.FilesCreated);
        }

        [Fact]
        public async Task GenerateTree_handles_events_with_same_title_different_days()
        {
            // Arrange
            var conferences = CreateTestConferences("37c3");
            // Dec 28 -> day 1 -> Season 01, Dec 29 -> day 2 -> Season 02, Dec 30 -> day 3 -> Season 03
            var events = new EventDto[]
            {
                CreateTestEvent(guid: "event-1", title: "Workshop", date: "2023-12-28"),
                CreateTestEvent(guid: "event-2", title: "Workshop", date: "2023-12-29"),
                CreateTestEvent(guid: "event-3", title: "Workshop", date: "2023-12-30")
            };

            _apiClientMock.Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(conferences);
            
            _apiClientMock.Setup(x => x.GetEventsAsync(37, It.IsAny<CancellationToken>()))
                .ReturnsAsync(events);

            var generator = CreateGenerator();

            // Act
            await generator.GenerateTreeAsync(_archivePath, CancellationToken.None);

            // Assert
            _strmGeneratorMock.Verify(
                x => x.GenerateStrmAsync(
                    Path.Combine(_archivePath, "37c3", "Season 01"),
                    It.Is<EventDto>(e => e.Guid == "event-1"),
                    It.IsAny<CancellationToken>()),
                Times.Once);

            _strmGeneratorMock.Verify(
                x => x.GenerateStrmAsync(
                    Path.Combine(_archivePath, "37c3", "Season 02"),
                    It.Is<EventDto>(e => e.Guid == "event-2"),
                    It.IsAny<CancellationToken>()),
                Times.Once);

            _strmGeneratorMock.Verify(
                x => x.GenerateStrmAsync(
                    Path.Combine(_archivePath, "37c3", "Season 03"),
                    It.Is<EventDto>(e => e.Guid == "event-3"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task GenerateTree_skips_conferences_without_events()
        {
            // Arrange
            var conferences = CreateTestConferences("37c3", "empty-conf", "36c3");
            
            var events37c3 = new EventDto[]
            {
                CreateTestEvent(guid: "37c3-1", title: "Event", date: "2023-12-27")
            };
            
            var events36c3 = new EventDto[]
            {
                CreateTestEvent(guid: "36c3-1", title: "Event", date: "2019-12-27")
            };

            _apiClientMock.Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(conferences);
            
            _apiClientMock.Setup(x => x.GetEventsAsync(37, It.IsAny<CancellationToken>()))
                .ReturnsAsync(events37c3);
            
            _apiClientMock.Setup(x => x.GetEventsAsync(36, It.IsAny<CancellationToken>()))
                .ReturnsAsync(events36c3);

            var generator = CreateGenerator();

            // Act
            var result = await generator.GenerateTreeAsync(_archivePath, CancellationToken.None);

            // Assert
            Assert.True(Directory.Exists(Path.Combine(_archivePath, "37c3")));
            Assert.True(Directory.Exists(Path.Combine(_archivePath, "36c3")));
            Assert.False(Directory.Exists(Path.Combine(_archivePath, "empty-conf")));
            
            Assert.Equal(2, result.ConferencesProcessed);
        }

        [Fact]
        public async Task GenerateTree_preserves_existing_files_on_rerun()
        {
            // Arrange
            var conferences = CreateTestConferences("37c3");
            var existingEvents = new EventDto[]
            {
                CreateTestEvent(guid: "existing-1", title: "Existing Event", date: "2023-12-28")
            };

            var newEvents = new EventDto[]
            {
                CreateTestEvent(guid: "existing-1", title: "Existing Event", date: "2023-12-28"),
                CreateTestEvent(guid: "new-1", title: "New Event", date: "2023-12-29")
            };

            _apiClientMock.SetupSequence(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(conferences)
                .ReturnsAsync(conferences);
            
            _apiClientMock.SetupSequence(x => x.GetEventsAsync(37, It.IsAny<CancellationToken>()))
                .ReturnsAsync(existingEvents)
                .ReturnsAsync(newEvents);

            // Make the mock actually create .strm files on disk
            _strmGeneratorMock.Setup(x => x.GenerateStrmAsync(It.IsAny<string>(), It.IsAny<EventDto>(), It.IsAny<CancellationToken>()))
                .Callback<string, EventDto, CancellationToken>((dir, evt, ct) =>
                {
                    Directory.CreateDirectory(dir);
                    var fileName = $"{evt.Slug}.strm";
                    File.WriteAllText(Path.Combine(dir, fileName), evt.Guid);
                });

            var generator = CreateGenerator();

            // First run
            await generator.GenerateTreeAsync(_archivePath, CancellationToken.None);

            var seasonPath = Path.Combine(_archivePath, "37c3", "Season 01");
            var existingStrmPath = Path.Combine(seasonPath, "existing-event-existing-1.strm");
            
            Assert.True(File.Exists(existingStrmPath));

            // Act - Second run with new events
            var result = await generator.GenerateTreeAsync(_archivePath, CancellationToken.None);

            // Assert
            // Existing file should still exist
            Assert.True(File.Exists(existingStrmPath));
            
            // New file should be created
            var newSeasonPath = Path.Combine(_archivePath, "37c3", "Season 02");
            var newStrmPath = Path.Combine(newSeasonPath, "new-event-new-1.strm");
            Assert.True(File.Exists(newStrmPath));
        }

        [Fact]
        public async Task GenerateTree_removes_stale_files_from_deleted_events()
        {
            // Arrange
            var conferences = CreateTestConferences("37c3");
            
            var initialEvents = new EventDto[]
            {
                CreateTestEvent(guid: "event-1", title: "Keep This", date: "2023-12-28"),
                CreateTestEvent(guid: "event-2", title: "Delete This", date: "2023-12-28")
            };

            var updatedEvents = new EventDto[]
            {
                CreateTestEvent(guid: "event-1", title: "Keep This", date: "2023-12-28")
            };

            _apiClientMock.SetupSequence(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(conferences)
                .ReturnsAsync(conferences);
            
            _apiClientMock.SetupSequence(x => x.GetEventsAsync(37, It.IsAny<CancellationToken>()))
                .ReturnsAsync(initialEvents)
                .ReturnsAsync(updatedEvents);

            // Make the mock actually create .strm files on disk
            _strmGeneratorMock.Setup(x => x.GenerateStrmAsync(It.IsAny<string>(), It.IsAny<EventDto>(), It.IsAny<CancellationToken>()))
                .Callback<string, EventDto, CancellationToken>((dir, evt, ct) =>
                {
                    Directory.CreateDirectory(dir);
                    var fileName = $"{evt.Slug}.strm";
                    File.WriteAllText(Path.Combine(dir, fileName), evt.Guid);
                });

            var generator = CreateGenerator();

            // First run - create both files
            await generator.GenerateTreeAsync(_archivePath, CancellationToken.None);

            var seasonPath = Path.Combine(_archivePath, "37c3", "Season 01");
            var keepPath = Path.Combine(seasonPath, "keep-this-event-1.strm");
            var deletePath = Path.Combine(seasonPath, "delete-this-event-2.strm");

            Assert.True(File.Exists(keepPath));
            Assert.True(File.Exists(deletePath));

            // Act - Second run with deleted event
            await generator.GenerateTreeAsync(_archivePath, CancellationToken.None);

            // Assert
            Assert.True(File.Exists(keepPath));
            Assert.False(File.Exists(deletePath));
        }

        [Fact]
        public async Task GenerateTree_logs_progress_per_conference()
        {
            // Arrange
            var conferences = CreateTestConferences("37c3", "36c3");
            
            var events37c3 = new EventDto[]
            {
                CreateTestEvent(guid: "1", title: "Event 1", date: "2023-12-27")
            };
            
            var events36c3 = new EventDto[]
            {
                CreateTestEvent(guid: "2", title: "Event 2", date: "2019-12-27")
            };

            _apiClientMock.Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(conferences);
            
            _apiClientMock.Setup(x => x.GetEventsAsync(37, It.IsAny<CancellationToken>()))
                .ReturnsAsync(events37c3);
            
            _apiClientMock.Setup(x => x.GetEventsAsync(36, It.IsAny<CancellationToken>()))
                .ReturnsAsync(events36c3);

            var generator = CreateGenerator();

            // Act
            await generator.GenerateTreeAsync(_archivePath, CancellationToken.None);

            // Assert - Verify logging occurred
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("37c3")),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce);

            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("36c3")),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce);
        }

        [Fact]
        public async Task GenerateTree_returns_count_of_created_files()
        {
            // Arrange
            var conferences = CreateTestConferences("37c3", "36c3");
            
            // Dec 27 -> dayNumber=1 (Season 01), Dec 28 -> dayNumber=2 (Season 02)
            // With conferenceFirstDay=Dec 27, events are offset from day 1
            var events37c3 = new EventDto[]
            {
                CreateTestEvent(guid: "1", title: "Event 1", date: "2023-12-27"),
                CreateTestEvent(guid: "2", title: "Event 2", date: "2023-12-27"),
                CreateTestEvent(guid: "3", title: "Event 3", date: "2023-12-28")
            };
            
            var events36c3 = new EventDto[]
            {
                CreateTestEvent(guid: "4", title: "Event 4", date: "2019-12-28"),
                CreateTestEvent(guid: "5", title: "Event 5", date: "2019-12-29")
            };

            _apiClientMock.Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(conferences);
            
            _apiClientMock.Setup(x => x.GetEventsAsync(37, It.IsAny<CancellationToken>()))
                .ReturnsAsync(events37c3);
            
            _apiClientMock.Setup(x => x.GetEventsAsync(36, It.IsAny<CancellationToken>()))
                .ReturnsAsync(events36c3);

            var generator = CreateGenerator();

            // Act
            var result = await generator.GenerateTreeAsync(_archivePath, CancellationToken.None);

            // Assert
            Assert.Equal(5, result.FilesCreated);
            Assert.Equal(2, result.ConferencesProcessed);
            Assert.Equal(4, result.SeasonsCreated);
            Assert.Equal(2, result.SeriesFoldersCreated);
        }

        [Fact]
        public async Task GenerateTree_handles_cancellation_gracefully()
        {
            // Arrange
            var conferences = CreateTestConferences("37c3", "36c3", "35c3");
            
            _apiClientMock.Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(conferences);

            var cts = new CancellationTokenSource();
            var callCount = 0;

            _apiClientMock.Setup(x => x.GetEventsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Callback<int, CancellationToken>((_, ct) =>
                {
                    callCount++;
                    if (callCount == 1)
                    {
                        cts.Cancel();
                    }
                })
                .ReturnsAsync(Array.Empty<EventDto>());

            var generator = CreateGenerator();

            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => generator.GenerateTreeAsync(_archivePath, cts.Token));
        }

        [Fact]
        public async Task GenerateTree_handles_api_errors_gracefully()
        {
            // Arrange
            var conferences = CreateTestConferences("37c3", "36c3");
            
            _apiClientMock.Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(conferences);

            var events37c3 = new EventDto[]
            {
                CreateTestEvent(guid: "1", title: "Event 1", date: "2023-12-28")
            };

            _apiClientMock.Setup(x => x.GetEventsAsync(37, It.IsAny<CancellationToken>()))
                .ReturnsAsync(events37c3);

            _apiClientMock.Setup(x => x.GetEventsAsync(36, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("API error"));

            var generator = CreateGenerator();

            // Act
            var result = await generator.GenerateTreeAsync(_archivePath, CancellationToken.None);

            // Assert - Should complete successfully, logging errors
            Assert.Equal(1, result.ConferencesProcessed);
            Assert.Equal(1, result.FailedConferences);

            // Implementation logs twice: once from inner catch, once from outer catch
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("36c3")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeast(2));
        }

        [Fact]
        public async Task GenerateTree_creates_archive_directory_if_not_exists()
        {
            // Arrange
            var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            var conferences = CreateTestConferences("37c3");
            var events = new EventDto[]
            {
                CreateTestEvent(guid: "1", title: "Event", date: "2023-12-27")
            };

            _apiClientMock.Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(conferences);
            
            _apiClientMock.Setup(x => x.GetEventsAsync(37, It.IsAny<CancellationToken>()))
                .ReturnsAsync(events);

            var generator = CreateGenerator();

            try
            {
                // Act
                await generator.GenerateTreeAsync(nonExistentPath, CancellationToken.None);

                // Assert
                Assert.True(Directory.Exists(nonExistentPath));
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(nonExistentPath))
                {
                    Directory.Delete(nonExistentPath, recursive: true);
                }
            }
        }

        [Fact]
        public async Task GenerateTree_groups_events_by_day_correctly()
        {
            // Arrange
            var conferences = CreateTestConferences("37c3");
            
            // Dec 28 -> Season 01, Dec 29 -> Season 02, Dec 30 -> Season 03
            var events = new EventDto[]
            {
                CreateTestEvent(guid: "1", title: "Morning Talk", date: "2023-12-28"),
                CreateTestEvent(guid: "2", title: "Afternoon Talk", date: "2023-12-28"),
                CreateTestEvent(guid: "3", title: "Evening Talk", date: "2023-12-28"),
                CreateTestEvent(guid: "4", title: "Second Day Talk", date: "2023-12-29"),
                CreateTestEvent(guid: "5", title: "Third Day Talk", date: "2023-12-30")
            };

            _apiClientMock.Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(conferences);
            
            _apiClientMock.Setup(x => x.GetEventsAsync(37, It.IsAny<CancellationToken>()))
                .ReturnsAsync(events);

            var generator = CreateGenerator();

            // Act
            await generator.GenerateTreeAsync(_archivePath, CancellationToken.None);

            // Assert
            var basePath = Path.Combine(_archivePath, "37c3");
            
            // Season 01 (December 28) - 3 events
            Assert.True(Directory.Exists(Path.Combine(basePath, "Season 01")));
            
            // Season 02 (December 29) - 1 event
            Assert.True(Directory.Exists(Path.Combine(basePath, "Season 02")));
            
            // Season 03 (December 30) - 1 event
            Assert.True(Directory.Exists(Path.Combine(basePath, "Season 03")));
        }

        [Fact]
        public async Task GenerateTree_handles_empty_date_field()
        {
            // Arrange
            var conferences = CreateTestConferences("37c3");
            
            // Dec 28 -> Season 01, null/invalid dates -> series path directly
            var events = new EventDto[]
            {
                CreateTestEvent(guid: "1", title: "Valid Date Event", date: "2023-12-28"),
                CreateTestEvent(guid: "2", title: "No Date Event", date: null),
                CreateTestEvent(guid: "3", title: "Invalid Date Event", date: "invalid-date")
            };

            _apiClientMock.Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(conferences);
            
            _apiClientMock.Setup(x => x.GetEventsAsync(37, It.IsAny<CancellationToken>()))
                .ReturnsAsync(events);

            var generator = CreateGenerator();

            // Act
            var result = await generator.GenerateTreeAsync(_archivePath, CancellationToken.None);

            // Assert - Should still create files, events without day numbers go to series path
            Assert.Equal(3, result.FilesCreated);
            Assert.True(Directory.Exists(Path.Combine(_archivePath, "37c3")));
        }

        #endregion

        #region Helper Methods

        private StrmTreeGenerator CreateGenerator()
        {
            // Clean up test directory before each test
            if (Directory.Exists(_archivePath))
            {
                Directory.Delete(_archivePath, recursive: true);
            }

            return new StrmTreeGenerator(
                _apiClientMock.Object,
                _strmGeneratorMock.Object,
                _loggerMock.Object);
        }

        private IReadOnlyList<ConferenceDto> CreateTestConferences(params string[] acronyms)
        {
            var conferences = new List<ConferenceDto>();
            foreach (var acronym in acronyms)
            {
                int id;
                if (acronym.StartsWith("empty"))
                {
                    id = 0;
                }
                else if (int.TryParse(acronym.Substring(0, 2), out var parsedId))
                {
                    id = parsedId;
                }
                else
                {
                    id = 0;
                }
                
                conferences.Add(new ConferenceDto
                {
                    Id = id,
                    Title = acronym.ToUpper(),
                    Acronym = acronym,
                    Slug = acronym,
                    AspectRatio = "16:9",
                    Url = $"https://media.ccc.de/c/{acronym}"
                });
            }
            return conferences;
        }

        private EventDto CreateTestEvent(string guid, string title, string? date)
        {
            return new EventDto
            {
                Guid = guid,
                Title = title,
                Slug = $"{title.ToLower().Replace(" ", "-")}-{guid}",
                Date = date,
                ConferenceId = 37,
                Length = 3600,
                Description = $"Description for {title}",
                Recordings = new List<RecordingDto>
                {
                    new RecordingDto
                    {
                        Language = "en",
                        Format = "mp4",
                        HighQuality = true,
                        Width = 1920,
                        Height = 1080,
                        Size = 1024000,
                        Url = $"https://cdn.media.ccc.de/{guid}.mp4"
                    }
                }
            };
        }

        #endregion
    }
}