using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.MediaCccDe.Services
{
    /// <summary>
    /// Resolves the signed-in Jellyfin user for a plugin-served page from the
    /// long-lived cookie issued by the user settings page. Jellyfin 12 cannot
    /// authenticate plugin pages, so there is no per-request API token here.
    /// </summary>
    internal sealed class UserPageSession
    {
        private readonly SettingsAccessTokenStore _tokenStore;
        private readonly IUserDataManager _userDataManager;

        public UserPageSession(SettingsAccessTokenStore tokenStore, IUserDataManager userDataManager)
        {
            _tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
            _userDataManager = userDataManager ?? throw new ArgumentNullException(nameof(userDataManager));
        }

        /// <summary>
        /// Issues the identification cookie after a successful API key check.
        /// </summary>
        public async Task<string> IssueCookieAsync(
            HttpResponse response,
            HttpRequest request,
            Guid userId,
            CancellationToken cancellationToken)
        {
            var token = await _tokenStore.GetAsync(cancellationToken).ConfigureAwait(false);
            var signer = new UserCookieSigner(token);

            response.Cookies.Append(
                UserCookieSigner.CookieName,
                signer.Issue(userId, DateTimeOffset.UtcNow),
                new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.Strict,
                    Secure = request.IsHttps,
                    Expires = DateTimeOffset.UtcNow.AddDays(90)
                });

            return userId.ToString();
        }

        /// <summary>
        /// Returns the identified user, or <c>null</c> when the visitor has not
        /// identified themselves yet or the cookie is no longer valid.
        /// </summary>
        public async Task<JellyfinUserIdentity?> ResolveAsync(HttpRequest request, CancellationToken cancellationToken)
        {
            var cookie = request.Cookies[UserCookieSigner.CookieName];
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

            await _userDataManager.EnsureLoadedAsync(userId).ConfigureAwait(false);

            return new JellyfinUserIdentity(userId, _userDataManager.GetUserName(userId) ?? userId.ToString());
        }

        /// <summary>
        /// Reads the API key posted by the identification form.
        /// </summary>
        public string ReadApiKey(HttpRequest request)
        {
            return request.Form["apikey"].ToString();
        }
    }
}