using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaCccDe.Api;
using Jellyfin.Plugin.MediaCccDe.Controllers;
using Jellyfin.Plugin.MediaCccDe.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.MediaCccDe.Tests.Unit
{
    public class ControllerUserResolutionTests
    {
        private const string JellyfinUserIdClaimType = "Jellyfin-UserId";

        private readonly Mock<IMediaCccApiClient> _apiClientMock = new Mock<IMediaCccApiClient>(MockBehavior.Loose);
        private readonly Mock<IUserDataManager> _userDataManagerMock = new Mock<IUserDataManager>(MockBehavior.Loose);
        private readonly Mock<ILogger<MediaCccController>> _loggerMock = new Mock<ILogger<MediaCccController>>(MockBehavior.Loose);

        private MediaCccController CreateController(params Claim[] claims)
        {
            var controller = new MediaCccController(
                _loggerMock.Object,
                _apiClientMock.Object,
                _userDataManagerMock.Object);

            var identity = new ClaimsIdentity(claims, "Test");
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(identity)
                }
            };

            return controller;
        }

        [Fact]
        public async Task GetWatchlist_ReturnsOk_WhenJellyfinUserIdClaimPresent()
        {
            // Jellyfin's CustomAuthenticationHandler emits the user id under
            // InternalClaimTypes.UserId ("Jellyfin-UserId") in "N" format.
            var userId = Guid.NewGuid();
            var controller = CreateController(new Claim(JellyfinUserIdClaimType, userId.ToString("N")));

            _userDataManagerMock.Setup(m => m.GetWatchlist(It.IsAny<Guid>())).Returns(new System.Collections.Generic.List<string>());

            var result = await controller.GetWatchlist();

            Assert.IsType<OkObjectResult>(result);
            _userDataManagerMock.Verify(m => m.GetWatchlist(userId), Times.Once);
        }

        [Fact]
        public async Task GetWatchlist_ReturnsUnauthorized_WhenOnlyNameIdentifierClaimPresent()
        {
            var controller = CreateController(new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()));

            var result = await controller.GetWatchlist();

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task GetWatchlist_ReturnsUnauthorized_WhenNoClaimsPresent()
        {
            var controller = CreateController();

            var result = await controller.GetWatchlist();

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task GetWatchlist_ReturnsUnauthorized_WhenJellyfinUserIdClaimIsNotAGuid()
        {
            var controller = CreateController(new Claim(JellyfinUserIdClaimType, "not-a-guid"));

            var result = await controller.GetWatchlist();

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task GetPreferredAudioLanguages_ReturnsOk_WhenJellyfinUserIdClaimPresent()
        {
            var userId = Guid.NewGuid();
            var controller = CreateController(new Claim(JellyfinUserIdClaimType, userId.ToString("N")));

            _userDataManagerMock.Setup(m => m.GetPreferredAudioLanguages(It.IsAny<Guid>()))
                .Returns(new System.Collections.Generic.List<string>());

            var result = await controller.GetPreferredAudioLanguages();

            Assert.IsType<OkObjectResult>(result);
            _userDataManagerMock.Verify(m => m.GetPreferredAudioLanguages(userId), Times.Once);
        }

        [Fact]
        public async Task GetPreferredSubtitleLanguages_ReturnsUnauthorized_WhenOnlyNameIdentifierClaimPresent()
        {
            var controller = CreateController(new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()));

            var result = await controller.GetPreferredSubtitleLanguages();

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task SetPreferredAudioLanguages_Rejects_OversizedList()
        {
            var controller = CreateController(new Claim(JellyfinUserIdClaimType, Guid.NewGuid().ToString("N")));

            var result = await controller.SetPreferredAudioLanguages(
                Enumerable.Range(0, 51).Select(i => "lang" + i).ToList());

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task SetPreferredAudioLanguages_Rejects_NullList()
        {
            var controller = CreateController(new Claim(JellyfinUserIdClaimType, Guid.NewGuid().ToString("N")));

            var result = await controller.SetPreferredAudioLanguages(null!);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task SetPreferredSubtitleLanguages_Rejects_OverlongCode()
        {
            var controller = CreateController(new Claim(JellyfinUserIdClaimType, Guid.NewGuid().ToString("N")));

            var result = await controller.SetPreferredSubtitleLanguages(new List<string> { new('x', 17) });

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task SetPreferredSubtitleLanguages_Trims_and_Deduplicates()
        {
            var userId = Guid.NewGuid();
            var controller = CreateController(new Claim(JellyfinUserIdClaimType, userId.ToString("N")));

            List<string>? persisted = null;
            _userDataManagerMock
                .Setup(m => m.SetPreferredSubtitleLanguages(userId, It.IsAny<List<string>>()))
                .Callback<Guid, List<string>>((_, l) => persisted = l);

            var result = await controller.SetPreferredSubtitleLanguages(new List<string> { " deu ", "DEU", "eng" });

            Assert.IsType<OkResult>(result);
            Assert.Equal(new List<string> { "deu", "eng" }, persisted);
        }
    }
}
