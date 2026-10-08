using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaCccDe.Api;
using Jellyfin.Plugin.MediaCccDe.Models;
using Jellyfin.Plugin.MediaCccDe.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.MediaCccDe.Providers
{
    public class MediaCccSeriesProvider : IRemoteMetadataProvider<Series, SeriesInfo>, IRemoteImageProvider
    {
        private readonly IMediaCccApiClient _apiClient;
        private readonly IHttpClientFactory _httpClientFactory;
        private IReadOnlyList<ConferenceDto>? _cachedConferences;
        private DateTime _cacheExpiry = DateTime.MinValue;
        private readonly SemaphoreSlim _cacheLock = new(1, 1);
        internal static TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

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
                var conferences = await GetConferencesWithCacheAsync(cancellationToken).ConfigureAwait(false);

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
                    OriginalTitle = matchingConference.Acronym,
                    Overview = matchingConference.Description,
                    ProviderIds = new Dictionary<string, string>
                    {
                        { "MediaCccDe", matchingConference.Acronym }
                    }
                };

                series.Genres = new[] { "Conference" };

                var logoUrl = ResolveLogoUrl(matchingConference);
                if (!string.IsNullOrWhiteSpace(logoUrl))
                {
                    result.RemoteImages = new List<(string, ImageType)> { (logoUrl, ImageType.Primary) };
                }

                result.HasMetadata = true;
                result.Item = series;

                return result;
            }
            catch (HttpRequestException)
            {
                return result;
            }
        }

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeriesInfo searchInfo, CancellationToken cancellationToken)
        {
            var conferences = await GetConferencesWithCacheAsync(cancellationToken).ConfigureAwait(false);

            var results = new List<RemoteSearchResult>();

            if (searchInfo.ProviderIds.TryGetValue("MediaCccDe", out var id))
            {
                var match = conferences.FirstOrDefault(c =>
                    string.Equals(c.Acronym, id, StringComparison.OrdinalIgnoreCase));

                if (match != null)
                {
                    results.Add(new RemoteSearchResult
                    {
                        Name = match.Title ?? match.Acronym,
                        ProviderIds = new Dictionary<string, string> { ["MediaCccDe"] = match.Acronym }
                    });
                }
            }
            else if (!string.IsNullOrWhiteSpace(searchInfo.Name))
            {
                var matches = conferences.Where(c =>
                    c.Acronym?.Contains(searchInfo.Name, StringComparison.OrdinalIgnoreCase) == true ||
                    c.Title?.Contains(searchInfo.Name, StringComparison.OrdinalIgnoreCase) == true);

                foreach (var match in matches.Take(10))
                {
                    results.Add(new RemoteSearchResult
                    {
                        Name = match.Title ?? match.Acronym,
                        ProviderIds = new Dictionary<string, string> { ["MediaCccDe"] = match.Acronym }
                    });
                }
            }

            return results;
        }

        public bool Supports(BaseItem item)
        {
            return item is Series;
        }

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            return new[] { ImageType.Primary };
        }

        /// <summary>
        /// Series carry the conference acronym as their provider id, so the logo is
        /// resolved from the cached conference list on demand.
        /// </summary>
        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            if (item is not Series series
                || !series.ProviderIds.TryGetValue("MediaCccDe", out var identifier)
                || string.IsNullOrWhiteSpace(identifier))
            {
                return Array.Empty<RemoteImageInfo>();
            }

            var conferences = await GetConferencesWithCacheAsync(cancellationToken).ConfigureAwait(false);

            var match = conferences.FirstOrDefault(c =>
                string.Equals(c.Acronym, identifier, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.Slug, identifier, StringComparison.OrdinalIgnoreCase));

            if (match == null)
            {
                return Array.Empty<RemoteImageInfo>();
            }

            var logoUrl = ResolveLogoUrl(match);
            if (string.IsNullOrWhiteSpace(logoUrl))
            {
                return Array.Empty<RemoteImageInfo>();
            }

            return new[]
            {
                new RemoteImageInfo
                {
                    Url = logoUrl,
                    Type = ImageType.Primary,
                    ProviderName = Name
                }
            };
        }

        public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            var httpClient = _httpClientFactory.CreateClient();
            await RemoteUrlValidator.ValidatePublicHttpsUrlAsync(url, cancellationToken).ConfigureAwait(false);
            return await httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        }

        private static string? ResolveLogoUrl(ConferenceDto conference)
        {
            if (!string.IsNullOrWhiteSpace(conference.LogoUrl))
            {
                return conference.LogoUrl;
            }

            return conference.Images?
                .FirstOrDefault(i => string.Equals(i.Type, "logo", StringComparison.OrdinalIgnoreCase))?
                .Url;
        }

        private async Task<IReadOnlyList<ConferenceDto>> GetConferencesWithCacheAsync(CancellationToken cancellationToken)
        {
            await _cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_cachedConferences != null && DateTime.UtcNow < _cacheExpiry)
                {
                    return _cachedConferences;
                }

                var conferences = await _apiClient.GetConferencesAsync(cancellationToken).ConfigureAwait(false);
                _cachedConferences = conferences;
                _cacheExpiry = DateTime.UtcNow + CacheDuration;
                return _cachedConferences;
            }
            finally
            {
                _cacheLock.Release();
            }
        }
    }
}
