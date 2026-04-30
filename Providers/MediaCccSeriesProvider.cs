using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaCccDe.Api;
using Jellyfin.Plugin.MediaCccDe.Models;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.MediaCccDe.Providers
{
    public class MediaCccSeriesProvider : IRemoteMetadataProvider<Series, SeriesInfo>
    {
        private readonly IMediaCccApiClient _apiClient;
        private readonly IHttpClientFactory _httpClientFactory;
        private IReadOnlyList<ConferenceDto>? _cachedConferences;

        public string Name => "MediaCccDe Series";

        public MediaCccSeriesProvider(IMediaCccApiClient apiClient, IHttpClientFactory httpClientFactory)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        }

        public async Task<MetadataResult<Series>> GetMetadata(SeriesInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Series>
            {
                HasMetadata = false,
                Item = null!
            };

            try
            {
                var conferences = await GetConferencesAsync(cancellationToken).ConfigureAwait(false);

                var matchingConference = conferences.FirstOrDefault(c =>
                    string.Equals(c.Acronym, info.Name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(c.Slug, info.Name, StringComparison.OrdinalIgnoreCase));

                if (matchingConference == null)
                {
                    return result;
                }

                var series = new Series
                {
                    Name = matchingConference.Title,
                    OriginalTitle = matchingConference.Slug,
                    Overview = matchingConference.Description,
                    ProviderIds = new Dictionary<string, string>
                    {
                        { "MediaCccDe", matchingConference.Acronym }
                    }
                };

                if (!string.IsNullOrEmpty(matchingConference.Description))
                {
                    series.Overview = matchingConference.Description;
                }

                series.Genres = new[] { "Talk" };

                result.HasMetadata = true;
                result.Item = series;

                return result;
            }
            catch (HttpRequestException)
            {
                return result;
            }
        }

        public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeriesInfo searchInfo, CancellationToken cancellationToken)
        {
            return Task.FromResult(Enumerable.Empty<RemoteSearchResult>());
        }

        public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            var httpClient = _httpClientFactory.CreateClient();
            return await httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        }

        private async Task<IReadOnlyList<ConferenceDto>> GetConferencesAsync(CancellationToken cancellationToken)
        {
            if (_cachedConferences != null)
            {
                return _cachedConferences;
            }

            _cachedConferences = await _apiClient.GetConferencesAsync(cancellationToken).ConfigureAwait(false);
            return _cachedConferences;
        }
    }
}