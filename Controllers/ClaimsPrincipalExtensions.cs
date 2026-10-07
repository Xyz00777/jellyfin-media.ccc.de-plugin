using System;
using System.Security.Claims;

namespace Jellyfin.Plugin.MediaCccDe.Controllers
{
    internal static class ClaimsPrincipalExtensions
    {
        /// <summary>
        /// Claim type Jellyfin uses for the authenticated user id.
        /// Jellyfin's CustomAuthenticationHandler emits InternalClaimTypes.UserId, whose
        /// literal name is "Jellyfin-UserId". It does not emit ClaimTypes.NameIdentifier.
        /// </summary>
        internal const string UserIdClaimType = "Jellyfin-UserId";

        /// <summary>
        /// Resolves the authenticated user's id, or null when the principal carries no
        /// usable user id.
        /// </summary>
        internal static Guid? GetUserId(this ClaimsPrincipal principal)
        {
            var value = principal.FindFirst(UserIdClaimType)?.Value;
            if (string.IsNullOrEmpty(value) || !Guid.TryParse(value, out var userId) || userId == Guid.Empty)
            {
                return null;
            }

            return userId;
        }
    }
}
