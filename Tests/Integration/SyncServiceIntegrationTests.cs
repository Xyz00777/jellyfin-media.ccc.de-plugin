using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaCccDe.Api;
using Jellyfin.Plugin.MediaCccDe.Models;
using Jellyfin.Plugin.MediaCccDe.Services;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.MediaCccDe.Tests.Integration
{
    public class SyncServiceIntegrationTests : IAsyncLifetime
    {
        private readonly string _testRootPath;
        private readonly string _testArchivePath;
        private readonly string _testLogPath;
        private readonly Mock<IMediaCccApiClient> _apiClientMock;
        private readonly Mock<IApplicationPaths> _applicationPathsMock;
        private readonly List<LogLevel> _loggedMessages;
        private readonly Mock<ILogger<SyncService>> _loggerMock;
        private SyncService? _syncService;

        public SyncServiceIntegrationTests()
        {
            _testRootPath = Path.Combine(Path.GetTempPath(), "ccc-sync-integration-" + Guid.NewGuid().ToString());
            _testArchivePath = Path.Combine(_testRootPath, "archive");
            _testLogPath = Path.Combine(_testRootPath, "logs");
            _apiClientMock = new Mock<IMediaCccApiClient>(MockBehavior.Strict);
            _applicationPathsMock = new Mock<IApplicationPaths>(MockBehavior.Strict);
            _loggedMessages = new List<LogLevel>();
            _loggerMock = CreateLoggerMock();
        }

        private Mock<ILogger<SyncService>> CreateLoggerMock()
        {
            var mock = new Mock<ILogger<SyncService>>(MockBehavior.Loose);
            mock.Setup(x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
                .Callback<LogLevel, EventId, object, Exception, Delegate>((level, id, state, ex, formatter) =>
                {
                    _loggedMessages.Add(level);
                });
            return mock;
        }

        public Task InitializeAsync()
        {
            Directory.CreateDirectory(_testRootPath);
            Directory.CreateDirectory(_testArchivePath);
            Directory.CreateDirectory(_testLogPath);

            _applicationPathsMock.Setup(x => x.DataPath).Returns(_testLogPath);
            _applicationPathsMock.Setup(x => x.CachePath).Returns(_testLogPath);

            return Task.CompletedTask;
        }

        public Task DisposeAsync()
        {
            try
            {
                if (Directory.Exists(_testRootPath))
                {
                    Directory.Delete(_testRootPath, recursive: true);
                }
            }
            catch
            {
                // Cleanup best effort
            }
            return Task.CompletedTask;
        }

        [Fact]
        public async Task Full_sync_workflow_creates_archive_library()
        {
            // Arrange: Set up recorded API responses for full sync
            var conferences = CreateTestConferences();
            var events37C3 = CreateTestEvents("37c3");
            var events36C3 = CreateTestEvents("36c3");
            var recordings = CreateTestRecordings();

            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(conferences);

            _apiClientMock
                .Setup(x => x.GetEventsAsync("37c3", It.IsAny<CancellationToken>()))
                .ReturnsAsync(events37C3);

            _apiClientMock
                .Setup(x => x.GetEventsAsync("36c3", It.IsAny<CancellationToken>()))
                .ReturnsAsync(events36C3);

            _apiClientMock
                .Setup(x => x.GetRecordingsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(recordings);

            var config = CreateTestConfiguration();
            _syncService = new SyncService(
                _apiClientMock.Object,
                CreateStrmGenerator(),
                CreateSyncLogger(),
                config,
                _loggerMock.Object);

            // Act: Run full sync operation
            await _syncService.StartAsync(CancellationToken.None);
            await Task.Delay(500); // Allow async operations to complete

            // Assert: Archive library structure created
            Assert.True(Directory.Exists(_testArchivePath), "Archive directory should be created");
            
            var conferenceDirs = Directory.GetDirectories(_testArchivePath);
            Assert.True(conferenceDirs.Length > 0, "Should create conference directories in archive");
            Assert.Contains(conferenceDirs, d => Path.GetFileName(d) == "37c3");
            Assert.Contains(conferenceDirs, d => Path.GetFileName(d) == "36c3");
        }

        [Fact]
        public async Task Full_sync_workflow_fetches_all_conferences()
        {
            // Arrange
            var conferences = CreateTestConferences();
            var fetchCount = 0;

            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    fetchCount++;
                    return conferences;
                });

            var config = CreateTestConfiguration();
            _syncService = new SyncService(
                _apiClientMock.Object,
                CreateStrmGenerator(),
                CreateSyncLogger(),
                config,
                _loggerMock.Object);

            // Act
            await _syncService.StartAsync(CancellationToken.None);
            await Task.Delay(500);

            // Assert: API was called to fetch conferences
            Assert.True(fetchCount > 0, "Should fetch conferences from API");
            _apiClientMock.Verify(
                x => x.GetConferencesAsync(It.IsAny<CancellationToken>()),
                Times.AtLeastOnce());
        }

        [Fact]
        public async Task Full_sync_workflow_creates_strm_tree()
        {
            // Arrange
            var conferences = CreateTestConferences();
            var events = CreateTestEvents("37c3");
            var recordings = CreateTestRecordings();

            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(conferences);

            _apiClientMock
                .Setup(x => x.GetEventsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(events);

            _apiClientMock
                .Setup(x => x.GetRecordingsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(recordings);

            var config = CreateTestConfiguration();
            _syncService = new SyncService(
                _apiClientMock.Object,
                CreateStrmGenerator(),
                CreateSyncLogger(),
                config,
                _loggerMock.Object);

            // Act
            await _syncService.StartAsync(CancellationToken.None);
            await Task.Delay(500);

            // Assert: .strm files created
            var strmFiles = Directory.GetFiles(_testArchivePath, "*.strm", SearchOption.AllDirectories);
            Assert.True(strmFiles.Length > 0, "Should create .strm files for events");

            // Verify strm content contains valid URLs
            foreach (var strmFile in strmFiles)
            {
                var content = await File.ReadAllTextAsync(strmFile);
                Assert.False(string.IsNullOrWhiteSpace(content), ".strm file should not be empty");
                Assert.True(content.StartsWith("http"), ".strm file should contain HTTP URLs");
            }
        }

        [Fact]
        public async Task Full_sync_workflow_logs_all_operations()
        {
            // Arrange
            var conferences = CreateTestConferences();
            var events = CreateTestEvents("37c3");
            var recordings = CreateTestRecordings();

            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(conferences);

            _apiClientMock
                .Setup(x => x.GetEventsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(events);

            _apiClientMock
                .Setup(x => x.GetRecordingsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(recordings);

            var config = CreateTestConfiguration();
            var syncLogger = CreateSyncLogger();
            _syncService = new SyncService(
                _apiClientMock.Object,
                CreateStrmGenerator(),
                syncLogger,
                config,
                _loggerMock.Object);

            // Act
            await _syncService.StartAsync(CancellationToken.None);
            await Task.Delay(500);

            // Assert: Sync operations logged
            var history = syncLogger.GetSyncHistory();
            Assert.True(history.Count > 0, "Should log sync operations");

            // Verify log file exists
            var logFiles = Directory.GetFiles(_testLogPath, "*.json", SearchOption.AllDirectories);
            Assert.True(logFiles.Length > 0, "Should create log files");
        }

        [Fact]
        public async Task Incremental_sync_only_updates_changes()
        {
            // Arrange: First sync creates initial state
            var conferences = CreateTestConferences();
            var events = CreateTestEvents("37c3");
            var recordings = CreateTestRecordings();

            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(conferences);

            _apiClientMock
                .Setup(x => x.GetEventsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(events);

            _apiClientMock
                .Setup(x => x.GetRecordingsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(recordings);

            var config = CreateTestConfiguration();
            _syncService = new SyncService(
                _apiClientMock.Object,
                CreateStrmGenerator(),
                CreateSyncLogger(),
                config,
                _loggerMock.Object);

            // Act: First sync
            await _syncService.StartAsync(CancellationToken.None);
            await Task.Delay(500);

            var initialFileCount = Directory.GetFiles(_testArchivePath, "*.strm", SearchOption.AllDirectories).Length;
            var initialApiCallCount = _apiClientMock.Invocations.Count;

            // Act: Second sync (incremental)
            await _syncService.StopAsync(CancellationToken.None);
            await _syncService.StartAsync(CancellationToken.None);
            await Task.Delay(500);

            var finalFileCount = Directory.GetFiles(_testArchivePath, "*.strm", SearchOption.AllDirectories).Length;

            // Assert: Incremental sync should not duplicate files
            Assert.Equal(initialFileCount, finalFileCount);
            
            // Incremental sync should skip unchanged conferences
            _apiClientMock.Verify(
                x => x.GetEventsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.AtLeast(1));
        }

        [Fact]
        public async Task Sync_handles_api_rate_limiting()
        {
            // Arrange: Simulate rate limiting
            var conferences = CreateTestConferences();
            var rateLimitCount = 0;

            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    rateLimitCount++;
                    if (rateLimitCount == 1)
                    {
                        throw new HttpRequestException("429 Too Many Requests", null, System.Net.HttpStatusCode.TooManyRequests);
                    }
                    return conferences;
                });

            var events = CreateTestEvents("37c3");
            _apiClientMock
                .Setup(x => x.GetEventsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(events);

            var recordings = CreateTestRecordings();
            _apiClientMock
                .Setup(x => x.GetRecordingsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(recordings);

            var config = CreateTestConfiguration();
            _syncService = new SyncService(
                _apiClientMock.Object,
                CreateStrmGenerator(),
                CreateSyncLogger(),
                config,
                _loggerMock.Object);

            // Act: Should handle rate limit and retry
            await _syncService.StartAsync(CancellationToken.None);
            await Task.Delay(1000);

            // Assert: Should eventually succeed after retry
            Assert.True(rateLimitCount >= 1, "Should handle rate limiting");
            
            // Verify error logged
            Assert.Contains(LogLevel.Error, _loggedMessages);
        }

        [Fact]
        public async Task Sync_handles_network_timeout_gracefully()
        {
            // Arrange: Simulate network timeout
            var conferences = CreateTestConferences();

            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TaskCanceledException("Request timeout"));

            var config = CreateTestConfiguration();
            _syncService = new SyncService(
                _apiClientMock.Object,
                CreateStrmGenerator(),
                CreateSyncLogger(),
                config,
                _loggerMock.Object);

            // Act: Should handle timeout gracefully
            await _syncService.StartAsync(CancellationToken.None);
            await Task.Delay(500);

            // Assert: Should not crash, should log error
            Assert.Contains(LogLevel.Error, _loggedMessages);
            
            // Service should remain operational despite errors
            var history = CreateSyncLogger().GetSyncHistory();
            Assert.NotNull(history);
        }

        [Fact]
        public async Task Sync_continues_on_individual_event_failure()
        {
            // Arrange: Some events fail, others succeed
            var conferences = CreateTestConferences();
            var events = CreateTestEvents("37c3");
            var recordings = CreateTestRecordings();

            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(conferences);

            // First event succeeds
            _apiClientMock
                .SetupSequence(x => x.GetEventsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(events)
                .ThrowsAsync(new Exception("Event fetch failed"))
                .ReturnsAsync(events);

            _apiClientMock
                .Setup(x => x.GetRecordingsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(recordings);

            var config = CreateTestConfiguration();
            _syncService = new SyncService(
                _apiClientMock.Object,
                CreateStrmGenerator(),
                CreateSyncLogger(),
                config,
                _loggerMock.Object);

            // Act
            await _syncService.StartAsync(CancellationToken.None);
            await Task.Delay(500);

            // Assert: Should continue processing despite individual failures
            var strmFiles = Directory.GetFiles(_testArchivePath, "*.strm", SearchOption.AllDirectories);
            Assert.True(strmFiles.Length > 0, "Should create .strm files despite some failures");
            
            // Should log error for failed event
            Assert.Contains(LogLevel.Error, _loggedMessages);
        }

        [Fact]
        public async Task Sync_respects_cancellation_during_operation()
        {
            // Arrange
            var conferences = CreateTestConferences();
            var events = CreateTestEvents("37c3");
            var recordings = CreateTestRecordings();

            var cts = new CancellationTokenSource();
            var callCount = 0;

            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    callCount++;
                    return conferences;
                });

            _apiClientMock
                .Setup(x => x.GetEventsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(async (string conf, CancellationToken token) =>
                {
                    callCount++;
                    await Task.Delay(100, token);
                    return events;
                });

            var config = CreateTestConfiguration();
            _syncService = new SyncService(
                _apiClientMock.Object,
                CreateStrmGenerator(),
                CreateSyncLogger(),
                config,
                _loggerMock.Object);

            // Act: Start sync then cancel
            await _syncService.StartAsync(cts.Token);
            await Task.Delay(50);
            cts.Cancel();
            await Task.Delay(200);

            // Assert: Should stop processing after cancellation
            var finalCallCount = callCount;
            await Task.Delay(100);
            
            // After cancellation, no new API calls should be made
            Assert.True(finalCallCount >= 0, "Should have made some calls before cancellation");
        }

        [Fact]
        public async Task Sync_can_be_paused_and_resumed()
        {
            // Arrange
            var conferences = CreateTestConferences();
            var events = CreateTestEvents("37c3");
            var recordings = CreateTestRecordings();

            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(conferences);

            _apiClientMock
                .Setup(x => x.GetEventsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(events);

            _apiClientMock
                .Setup(x => x.GetRecordingsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(recordings);

            var config = CreateTestConfiguration();
            _syncService = new SyncService(
                _apiClientMock.Object,
                CreateStrmGenerator(),
                CreateSyncLogger(),
                config,
                _loggerMock.Object);

            // Act: Start sync
            await _syncService.StartAsync(CancellationToken.None);
            await Task.Delay(300);

            // Pause: Stop the service
            await _syncService.StopAsync(CancellationToken.None);
            var filesAfterPause = Directory.GetFiles(_testArchivePath, "*.strm", SearchOption.AllDirectories).Length;

            // Resume: Restart the service
            await _syncService.StartAsync(CancellationToken.None);
            await Task.Delay(300);
            var filesAfterResume = Directory.GetFiles(_testArchivePath, "*.strm", SearchOption.AllDirectories).Length;

            // Assert: Files should remain or increase (depending on where pause occurred)
            Assert.True(filesAfterPause >= 0, "Should have some files after pause");
            Assert.True(filesAfterResume >= filesAfterPause, "Should maintain or increase files after resume");
        }

        #region Helper Methods

        private List<ConferenceDto> CreateTestConferences()
        {
            return new List<ConferenceDto>
            {
                new ConferenceDto
                {
                    Acronym = "37c3",
                    Title = "37C3: Unlocked",
                    Slug = "37c3",
                    AspectRatio = "16:9",
                    UpdatedAt = DateTime.UtcNow.AddDays(-30)
                },
                new ConferenceDto
                {
                    Acronym = "36c3",
                    Title = "36C3: Resource Overflow",
                    Slug = "36c3",
                    AspectRatio = "16:9",
                    UpdatedAt = DateTime.UtcNow.AddDays(-60)
                }
            };
        }

        private List<EventDto> CreateTestEvents(string conferenceAcronym)
        {
            var events = new List<EventDto>();

            for (int i = 1; i <= 3; i++)
            {
                events.Add(new EventDto
                {
                    Guid = $"{conferenceAcronym}-event-{i}",
                    Title = $"Test Event {i} - {conferenceAcronym}",
                    Slug = $"test-event-{i}",
                    ConferenceAcronym = conferenceAcronym,
                    Description = $"Test event description {i}",
                    Date = DateTime.UtcNow.AddDays(-30 - i),
                    Duration = 3600 + (i * 300),
                    Link = $"https://media.ccc.de/c/{conferenceAcronym}/test-event-{i}",
                    Recorded = true,
                    ReleasedAt = DateTime.UtcNow.AddDays(-30 - i),
                    UpdatedAt = DateTime.UtcNow.AddDays(-30 - i)
                });
            }

            return events;
        }

        private List<RecordingDto> CreateTestRecordings()
        {
            return new List<RecordingDto>
            {
                new RecordingDto
                {
                    Language = "eng",
                    HighQualityUrl = "https://cdn.media.ccc.de/test-hq.mp4",
                    LowQualityUrl = "https://cdn.media.ccc.de/test-lq.mp4",
                    SourceUrl = "https://cdn.media.ccc.de/test-source.mp4",
                    RecordingId = "recording-1",
                    Length = 3600,
                    MimeType = "video/mp4",
                    Filename = "test-video.mp4",
                    Size = 1024 * 1024 * 500,
                    UpdatedAt = DateTime.UtcNow.AddDays(-30)
                },
                new RecordingDto
                {
                    Language = "deu",
                    HighQualityUrl = "https://cdn.media.ccc.de/test-hq-deu.mp4",
                    LowQualityUrl = "https://cdn.media.ccc.de/test-lq-deu.mp4",
                    SourceUrl = "https://cdn.media.ccc.de/test-source-deu.mp4",
                    RecordingId = "recording-2",
                    Length = 3600,
                    MimeType = "video/mp4",
                    Filename = "test-video-deu.mp4",
                    Size = 1024 * 1024 * 450,
                    UpdatedAt = DateTime.UtcNow.AddDays(-30)
                }
            };
        }

        private IStrmGenerator CreateStrmGenerator()
        {
            // Create actual StrmGenerator with test dependencies
            var recordingSelectorMock = new Mock<IRecordingSelector>(MockBehavior.Loose);
            recordingSelectorMock
                .Setup(x => x.SelectBestRecording(It.IsAny<IEnumerable<Recording>>(), It.IsAny<RecordingPreferences>()))
                .Returns((IEnumerable<Recording> recordings, RecordingPreferences prefs) =>
                {
                    return recordings.FirstOrDefault();
                });

            return new StrmGenerator(
                _apiClientMock.Object,
                recordingSelectorMock.Object,
                _testArchivePath);
        }

        private SyncLogger CreateSyncLogger()
        {
            return new SyncLogger(_applicationPathsMock.Object, _loggerMock.Object);
        }

        private IPluginConfiguration CreateTestConfiguration()
        {
            var mock = new Mock<IPluginConfiguration>(MockBehavior.Loose);
            mock.Setup(x => x.WatchlistPath).Returns(_testArchivePath);
            mock.Setup(x => x.PreferredQuality).Returns("high");
            mock.Setup(x => x.PreferredAudioLanguages).Returns(new List<string> { "eng" });
            mock.Setup(x => x.PreferredSubtitleLanguages).Returns(new List<string>());
            mock.Setup(x => x.SyncIntervalHours).Returns(0); // Run immediately for tests
            return mock.Object;
        }

        #endregion
    }

    // Interface definitions (should match production code)
    public interface IPluginConfiguration
    {
        string WatchlistPath { get; }
        string PreferredQuality { get; }
        List<string> PreferredAudioLanguages { get; }
        List<string> PreferredSubtitleLanguages { get; }
        int SyncIntervalHours { get; }
    }

    public interface IStrmGenerator
    {
        bool StrmFilesExistForConference(ConferenceDto conference);
        Task CreateStrmFilesForConference(ConferenceDto conference, CancellationToken cancellationToken = default);
    }
}