using System;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.MediaCccDe.Services
{
    public sealed record JellyfinUserIdentity(Guid Id, string Name);

    /// <summary>
    /// Confirms who a visitor is by asking Jellyfin about the API key they supplied. The
    /// key is only ever held in memory for the length of the call: it is never stored,
    /// logged, or echoed back, and the identity comes from the server rather than the form.
    /// </summary>
    public sealed class JellyfinIdentityVerifier
    {
        internal const string VerificationClientName = "JellyfinLocal";
        private const int MaxKeyLength = 128;

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _baseUrl;

        public JellyfinIdentityVerifier(IHttpClientFactory httpClientFactory, string baseUrl)
        {
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _baseUrl = baseUrl?.TrimEnd('/')
                ?? throw new ArgumentNullException(nameof(baseUrl));
        }

        public async Task<JellyfinUserIdentity?> VerifyAsync(string? apiKey, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return null;
            }

            var candidate = apiKey.Trim();

            // A Jellyfin API key is a fixed-length hex string. Anything else is rejected
            // before it reaches the network.
            if (candidate.Length > MaxKeyLength || !IsHex(candidate))
            {
                return null;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, _baseUrl + "/Users");
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "MediaBrowser",
                "Token=" + candidate);

            var httpClient = _httpClientFactory.CreateClient(VerificationClientName);

            HttpResponseMessage response;
            try
            {
                response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                return null;
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

                // A Jellyfin API key belongs to exactly one user, and listing users with
                // such a key returns only that user. An administrator's session token would
                // return many, so anything other than a single user is refused rather than
                // guessed at, which also stops a pasted admin token from being accepted.
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return null;
                }

                if (document.RootElement.GetArrayLength() != 1)
                {
                    return null;
                }

                var root = document.RootElement[0];
                if (!root.TryGetProperty("Id", out var idElement) || idElement.ValueKind != JsonValueKind.String)
                {
                    return null;
                }

                if (!Guid.TryParse(idElement.GetString(), out var userId) || userId == Guid.Empty)
                {
                    return null;
                }

                var name = root.TryGetProperty("Name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
                    ? nameElement.GetString() ?? string.Empty
                    : string.Empty;

                return new JellyfinUserIdentity(userId, name);
            }
        }

        private static bool IsHex(string value)
        {
            foreach (var c in value)
            {
                var isHexDigit = c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
                if (!isHexDigit)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
