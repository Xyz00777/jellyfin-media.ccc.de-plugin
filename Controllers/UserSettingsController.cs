using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaCccDe.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using IUserManager = MediaBrowser.Controller.Library.IUserManager;

namespace Jellyfin.Plugin.MediaCccDe.Controllers
{
    [ApiController]
    [Route("media_ccc/settings/languages")]
    [AllowAnonymous]
    public class UserSettingsController : ControllerBase
    {
        private readonly JellyfinIdentityVerifier _identityVerifier;
        private readonly SettingsAccessTokenStore _tokenStore;
        private readonly IUserDataManager _userDataManager;
        private readonly IUserManager _userManager;

        public UserSettingsController(
            JellyfinIdentityVerifier identityVerifier,
            SettingsAccessTokenStore tokenStore,
            IUserDataManager userDataManager,
            IUserManager userManager)
        {
            _identityVerifier = identityVerifier ?? throw new ArgumentNullException(nameof(identityVerifier));
            _tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
            _userDataManager = userDataManager ?? throw new ArgumentNullException(nameof(userDataManager));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        }

        [HttpGet("")]
        [ProducesResponseType(typeof(string), 200)]
        public async Task<IActionResult> Get(CancellationToken cancellationToken)
        {
            var language = PluginLanguage.Resolve(Request);
            var translations = Translations.For(language);
            var identity = await TryIdentifyAsync(cancellationToken).ConfigureAwait(false);
            if (identity is null)
            {
                return Content(UserSettingsPageHtml.Render(null, null, null, null, null, language, translations), "text/html");
            }

            await _userDataManager.EnsureLoadedAsync(identity.Id).ConfigureAwait(false);

            var languages = _userDataManager.GetPreferredAudioLanguages(identity.Id);
            var subtitles = _userDataManager.GetPreferredSubtitleLanguages(identity.Id);

            return Content(
                UserSettingsPageHtml.Render(identity.Name, languages, subtitles, null, null, language, translations),
                "text/html");
        }

        [HttpPost("identify")]
        [ProducesResponseType(typeof(string), 200)]
        public async Task<IActionResult> Identify(CancellationToken cancellationToken)
        {
            var language = PluginLanguage.Resolve(Request);
            var translations = Translations.For(language);

            if (!CredentialTransportGuard.AllowsCredentialSubmission(Request))
            {
                return StatusCode(StatusCodes.Status426UpgradeRequired, translations["userSettings.error.httpsRequired"]);
            }

            var token = await _tokenStore.GetAsync(cancellationToken).ConfigureAwait(false);
            var username = Request.Form["username"].ToString();
            var password = Request.Form["password"].ToString();

            var identity = await _identityVerifier.VerifyAsync(username, password, cancellationToken).ConfigureAwait(false);
            if (identity is null)
            {
                return Content(
                    UserSettingsPageHtml.Render(
                        null,
                        null,
                        null,
                        null,
                        translations["userSettings.error.keyRejected"],
                        language,
                        translations),
                    "text/html");
            }

            await _userDataManager.EnsureLoadedAsync(identity.Id).ConfigureAwait(false);
            _userDataManager.SetUserName(identity.Id, identity.Name);
            await _userDataManager.PersistAsync(identity.Id).ConfigureAwait(false);

            var signer = new UserCookieSigner(token);
            Response.Cookies.Append(
                UserCookieSigner.CookieName,
                signer.Issue(identity.Id, DateTimeOffset.UtcNow),
                new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.Strict,
                    Secure = true,
                    Expires = DateTimeOffset.UtcNow.AddDays(90)
                });

            return Redirect(UserSettingsPageHtml.PagePath + "?lang=" + language);
        }

        [HttpPost("")]
        [ProducesResponseType(typeof(string), 200)]
        public async Task<IActionResult> Save(CancellationToken cancellationToken)
        {
            var language = PluginLanguage.Resolve(Request);
            var translations = Translations.For(language);
            var identity = await TryIdentifyAsync(cancellationToken).ConfigureAwait(false);
            if (identity is null)
            {
                return Content(
                    UserSettingsPageHtml.Render(null, null, null, null, translations["userSettings.error.sessionExpired"], language, translations),
                    "text/html");
            }

            var form = Request.Form;
            var audio = UserSettingsPageHtml.SplitLanguages(form["audio"].ToString());
            var subtitles = UserSettingsPageHtml.SplitLanguages(form["subtitles"].ToString());

            await _userDataManager.EnsureLoadedAsync(identity.Id).ConfigureAwait(false);
            _userDataManager.SetPreferredAudioLanguages(identity.Id, audio);
            _userDataManager.SetPreferredSubtitleLanguages(identity.Id, subtitles);
            await _userDataManager.PersistAsync(identity.Id).ConfigureAwait(false);

            return Content(
                UserSettingsPageHtml.Render(identity.Name, audio, subtitles, translations["userSettings.notice.saved"], null, language, translations),
                "text/html");
        }

        private async Task<JellyfinUserIdentity?> TryIdentifyAsync(CancellationToken cancellationToken)
        {
            var cookie = Request.Cookies[UserCookieSigner.CookieName];
            if (string.IsNullOrEmpty(cookie))
            {
                return null;
            }

            var token = await _tokenStore.GetAsync(cancellationToken).ConfigureAwait(false);
            var signer = new UserCookieSigner(token);

            if (!signer.Validate(cookie, DateTimeOffset.UtcNow, out var userId))
            {
                return null;
            }

            if (!PluginUserAccess.IsActive(_userManager, userId))
            {
                return null;
            }

            await _userDataManager.EnsureLoadedAsync(userId).ConfigureAwait(false);

            return new JellyfinUserIdentity(userId, _userDataManager.GetUserName(userId) ?? userId.ToString());
        }
    }
}
