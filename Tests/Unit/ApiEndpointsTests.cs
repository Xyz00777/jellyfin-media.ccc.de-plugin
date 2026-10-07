using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaCccDe.Api;
using Jellyfin.Plugin.MediaCccDe.Controllers;
using Jellyfin.Plugin.MediaCccDe.Models;
using Jellyfin.Plugin.MediaCccDe.Services;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.MediaCccDe.Tests.Unit
{
    /// <summary>
    /// RED phase tests for API endpoint controllers.
    /// These tests define the expected behavior for MediaCccController and SyncController.
    /// Tests will FAIL until controllers are implemented in GREEN phase.
    /// </summary>
    public class ApiEndpointsTests
    {
        private readonly Mock<IMediaCccApiClient> _apiClientMock;
        private readonly Mock<IUserDataManager> _userDataManagerMock;
        private readonly Mock<ISyncLogger> _syncLoggerMock;
        private readonly Mock<ISyncTrigger> _syncTriggerMock;
        private readonly Mock<ILogger<MediaCccController>> _mediaCccLoggerMock;
        private readonly Mock<ILogger<SyncController>> _syncLoggerControllerMock;

        public ApiEndpointsTests()
        {
            _apiClientMock = new Mock<IMediaCccApiClient>(MockBehavior.Strict);
            _userDataManagerMock = new Mock<IUserDataManager>(MockBehavior.Loose);
            _syncLoggerMock = new Mock<ISyncLogger>(MockBehavior.Strict);
            _syncTriggerMock = new Mock<ISyncTrigger>(MockBehavior.Loose);
            _mediaCccLoggerMock = new Mock<ILogger<MediaCccController>>(MockBehavior.Loose);
            _syncLoggerControllerMock = new Mock<ILogger<SyncController>>(MockBehavior.Loose);
        }

        #region MediaCccController - GetConferences Tests

        [Fact]
        public async Task GetConferences_ReturnsOkWithConferences()
        {
            // Arrange
            var conferences = new List<ConferenceDto>
            {
                new ConferenceDto { Id = 1, Title = "37C3", Acronym = "37c3", Slug = "37c3" },
                new ConferenceDto { Id = 2, Title = "36C3", Acronym = "36c3", Slug = "36c3" }
            };

            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(conferences);

            var controller = CreateMediaCccController();

            // Act
            var result = await controller.GetConferences(CancellationToken.None);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var returnedConferences = Assert.IsType<List<ConferenceDto>>(okResult.Value);
            Assert.Equal(2, returnedConferences.Count);
        }

        [Fact]
        public async Task GetConferences_ReturnsEmptyList_WhenNoConferences()
        {
            // Arrange
            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ConferenceDto>());

            var controller = CreateMediaCccController();

            // Act
            var result = await controller.GetConferences(CancellationToken.None);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var returnedConferences = Assert.IsType<List<ConferenceDto>>(okResult.Value);
            Assert.Empty(returnedConferences);
        }

        [Fact]
        public async Task GetConferences_Returns500_OnApiException()
        {
            // Arrange
            _apiClientMock
                .Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("API unavailable"));

            var controller = CreateMediaCccController();

            // Act
            var result = await controller.GetConferences(CancellationToken.None);

            // Assert
            Assert.IsType<ObjectResult>(result);
            var objectResult = (ObjectResult)result;
            Assert.Equal(500, objectResult.StatusCode);
        }

        #endregion

        #region MediaCccController - GetConferenceEvents Tests

        [Fact]
        public async Task GetConferenceEvents_ReturnsOkWithEvents()
        {
            // Arrange
            var conferenceId = "37c3";
            var events = new EventDto[]
            {
                new EventDto { Guid = "event-1", Title = "Opening Ceremony", ConferenceId = 1 },
                new EventDto { Guid = "event-2", Title = "Closing Ceremony", ConferenceId = 1 }
            };

            _apiClientMock
                .Setup(x => x.GetEventsAsync(conferenceId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(events);

            var controller = CreateMediaCccController();

            // Act
            var result = await controller.GetConferenceEvents(conferenceId, CancellationToken.None);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var returnedEvents = Assert.IsType<EventDto[]>(okResult.Value);
            Assert.Equal(2, returnedEvents.Length);
        }

        [Fact]
        public async Task GetConferenceEvents_Returns404_ForInvalidConferenceId()
        {
            // Arrange
            var invalidConferenceId = "-1";

            _apiClientMock
                .Setup(x => x.GetEventsAsync(invalidConferenceId, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ArgumentException("Conference identifier is invalid"));

            var controller = CreateMediaCccController();

            // Act
            var result = await controller.GetConferenceEvents(invalidConferenceId, CancellationToken.None);

            // Assert
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task GetConferenceEvents_ReturnsEmptyArray_WhenNoEvents()
        {
            // Arrange
            var conferenceId = "unknown";

            _apiClientMock
                .Setup(x => x.GetEventsAsync(conferenceId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<EventDto>());

            var controller = CreateMediaCccController();

            // Act
            var result = await controller.GetConferenceEvents(conferenceId, CancellationToken.None);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var returnedEvents = Assert.IsType<EventDto[]>(okResult.Value);
            Assert.Empty(returnedEvents);
        }

        #endregion

        #region MediaCccController - GetEvent Tests

        [Fact]
        public async Task GetEvent_ReturnsOkWithEvent()
        {
            // Arrange
            var eventGuid = "37c3-12345-test-event";
            var eventDto = new EventDto
            {
                Guid = eventGuid,
                Title = "Test Event",
                Description = "A test event description",
                Length = 3600
            };

            _apiClientMock
                .Setup(x => x.GetEventAsync(eventGuid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(eventDto);

            var controller = CreateMediaCccController();

            // Act
            var result = await controller.GetEvent(eventGuid, CancellationToken.None);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var returnedEvent = Assert.IsType<EventDto>(okResult.Value);
            Assert.Equal(eventGuid, returnedEvent.Guid);
            Assert.Equal("Test Event", returnedEvent.Title);
        }

        [Fact]
        public async Task GetEvent_Returns404_ForNonExistentEvent()
        {
            // Arrange
            var nonExistentGuid = "nonexistent-guid";

            _apiClientMock
                .Setup(x => x.GetEventAsync(nonExistentGuid, It.IsAny<CancellationToken>()))
                .ReturnsAsync((EventDto?)null);

            var controller = CreateMediaCccController();

            // Act
            var result = await controller.GetEvent(nonExistentGuid, CancellationToken.None);

            // Assert
            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task GetEvent_Returns400_ForEmptyGuid()
        {
            // Arrange
            var controller = CreateMediaCccController();

            // Act
            var result = await controller.GetEvent("", CancellationToken.None);

            // Assert
            Assert.IsType<BadRequestObjectResult>(result);
        }

        #endregion

        #region MediaCccController - Watchlist Add Tests

        [Fact]
        public async Task AddToWatchlist_ReturnsOk_WhenEventAdded()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var eventGuid = "event-123";

            _userDataManagerMock
                .Setup(x => x.AddToWatchlistIfMissing(userId, eventGuid))
                .Returns(true);

            _userDataManagerMock
                .Setup(x => x.PersistAsync(userId))
                .Returns(Task.CompletedTask);

            var controller = CreateMediaCccControllerWithUser(userId);

            // Act
            var result = await controller.AddToWatchlist(eventGuid);

            // Assert
            Assert.IsType<OkResult>(result);
            _userDataManagerMock.Verify(x => x.AddToWatchlistIfMissing(userId, eventGuid), Times.Once);
            _userDataManagerMock.Verify(x => x.PersistAsync(userId), Times.Once);
        }

        [Fact]
        public async Task AddToWatchlist_Returns401_WhenUserNotAuthenticated()
        {
            // Arrange
            var controller = CreateMediaCccControllerWithoutUser();

            // Act
            var result = await controller.AddToWatchlist("event-123");

            // Assert
            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task AddToWatchlist_Returns400_ForEmptyEventGuid()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var controller = CreateMediaCccControllerWithUser(userId);

            // Act
            var result = await controller.AddToWatchlist("");

            // Assert
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task AddToWatchlist_DoesNotDuplicate()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var eventGuid = "event-123";

            _userDataManagerMock
                .Setup(x => x.AddToWatchlistIfMissing(userId, eventGuid))
                .Returns(false);

            _userDataManagerMock
                .Setup(x => x.PersistAsync(userId))
                .Returns(Task.CompletedTask);

            var controller = CreateMediaCccControllerWithUser(userId);

            // Act
            var result = await controller.AddToWatchlist(eventGuid);

            // Assert - Should return Ok but not write a duplicate
            Assert.IsType<OkResult>(result);
            _userDataManagerMock.Verify(x => x.PersistAsync(userId), Times.Never);
        }

        #endregion

        #region MediaCccController - Watchlist Remove Tests

        [Fact]
        public async Task RemoveFromWatchlist_ReturnsOk_WhenEventRemoved()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var eventGuid = "event-123";

            _userDataManagerMock
                .Setup(x => x.RemoveFromWatchlist(userId, eventGuid));

            _userDataManagerMock
                .Setup(x => x.PersistAsync(userId))
                .Returns(Task.CompletedTask);

            var controller = CreateMediaCccControllerWithUser(userId);

            // Act
            var result = await controller.RemoveFromWatchlist(eventGuid);

            // Assert
            Assert.IsType<OkResult>(result);
            _userDataManagerMock.Verify(x => x.RemoveFromWatchlist(userId, eventGuid), Times.Once);
        }

        [Fact]
        public async Task RemoveFromWatchlist_Returns401_WhenUserNotAuthenticated()
        {
            // Arrange
            var controller = CreateMediaCccControllerWithoutUser();

            // Act
            var result = await controller.RemoveFromWatchlist("event-123");

            // Assert
            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task RemoveFromWatchlist_Returns400_ForEmptyEventGuid()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var controller = CreateMediaCccControllerWithUser(userId);

            // Act
            var result = await controller.RemoveFromWatchlist("");

            // Assert
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task RemoveFromWatchlist_ReturnsOk_EvenWhenEventNotOnList()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var eventGuid = "nonexistent-event";

            _userDataManagerMock
                .Setup(x => x.RemoveFromWatchlist(userId, eventGuid));

            _userDataManagerMock
                .Setup(x => x.PersistAsync(userId))
                .Returns(Task.CompletedTask);

            var controller = CreateMediaCccControllerWithUser(userId);

            // Act
            var result = await controller.RemoveFromWatchlist(eventGuid);

            // Assert - Should return Ok (idempotent operation)
            Assert.IsType<OkResult>(result);
        }

        #endregion

        #region MediaCccController - GetWatchlist Tests

        [Fact]
        public async Task GetWatchlist_ReturnsOkWithWatchlist()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var watchlist = new List<string> { "event-1", "event-2", "event-3" };

            _userDataManagerMock
                .Setup(x => x.GetWatchlist(userId))
                .Returns(watchlist);

            var controller = CreateMediaCccControllerWithUser(userId);

            // Act
            var result = await controller.GetWatchlist();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var returnedWatchlist = Assert.IsType<List<string>>(okResult.Value);
            Assert.Equal(3, returnedWatchlist.Count);
        }

        [Fact]
        public async Task GetWatchlist_Returns401_WhenUserNotAuthenticated()
        {
            // Arrange
            var controller = CreateMediaCccControllerWithoutUser();

            // Act
            var result = await controller.GetWatchlist();

            // Assert
            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task GetWatchlist_ReturnsEmptyList_ForNewUser()
        {
            // Arrange
            var userId = Guid.NewGuid();

            _userDataManagerMock
                .Setup(x => x.GetWatchlist(userId))
                .Returns(new List<string>());

            var controller = CreateMediaCccControllerWithUser(userId);

            // Act
            var result = await controller.GetWatchlist();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var returnedWatchlist = Assert.IsType<List<string>>(okResult.Value);
            Assert.Empty(returnedWatchlist);
        }

        #endregion

        #region SyncController - TriggerSync Tests

        [Fact]
        public async Task TriggerSync_Returns202Accepted_ForAdminUser()
        {
            // Arrange
            var controller = CreateSyncControllerAsAdmin();

            // Act
            var result = await controller.TriggerSync();

            // Assert
            Assert.IsType<AcceptedResult>(result);
        }

        [Fact]
        public async Task TriggerSync_ActuallyInvokes_the_sync_trigger()
        {
            var controller = CreateSyncControllerAsAdmin();

            await controller.TriggerSync();

            _syncTriggerMock.Verify(
                t => t.TriggerSyncAsync(It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task TriggerSync_Returns202_ForAnyAuthenticatedUser()
        {
            // Authorization is handled by [Authorize(Policy = "Elevation")] at class level,
            // not by manual IsAdmin() checks inside the method.
            var userId = Guid.NewGuid();
            var controller = CreateSyncControllerWithUser(userId, isAdmin: false);

            var result = await controller.TriggerSync();

            Assert.IsType<AcceptedResult>(result);
        }

        [Fact]
        public void GetSyncHistory_ReturnsData_ForAnyAuthenticatedUser()
        {
            // Authorization handled by class-level [Authorize(Policy = "Elevation")]
            _syncLoggerMock
                .Setup(x => x.GetSyncHistory(null))
                .Returns(new List<SyncLogEntry>().AsReadOnly());

            var controller = CreateSyncControllerWithoutUser();

            var result = controller.GetSyncHistory();

            Assert.IsType<OkObjectResult>(result);
        }

        #endregion

        #region SyncController - GetSyncStatus Tests

        [Fact]
        public void GetSyncStatus_ReturnsOkWithStatus()
        {
            // Arrange
            var history = new List<SyncLogEntry>
            {
                new SyncLogEntry
                {
                    Timestamp = DateTime.UtcNow,
                    ConferenceAcronym = "all",
                    Status = SyncStatus.Completed,
                    EventsProcessed = 10,
                    FilesCreated = 5
                }
            };

            _syncLoggerMock
                .Setup(x => x.GetSyncHistory(null))
                .Returns(history.AsReadOnly());

            var controller = CreateSyncControllerAsAdmin();

            // Act
            var result = controller.GetSyncStatus();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }

        [Fact]
        public void GetSyncStatus_ReturnsEmptyHistory_WhenNoSyncs()
        {
            // Arrange
            _syncLoggerMock
                .Setup(x => x.GetSyncHistory(null))
                .Returns(new List<SyncLogEntry>().AsReadOnly());

            var controller = CreateSyncControllerAsAdmin();

            // Act
            var result = controller.GetSyncStatus();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var history = Assert.IsAssignableFrom<IReadOnlyList<SyncLogEntry>>(okResult.Value);
            Assert.Empty(history);
        }

        [Fact]
        public void GetSyncStatus_ReturnsOk_ForAnyAuthenticatedUser()
        {
            // Authorization handled by class-level [Authorize(Policy = "Elevation")]
            var userId = Guid.NewGuid();
            _syncLoggerMock
                .Setup(x => x.GetSyncHistory(null))
                .Returns(new List<SyncLogEntry>().AsReadOnly());

            var controller = CreateSyncControllerWithUser(userId, isAdmin: false);

            var result = controller.GetSyncStatus();

            Assert.IsType<OkObjectResult>(result);
        }

        #endregion

        #region SyncController - GetSyncHistory Tests

        [Fact]
        public void GetSyncHistory_ReturnsOkWithHistory()
        {
            // Arrange
            var history = new List<SyncLogEntry>
            {
                new SyncLogEntry
                {
                    Timestamp = DateTime.UtcNow.AddDays(-1),
                    ConferenceAcronym = "37c3",
                    Status = SyncStatus.Completed,
                    EventsProcessed = 100,
                    FilesCreated = 50
                },
                new SyncLogEntry
                {
                    Timestamp = DateTime.UtcNow.AddDays(-2),
                    ConferenceAcronym = "36c3",
                    Status = SyncStatus.Completed,
                    EventsProcessed = 80,
                    FilesCreated = 40
                }
            };

            _syncLoggerMock
                .Setup(x => x.GetSyncHistory(null))
                .Returns(history.AsReadOnly());

            var controller = CreateSyncControllerAsAdmin();

            // Act
            var result = controller.GetSyncHistory();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var returnedHistory = Assert.IsAssignableFrom<IReadOnlyList<SyncLogEntry>>(okResult.Value);
            Assert.Equal(2, returnedHistory.Count);
        }

        [Fact]
        public void GetSyncHistory_FiltersByConference_WhenAcronymProvided()
        {
            // Arrange
            var acronym = "37c3";
            var filteredHistory = new List<SyncLogEntry>
            {
                new SyncLogEntry
                {
                    Timestamp = DateTime.UtcNow,
                    ConferenceAcronym = acronym,
                    Status = SyncStatus.Completed
                }
            };

            _syncLoggerMock
                .Setup(x => x.GetSyncHistory(acronym))
                .Returns(filteredHistory.AsReadOnly());

            var controller = CreateSyncControllerAsAdmin();

            // Act
            var result = controller.GetSyncHistory(acronym);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var returnedHistory = Assert.IsAssignableFrom<IReadOnlyList<SyncLogEntry>>(okResult.Value);
            Assert.Single(returnedHistory);
            Assert.Equal(acronym, returnedHistory[0].ConferenceAcronym);
        }

        [Fact]
        public void GetSyncHistory_ReturnsOk_ForAnyAuthenticatedUser()
        {
            // Authorization handled by class-level [Authorize(Policy = "Elevation")]
            var userId = Guid.NewGuid();
            _syncLoggerMock
                .Setup(x => x.GetSyncHistory(null))
                .Returns(new List<SyncLogEntry>().AsReadOnly());

            var controller = CreateSyncControllerWithUser(userId, isAdmin: false);

            var result = controller.GetSyncHistory();

            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public async Task TriggerSync_Returns202_WithoutManualAuthCheck()
        {
            // Authorization handled by class-level [Authorize(Policy = "Elevation")]
            var controller = CreateSyncControllerWithoutUser();

            var result = await controller.TriggerSync();

            Assert.IsType<AcceptedResult>(result);
        }

        #endregion

        #region Authorization Attribute Tests

        [Fact]
        public void MediaCccController_HasAuthorizeAttribute()
        {
            var attr = typeof(MediaCccController)
                .GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true)
                .FirstOrDefault();
            // Prevent anonymous users from using Jellyfin as open proxy to media.ccc.de API
            Assert.NotNull(attr);
        }

        [Fact]
        public void GetConferences_requires_authentication()
        {
            var method = typeof(MediaCccController).GetMethod("GetConferences");
            var authAttr = method?.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true).FirstOrDefault()
                ?? typeof(MediaCccController).GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true).FirstOrDefault();
            Assert.NotNull(authAttr);
        }

        [Fact]
        public void GetConferenceEvents_requires_authentication()
        {
            var method = typeof(MediaCccController).GetMethod("GetConferenceEvents");
            var authAttr = method?.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true).FirstOrDefault()
                ?? typeof(MediaCccController).GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true).FirstOrDefault();
            Assert.NotNull(authAttr);
        }

        [Fact]
        public void GetEvent_requires_authentication()
        {
            var method = typeof(MediaCccController).GetMethod("GetEvent");
            var authAttr = method?.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true).FirstOrDefault()
                ?? typeof(MediaCccController).GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true).FirstOrDefault();
            Assert.NotNull(authAttr);
        }

        [Fact]
        public void GetRecentEvents_requires_authentication()
        {
            var method = typeof(MediaCccController).GetMethod("GetRecentEvents");
            var authAttr = method?.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true).FirstOrDefault()
                ?? typeof(MediaCccController).GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true).FirstOrDefault();
            Assert.NotNull(authAttr);
        }

        [Fact]
        public void SyncController_DoesNotHaveRedundantIsAdminMethod()
        {
            var method = typeof(SyncController).GetMethod("IsAdmin",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.Null(method);
        }

        [Fact]
        public void MediaCccController_HasRouteAttribute()
        {
            // Arrange & Act
            var controllerType = typeof(MediaCccController);
            var routeAttribute = Attribute.GetCustomAttribute(controllerType, typeof(RouteAttribute)) as RouteAttribute;

            // Assert
            Assert.NotNull(routeAttribute);
            Assert.Equal("media_ccc", routeAttribute.Template);
        }

        [Fact]
        public void SyncController_HasRouteAttribute()
        {
            // Arrange & Act
            var controllerType = typeof(SyncController);
            var routeAttribute = Attribute.GetCustomAttribute(controllerType, typeof(RouteAttribute)) as RouteAttribute;

            // Assert
            Assert.NotNull(routeAttribute);
            Assert.Equal("media_ccc/sync", routeAttribute.Template);
        }

        [Fact]
        public void SyncController_TriggerSync_RequiresAuthorizationPolicy()
        {
            // Arrange & Act
            var controllerType = typeof(SyncController);
            var authorizeAttribute = Attribute.GetCustomAttribute(controllerType, typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute)) as Microsoft.AspNetCore.Authorization.AuthorizeAttribute;

            // Assert - SyncController has class-level Authorize attribute with Elevation policy
            Assert.NotNull(authorizeAttribute);
            Assert.Equal(Policies.RequiresElevation, authorizeAttribute.Policy);
        }

        #endregion

        #region Helper Methods

        private MediaCccController CreateMediaCccController()
        {
            var controller = new MediaCccController(
                _mediaCccLoggerMock.Object,
                _apiClientMock.Object,
                _userDataManagerMock.Object);

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            };

            return controller;
        }

        private MediaCccController CreateMediaCccControllerWithUser(Guid userId)
        {
            var controller = CreateMediaCccController();

            var claims = new List<Claim>
            {
                new Claim("Jellyfin-UserId", userId.ToString("N"))
            };
            var identity = new ClaimsIdentity(claims, "Test");
            var principal = new ClaimsPrincipal(identity);

            controller.ControllerContext.HttpContext.User = principal;

            return controller;
        }

        private MediaCccController CreateMediaCccControllerWithoutUser()
        {
            var controller = CreateMediaCccController();
            controller.ControllerContext.HttpContext.User = new ClaimsPrincipal();
            return controller;
        }

        private SyncController CreateSyncController()
        {
            var controller = new SyncController(
                _syncLoggerControllerMock.Object,
                _syncLoggerMock.Object,
                _apiClientMock.Object,
                _syncTriggerMock.Object);

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            };

            return controller;
        }

        private SyncController CreateSyncControllerAsAdmin()
        {
            var controller = CreateSyncController();
            var userId = Guid.NewGuid();

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Role, "Admin")
            };
            var identity = new ClaimsIdentity(claims, "Test");
            var principal = new ClaimsPrincipal(identity);

            controller.ControllerContext.HttpContext.User = principal;

            return controller;
        }

        private SyncController CreateSyncControllerWithUser(Guid userId, bool isAdmin)
        {
            var controller = CreateSyncController();

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString())
            };

            if (isAdmin)
            {
                claims.Add(new Claim(ClaimTypes.Role, "Admin"));
            }

            var identity = new ClaimsIdentity(claims, "Test");
            var principal = new ClaimsPrincipal(identity);

            controller.ControllerContext.HttpContext.User = principal;

            return controller;
        }

        private SyncController CreateSyncControllerWithoutUser()
        {
            var controller = CreateSyncController();
            controller.ControllerContext.HttpContext.User = new ClaimsPrincipal();
            return controller;
        }

        #endregion
    }
}
