using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaCccDe.Controllers;
using Jellyfin.Plugin.MediaCccDe.Models;
using Jellyfin.Plugin.MediaCccDe.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.MediaCccDe.Tests.Unit
{
    public class DownloadControllerTests
    {
        private const string JellyfinUserIdClaimType = "Jellyfin-UserId";

        private readonly Mock<IWatchlistDownloadService> _downloadServiceMock =
            new Mock<IWatchlistDownloadService>(MockBehavior.Loose);

        private DownloadController CreateController(params Claim[] claims)
        {
            var controller = new DownloadController(_downloadServiceMock.Object);
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
                }
            };
            return controller;
        }

        [Fact]
        public async Task GetDownloads_ReturnsOk_ForJellyfinUserIdClaim()
        {
            var userId = Guid.NewGuid();
            var item = new DownloadQueueItem { EventGuid = "evt-1" };
            _downloadServiceMock
                .Setup(s => s.GetUserQueueAsync(userId))
                .ReturnsAsync(new List<DownloadQueueItem> { item });

            var controller = CreateController(new Claim(JellyfinUserIdClaimType, userId.ToString("N")));

            var result = await controller.GetDownloads();

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Same(item, Assert.Single(Assert.IsType<List<DownloadQueueItem>>(ok.Value)));
        }

        [Fact]
        public async Task GetDownloads_ReturnsUnauthorized_WhenOnlyNameIdentifierClaimPresent()
        {
            var controller = CreateController(new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()));

            var result = await controller.GetDownloads();

            Assert.IsType<UnauthorizedResult>(result);
            _downloadServiceMock.Verify(
                s => s.GetUserQueueAsync(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task GetDownloads_ReturnsUnauthorized_WhenNoClaimsPresent()
        {
            var controller = CreateController();

            var result = await controller.GetDownloads();

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task EnqueueDownload_ReturnsUnauthorized_WhenOnlyNameIdentifierClaimPresent()
        {
            var controller = CreateController(new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()));

            var result = await controller.EnqueueDownload("evt-1", CancellationToken.None);

            Assert.IsType<UnauthorizedResult>(result);
            _downloadServiceMock.Verify(
                s => s.EnqueueAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task EnqueueDownload_ReturnsBadRequest_ForEmptyEventGuid()
        {
            var controller = CreateController(new Claim(JellyfinUserIdClaimType, Guid.NewGuid().ToString("N")));

            var result = await controller.EnqueueDownload("   ", CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task EnqueueDownload_ReturnsAccepted_WhenServiceResolvesItem()
        {
            var userId = Guid.NewGuid();
            var item = new DownloadQueueItem { EventGuid = "evt-1" };
            _downloadServiceMock
                .Setup(s => s.EnqueueAsync(userId, "evt-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(item);

            var controller = CreateController(new Claim(JellyfinUserIdClaimType, userId.ToString("N")));

            var result = await controller.EnqueueDownload("evt-1", CancellationToken.None);

            var accepted = Assert.IsType<AcceptedResult>(result);
            Assert.Same(item, accepted.Value);
        }

        [Fact]
        public async Task EnqueueDownload_ReturnsNotFound_WhenServiceCannotResolveEvent()
        {
            var userId = Guid.NewGuid();
            _downloadServiceMock
                .Setup(s => s.EnqueueAsync(userId, "missing", It.IsAny<CancellationToken>()))
                .ReturnsAsync((DownloadQueueItem?)null);

            var controller = CreateController(new Claim(JellyfinUserIdClaimType, userId.ToString("N")));

            var result = await controller.EnqueueDownload("missing", CancellationToken.None);

            Assert.IsType<NotFoundObjectResult>(result);
        }
    }
}
