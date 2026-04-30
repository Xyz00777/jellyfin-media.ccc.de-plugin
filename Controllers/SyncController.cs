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
    [ApiController]
    [Route("media_ccc/sync")]
    [Authorize(Policy = "Elevation")]
    public class SyncController : ControllerBase
    {
        private readonly ILogger<SyncController> _logger;
        private readonly ISyncLogger _syncLogger;
        private readonly IMediaCccApiClient _apiClient;
        private readonly PluginConfiguration _configuration;

        public SyncController(
            ILogger<SyncController> logger,
            ISyncLogger syncLogger,
            IMediaCccApiClient apiClient,
            PluginConfiguration configuration)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _syncLogger = syncLogger ?? throw new ArgumentNullException(nameof(syncLogger));
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        [HttpPost("trigger")]
        [ProducesResponseType(202)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        public IActionResult TriggerSync()
        {
            if (!IsAdmin())
            {
                return Forbid();
            }

            _logger.LogInformation("Manual sync triggered by user");
            return Accepted();
        }

        [HttpGet("status")]
        [ProducesResponseType(typeof(IReadOnlyList<SyncLogEntry>), 200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        public IActionResult GetSyncStatus()
        {
            if (!IsAdmin())
            {
                return Forbid();
            }

            var history = _syncLogger.GetSyncHistory(null);
            return Ok(history);
        }

        [HttpGet("history")]
        [ProducesResponseType(typeof(IReadOnlyList<SyncLogEntry>), 200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        public IActionResult GetSyncHistory([FromQuery] string? conferenceAcronym = null)
        {
            if (!IsAdmin())
            {
                return Forbid();
            }

            var history = _syncLogger.GetSyncHistory(conferenceAcronym);
            return Ok(history);
        }

        [HttpDelete("history")]
        [ProducesResponseType(204)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        public IActionResult ClearSyncHistory()
        {
            if (!IsAdmin())
            {
                return Forbid();
            }

            _syncLogger.ClearHistory();
            return NoContent();
        }

        private bool IsAdmin()
        {
            return User.IsInRole("Admin");
        }
    }
}