using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Jellyfin.Plugin.MediaCccDe.Services
{
    /// <summary>
    /// Issues the plugin's own signed cookie so a script-free settings form can prove it
    /// already exchanged the log token. The signing key is the access token, so no
    /// additional secret has to be persisted.
    /// </summary>
    public sealed class SettingsCookieSigner
    {
        internal const string CookieName = "ccc_media_settings";
        private const int LifetimeDays = 30;

        private readonly byte[] _key;

        public SettingsCookieSigner(string accessToken)
        {
            _key = SHA256.HashData(Encoding.UTF8.GetBytes(accessToken));
        }

        public string Issue(DateTimeOffset now)
        {
            var expiry = now.AddDays(LifetimeDays).ToUnixTimeSeconds()
                .ToString(CultureInfo.InvariantCulture);

            return expiry + "." + Convert.ToHexString(Sign(expiry));
        }

        public bool Validate(string? cookieValue, DateTimeOffset now)
        {
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

            if (!long.TryParse(payload, NumberStyles.None, CultureInfo.InvariantCulture, out var expiry))
            {
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
