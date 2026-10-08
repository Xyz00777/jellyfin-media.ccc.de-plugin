using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaCccDe.Api;
using Jellyfin.Plugin.MediaCccDe.Models;
using Jellyfin.Plugin.MediaCccDe.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.MediaCccDe.Controllers
{
    [ApiController]
    [Route("media_ccc/watchlist/page")]
    [AllowAnonymous]
    public sealed class WatchlistController : Controller
    {
        private readonly IUserDataManager _userDataManager;
        private readonly IMediaCccApiClient _apiClient;
        private readonly IWatchlistDownloadService _downloadService;
        private readonly UserPageSession _userPageSession;
        private readonly JellyfinIdentityVerifier _identityVerifier;

        public WatchlistController(
            IUserDataManager userDataManager,
            IMediaCccApiClient apiClient,
            IWatchlistDownloadService downloadService,
            IServiceProvider serviceProvider,
            JellyfinIdentityVerifier identityVerifier)
        {
            _userDataManager = userDataManager ?? throw new ArgumentNullException(nameof(userDataManager));
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _downloadService = downloadService ?? throw new ArgumentNullException(nameof(downloadService));
            ArgumentNullException.ThrowIfNull(serviceProvider);
            _userPageSession = serviceProvider.GetRequiredService<UserPageSession>();
            _identityVerifier = identityVerifier ?? throw new ArgumentNullException(nameof(identityVerifier));
        }

        [HttpGet]
        public async Task<IActionResult> Get(CancellationToken cancellationToken)
        {
            var language = PluginLanguage.Resolve(Request);
            var translations = Translations.For(language);
            var identity = await _userPageSession.ResolveAsync(Request, cancellationToken).ConfigureAwait(false);
            if (identity is null)
            {
                var error = Request.Cookies.ContainsKey("ccc_media_user")
                    ? translations["userSettings.error.sessionExpired"]
                    : null;
                return Content(WatchlistPageHtml.Render(null, null, null, error, language, translations), "text/html");
            }

            try
            {
                await _userDataManager.EnsureLoadedAsync(identity.Id).ConfigureAwait(false);
                var events = new List<WatchlistPageItem>();
                foreach (var eventGuid in _userDataManager.GetWatchlist(identity.Id))
                {
                    var item = await _apiClient.GetEventAsync(eventGuid, cancellationToken).ConfigureAwait(false);
                    if (item is not null)
                    {
                        var conference = await _apiClient.GetConferenceAsync(item.ConferenceId.ToString(), cancellationToken).ConfigureAwait(false);
                        events.Add(new WatchlistPageItem(item.Guid, item.Title, conference?.Acronym));
                    }
                }

                var queue = await _downloadService.GetUserQueueAsync(identity.Id).ConfigureAwait(false);
                return Content(WatchlistPageHtml.Render(identity.Name, events, queue, null, language, translations), "text/html");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return Content(
                    WatchlistPageHtml.Render(identity.Name, null, null, translations["watchlist.error.load"], language, translations),
                    "text/html");
            }
        }

        [HttpPost("identify")]
        public async Task<IActionResult> Identify(CancellationToken cancellationToken)
        {
            var language = PluginLanguage.Resolve(Request);
            var translations = Translations.For(language);
            var identity = await _identityVerifier.VerifyAsync(_userPageSession.ReadApiKey(Request), cancellationToken).ConfigureAwait(false);
            if (identity is null)
            {
                return Content(WatchlistPageHtml.Render(null, null, null, translations["userSettings.error.keyRejected"], language, translations), "text/html");
            }

            await _userDataManager.EnsureLoadedAsync(identity.Id).ConfigureAwait(false);
            _userDataManager.SetUserName(identity.Id, identity.Name);
            await _userDataManager.PersistAsync(identity.Id).ConfigureAwait(false);
            await _userPageSession.IssueCookieAsync(Response, Request, identity.Id, cancellationToken).ConfigureAwait(false);
            return Redirect(WatchlistPageHtml.PagePath + "?lang=" + language);
        }

        [HttpPost("start")]
        public async Task<IActionResult> Start(CancellationToken cancellationToken)
        {
            var identity = await _userPageSession.ResolveAsync(Request, cancellationToken).ConfigureAwait(false);
            if (identity is null)
            {
                return Redirect(WatchlistPageHtml.PagePath + "?lang=" + PluginLanguage.Resolve(Request));
            }

            await _userDataManager.EnsureLoadedAsync(identity.Id).ConfigureAwait(false);
            foreach (var eventGuid in _userDataManager.GetWatchlist(identity.Id))
            {
                await _downloadService.EnqueueAsync(identity.Id, eventGuid, cancellationToken).ConfigureAwait(false);
            }

            return Redirect(WatchlistPageHtml.PagePath + "?lang=" + PluginLanguage.Resolve(Request));
        }

        [HttpPost("remove")]
        public async Task<IActionResult> Remove(CancellationToken cancellationToken)
        {
            var identity = await _userPageSession.ResolveAsync(Request, cancellationToken).ConfigureAwait(false);
            if (identity is null)
            {
                return Redirect(WatchlistPageHtml.PagePath + "?lang=" + PluginLanguage.Resolve(Request));
            }

            var eventGuid = Request.Form["eventGuid"].ToString();
            if (string.IsNullOrWhiteSpace(eventGuid) || eventGuid.Length > 256)
            {
                return BadRequest();
            }

            await _userDataManager.EnsureLoadedAsync(identity.Id).ConfigureAwait(false);
            if (_userDataManager.IsOnWatchlist(identity.Id, eventGuid))
            {
                _userDataManager.RemoveFromWatchlist(identity.Id, eventGuid);
                await _userDataManager.PersistAsync(identity.Id).ConfigureAwait(false);
            }

            return Redirect(WatchlistPageHtml.PagePath + "?lang=" + PluginLanguage.Resolve(Request));
        }

        [HttpPost("retry")]
        public async Task<IActionResult> Retry(CancellationToken cancellationToken)
        {
            var identity = await _userPageSession.ResolveAsync(Request, cancellationToken).ConfigureAwait(false);
            if (identity is null)
            {
                return Redirect(WatchlistPageHtml.PagePath + "?lang=" + PluginLanguage.Resolve(Request));
            }

            var eventGuid = Request.Form["eventGuid"].ToString();
            if (string.IsNullOrWhiteSpace(eventGuid) || eventGuid.Length > 256)
            {
                return BadRequest();
            }

            await _userDataManager.EnsureLoadedAsync(identity.Id).ConfigureAwait(false);
            if (!_userDataManager.IsOnWatchlist(identity.Id, eventGuid))
            {
                return NotFound();
            }

            await _downloadService.EnqueueAsync(identity.Id, eventGuid, cancellationToken).ConfigureAwait(false);
            return Redirect(WatchlistPageHtml.PagePath + "?lang=" + PluginLanguage.Resolve(Request));
        }
    }
}
