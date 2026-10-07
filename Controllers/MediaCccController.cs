using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaCccDe.Api;
using Jellyfin.Plugin.MediaCccDe.Models;
using Jellyfin.Plugin.MediaCccDe.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediaCccDe.Controllers
{
    /// <summary>
    /// API controller for browsing MediaCCC conferences and events.
    /// Provides endpoints for the browse page UI.
    /// </summary>
    [ApiController]
    [Route("media_ccc")]
    [Authorize]
    public class MediaCccController : ControllerBase
    {
        private readonly ILogger<MediaCccController> _logger;
        private readonly IMediaCccApiClient _apiClient;
        private readonly IUserDataManager _userDataManager;

        public MediaCccController(
            ILogger<MediaCccController> logger,
            IMediaCccApiClient apiClient,
            IUserDataManager userDataManager)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _userDataManager = userDataManager ?? throw new ArgumentNullException(nameof(userDataManager));
        }

        /// <summary>
        /// Get all conferences from media.ccc.de.
        /// </summary>
        /// <returns>List of conferences.</returns>
        [HttpGet("conferences")]
        [ProducesResponseType(typeof(List<ConferenceDto>), 200)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> GetConferences(CancellationToken cancellationToken)
        {
            try
            {
                var conferences = await _apiClient.GetConferencesAsync(cancellationToken).ConfigureAwait(false);
                return Ok(conferences);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch conferences from API");
                return StatusCode(500, new { error = "Failed to fetch conferences" });
            }
        }

        /// <summary>
        /// Get events for a specific conference.
        /// </summary>
        /// <param name="conferenceId">Conference ID.</param>
        /// <returns>List of events.</returns>
        [HttpGet("conferences/{conferenceId}/events")]
        [ProducesResponseType(typeof(EventDto[]), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> GetConferenceEvents(string conferenceId, CancellationToken cancellationToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(conferenceId))
                {
                    return BadRequest(new { error = "Conference identifier cannot be empty" });
                }

                var events = await _apiClient.GetEventsAsync(conferenceId, cancellationToken).ConfigureAwait(false);
                return Ok(events);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Invalid conference identifier: {ConferenceId}", conferenceId);
                return BadRequest(new { error = ex.Message });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch events for conference {ConferenceId}", conferenceId);
                return StatusCode(500, new { error = "Failed to fetch events" });
            }
        }

        /// <summary>
        /// Get a specific event by GUID.
        /// </summary>
        /// <param name="guid">Event GUID.</param>
        /// <returns>Event details.</returns>
        [HttpGet("events/{guid}")]
        [ProducesResponseType(typeof(EventDto), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(404)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> GetEvent(string guid, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(guid))
            {
                return BadRequest(new { error = "Event GUID cannot be empty" });
            }

            try
            {
                var eventDto = await _apiClient.GetEventAsync(guid, cancellationToken).ConfigureAwait(false);
                if (eventDto == null)
                {
                    return NotFound(new { error = $"Event not found: {guid}" });
                }
                return Ok(eventDto);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch event {Guid}", guid);
                return StatusCode(500, new { error = "Failed to fetch event" });
            }
        }

        /// <summary>
        /// Get recent events.
        /// </summary>
        /// <param name="limit">Maximum number of events to return.</param>
        /// <returns>List of recent events.</returns>
        [HttpGet("events/recent")]
        [ProducesResponseType(typeof(EventDto[]), 200)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> GetRecentEvents([FromQuery] int? limit, CancellationToken cancellationToken)
        {
            try
            {
                var events = await _apiClient.GetRecentAsync(limit, cancellationToken).ConfigureAwait(false);
                return Ok(events);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch recent events");
                return StatusCode(500, new { error = "Failed to fetch recent events" });
            }
        }

        /// <summary>
        /// Add an event to the user's watchlist.
        /// </summary>
        /// <param name="eventGuid">Event GUID to add.</param>
        /// <returns>Success or error status.</returns>
        [HttpPost("watchlist/{eventGuid}")]
        [ProducesResponseType(200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> AddToWatchlist(string eventGuid)
        {
            var userId = GetUserGuid();
            if (userId == null)
            {
                return Unauthorized();
            }

            await _userDataManager.EnsureLoadedAsync(userId.Value).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(eventGuid))
            {
                return BadRequest(new { error = "Event GUID cannot be empty" });
            }

            if (_userDataManager.IsOnWatchlist(userId.Value, eventGuid))
            {
                return Ok(); // Already on watchlist, idempotent
            }

            _userDataManager.AddToWatchlist(userId.Value, eventGuid);
            await _userDataManager.PersistAsync(userId.Value).ConfigureAwait(false);
            
            return Ok();
        }

        /// <summary>
        /// Remove an event from the user's watchlist.
        /// </summary>
        /// <param name="eventGuid">Event GUID to remove.</param>
        /// <returns>Success or error status.</returns>
        [HttpDelete("watchlist/{eventGuid}")]
        [ProducesResponseType(200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> RemoveFromWatchlist(string eventGuid)
        {
            var userId = GetUserGuid();
            if (userId == null)
            {
                return Unauthorized();
            }

            await _userDataManager.EnsureLoadedAsync(userId.Value).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(eventGuid))
            {
                return BadRequest(new { error = "Event GUID cannot be empty" });
            }

            _userDataManager.RemoveFromWatchlist(userId.Value, eventGuid);
            await _userDataManager.PersistAsync(userId.Value).ConfigureAwait(false);
            
            return Ok();
        }

        /// <summary>
        /// Get the user's watchlist.
        /// </summary>
        /// <returns>List of event GUIDs on the watchlist.</returns>
        [HttpGet("watchlist")]
        [ProducesResponseType(typeof(List<string>), 200)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> GetWatchlist()
        {
            var userId = GetUserGuid();
            if (userId == null)
            {
                return Unauthorized();
            }

            await _userDataManager.EnsureLoadedAsync(userId.Value).ConfigureAwait(false);

            var watchlist = _userDataManager.GetWatchlist(userId.Value);
            return Ok(watchlist);
        }

        /// <summary>
        /// Get the user's preferred audio languages.
        /// </summary>
        /// <returns>Ordered list of preferred audio languages.</returns>
        [HttpGet("languages/audio")]
        [ProducesResponseType(typeof(List<string>), 200)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> GetPreferredAudioLanguages()
        {
            var userId = GetUserGuid();
            if (userId == null)
            {
                return Unauthorized();
            }

            await _userDataManager.EnsureLoadedAsync(userId.Value).ConfigureAwait(false);

            var languages = _userDataManager.GetPreferredAudioLanguages(userId.Value);
            return Ok(languages);
        }

        /// <summary>
        /// Set the user's preferred audio languages.
        /// </summary>
        /// <param name="languages">Ordered list of language codes.</param>
        /// <returns>Success or error status.</returns>
        [HttpPost("languages/audio")]
        [ProducesResponseType(200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> SetPreferredAudioLanguages([FromBody] List<string> languages)
        {
            var userId = GetUserGuid();
            if (userId == null)
            {
                return Unauthorized();
            }

            await _userDataManager.EnsureLoadedAsync(userId.Value).ConfigureAwait(false);

            if (languages == null)
            {
                return BadRequest(new { error = "Languages list cannot be null" });
            }

            _userDataManager.SetPreferredAudioLanguages(userId.Value, languages);
            await _userDataManager.PersistAsync(userId.Value).ConfigureAwait(false);

            return Ok();
        }

        /// <summary>
        /// Get the user's preferred subtitle languages.
        /// </summary>
        /// <returns>Ordered list of preferred subtitle languages.</returns>
        [HttpGet("languages/subtitles")]
        [ProducesResponseType(typeof(List<string>), 200)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> GetPreferredSubtitleLanguages()
        {
            var userId = GetUserGuid();
            if (userId == null)
            {
                return Unauthorized();
            }

            await _userDataManager.EnsureLoadedAsync(userId.Value).ConfigureAwait(false);

            var languages = _userDataManager.GetPreferredSubtitleLanguages(userId.Value);
            return Ok(languages);
        }

        /// <summary>
        /// Set the user's preferred subtitle languages.
        /// </summary>
        /// <param name="languages">Ordered list of language codes.</param>
        /// <returns>Success or error status.</returns>
        [HttpPost("languages/subtitles")]
        [ProducesResponseType(200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> SetPreferredSubtitleLanguages([FromBody] List<string> languages)
        {
            var userId = GetUserGuid();
            if (userId == null)
            {
                return Unauthorized();
            }

            await _userDataManager.EnsureLoadedAsync(userId.Value).ConfigureAwait(false);

            if (languages == null)
            {
                return BadRequest(new { error = "Languages list cannot be null" });
            }

            _userDataManager.SetPreferredSubtitleLanguages(userId.Value, languages);
            await _userDataManager.PersistAsync(userId.Value).ConfigureAwait(false);

            return Ok();
        }

        private Guid? GetUserGuid()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim != null && Guid.TryParse(userIdClaim.Value, out var userId))
            {
                return userId;
            }
            return null;
        }
    }
}
