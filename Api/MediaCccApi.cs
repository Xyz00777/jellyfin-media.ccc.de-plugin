using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
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
        Task<EventDto[]> GetEventsAsync(string conferenceIdentifier, CancellationToken cancellationToken = default);
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
            var payload = await GetAsyncInternal<JsonElement>(httpClient, "conferences", cancellationToken).ConfigureAwait(false);

            if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("conferences", out var conferences))
            {
                payload = conferences;
            }

            return payload.ValueKind == JsonValueKind.Array
                ? payload.Deserialize<List<ConferenceDto>>(JsonOptions) ?? new List<ConferenceDto>()
                : new List<ConferenceDto>();
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

        public async Task<EventDto[]> GetEventsAsync(string conferenceIdentifier, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(conferenceIdentifier))
            {
                throw new ArgumentException("Conference identifier cannot be empty", nameof(conferenceIdentifier));
            }

            var httpClient = _httpClientFactory.CreateClient(HttpClientName);
            var conference = await GetAsyncInternal<ConferenceDto>(
                httpClient,
                $"conferences/{Uri.EscapeDataString(conferenceIdentifier)}",
                cancellationToken).ConfigureAwait(false);

            var summaries = conference?.Events ?? new List<EventDto>();
            if (summaries.Count == 0)
            {
                return Array.Empty<EventDto>();
            }

            return await HydrateEventsAsync(summaries, cancellationToken).ConfigureAwait(false);
        }

        private async Task<EventDto[]> HydrateEventsAsync(
            IReadOnlyList<EventDto> summaries,
            CancellationToken cancellationToken)
        {
            using var concurrencyGate = new SemaphoreSlim(8);
            var hydratedEvents = summaries.Select(async summary =>
            {
                if (!string.IsNullOrWhiteSpace(summary.Guid) && summary.Recordings is not { Count: > 0 })
                {
                    await concurrencyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        return await GetEventAsync(summary.Guid, cancellationToken).ConfigureAwait(false) ?? summary;
                    }
                    catch (HttpRequestException)
                    {
                        return summary;
                    }
                    finally
                    {
                        concurrencyGate.Release();
                    }
                }

                return summary;
            });

            return await Task.WhenAll(hydratedEvents).ConfigureAwait(false);
        }

        public async Task<EventDto?> GetEventAsync(string guid, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(guid))
            {
                throw new ArgumentException("GUID cannot be null or empty", nameof(guid));
            }

            var httpClient = _httpClientFactory.CreateClient(HttpClientName);
            return await GetAsyncInternal<EventDto>(httpClient, $"events/{Uri.EscapeDataString(guid)}", cancellationToken).ConfigureAwait(false);
        }

        public async Task<EventDto[]> GetRecentAsync(int? limit = null, CancellationToken cancellationToken = default)
        {
            if (limit < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(limit), "Limit cannot be negative.");
            }

            var endpoint = limit.HasValue ? $"events/recent?limit={limit.Value}" : "events/recent";
            var httpClient = _httpClientFactory.CreateClient(HttpClientName);
            var payload = await GetAsyncInternal<JsonElement>(httpClient, endpoint, cancellationToken).ConfigureAwait(false);
            var shouldHydrate = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("events", out _);
            var result = DeserializeEvents(payload);
            
            if (result == null || result.Length == 0)
            {
                return Array.Empty<EventDto>();
            }

            if (shouldHydrate)
            {
                result = await HydrateEventsAsync(result, cancellationToken).ConfigureAwait(false);
            }

            // Sort by date descending (most recent first)
            var sorted = result
                .OrderByDescending(e => ParseDate(e.Date) ?? DateTimeOffset.MinValue)
                .ToArray();
            
            // Apply limit locally as well
            if (limit.HasValue && sorted.Length > limit.Value)
            {
                sorted = sorted.Take(limit.Value).ToArray();
            }

            return sorted;
        }

        private static EventDto[] DeserializeEvents(JsonElement payload)
        {
            if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("events", out var events))
            {
                payload = events;
            }

            return payload.ValueKind == JsonValueKind.Array
                ? payload.Deserialize<EventDto[]>(JsonOptions) ?? Array.Empty<EventDto>()
                : Array.Empty<EventDto>();
        }

        private static DateTimeOffset? ParseDate(string? value)
        {
            return DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed)
                ? parsed
                : null;
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
