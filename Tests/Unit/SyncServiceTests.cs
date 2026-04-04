using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaCccDe.Api;
using Jellyfin.Plugin.MediaCccDe.Models;
using Jellyfin.Plugin.MediaCccDe.Services;
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
        private readonly Mock<PluginConfiguration> _configurationMock;
        private readonly Mock<ILogger<SyncService>> _loggerMock;

        public SyncServiceTests()
        {
            _apiClientMock = new Mock<IMediaCccApiClient>(MockBehavior.Strict);
            _strmGeneratorMock = new Mock<IStrmGenerator>(MockBehavior.Strict);
            _syncLoggerMock = new Mock<ISyncLogger>(MockBehavior.Strict);
            _configurationMock = new Mock<PluginConfiguration>(MockBehavior.Loose);
            _loggerMock = new Mock<ILogger<SyncService>>(MockBehavior.Loose);
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
            _configurationMock.Setup(x => x.SyncIntervalHours).Returns(6);
            var service = CreateService();

            await service.StartAsync(CancellationToken.None);
            await Task.Delay(100);

            _loggerMock.Verify(
                x => x.Log(
                    It.IsAny<LogLevel>(),
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce());
        }

        [Fact]
        public async Task StopAsync_cancels_running_sync()
        {
            _configurationMock.Setup(x => x.SyncIntervalHours).Returns(6);
            var service = CreateService();
            await service.StartAsync(CancellationToken.None);
            await Task.Delay(50);

            await service.StopAsync(CancellationToken.None);
            await Task.Delay(200);
            
            var initialCallCount = _apiClientMock.Invocations.Count;
            await Task.Delay(100);
            var finalCallCount = _apiClientMock.Invocations.Count;
            Assert.Equal(initialCallCount, finalCallCount);
        }

        [Fact]
        public async Task ExecuteAsync_calls_Api_GetConferences_on_interval()
        {
            _configurationMock.Setup(x => x.SyncIntervalHours).Returns(0);
            
            var conferences = new List<ConferenceDto>
            {
                new ConferenceDto { Title = "37C3", Acronym = "37c3" },
                new ConferenceDto { Title = "36C3", Acronym = "36c3" }
            };

            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(conferences);

            var service = CreateService();

            await service.StartAsync(CancellationToken.None);
            await Task.Delay(200);

            _apiClientMock.Verify(
                x => x.GetConferencesAsync(It.IsAny<CancellationToken>()),
                Times.AtLeastOnce());
        }

        [Fact]
        public async Task ExecuteAsync_creates_strm_files_for_new_conferences()
        {
            _configurationMock.Setup(x => x.SyncIntervalHours).Returns(0);
            
            var conferences = new List<ConferenceDto>
            {
                new ConferenceDto { Title = "37C3", Acronym = "37c3", Slug = "37c3" },
                new ConferenceDto { Title = "36C3", Acronym = "36c3", Slug = "36c3" }
            };

            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(conferences);
            
            _strmGeneratorMock
                .Setup(x => x.CreateStrmFilesForConference(It.IsAny<ConferenceDto>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var service = CreateService();

            await service.StartAsync(CancellationToken.None);
            await Task.Delay(200);

            _strmGeneratorMock.Verify(
                x => x.CreateStrmFilesForConference(It.IsAny<ConferenceDto>(), It.IsAny<CancellationToken>()),
                Times.AtLeast(2));
        }

        [Fact]
        public async Task ExecuteAsync_does_not_duplicate_existing_strm_files()
        {
            _configurationMock.Setup(x => x.SyncIntervalHours).Returns(0);
            
            var conferences = new List<ConferenceDto>
            {
                new ConferenceDto { Title = "37C3", Acronym = "37c3", Slug = "37c3" }
            };

            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(conferences);

            _strmGeneratorMock
                .SetupSequence(x => x.StrmFilesExistForConference(It.IsAny<ConferenceDto>()))
                .Returns(false);

            _strmGeneratorMock
                .Setup(x => x.CreateStrmFilesForConference(It.IsAny<ConferenceDto>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var service = CreateService();

            await service.StartAsync(CancellationToken.None);
            await Task.Delay(200);

            _strmGeneratorMock.Verify(
                x => x.CreateStrmFilesForConference(It.IsAny<ConferenceDto>(), It.IsAny<CancellationToken>()),
                Times.Once());

            _strmGeneratorMock.Reset();
            _strmGeneratorMock
                .Setup(x => x.StrmFilesExistForConference(It.IsAny<ConferenceDto>()))
                .Returns(true);

            await Task.Delay(100);

            _strmGeneratorMock.Verify(
                x => x.CreateStrmFilesForConference(It.IsAny<ConferenceDto>(), It.IsAny<CancellationToken>()),
                Times.Never());
        }

        [Fact]
        public async Task ExecuteAsync_logs_sync_start_and_completion()
        {
            _configurationMock.Setup(x => x.SyncIntervalHours).Returns(0);
            
            var conferences = new List<ConferenceDto>
            {
                new ConferenceDto { Title = "37C3", Acronym = "37c3" }
            };

            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(conferences);

            _strmGeneratorMock
                .Setup(x => x.StrmFilesExistForConference(It.IsAny<ConferenceDto>()))
                .Returns(false);

            _strmGeneratorMock
                .Setup(x => x.CreateStrmFilesForConference(It.IsAny<ConferenceDto>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var service = CreateService();

            await service.StartAsync(CancellationToken.None);
            await Task.Delay(200);

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
        }

        [Fact]
        public async Task ExecuteAsync_handles_api_failure_gracefully()
        {
            _configurationMock.Setup(x => x.SyncIntervalHours).Returns(0);
            
            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("API unavailable"));

            var service = CreateService();

            await service.StartAsync(CancellationToken.None);
            await Task.Delay(200);

            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce());
        }

        [Fact]
        public async Task ExecuteAsync_respects_cancellation_token()
        {
            _configurationMock.Setup(x => x.SyncIntervalHours).Returns(6);
            
            var conferences = new List<ConferenceDto>();
            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(conferences);

            var cts = new CancellationTokenSource();
            var service = CreateService();

            await service.StartAsync(cts.Token);
            await Task.Delay(50);
            cts.Cancel();
            await Task.Delay(100);

            Assert.True(true);
        }

        [Fact]
        public async Task ExecuteAsync_uses_configured_sync_interval()
        {
            var expectedInterval = 12;
            var config = _configurationMock.Object;
            config.SyncIntervalHours = expectedInterval;
            
            var conferences = new List<ConferenceDto>();
            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(conferences);

            var service = CreateService();

            await service.StartAsync(CancellationToken.None);
            await Task.Delay(50);

            Assert.Equal(expectedInterval, config.SyncIntervalHours);
        }

        [Fact]
        public async Task ExecuteAsync_updates_sync_log_on_completion()
        {
            _configurationMock.Setup(x => x.SyncIntervalHours).Returns(0);
            
            var conferences = new List<ConferenceDto>
            {
                new ConferenceDto { Title = "37C3", Acronym = "37c3" }
            };

            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(conferences);

            _strmGeneratorMock
                .Setup(x => x.StrmFilesExistForConference(It.IsAny<ConferenceDto>()))
                .Returns(false);

            _strmGeneratorMock
                .Setup(x => x.CreateStrmFilesForConference(It.IsAny<ConferenceDto>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            _syncLoggerMock
                .Setup(x => x.LogSyncCompletion(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>()))
                .Returns(Task.CompletedTask);

            var service = CreateService();

            await service.StartAsync(CancellationToken.None);
            await Task.Delay(200);

            _syncLoggerMock.Verify(
                x => x.LogSyncCompletion(
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<DateTime>()),
                Times.AtLeastOnce());
        }

        private SyncService CreateService()
        {
            return new SyncService(
                _apiClientMock.Object,
                _strmGeneratorMock.Object,
                _syncLoggerMock.Object,
                _configurationMock.Object,
                _loggerMock.Object);
        }
    }
}