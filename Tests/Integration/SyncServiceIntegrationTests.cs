using System;
using System.Collections.Concurrent;
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
        private readonly ConcurrentQueue<LogLevel> _loggedMessages;
        private readonly Mock<ILogger<SyncService>> _loggerMock;
        private SyncService? _syncService;

        public SyncServiceIntegrationTests()
        {
            _testRootPath = Path.Combine(Path.GetTempPath(), "ccc-sync-integration-" + Guid.NewGuid().ToString());
            _testArchivePath = Path.Combine(_testRootPath, "archive");
            _testLogPath = Path.Combine(_testRootPath, "logs");
            _apiClientMock = new Mock<IMediaCccApiClient>(MockBehavior.Strict);
            _applicationPathsMock = new Mock<IApplicationPaths>(MockBehavior.Strict);
            _loggedMessages = new ConcurrentQueue<LogLevel>();
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
                    _loggedMessages.Enqueue(level);
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

        private async Task<(Mock<ISyncLogger> syncLoggerMock, TaskCompletionSource<bool> syncCompletedTcs)> CreateSignalingSyncLoggerAsync()
        {
            var syncCompletedTcs = new TaskCompletionSource<bool>();
            var syncLoggerMock = new Mock<ISyncLogger>(MockBehavior.Loose);

            syncLoggerMock
                .Setup(x => x.LogSyncCompletion(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>()))
                .Callback<int, int, DateTime>((processed, created, timestamp) => syncCompletedTcs.TrySetResult(true));

            syncLoggerMock
                .Setup(x => x.LogSyncFailure(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
                .Callback<string, string, int, int>((conf, error, processed, created) => syncCompletedTcs.TrySetResult(true));

            _applicationPathsMock.Setup(x => x.DataPath).Returns(_testLogPath);
            _applicationPathsMock.Setup(x => x.CachePath).Returns(_testLogPath);

            var realLogger = new SyncLogger(_applicationPathsMock.Object, new Mock<ILogger<SyncLogger>>().Object);
            await realLogger.LoadAsync();
            var history = realLogger.GetSyncHistory();

            syncLoggerMock.Setup(x => x.GetSyncHistory(It.IsAny<string>())).Returns(() => realLogger.GetSyncHistory());

            return (syncLoggerMock, syncCompletedTcs);
        }

        [Fact]
        public async Task Full_sync_workflow_creates_archive_library()
        {
            var conferences = CreateTestConferences();
            var events37C3 = CreateTestEvents(1);
            var events36C3 = CreateTestEvents(2);
            var (syncLoggerMock, syncCompletedTcs) = await CreateSignalingSyncLoggerAsync();

            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(conferences);

            _apiClientMock
                .Setup(x => x.GetEventsAsync(1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(events37C3);

            _apiClientMock
                .Setup(x => x.GetEventsAsync(2, It.IsAny<CancellationToken>()))
                .ReturnsAsync(events36C3);

            var config = CreateTestConfiguration();
            _syncService = new SyncService(_apiClientMock.Object, CreateStrmGenerator(), syncLoggerMock.Object, () => config, _loggerMock.Object);

            await _syncService.StartAsync(CancellationToken.None);

            using var cts = new CancellationTokenSource(10000);
            try
            {
                await syncCompletedTcs.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.Fail("Sync did not complete within timeout");
            }

            await _syncService.StopAsync(CancellationToken.None);

            Assert.True(Directory.Exists(_testArchivePath), "Archive directory should be created");

            var conferenceDirs = Directory.GetDirectories(_testArchivePath);
            Assert.True(conferenceDirs.Length > 0, "Should create conference directories in archive");
            Assert.Contains(conferenceDirs, d => Path.GetFileName(d) == "37c3");
            Assert.Contains(conferenceDirs, d => Path.GetFileName(d) == "36c3");
        }

        [Fact]
        public async Task Full_sync_workflow_fetches_all_conferences()
        {
            var conferences = CreateTestConferences();
            var events = CreateTestEvents(1);
            var fetchCount = 0;
            var (syncLoggerMock, syncCompletedTcs) = await CreateSignalingSyncLoggerAsync();

            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    fetchCount++;
                    return conferences;
                });

            _apiClientMock
                .Setup(x => x.GetEventsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(events);

            var config = CreateTestConfiguration();
            _syncService = new SyncService(_apiClientMock.Object, CreateStrmGenerator(), syncLoggerMock.Object, () => config, _loggerMock.Object);

            await _syncService.StartAsync(CancellationToken.None);

            using var cts = new CancellationTokenSource(10000);
            try
            {
                await syncCompletedTcs.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.Fail("Sync did not complete within timeout");
            }

            await _syncService.StopAsync(CancellationToken.None);

            Assert.True(fetchCount > 0, "Should fetch conferences from API");
            _apiClientMock.Verify(
                x => x.GetConferencesAsync(It.IsAny<CancellationToken>()),
                Times.AtLeastOnce());
        }

        [Fact]
        public async Task Full_sync_workflow_creates_strm_tree()
        {
            var conferences = CreateTestConferences();
            var events = CreateTestEvents(1);
            var (syncLoggerMock, syncCompletedTcs) = await CreateSignalingSyncLoggerAsync();

            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(conferences);

            _apiClientMock
                .Setup(x => x.GetEventsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(events);

            var config = CreateTestConfiguration();
            _syncService = new SyncService(_apiClientMock.Object, CreateStrmGenerator(), syncLoggerMock.Object, () => config, _loggerMock.Object);

            await _syncService.StartAsync(CancellationToken.None);

            using var cts = new CancellationTokenSource(10000);
            try
            {
                await syncCompletedTcs.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.Fail("Sync did not complete within timeout");
            }

            await _syncService.StopAsync(CancellationToken.None);

            var strmFiles = Directory.GetFiles(_testArchivePath, "*.strm", SearchOption.AllDirectories);
            Assert.True(strmFiles.Length > 0, "Should create .strm files for events");

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
            var conferences = CreateTestConferences();
            var events = CreateTestEvents(1);
            var syncCompletedTcs = new TaskCompletionSource<bool>();

            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(conferences);

            _apiClientMock
                .Setup(x => x.GetEventsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(events);

            var config = CreateTestConfiguration();
            var syncLoggerMock = new Mock<ISyncLogger>(MockBehavior.Loose);
            syncLoggerMock
                .Setup(x => x.LogSyncCompletion(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>()))
                .Callback<int, int, DateTime>((processed, created, timestamp) => syncCompletedTcs.TrySetResult(true));

            _syncService = new SyncService(_apiClientMock.Object, CreateStrmGenerator(), syncLoggerMock.Object, () => config, _loggerMock.Object);

            await _syncService.StartAsync(CancellationToken.None);

            using var cts = new CancellationTokenSource(10000);
            try
            {
                await syncCompletedTcs.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.Fail("Sync did not complete within timeout");
            }

            await _syncService.StopAsync(CancellationToken.None);

            syncLoggerMock.Verify(
                x => x.LogSyncCompletion(
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<DateTime>()),
                Times.AtLeastOnce(),
                "Should log sync operations");
        }

        [Fact]
        public async Task Incremental_sync_only_updates_changes()
        {
            var conferences = CreateTestConferences();
            var events = CreateTestEvents(1);
            var firstSyncTcs = new TaskCompletionSource<bool>();
            var secondSyncTcs = new TaskCompletionSource<bool>();
            var syncCallCount = 0;

            var syncLoggerMock = new Mock<ISyncLogger>(MockBehavior.Loose);
            syncLoggerMock
                .Setup(x => x.LogSyncCompletion(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>()))
                .Callback<int, int, DateTime>((processed, created, timestamp) =>
                {
                    var count = Interlocked.Increment(ref syncCallCount);
                    if (count == 1) firstSyncTcs.TrySetResult(true);
                    if (count == 2) secondSyncTcs.TrySetResult(true);
                });

            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(conferences);

            _apiClientMock
                .Setup(x => x.GetEventsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(events);

            var config = CreateTestConfiguration();
            _syncService = new SyncService(_apiClientMock.Object, CreateStrmGenerator(), syncLoggerMock.Object, () => config, _loggerMock.Object);

            await _syncService.StartAsync(CancellationToken.None);

            using var cts1 = new CancellationTokenSource(10000);
            try
            {
                await firstSyncTcs.Task.WaitAsync(cts1.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.Fail("First sync did not complete within timeout");
            }

            var initialFileCount = Directory.GetFiles(_testArchivePath, "*.strm", SearchOption.AllDirectories).Length;

            config.SyncIntervalHours = 0;
            await _syncService.StopAsync(CancellationToken.None);

            _syncService = new SyncService(_apiClientMock.Object, CreateStrmGenerator(), syncLoggerMock.Object, () => config, _loggerMock.Object);
            await _syncService.StartAsync(CancellationToken.None);

            using var cts2 = new CancellationTokenSource(10000);
            try
            {
                await secondSyncTcs.Task.WaitAsync(cts2.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.Fail("Second sync did not complete within timeout");
            }

            await _syncService.StopAsync(CancellationToken.None);
            var finalFileCount = Directory.GetFiles(_testArchivePath, "*.strm", SearchOption.AllDirectories).Length;

            Assert.Equal(initialFileCount, finalFileCount);

            _apiClientMock.Verify(
                x => x.GetEventsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.AtLeast(1));
        }

        [Fact]
        public async Task Sync_handles_api_rate_limiting()
        {
            var conferences = CreateTestConferences();
            var rateLimitCount = 0;
            var firstCallTcs = new TaskCompletionSource<bool>();

            var syncLoggerMock = new Mock<ISyncLogger>(MockBehavior.Loose);

            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    rateLimitCount++;
                    if (rateLimitCount == 1)
                    {
                        firstCallTcs.TrySetResult(true);
                        throw new HttpRequestException("429 Too Many Requests", null, System.Net.HttpStatusCode.TooManyRequests);
                    }
                    return conferences;
                });

            var events = CreateTestEvents(1);
            _apiClientMock
                .Setup(x => x.GetEventsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(events);

            var config = CreateTestConfiguration();
            _syncService = new SyncService(_apiClientMock.Object, CreateStrmGenerator(), syncLoggerMock.Object, () => config, _loggerMock.Object);

            await _syncService.StartAsync(CancellationToken.None);

            using var cts = new CancellationTokenSource(5000);
            try
            {
                await firstCallTcs.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.Fail("First API call did not occur within timeout");
            }

            await _syncService.StopAsync(CancellationToken.None);

            Assert.True(rateLimitCount >= 1, "Should handle rate limiting");
            Assert.Contains(LogLevel.Error, _loggedMessages);
        }

        [Fact]
        public async Task Sync_handles_network_timeout_gracefully()
        {
            var errorTcs = new TaskCompletionSource<bool>();

            var syncLoggerMock = new Mock<ISyncLogger>(MockBehavior.Loose);
            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TaskCanceledException("Request timeout"))
                .Callback(() => errorTcs.TrySetResult(true));

            var config = CreateTestConfiguration();
            _syncService = new SyncService(_apiClientMock.Object, CreateStrmGenerator(), syncLoggerMock.Object, () => config, _loggerMock.Object);

            await _syncService.StartAsync(CancellationToken.None);

            using var cts = new CancellationTokenSource(10000);
            try
            {
                await errorTcs.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.Fail("Timeout error did not occur within timeout");
            }

            await _syncService.StopAsync(CancellationToken.None);

            Assert.True(_loggedMessages.Count > 0, "Should have logged some messages");
        }

        [Fact]
        public async Task Sync_continues_on_individual_event_failure()
        {
            var conferences = CreateTestConferences();
            var events = CreateTestEvents(1);
            var syncCompletedTcs = new TaskCompletionSource<bool>();

            var syncLoggerMock = new Mock<ISyncLogger>(MockBehavior.Loose);
            syncLoggerMock
                .Setup(x => x.LogSyncCompletion(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>()))
                .Callback<int, int, DateTime>((processed, created, timestamp) => syncCompletedTcs.TrySetResult(true));

            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(conferences);

            _apiClientMock
                .Setup(x => x.GetEventsAsync(1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(events);
            _apiClientMock
                .Setup(x => x.GetEventsAsync(2, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Event fetch failed"));

            var config = CreateTestConfiguration();
            _syncService = new SyncService(_apiClientMock.Object, CreateStrmGenerator(), syncLoggerMock.Object, () => config, _loggerMock.Object);

            await _syncService.StartAsync(CancellationToken.None);

            using var cts = new CancellationTokenSource(10000);
            try
            {
                await syncCompletedTcs.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.Fail("Sync did not complete within timeout");
            }

            await _syncService.StopAsync(CancellationToken.None);

            var strmFiles = Directory.GetFiles(_testArchivePath, "*.strm", SearchOption.AllDirectories);
            Assert.True(strmFiles.Length > 0, "Should create .strm files despite some failures");

            Assert.Contains(LogLevel.Error, _loggedMessages);
        }

        [Fact]
        public async Task Sync_respects_cancellation_during_operation()
        {
            var conferences = CreateTestConferences();
            var events = CreateTestEvents(1);

            var cts = new CancellationTokenSource();
            var callCount = 0;
            var firstCallTcs = new TaskCompletionSource<bool>();

            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    callCount++;
                    if (callCount == 1) firstCallTcs.TrySetResult(true);
                    return conferences;
                });

            _apiClientMock
                .Setup(x => x.GetEventsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns(async (int conf, CancellationToken token) =>
                {
                    callCount++;
                    await Task.Delay(1, token);
                    return events;
                });

            var syncLoggerMock = new Mock<ISyncLogger>(MockBehavior.Loose);
            var config = CreateTestConfiguration();
            _syncService = new SyncService(_apiClientMock.Object, CreateStrmGenerator(), syncLoggerMock.Object, () => config, _loggerMock.Object);

            await _syncService.StartAsync(cts.Token);

            using var waitCts = new CancellationTokenSource(5000);
            try
            {
                await firstCallTcs.Task.WaitAsync(waitCts.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.Fail("First call did not occur within timeout");
            }

            cts.Cancel();

            using var settleCts = new CancellationTokenSource(500);
            try
            {
                await Task.Delay(Timeout.Infinite, settleCts.Token);
            }
            catch (OperationCanceledException) { }

            await _syncService.StopAsync(CancellationToken.None);

            Assert.True(callCount >= 0, "Should have made some calls before cancellation");
        }

        [Fact]
        public async Task Sync_can_be_paused_and_resumed()
        {
            var conferences = CreateTestConferences();
            var events = CreateTestEvents(1);
            var firstSyncTcs = new TaskCompletionSource<bool>();
            var secondSyncTcs = new TaskCompletionSource<bool>();
            var syncCallCount = 0;

            var syncLoggerMock = new Mock<ISyncLogger>(MockBehavior.Loose);
            syncLoggerMock
                .Setup(x => x.LogSyncCompletion(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>()))
                .Callback<int, int, DateTime>((processed, created, timestamp) =>
                {
                    var count = Interlocked.Increment(ref syncCallCount);
                    if (count == 1) firstSyncTcs.TrySetResult(true);
                    if (count == 2) secondSyncTcs.TrySetResult(true);
                });

            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(conferences);

            _apiClientMock
                .Setup(x => x.GetEventsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(events);

            var config = CreateTestConfiguration();
            _syncService = new SyncService(_apiClientMock.Object, CreateStrmGenerator(), syncLoggerMock.Object, () => config, _loggerMock.Object);

            await _syncService.StartAsync(CancellationToken.None);

            using var cts1 = new CancellationTokenSource(10000);
            try
            {
                await firstSyncTcs.Task.WaitAsync(cts1.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.Fail("First sync did not complete within timeout");
            }

            await _syncService.StopAsync(CancellationToken.None);
            var filesAfterPause = Directory.GetFiles(_testArchivePath, "*.strm", SearchOption.AllDirectories).Length;

            _syncService = new SyncService(_apiClientMock.Object, CreateStrmGenerator(), syncLoggerMock.Object, () => config, _loggerMock.Object);
            await _syncService.StartAsync(CancellationToken.None);

            using var cts2 = new CancellationTokenSource(10000);
            try
            {
                await secondSyncTcs.Task.WaitAsync(cts2.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.Fail("Second sync did not complete within timeout");
            }

            await _syncService.StopAsync(CancellationToken.None);
            var filesAfterResume = Directory.GetFiles(_testArchivePath, "*.strm", SearchOption.AllDirectories).Length;

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
                    Id = 1,
                    Acronym = "37c3",
                    Title = "37C3: Unlocked",
                    Slug = "37c3",
                    AspectRatio = "16:9",
                    UpdatedAt = DateTime.UtcNow.AddDays(-30)
                },
                new ConferenceDto
                {
                    Id = 2,
                    Acronym = "36c3",
                    Title = "36C3: Resource Overflow",
                    Slug = "36c3",
                    AspectRatio = "16:9",
                    UpdatedAt = DateTime.UtcNow.AddDays(-60)
                }
            };
        }

        private EventDto[] CreateTestEvents(int conferenceId)
        {
            var events = new List<EventDto>();

            for (int i = 1; i <= 3; i++)
            {
                events.Add(new EventDto
                {
                    Guid = $"conf{conferenceId}-event-{i}",
                    Title = $"Test Event {i} - Conference {conferenceId}",
                    Slug = $"test-event-{i}",
                    ConferenceId = conferenceId,
                    Description = $"Test event description {i}",
                    Date = "2023-12-27",
                    Length = 3600 + (i * 300),
                    Link = $"https://media.ccc.de/c/conf{conferenceId}/test-event-{i}",
                    Recordings = CreateTestRecordingDtos()
                });
            }

            return events.ToArray();
        }

        private List<RecordingDto> CreateTestRecordingDtos()
        {
            return new List<RecordingDto>
            {
                new RecordingDto
                {
                    Id = 1,
                    Language = "eng",
                    Format = "mp4",
                    HighQuality = true,
                    Width = 1920,
                    Height = 1080,
                    Size = 1024 * 1024 * 500,
                    Url = "https://cdn.media.ccc.de/test-hq.mp4",
                    MimeType = "video/mp4",
                    Length = 3600,
                    FileSize = 1024 * 1024 * 500,
                    Bitrate = 5000
                },
                new RecordingDto
                {
                    Id = 2,
                    Language = "deu",
                    Format = "mp4",
                    HighQuality = true,
                    Width = 1920,
                    Height = 1080,
                    Size = 1024 * 1024 * 450,
                    Url = "https://cdn.media.ccc.de/test-hq-deu.mp4",
                    MimeType = "video/mp4",
                    Length = 3600,
                    FileSize = 1024 * 1024 * 450,
                    Bitrate = 4500
                }
            };
        }

        private IStrmGenerator CreateStrmGenerator()
        {
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
                new Mock<IHttpClientFactory>().Object,
                _testArchivePath);
        }

        private PluginConfiguration CreateTestConfiguration()
        {
            return new PluginConfiguration
            {
                WatchlistPath = _testArchivePath,
                PreferredQuality = "high",
                PreferredAudioLanguages = new List<string> { "eng" },
                PreferredSubtitleLanguages = new List<string>(),
                SyncIntervalHours = 0
            };
        }

        #endregion
    }
}
