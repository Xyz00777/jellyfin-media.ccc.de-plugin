using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaCccDe.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.MediaCccDe.Controllers
{
    [ApiController]
    [Route("media_ccc/settings")]
    [AllowAnonymous]
    public class SettingsController : ControllerBase
    {
        private const string DownloadUrl = "/media_ccc/settings/download";

        private readonly PluginInstanceResolver _pluginResolver;
        private readonly SettingsAccessTokenStore _tokenStore;
        private readonly Func<PluginConfiguration> _configurationProvider;

        public SettingsController(
            PluginInstanceResolver pluginResolver,
            SettingsAccessTokenStore tokenStore,
            Func<PluginConfiguration> configurationProvider)
        {
            _pluginResolver = pluginResolver ?? throw new ArgumentNullException(nameof(pluginResolver));
            _tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
            _configurationProvider = configurationProvider ?? throw new ArgumentNullException(nameof(configurationProvider));
        }

        [HttpGet("")]
        [ProducesResponseType(typeof(string), 200)]
        public async Task<IActionResult> Get(CancellationToken cancellationToken)
        {
            var language = PluginLanguage.Resolve(Request);
            var translations = Translations.For(language);
            var token = await _tokenStore.GetAsync(cancellationToken).ConfigureAwait(false);
            var signer = new SettingsCookieSigner(token);

            if (signer.Validate(Request.Cookies[SettingsCookieSigner.CookieName], DateTimeOffset.UtcNow))
            {
                return Content(SettingsPageHtml.Render(_configurationProvider(), null, null, language, translations), "text/html");
            }

            return Content(SettingsPageHtml.Render(null, null, null, language, translations), "text/html");
        }

        [HttpPost("unlock")]
        [ProducesResponseType(typeof(string), 200)]
        public async Task<IActionResult> Unlock(CancellationToken cancellationToken)
        {
            var language = PluginLanguage.Resolve(Request);
            var translations = Translations.For(language);

            if (!CredentialTransportGuard.AllowsCredentialSubmission(Request))
            {
                return StatusCode(StatusCodes.Status426UpgradeRequired, translations["settings.error.httpsRequired"]);
            }

            var token = await _tokenStore.GetAsync(cancellationToken).ConfigureAwait(false);
            var candidate = Request.Form["token"].ToString();

            if (!_tokenStore.Matches(candidate, token))
            {
                return Content(
                    SettingsPageHtml.Render(null, null, translations["settings.error.tokenRejected"], language, translations),
                    "text/html");
            }

            var signer = new SettingsCookieSigner(token);
            Response.Cookies.Append(
                SettingsCookieSigner.CookieName,
                signer.Issue(DateTimeOffset.UtcNow),
                new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.Strict,
                    Secure = true,
                    Expires = DateTimeOffset.UtcNow.AddDays(30)
                });

            return Redirect(SettingsPageHtml.SettingsPath);
        }

        [HttpPost("")]
        [ProducesResponseType(typeof(string), 200)]
        public async Task<IActionResult> Save(CancellationToken cancellationToken)
        {
            var language = PluginLanguage.Resolve(Request);
            var translations = Translations.For(language);
            var token = await _tokenStore.GetAsync(cancellationToken).ConfigureAwait(false);
            var signer = new SettingsCookieSigner(token);

            if (!signer.Validate(Request.Cookies[SettingsCookieSigner.CookieName], DateTimeOffset.UtcNow))
            {
                return Content(SettingsPageHtml.Render(null, null, translations["settings.error.sessionExpired"], language, translations), "text/html");
            }

            var form = Request.Form;
            var configuration = _configurationProvider();

            configuration.WatchlistPath = form["WatchlistPath"].ToString().Trim();
            var quality = form["PreferredQuality"].ToString().Trim();
            if (quality.Length > 0)
            {
                configuration.PreferredQuality = quality;
            }
            configuration.SyncIntervalHours = ClampHours(form["SyncIntervalHours"].ToString(), configuration.SyncIntervalHours);
            configuration.PreferredAudioLanguages = SettingsPageHtml.SplitLanguages(form["PreferredAudioLanguages"].ToString());
            configuration.PreferredSubtitleLanguages = SettingsPageHtml.SplitLanguages(form["PreferredSubtitleLanguages"].ToString());
            configuration.DownloadSubtitles = form["DownloadSubtitles"].ToString() is "on" or "true";

            var plugin = _pluginResolver.Resolve();
            if (plugin is null)
            {
                return Content(SettingsPageHtml.Render(null, null, translations["settings.error.pluginNotLoaded"], language, translations), "text/html");
            }

            plugin.UpdateConfiguration(configuration);

            return Content(
                SettingsPageHtml.Render(configuration, translations["settings.notice.saved"], null, language, translations),
                "text/html");
        }

        [HttpGet("download")]
        [ProducesResponseType(typeof(string), 200)]
        public async Task<IActionResult> Download(CancellationToken cancellationToken)
        {
            var token = await _tokenStore.GetAsync(cancellationToken).ConfigureAwait(false);
            var signer = new SettingsCookieSigner(token);

            if (!signer.Validate(Request.Cookies[SettingsCookieSigner.CookieName], DateTimeOffset.UtcNow))
            {
                return StatusCode(StatusCodes.Status403Forbidden);
            }

            var plugin = _pluginResolver.Resolve();
            if (plugin is null)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            var path = plugin.ConfigurationFilePath;
            return PhysicalFile(path, "application/xml", Path.GetFileName(path));
        }

        private static int ClampHours(string value, int fallback)
        {
            if (!int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            {
                return fallback;
            }

            return Math.Clamp(parsed, 1, 168);
        }
    }
}
