using System;
using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.MediaCccDe.Services
{
    /// <summary>
    /// Decides whether a request may carry a credential. The settings token and the
    /// user's own Jellyfin API key are bearer secrets: replayed, the first grants plugin
    /// settings access and the second grants the account's Jellyfin permissions.
    /// Submitting either over cleartext HTTP hands it to everyone on the path, so only
    /// TLS may carry them. A loopback-only install has no network path to observe and
    /// keeps working, which matters because Jellyfin's default is plain HTTP locally.
    /// </summary>
    internal static class CredentialTransportGuard
    {
        /// <summary>
        /// Returns whether this request may submit a credential.
        /// </summary>
        /// <param name="request">The incoming request.</param>
        /// <returns><see langword="true"/> for HTTPS or unproxied local requests.</returns>
        public static bool AllowsCredentialSubmission(HttpRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            return request.IsHttps || IsUnproxiedLocalAccess(request);
        }

        /// <summary>
        /// Returns whether the request came from the machine Jellyfin runs on, with no
        /// proxy in the path and so no network hop an observer could read.
        /// </summary>
        /// <param name="request">The incoming request.</param>
        /// <returns><see langword="true"/> for direct loopback or localhost-addressed access.</returns>
        private static bool IsUnproxiedLocalAccess(HttpRequest request)
        {
            // A reverse proxy on this host connects from loopback, so the peer address alone would
            // let a plain-HTTP proxy pass while the client-facing hop stayed cleartext.
            // Forwarding headers identify a proxy; with one present the transport is trusted
            // only if it already reports HTTPS.
            if (WasForwarded(request))
            {
                return false;
            }

            return IsLoopbackPeer(request) || IsLoopbackHost(request.Host.Value);
        }

        private static bool IsLoopbackPeer(HttpRequest request)
        {
            var remote = request.HttpContext?.Connection?.RemoteIpAddress;
            if (remote is null)
            {
                return false;
            }

            // An IPv4 peer frequently arrives as ::ffff:a.b.c.d; comparing that directly
            // against the IPv4 loopback range would miss local browsers.
            if (remote.IsIPv4MappedToIPv6)
            {
                remote = remote.MapToIPv4();
            }

            return IPAddress.IsLoopback(remote);
        }

        /// <summary>
        /// Returns whether the client reached the server through a loopback name.
        /// </summary>
        /// <remarks>
        /// Container, VM, and reverse-proxy deployments hide the real client behind a bridge
        /// address, so <c>RemoteIpAddress</c> stays private even for a browser on the host.
        /// The name the client dialled still shows that no network hop was involved. A
        /// remote client cannot resolve a local name, and one that forges the header already
        /// holds the credential it submits and gains nothing it did not have.
        /// </remarks>
        /// <param name="host">The request's Host header value.</param>
        /// <returns><see langword="true"/> for localhost, 127.0.0.0/8, or ::1.</returns>
        private static bool IsLoopbackHost(string? host)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return false;
            }

            if (string.Equals(StripPort(host), "localhost", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!IPAddress.TryParse(StripPort(host).Trim('[', ']'), out var address))
            {
                return false;
            }

            return IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);
        }

        /// <summary>
        /// Reduces a Host header to just the host, dropping any port and IPv6 brackets.
        /// </summary>
        private static string StripPort(string host)
        {
            if (host.StartsWith('['))
            {
                var close = host.IndexOf(']');
                return close > 0 ? host[..(close + 1)] : host;
            }

            var separator = host.LastIndexOf(':');
            if (separator <= 0)
            {
                return host;
            }

            var suffix = host[(separator + 1)..];
            return suffix.Length > 0 && suffix.All(char.IsDigit) ? host[..separator] : host;
        }

        private static bool WasForwarded(HttpRequest request)
        {
            var headers = request.Headers;
            return headers.ContainsKey("X-Forwarded-For")
                || headers.ContainsKey("X-Forwarded-Proto")
                || headers.ContainsKey("X-Forwarded-Host")
                || headers.ContainsKey("X-Real-IP");
        }
    }
}
