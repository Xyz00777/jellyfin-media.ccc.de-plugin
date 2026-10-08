using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaCccDe.Api;
using Jellyfin.Plugin.MediaCccDe.Models;
using Jellyfin.Plugin.MediaCccDe.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.MediaCccDe.Controllers
{
    [ApiController]
    [Route("media_ccc/browse")]
    [AllowAnonymous]
    public sealed class BrowseController : ControllerBase
    {
        private readonly IMediaCccApiClient _apiClient;
        private readonly IUserDataManager _userDataManager;
        private readonly IWatchlistDownloadService _downloadService;
        private readonly JellyfinIdentityVerifier _identityVerifier;
        private readonly UserPageSession _userPageSession;

        public BrowseController(
            IMediaCccApiClient apiClient,
            IUserDataManager userDataManager,
            IWatchlistDownloadService downloadService,
            JellyfinIdentityVerifier identityVerifier,
            IServiceProvider serviceProvider)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _userDataManager = userDataManager ?? throw new ArgumentNullException(nameof(userDataManager));
            _downloadService = downloadService ?? throw new ArgumentNullException(nameof(downloadService));
            _identityVerifier = identityVerifier ?? throw new ArgumentNullException(nameof(identityVerifier));
            _userPageSession = (UserPageSession?)serviceProvider?.GetService(typeof(UserPageSession))
                ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        [HttpGet("")]
        public async Task<IActionResult> Get(
            [FromQuery] string? q,
            [FromQuery] string? year,
            [FromQuery] string? conference,
            [FromQuery] string? status,
            CancellationToken cancellationToken)
        {
            var language = PluginLanguage.Resolve(Request);
            var translations = Translations.For(language);
            var identity = await _userPageSession.ResolveAsync(Request, cancellationToken).ConfigureAwait(false);
            if (identity is null)
            {
                return Content(BrowsePageHtml.Render(null, null, q, year, null, null, false, null, false, language, translations,
                    identifyConference: conference), "text/html");
            }

            IReadOnlyList<ConferenceDto> conferences;
            try
            {
                conferences = await _apiClient.GetConferencesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return Content(BrowsePageHtml.Render(identity.Name, null, q, year, null, null, false, null, false, language, translations), "text/html");
            }

            var selectedConference = conferences.FirstOrDefault(item =>
                string.Equals(item.Acronym, conference, StringComparison.OrdinalIgnoreCase));
            EventDto[]? events = null;
            var eventError = false;
            if (selectedConference is not null)
            {
                try
                {
                    events = await _apiClient.GetEventsAsync(selectedConference.Acronym, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    eventError = true;
                }
            }

            var queued = string.Equals(status, "queued", StringComparison.Ordinal);
            var retry = string.Equals(status, "retry", StringComparison.Ordinal);
            return Content(
                BrowsePageHtml.Render(identity.Name, conferences, q, year, selectedConference, events, eventError, queued, retry, language, translations),
                "text/html");
        }

        [HttpPost("events")]
        public async Task<IActionResult> Events(CancellationToken cancellationToken)
        {
            var language = PluginLanguage.Resolve(Request);
            var form = await Request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
            return Redirect(BrowsePageHtml.BuildUrl(form["lang"], form["q"], form["year"], form["conference"]));
        }

        [HttpPost("add")]
        public async Task<IActionResult> Add(CancellationToken cancellationToken)
        {
            var language = PluginLanguage.Resolve(Request);
            var translations = Translations.For(language);
            var identity = await _userPageSession.ResolveAsync(Request, cancellationToken).ConfigureAwait(false);
            var form = await Request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
            var conference = form["conference"].ToString();
            var query = form["q"].ToString();
            var year = form["year"].ToString();
            var eventGuid = form["eventGuid"].ToString();

            if (identity is null || string.IsNullOrWhiteSpace(eventGuid))
            {
                return Content(
                    BrowsePageHtml.Render(null, null, query, year, null, null, false, null, false, language, translations,
                        identifyConference: conference),
                    "text/html");
            }

            await _userDataManager.EnsureLoadedAsync(identity.Id).ConfigureAwait(false);
            if (_userDataManager.AddToWatchlistIfMissing(identity.Id, eventGuid))
            {
                await _userDataManager.PersistAsync(identity.Id).ConfigureAwait(false);
            }

            try
            {
                var item = await _downloadService.EnqueueAsync(identity.Id, eventGuid, cancellationToken).ConfigureAwait(false);
                return Redirect(BrowsePageHtml.BuildUrl(language, query, year, conference, item is null ? "retry" : "queued"));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return Redirect(BrowsePageHtml.BuildUrl(language, query, year, conference, "retry"));
            }
        }

        [HttpPost("identify")]
        public async Task<IActionResult> Identify(CancellationToken cancellationToken)
        {
            var language = PluginLanguage.Resolve(Request);
            var translations = Translations.For(language);
            var form = await Request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
            var identity = await _identityVerifier.VerifyAsync(_userPageSession.ReadApiKey(Request), cancellationToken).ConfigureAwait(false);
            if (identity is null)
            {
                return Content(
                    BrowsePageHtml.Render(null, null, form["q"], form["year"], null, null, false, null, false, language, translations,
                        translations["userSettings.error.keyRejected"], form["conference"]),
                    "text/html");
            }

            await _userDataManager.EnsureLoadedAsync(identity.Id).ConfigureAwait(false);
            _userDataManager.SetUserName(identity.Id, identity.Name);
            await _userDataManager.PersistAsync(identity.Id).ConfigureAwait(false);
            await _userPageSession.IssueCookieAsync(Response, Request, identity.Id, cancellationToken).ConfigureAwait(false);
            return Redirect(BrowsePageHtml.BuildUrl(form["lang"], form["q"], form["year"], form["conference"]));
        }
    }
}
