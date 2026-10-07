using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.MediaCccDe.Services
{
    public static class RemoteUrlValidator
    {
        public static async Task<Uri> ValidatePublicHttpsUrlAsync(string url, CancellationToken cancellationToken)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(uri.Host))
            {
                throw new ArgumentException("Only absolute HTTPS URLs are allowed.", nameof(url));
            }

            var addresses = IPAddress.TryParse(uri.DnsSafeHost, out var literal)
                ? new[] { literal }
                : await Dns.GetHostAddressesAsync(uri.DnsSafeHost, cancellationToken).ConfigureAwait(false);

            if (addresses.Length == 0 || addresses.Any(IsPrivateAddress))
            {
                throw new ArgumentException("URLs must resolve to a public address.", nameof(url));
            }

            return uri;
        }

        private static bool IsPrivateAddress(IPAddress address)
        {
            if (address.IsIPv4MappedToIPv6)
            {
                address = address.MapToIPv4();
            }

            if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
            {
                return true;
            }

            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                var bytes = address.GetAddressBytes();
                return bytes[0] == 0 ||
                       bytes[0] == 10 ||
                       (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) ||
                       (bytes[0] == 127) ||
                       (bytes[0] == 169 && bytes[1] == 254) ||
                       (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                       (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0) ||
                       (bytes[0] == 192 && bytes[1] == 168) ||
                       (bytes[0] == 198 && bytes[1] >= 18 && bytes[1] <= 19) ||
                       bytes[0] >= 224;
            }

            var ipv6 = address.GetAddressBytes();
            return (ipv6[0] & 0xfe) == 0xfc ||
                   (ipv6[0] == 0xfe && (ipv6[1] & 0xc0) == 0x80) ||
                   ipv6.All(static value => value == 0);
        }
    }
}
