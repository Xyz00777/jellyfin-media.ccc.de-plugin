using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaCccDe.Api;
using Jellyfin.Plugin.MediaCccDe.Controllers;
using Jellyfin.Plugin.MediaCccDe.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.MediaCccDe.Tests.Unit
{
    public class MediaCccControllerTests
    {
        private readonly Mock<ILogger<MediaCccController>> _loggerMock;
        private readonly Mock<IMediaCccApiClient> _apiClientMock;
        private readonly Mock<IUserDataManager> _userDataManagerMock;
        private readonly MediaCccController _controller;

        public MediaCccControllerTests()
        {
            _loggerMock = new Mock<ILogger<MediaCccController>>(MockBehavior.Loose);
            _apiClientMock = new Mock<IMediaCccApiClient>(MockBehavior.Strict);
            _userDataManagerMock = new Mock<IUserDataManager>(MockBehavior.Loose);
            _controller = new MediaCccController(_loggerMock.Object, _apiClientMock.Object, _userDataManagerMock.Object);
        }

        [Fact]
        public async Task GetConferences_propagates_OperationCanceledException()
        {
            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException("Request cancelled"));

            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                _controller.GetConferences(CancellationToken.None));
        }

        [Fact]
        public async Task GetConferenceEvents_propagates_OperationCanceledException()
        {
            _apiClientMock
                .Setup(x => x.GetEventsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException("Request cancelled"));

            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                _controller.GetConferenceEvents("37c3", CancellationToken.None));
        }

        [Fact]
        public async Task GetEvent_propagates_OperationCanceledException()
        {
            _apiClientMock
                .Setup(x => x.GetEventAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException("Request cancelled"));

            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                _controller.GetEvent("test-guid", CancellationToken.None));
        }

        [Fact]
        public async Task GetRecentEvents_propagates_OperationCanceledException()
        {
            _apiClientMock
                .Setup(x => x.GetRecentAsync(It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException("Request cancelled"));

            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                _controller.GetRecentEvents(null, CancellationToken.None));
        }

        [Fact]
        public async Task GetConferences_returns_500_on_generic_exception()
        {
            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("API error"));

            var result = await _controller.GetConferences(CancellationToken.None);

            var statusCodeResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusCodeResult.StatusCode);
        }

        [Fact]
        public async Task GetConferenceEvents_returns_500_on_generic_exception()
        {
            _apiClientMock
                .Setup(x => x.GetEventsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("API error"));

            var result = await _controller.GetConferenceEvents("37c3", CancellationToken.None);

            var statusCodeResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusCodeResult.StatusCode);
        }

        [Fact]
        public async Task GetEvent_returns_500_on_generic_exception()
        {
            _apiClientMock
                .Setup(x => x.GetEventAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("API error"));

            var result = await _controller.GetEvent("test-guid", CancellationToken.None);

            var statusCodeResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusCodeResult.StatusCode);
        }

        [Fact]
        public async Task GetRecentEvents_returns_500_on_generic_exception()
        {
            _apiClientMock
                .Setup(x => x.GetRecentAsync(It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("API error"));

            var result = await _controller.GetRecentEvents(null, CancellationToken.None);

            var statusCodeResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusCodeResult.StatusCode);
        }
    }
}
