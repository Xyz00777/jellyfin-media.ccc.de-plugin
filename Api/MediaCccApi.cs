using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaCccDe.Models;

namespace Jellyfin.Plugin.MediaCccDe.Api
{
    public interface IMediaCccApiClient
    {
        Task<T?> GetAsync<T>(string endpoint, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<ConferenceDto>> GetConferencesAsync(CancellationToken cancellationToken = default);
        Task<EventDto[]> GetEventsAsync(int conferenceId, CancellationToken cancellationToken = default);
        Task<EventDto?> GetEventAsync(string guid, CancellationToken cancellationToken = default);
        Task<EventDto[]> GetRecentAsync(int? limit = null, CancellationToken cancellationToken = default);
        string BuildUrl(string endpoint);
        string BuildUrl(string endpoint, object parameters);
    }

    public class MediaCccApi : IMediaCccApiClient
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private const string BaseUrl = "https://api.media.ccc.de/public/";
        private const string HttpClientName = "MediaCccApi";
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };

        public MediaCccApi(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        }

        public async Task<T?> GetAsync<T>(string endpoint, CancellationToken cancellationToken = default)
        {
            var httpClient = _httpClientFactory.CreateClient(HttpClientName);
            var response = await httpClient.GetAsync(endpoint, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<ConferenceDto>> GetConferencesAsync(CancellationToken cancellationToken = default)
        {
            var httpClient = _httpClientFactory.CreateClient(HttpClientName);
            var result = await GetAsyncInternal<List<ConferenceDto>>(httpClient, "conferences", cancellationToken).ConfigureAwait(false);
            return result ?? new List<ConferenceDto>();
        }

        public async Task<EventDto[]> GetEventsAsync(int conferenceId, CancellationToken cancellationToken = default)
        {
            if (conferenceId <= 0)
            {
                throw new ArgumentException("Conference ID must be a positive integer", nameof(conferenceId));
            }

            var httpClient = _httpClientFactory.CreateClient(HttpClientName);
            var result = await GetAsyncInternal<EventDto[]>(httpClient, $"conferences/{conferenceId}/events", cancellationToken).ConfigureAwait(false);
            return result ?? Array.Empty<EventDto>();
        }

        public async Task<EventDto?> GetEventAsync(string guid, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(guid))
            {
                throw new ArgumentException("GUID cannot be null or empty", nameof(guid));
            }

            var httpClient = _httpClientFactory.CreateClient(HttpClientName);
            return await GetAsyncInternal<EventDto>(httpClient, $"events/{guid}", cancellationToken).ConfigureAwait(false);
        }

        public async Task<EventDto[]> GetRecentAsync(int? limit = null, CancellationToken cancellationToken = default)
        {
            var endpoint = limit.HasValue ? $"events/recent?limit={limit.Value}" : "events/recent";
            var httpClient = _httpClientFactory.CreateClient(HttpClientName);
            var result = await GetAsyncInternal<EventDto[]>(httpClient, endpoint, cancellationToken).ConfigureAwait(false);
            
            if (result == null || result.Length == 0)
            {
                return Array.Empty<EventDto>();
            }

            // Sort by date descending (most recent first)
            var sorted = result.OrderByDescending(e => e.Date ?? string.Empty).ToArray();
            
            // Apply limit locally as well
            if (limit.HasValue && sorted.Length > limit.Value)
            {
                sorted = sorted.Take(limit.Value).ToArray();
            }

            return sorted;
        }

        private async Task<T?> GetAsyncInternal<T>(HttpClient httpClient, string endpoint, CancellationToken cancellationToken)
        {
            var response = await httpClient.GetAsync(endpoint, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        public string BuildUrl(string endpoint)
        {
            return $"{BaseUrl}{endpoint.TrimStart('/')}";
        }

        public string BuildUrl(string endpoint, object parameters)
        {
            var baseUrl = BuildUrl(endpoint);

            var properties = parameters.GetType().GetProperties();
            var queryParams = new List<string>();

            foreach (var property in properties)
            {
                var value = property.GetValue(parameters);
                if (value != null)
                {
                    var jsonPropertyName = property.Name;
                    var jsonAttr = property.GetCustomAttributes(typeof(JsonPropertyNameAttribute), true);
                    if (jsonAttr.Length > 0)
                    {
                        jsonPropertyName = ((JsonPropertyNameAttribute)jsonAttr[0]).Name;
                    }

                    var encodedValue = Uri.EscapeDataString(value.ToString() ?? string.Empty);
                    queryParams.Add($"{jsonPropertyName}={encodedValue}");
                }
            }

            if (queryParams.Count == 0)
            {
                return baseUrl;
            }

            var separator = baseUrl.Contains('?') ? "&" : "?";
            return $"{baseUrl}{separator}{string.Join("&", queryParams)}";
        }
    }
}