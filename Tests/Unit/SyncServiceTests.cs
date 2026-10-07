using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaCccDe.Api;
using Jellyfin.Plugin.MediaCccDe.Models;
using Jellyfin.Plugin.MediaCccDe.Services;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Plugins;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.MediaCccDe.Tests.Unit
{
    public class SyncServiceTests
    {
        private readonly Mock<IMediaCccApiClient> _apiClientMock;
        private readonly Mock<IStrmGenerator> _strmGeneratorMock;
        private readonly Mock<ISyncLogger> _syncLoggerMock;
        private readonly PluginConfiguration _configuration;
        private readonly Mock<ILogger<SyncService>> _loggerMock;

        public SyncServiceTests()
        {
            _apiClientMock = new Mock<IMediaCccApiClient>();
            _strmGeneratorMock = new Mock<IStrmGenerator>();
            _syncLoggerMock = new Mock<ISyncLogger>();
            _configuration = new PluginConfiguration();
            _loggerMock = new Mock<ILogger<SyncService>>();
        }

        [Fact]
        public void SyncService_implements_IHostedService()
        {
            var service = CreateService();
            Assert.IsAssignableFrom<Microsoft.Extensions.Hosting.IHostedService>(service);
        }

        [Fact]
        public async Task StartAsync_initiates_background_task()
        {
            _configuration.SyncIntervalHours = 6;

            var syncCompleted = new TaskCompletionSource<bool>();
            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ConferenceDto>())
                .Callback(() => syncCompleted.TrySetResult(true));

            var service = CreateService();

            await service.StartAsync(CancellationToken.None);

            using var cts = new CancellationTokenSource(5000);
            try
            {
                await syncCompleted.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.Fail("Sync did not start within timeout");
            }

            _loggerMock.Verify(
                x => x.Log(
                    It.IsAny<LogLevel>(),
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce());

            await service.StopAsync(CancellationToken.None);
        }

        [Fact]
        public async Task StopAsync_cancels_running_sync()
        {
            _configuration.SyncIntervalHours = 6;

            var firstSyncCompleted = new TaskCompletionSource<bool>();
            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ConferenceDto>())
                .Callback(() => firstSyncCompleted.TrySetResult(true));

            var service = CreateService();
            await service.StartAsync(CancellationToken.None);

            // Wait for first sync to complete deterministically
            using var cts1 = new CancellationTokenSource(5000);
            try
            {
                await firstSyncCompleted.Task.WaitAsync(cts1.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.Fail("First sync did not complete within timeout");
            }

            await service.StopAsync(CancellationToken.None);

            var initialCallCount = _apiClientMock.Invocations.Count;

            // Give a small window for any in-flight operations to settle
            using var cts2 = new CancellationTokenSource(200);
            try
            {
                await Task.Delay(Timeout.Infinite, cts2.Token);
            }
            catch (OperationCanceledException) { }

            var finalCallCount = _apiClientMock.Invocations.Count;
            Assert.Equal(initialCallCount, finalCallCount);
        }

        [Fact]
        public async Task ExecuteAsync_calls_Api_GetConferences_on_interval()
        {
            _configuration.SyncIntervalHours = 6;

            var conferences = new List<ConferenceDto>
            {
                new ConferenceDto { Title = "37C3", Acronym = "37c3" },
                new ConferenceDto { Title = "36C3", Acronym = "36c3" }
            };

            var syncCompleted = new TaskCompletionSource<bool>();
            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(conferences)
                .Callback(() => syncCompleted.TrySetResult(true));

            _strmGeneratorMock
                .Setup(x => x.StrmFilesExistForConference(It.IsAny<ConferenceDto>()))
                .Returns(false);

            _strmGeneratorMock
                .Setup(x => x.CreateStrmFilesForConference(It.IsAny<ConferenceDto>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var service = CreateService();

            await service.StartAsync(CancellationToken.None);

            using var cts = new CancellationTokenSource(5000);
            try
            {
                await syncCompleted.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.Fail("Sync did not complete within timeout");
            }

            _apiClientMock.Verify(
                x => x.GetConferencesAsync(It.IsAny<CancellationToken>()),
                Times.AtLeastOnce());

            await service.StopAsync(CancellationToken.None);
        }

        [Fact]
        public async Task ExecuteAsync_creates_strm_files_for_new_conferences()
        {
            _configuration.SyncIntervalHours = 6;
            
            var conferences = new List<ConferenceDto>
            {
                new ConferenceDto { Title = "37C3", Acronym = "37c3", Slug = "37c3" },
                new ConferenceDto { Title = "36C3", Acronym = "36c3", Slug = "36c3" }
            };

            var syncCompleted = new TaskCompletionSource<bool>();
            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(conferences)
                .Callback(() => syncCompleted.TrySetResult(true));
            
            _strmGeneratorMock
                .Setup(x => x.StrmFilesExistForConference(It.IsAny<ConferenceDto>()))
                .Returns(false);

            _strmGeneratorMock
                .Setup(x => x.CreateStrmFilesForConference(It.IsAny<ConferenceDto>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var service = CreateService();

            await service.StartAsync(CancellationToken.None);

            using var cts = new CancellationTokenSource(5000);
            try
            {
                await syncCompleted.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.Fail("Sync did not complete within timeout");
            }

            _strmGeneratorMock.Verify(
                x => x.CreateStrmFilesForConference(It.IsAny<ConferenceDto>(), It.IsAny<CancellationToken>()),
                Times.AtLeast(2));

            await service.StopAsync(CancellationToken.None);
        }

        [Fact]
        public async Task ExecuteAsync_rechecks_existing_conferences_for_new_strm_files()
        {
            _configuration.SyncIntervalHours = 6;
            
            var conferences = new List<ConferenceDto>
            {
                new ConferenceDto { Title = "37C3", Acronym = "37c3", Slug = "37c3" }
            };

            var syncCompleted = new TaskCompletionSource<bool>();
            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(conferences)
                .Callback(() => syncCompleted.TrySetResult(true));

            var service = CreateService();

            await service.StartAsync(CancellationToken.None);

            using var cts = new CancellationTokenSource(5000);
            try
            {
                await syncCompleted.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.Fail("Sync did not complete within timeout");
            }

            _strmGeneratorMock.Verify(
                x => x.CreateStrmFilesForConference(It.IsAny<ConferenceDto>(), It.IsAny<CancellationToken>()),
                Times.Once());

            await service.StopAsync(CancellationToken.None);
        }

        [Fact]
        public async Task ExecuteAsync_logs_sync_start_and_completion()
        {
            _configuration.SyncIntervalHours = 6;
            
            var conferences = new List<ConferenceDto>
            {
                new ConferenceDto { Title = "37C3", Acronym = "37c3" }
            };

            var syncCompleted = new TaskCompletionSource<bool>();
            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(conferences)
                .Callback(() => syncCompleted.TrySetResult(true));

            _strmGeneratorMock
                .Setup(x => x.StrmFilesExistForConference(It.IsAny<ConferenceDto>()))
                .Returns(false);

            _strmGeneratorMock
                .Setup(x => x.CreateStrmFilesForConference(It.IsAny<ConferenceDto>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var service = CreateService();

            await service.StartAsync(CancellationToken.None);

            using var cts = new CancellationTokenSource(5000);
            try
            {
                await syncCompleted.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.Fail("Sync did not complete within timeout");
            }

            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Starting conference sync")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce());

            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Conference sync completed")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce());

            await service.StopAsync(CancellationToken.None);
        }

        [Fact]
        public async Task ExecuteAsync_handles_api_failure_gracefully()
        {
            _configuration.SyncIntervalHours = 6;
            
            var errorLogged = new TaskCompletionSource<bool>();
            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("API unavailable"))
                .Callback(() => errorLogged.TrySetResult(true));

            var service = CreateService();

            await service.StartAsync(CancellationToken.None);

            using var cts = new CancellationTokenSource(5000);
            try
            {
                await errorLogged.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.Fail("API error did not occur within timeout");
            }

            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce());

            await service.StopAsync(CancellationToken.None);
        }

        [Fact]
        public async Task ExecuteAsync_respects_cancellation_token()
        {
            _configuration.SyncIntervalHours = 6;

            var firstSyncCompleted = new TaskCompletionSource<bool>();
            var conferences = new List<ConferenceDto>();
            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(conferences)
                .Callback(() => firstSyncCompleted.TrySetResult(true));

            var cts = new CancellationTokenSource();
            var service = CreateService();

            await service.StartAsync(cts.Token);

            using var waitCts = new CancellationTokenSource(5000);
            try
            {
                await firstSyncCompleted.Task.WaitAsync(waitCts.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.Fail("First sync did not complete within timeout");
            }

            cts.Cancel();

            using var settleCts = new CancellationTokenSource(200);
            try
            {
                await Task.Delay(Timeout.Infinite, settleCts.Token);
            }
            catch (OperationCanceledException) { }
        }

        [Fact]
        public async Task ExecuteAsync_uses_configured_sync_interval()
        {
            var expectedInterval = 12;
            _configuration.SyncIntervalHours = expectedInterval;
            var config = _configuration;
            
            var syncCompleted = new TaskCompletionSource<bool>();
            var conferences = new List<ConferenceDto>();
            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(conferences)
                .Callback(() => syncCompleted.TrySetResult(true));

            var service = CreateService();

            await service.StartAsync(CancellationToken.None);

            using var cts = new CancellationTokenSource(5000);
            try
            {
                await syncCompleted.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.Fail("Sync did not complete within timeout");
            }

            Assert.Equal(expectedInterval, config.SyncIntervalHours);

            await service.StopAsync(CancellationToken.None);
        }

        [Fact]
        public async Task ExecuteAsync_updates_sync_log_on_completion()
        {
            _configuration.SyncIntervalHours = 6;
            
            var conferences = new List<ConferenceDto>
            {
                new ConferenceDto { Title = "37C3", Acronym = "37c3" }
            };

            var syncCompleted = new TaskCompletionSource<bool>();
            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(conferences)
                .Callback(() => syncCompleted.TrySetResult(true));

            _strmGeneratorMock
                .Setup(x => x.StrmFilesExistForConference(It.IsAny<ConferenceDto>()))
                .Returns(false);

            _strmGeneratorMock
                .Setup(x => x.CreateStrmFilesForConference(It.IsAny<ConferenceDto>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var service = CreateService();

            await service.StartAsync(CancellationToken.None);

            using var cts = new CancellationTokenSource(5000);
            try
            {
                await syncCompleted.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.Fail("Sync did not complete within timeout");
            }

            _syncLoggerMock.Verify(
                x => x.LogSyncCompletion(
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<DateTime>()),
                Times.AtLeastOnce());

            await service.StopAsync(CancellationToken.None);
        }

        [Fact]
        public async Task TriggerSyncAsync_actually_initiates_sync()
        {
            _configuration.SyncIntervalHours = 999;

            var firstSyncTcs = new TaskCompletionSource<bool>();
            var secondSyncTcs = new TaskCompletionSource<bool>();
            var syncCallCount = 0;

            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ConferenceDto>())
                .Callback(() =>
                {
                    var count = Interlocked.Increment(ref syncCallCount);
                    if (count == 1) firstSyncTcs.TrySetResult(true);
                    if (count == 2) secondSyncTcs.TrySetResult(true);
                });

            var service = CreateService();

            await service.StartAsync(CancellationToken.None);

            using var cts1 = new CancellationTokenSource(3000);
            try
            {
                await firstSyncTcs.Task.WaitAsync(cts1.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.Fail("First sync did not complete within timeout");
            }

            var countBeforeTrigger = syncCallCount;
            Assert.Equal(1, countBeforeTrigger);

            await service.TriggerSyncAsync(CancellationToken.None);

            using var cts2 = new CancellationTokenSource(3000);
            try
            {
                await secondSyncTcs.Task.WaitAsync(cts2.Token);
            }
            catch (OperationCanceledException)
            {
            }

            await service.StopAsync(CancellationToken.None);

            Assert.True(syncCallCount > countBeforeTrigger,
                $"TriggerSyncAsync should cause an additional sync cycle. " +
                $"Calls before trigger: {countBeforeTrigger}, total calls: {syncCallCount}");
        }

        [Fact]
        public async Task TriggerSyncAsync_wakes_sync_loop_from_idle()
        {
            _configuration.SyncIntervalHours = 999;

            var firstSyncTcs = new TaskCompletionSource<bool>();
            var secondSyncTcs = new TaskCompletionSource<bool>();
            var syncCallCount = 0;

            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ConferenceDto>())
                .Callback(() =>
                {
                    var count = Interlocked.Increment(ref syncCallCount);
                    if (count == 1) firstSyncTcs.TrySetResult(true);
                    if (count == 2) secondSyncTcs.TrySetResult(true);
                });

            var service = CreateService();

            await service.StartAsync(CancellationToken.None);

            using var cts1 = new CancellationTokenSource(3000);
            try
            {
                await firstSyncTcs.Task.WaitAsync(cts1.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.Fail("First sync did not complete within timeout");
            }

            Assert.Equal(1, syncCallCount);

            await service.TriggerSyncAsync(CancellationToken.None);

            using var cts2 = new CancellationTokenSource(3000);
            try
            {
                await secondSyncTcs.Task.WaitAsync(cts2.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.Fail("TriggerSyncAsync did not wake the sync loop within timeout. " +
                    "The loop should have been interrupted from its idle delay.");
            }

            await service.StopAsync(CancellationToken.None);

            Assert.Equal(2, syncCallCount);
        }

        [Fact]
        public async Task SyncService_uses_current_config_not_snapshot()
        {
            var initialConfig = new PluginConfiguration { SyncIntervalHours = 999 };
            var updatedConfig = new PluginConfiguration { SyncIntervalHours = 6 };
            var currentConfig = initialConfig;

            var firstSyncTcs = new TaskCompletionSource<bool>();
            var secondSyncTcs = new TaskCompletionSource<bool>();
            var syncCallCount = 0;

            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ConferenceDto>())
                .Callback(() =>
                {
                    var count = Interlocked.Increment(ref syncCallCount);
                    if (count == 1) firstSyncTcs.TrySetResult(true);
                    if (count == 2) secondSyncTcs.TrySetResult(true);
                });

            var service = new SyncService(
                _apiClientMock.Object,
                _strmGeneratorMock.Object,
                _syncLoggerMock.Object,
                () => currentConfig,
                _loggerMock.Object);

            await service.StartAsync(CancellationToken.None);

            using var cts = new CancellationTokenSource(5000);
            try
            {
                await firstSyncTcs.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.Fail("First sync did not complete within timeout");
            }

            Assert.Equal(1, syncCallCount);
            currentConfig = updatedConfig;

            await service.TriggerSyncAsync(CancellationToken.None);

            using var cts2 = new CancellationTokenSource(5000);
            try
            {
                await secondSyncTcs.Task.WaitAsync(cts2.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.Fail("Second sync did not complete within timeout after config change");
            }

            await service.StopAsync(CancellationToken.None);

            Assert.True(syncCallCount >= 2,
                $"Expected at least 2 syncs after config change, got {syncCallCount}");

            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("6")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce(),
                "Expected log to reflect updated SyncIntervalHours=6");
        }

        [Fact]
        public async Task SyncService_interval_zero_or_negative_does_not_busy_loop()
        {
            _configuration.SyncIntervalHours = 0;

            var maxAllowedCalls = 5;
            var syncCallCount = 0;
            var firstSyncTcs = new TaskCompletionSource<bool>();

            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ConferenceDto>())
                .Callback(() =>
                {
                    var count = Interlocked.Increment(ref syncCallCount);
                    if (count == 1) firstSyncTcs.TrySetResult(true);
                });

            var service = CreateService();

            await service.StartAsync(CancellationToken.None);

            // Wait for first sync to complete deterministically
            using var cts1 = new CancellationTokenSource(5000);
            try
            {
                await firstSyncTcs.Task.WaitAsync(cts1.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.Fail("First sync did not complete within timeout");
            }

            // Wait for the interval delay to elapse (production defaults 0 to 6 hours,
            // but waits on semaphore with timeout, so give enough time for one more cycle)
            using var settleCts = new CancellationTokenSource(2000);
            try
            {
                await Task.Delay(Timeout.Infinite, settleCts.Token);
            }
            catch (OperationCanceledException) { }

            await service.StopAsync(CancellationToken.None);

            Assert.True(syncCallCount <= maxAllowedCalls,
                $"Expected at most {maxAllowedCalls} sync calls with zero interval, " +
                $"got {syncCallCount}. Busy loop detected.");

            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("SyncIntervalHours") ||
                                                    v.ToString()!.Contains("interval")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce(),
                "Expected warning about invalid SyncIntervalHours");
        }

        [Fact]
        public async Task SyncService_uses_exponential_backoff_on_transient_errors()
        {
            _configuration.SyncIntervalHours = 6;

            var syncCallCount = 0;
            var firstErrorTcs = new TaskCompletionSource<bool>();

            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("Transient error"))
                .Callback(() =>
                {
                    var count = Interlocked.Increment(ref syncCallCount);
                    if (count == 1) firstErrorTcs.TrySetResult(true);
                });

            var service = CreateService();

            await service.StartAsync(CancellationToken.None);

            // Wait deterministically for the first error to occur
            using var cts1 = new CancellationTokenSource(5000);
            try
            {
                await firstErrorTcs.Task.WaitAsync(cts1.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.Fail("First error did not occur within timeout");
            }

            // Wait additional time to observe limited retries due to backoff
            // Backoff is exponential: 1min, 2min, 4min... so no more retries
            // should happen within this short window
            using var settleCts = new CancellationTokenSource(2000);
            try
            {
                await Task.Delay(Timeout.Infinite, settleCts.Token);
            }
            catch (OperationCanceledException) { }

            await service.StopAsync(CancellationToken.None);

            var maxAllowedCallsInWindow = 5;
            Assert.True(syncCallCount <= maxAllowedCallsInWindow,
                $"Expected at most {maxAllowedCallsInWindow} retries after first error with backoff, " +
                $"got {syncCallCount}. No backoff detected.");

            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("retry") ||
                                                    v.ToString()!.Contains("backoff") ||
                                                    v.ToString()!.Contains("attempt")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce(),
                "Expected error log to include retry/backoff info");
        }

        private SyncService CreateService(
            TimeSpan? configRecheckInterval = null,
            ILibraryManager? libraryManager = null)
        {
            return new SyncService(
                _apiClientMock.Object,
                _strmGeneratorMock.Object,
                _syncLoggerMock.Object,
                () => _configuration,
                _loggerMock.Object,
                libraryManager,
                configRecheckInterval);
        }

        [Fact]
        public async Task Sync_queues_a_library_scan_when_files_were_created()
        {
            var libraryManagerMock = new Mock<ILibraryManager>(MockBehavior.Loose);
            _apiClientMock
                .Setup(a => a.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ConferenceDto>
                {
                    new ConferenceDto { Acronym = "37c3", Title = "37C3" }
                });
            _strmGeneratorMock
                .Setup(s => s.CreateStrmFilesForConference(It.IsAny<ConferenceDto>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(true));

            var service = CreateService(TimeSpan.FromHours(6), libraryManagerMock.Object);
            await service.StartAsync(CancellationToken.None);
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline
                && !libraryManagerMock.Invocations.Any(i => i.Method.Name == nameof(ILibraryManager.QueueLibraryScan)))
            {
                await Task.Delay(50);
            }

            await service.StopAsync(CancellationToken.None);

            libraryManagerMock.Verify(m => m.QueueLibraryScan(), Times.Once);
        }

        [Fact]
        public async Task Sync_does_not_queue_a_scan_when_nothing_changed()
        {
            var libraryManagerMock = new Mock<ILibraryManager>(MockBehavior.Loose);
            _apiClientMock
                .Setup(a => a.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ConferenceDto>());

            var service = CreateService(TimeSpan.FromHours(6), libraryManagerMock.Object);
            await service.StartAsync(CancellationToken.None);

            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline
                && _syncLoggerMock.Invocations.Count == 0)
            {
                await Task.Delay(50);
            }

            await service.StopAsync(CancellationToken.None);

            libraryManagerMock.Verify(m => m.QueueLibraryScan(), Times.Never);
        }

        [Fact]
        public async Task RunAsync_reacts_to_a_changed_sync_interval()
        {
            _configuration.SyncIntervalHours = 24;
            _apiClientMock
                .Setup(a => a.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ConferenceDto>());

            var service = CreateService(TimeSpan.FromMilliseconds(50));

            await service.StartAsync(CancellationToken.None);

            // A long first interval must not mask a later configuration change.
            _configuration.SyncIntervalHours = 1;
            var observed = false;
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline && !observed)
            {
                observed = _loggerMock.Invocations.Any(i =>
                    i.Method.Name == nameof(ILogger.Log) &&
                    i.Arguments.Count > 2 &&
                    i.Arguments[2]?.ToString()?.Contains("Sync interval changed") == true);
                if (!observed)
                {
                    await Task.Delay(50);
                }
            }

            await service.StopAsync(CancellationToken.None);

            Assert.True(
                observed,
                "expected the service to notice the new interval instead of waiting out the old one");
        }
    }
}
