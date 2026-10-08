using System;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.MediaCccDe.Services
{
    /// <summary>
    /// Re-checks the user id inside a signed cookie against Jellyfin on every request.
    /// The signature only proves the plugin issued the cookie; it says nothing about
    /// whether the account may still be used. Without this check, disabling or deleting
    /// an account left its plugin session working until the cookie expired, so an
    /// administrator's deprovisioning had no effect on these pages.
    /// </summary>
    internal static class PluginUserAccess
    {
        /// <summary>
        /// Returns whether the Jellyfin account still exists and is enabled.
        /// </summary>
        /// <param name="userManager">Jellyfin's user manager.</param>
        /// <param name="userId">The user id carried by the cookie.</param>
        /// <returns><see langword="false"/> for a deleted, disabled, or unknown user.</returns>
        public static bool IsActive(IUserManager? userManager, Guid userId)
        {
            if (userManager is null || userId == Guid.Empty)
            {
                return false;
            }

            var user = userManager.GetUserById(userId);
            if (user is null)
            {
                return false;
            }

            // Jellyfin 12 stores the policy's IsDisabled flag as a permission on the
            // user rather than as a UserPolicy property, so this is the only place the
            // disabled state is readable.
            return !user.HasPermission(PermissionKind.IsDisabled);
        }
    }
}
