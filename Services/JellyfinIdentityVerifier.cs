using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.MediaCccDe.Services
{
    public sealed record JellyfinUserIdentity(Guid Id, string Name);

    /// <summary>
    /// Confirms who a visitor is by asking Jellyfin to authenticate the credentials they
    /// supplied. Both values are held in memory only for the length of that call: they are
    /// never stored, logged, or echoed back, and the identity is whatever Jellyfin returns
    /// rather than anything the form claimed.
    /// </summary>
    /// <remarks>
    /// This used to take an API key and list <c>/Users</c>, accepting a key only when it
    /// resolved to exactly one account. Jellyfin 12 stores API keys globally with no user
    /// association, so every key resolves to every user and the check could never succeed.
    /// Authenticating instead yields the account directly and keeps the identity
    /// server-verified, which a client-supplied user id would not be.
    /// </remarks>
    public sealed class JellyfinIdentityVerifier
    {
        internal const string VerificationClientName = "JellyfinLocal";
        private const int MaxNameLength = 256;
        private const int MaxSecretLength = 256;

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _baseUrl;

        public JellyfinIdentityVerifier(IHttpClientFactory httpClientFactory, string baseUrl)
        {
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _baseUrl = baseUrl?.TrimEnd('/')
                ?? throw new ArgumentNullException(nameof(baseUrl));
        }

        /// <summary>
        /// Authenticates the supplied credentials against Jellyfin.
        /// </summary>
        /// <param name="username">The visitor's Jellyfin username.</param>
        /// <param name="password">The visitor's Jellyfin password.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The authenticated account, or null when the credentials are rejected.</returns>
        public async Task<JellyfinUserIdentity?> VerifyAsync(string? username, string? password, CancellationToken cancellationToken)
        {
            var candidateName = username?.Trim();
            if (string.IsNullOrEmpty(candidateName)
                || candidateName.Length > MaxNameLength
                || string.IsNullOrEmpty(password)
                || password.Length > MaxSecretLength)
            {
                return null;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/Users/AuthenticateByName")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        Username = candidateName,
                        Pw = password
                    }),
                    Encoding.UTF8,
                    "application/json")
            };

            request.Headers.Authorization = new AuthenticationHeaderValue(
                "MediaBrowser",
                "Client=\"Media.CCC.de\", Device=\"Media.CCC.de\", DeviceId=\"mediacccde\", Version=\"1.0.0\"");

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

                if (!document.RootElement.TryGetProperty("User", out var user)
                    || user.ValueKind != JsonValueKind.Object
                    || !user.TryGetProperty("Id", out var idElement)
                    || idElement.ValueKind != JsonValueKind.String
                    || !Guid.TryParse(idElement.GetString(), out var userId)
                    || userId == Guid.Empty)
                {
                    return null;
                }

                var name = user.TryGetProperty("Name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
                    ? nameElement.GetString() ?? string.Empty
                    : string.Empty;

                if (string.IsNullOrWhiteSpace(name))
                {
                    name = candidateName;
                }

                return new JellyfinUserIdentity(userId, name);
            }
        }
    }
}
