using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Jellyfin.Plugin.MediaCccDe.Services
{
    /// <summary>
    /// Issues the plugin's own cookie for a per-user page, binding it to one Jellyfin user
    /// id so a cookie cannot be replayed against a different account. The signing key is
    /// the access token, so nothing extra has to be persisted.
    /// </summary>
    public sealed class UserCookieSigner
    {
        internal const string CookieName = "ccc_media_user";
        private const int LifetimeDays = 90;

        private readonly byte[] _key;

        public UserCookieSigner(string accessToken)
        {
            _key = SHA256.HashData(Encoding.UTF8.GetBytes(accessToken));
        }

        public string Issue(Guid userId, DateTimeOffset now)
        {
            var expiry = now.AddDays(LifetimeDays).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            var user = userId.ToString("N", CultureInfo.InvariantCulture);
            var payload = expiry + ":" + user;

            return payload + "." + Convert.ToHexString(Sign(payload));
        }

        public bool Validate(string? cookieValue, DateTimeOffset now, out Guid userId)
        {
            userId = Guid.Empty;

            if (string.IsNullOrWhiteSpace(cookieValue))
            {
                return false;
            }

            var separator = cookieValue.IndexOf('.');
            if (separator <= 0 || separator == cookieValue.Length - 1)
            {
                return false;
            }

            var payload = cookieValue[..separator];
            var signature = cookieValue[(separator + 1)..];

            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(signature),
                    Encoding.UTF8.GetBytes(Convert.ToHexString(Sign(payload)))))
            {
                return false;
            }

            var parts = payload.Split(':');
            if (parts.Length != 2
                || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var expiry)
                || !Guid.TryParseExact(parts[1], "N", out userId)
                || userId == Guid.Empty)
            {
                userId = Guid.Empty;
                return false;
            }

            return DateTimeOffset.FromUnixTimeSeconds(expiry) > now;
        }

        private byte[] Sign(string payload)
        {
            using var hmac = new HMACSHA256(_key);
            return hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        }
    }
}
