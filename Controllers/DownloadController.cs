using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaCccDe.Models;
using Jellyfin.Plugin.MediaCccDe.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.MediaCccDe.Controllers
{
    [ApiController]
    [Route("media_ccc/downloads")]
    [Authorize]
    public sealed class DownloadController : ControllerBase
    {
        private readonly IWatchlistDownloadService _downloadService;

        public DownloadController(IWatchlistDownloadService downloadService)
        {
            _downloadService = downloadService ?? throw new ArgumentNullException(nameof(downloadService));
        }

        [HttpGet]
        [ProducesResponseType(typeof(IReadOnlyList<DownloadQueueItem>), 200)]
        public async Task<IActionResult> GetDownloads()
        {
            var userId = GetUserGuid();
            if (userId == null)
            {
                return Unauthorized();
            }

            return Ok(await _downloadService.GetUserQueueAsync(userId.Value).ConfigureAwait(false));
        }

        [HttpPost("{eventGuid}")]
        [ProducesResponseType(typeof(DownloadQueueItem), 202)]
        [ProducesResponseType(400)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> EnqueueDownload(string eventGuid, CancellationToken cancellationToken)
        {
            var userId = GetUserGuid();
            if (userId == null)
            {
                return Unauthorized();
            }

            if (string.IsNullOrWhiteSpace(eventGuid))
            {
                return BadRequest(new { error = "Event GUID cannot be empty" });
            }

            var item = await _downloadService
                .EnqueueAsync(userId.Value, eventGuid, cancellationToken)
                .ConfigureAwait(false);
            return item == null ? NotFound(new { error = "Event or recording not found" }) : Accepted(item);
        }

        private Guid? GetUserGuid()
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier);
            return claim != null && Guid.TryParse(claim.Value, out var userId) ? userId : null;
        }
    }
}
