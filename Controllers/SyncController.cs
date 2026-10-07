using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaCccDe.Api;
using Jellyfin.Plugin.MediaCccDe.Models;
using Jellyfin.Plugin.MediaCccDe.Services;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediaCccDe.Controllers
{
    [ApiController]
    [Route("media_ccc/sync")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public class SyncController : ControllerBase
    {
        private readonly ILogger<SyncController> _logger;
        private readonly ISyncLogger _syncLogger;
        private readonly ISyncTrigger _syncTrigger;

        public SyncController(
            ILogger<SyncController> logger,
            ISyncLogger syncLogger,
            IMediaCccApiClient apiClient,
            ISyncTrigger syncTrigger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _syncLogger = syncLogger ?? throw new ArgumentNullException(nameof(syncLogger));
            _ = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _syncTrigger = syncTrigger ?? throw new ArgumentNullException(nameof(syncTrigger));
        }

        [HttpPost("trigger")]
        [ProducesResponseType(202)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        public async Task<IActionResult> TriggerSync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Manual sync triggered by user");
            await _syncTrigger.TriggerSyncAsync(cancellationToken).ConfigureAwait(false);
            return Accepted();
        }

        [HttpGet("status")]
        [ProducesResponseType(typeof(IReadOnlyList<SyncLogEntry>), 200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        public IActionResult GetSyncStatus()
        {
            var history = _syncLogger.GetSyncHistory(null);
            return Ok(history);
        }

        [HttpGet("history")]
        [ProducesResponseType(typeof(IReadOnlyList<SyncLogEntry>), 200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        public IActionResult GetSyncHistory([FromQuery] string? conferenceAcronym = null)
        {
            var history = _syncLogger.GetSyncHistory(conferenceAcronym);
            return Ok(history);
        }

        [HttpDelete("history")]
        [ProducesResponseType(204)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        public async Task<IActionResult> ClearSyncHistory()
        {
            _syncLogger.ClearHistory();
            await _syncLogger.PersistAsync().ConfigureAwait(false);
            return NoContent();
        }
    }
}
