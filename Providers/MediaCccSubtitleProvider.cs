using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaCccDe.Api;
using Jellyfin.Plugin.MediaCccDe.Models;
using Jellyfin.Plugin.MediaCccDe.Services;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.MediaCccDe.Providers
{
    /// <summary>
    /// Offers media.ccc.de subtitles to Jellyfin's own subtitle search, so a subtitle is
    /// fetched only when someone actually asks for it instead of downloading one file per
    /// talk up front. Jellyfin stores what it downloads beside the media, after which the
    /// subtitle is local and works for every client.
    /// </summary>
    public class MediaCccSubtitleProvider : ISubtitleProvider
    {
        internal const string SubtitleMimeType = "application/x-subrip";
        internal const string SubtitleClientName = "CccSubtitles";
        private const int MaxRedirects = 5;
        private const string AllowedSubtitleHostSuffix = ".media.ccc.de";

        private readonly IMediaCccApiClient _apiClient;
        private readonly IConferenceScheduleCache _scheduleCache;
        private readonly IHttpClientFactory _httpClientFactory;

        public string Name => "Media.CCC.de";

        public IEnumerable<VideoContentType> SupportedMediaTypes => new[] { VideoContentType.Episode, VideoContentType.Movie };

        public MediaCccSubtitleProvider(
            IMediaCccApiClient apiClient,
            IConferenceScheduleCache scheduleCache,
            IHttpClientFactory httpClientFactory)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _scheduleCache = scheduleCache ?? throw new ArgumentNullException(nameof(scheduleCache));
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        }

        public async Task<IEnumerable<RemoteSubtitleInfo>> Search(SubtitleSearchRequest request, CancellationToken cancellationToken)
        {
            var slug = ResolveSlug(request);
            if (slug is null)
            {
                return Array.Empty<RemoteSubtitleInfo>();
            }

            var acronym = MediaCccEpisodeProvider.ResolveConferenceAcronym(request.MediaPath);
            if (acronym is null)
            {
                return Array.Empty<RemoteSubtitleInfo>();
            }

            var schedule = await _scheduleCache.GetAsync(acronym, cancellationToken).ConfigureAwait(false);
            if (schedule is null || !schedule.TryGet(slug, slug, out var scheduled) || scheduled is null)
            {
                return Array.Empty<RemoteSubtitleInfo>();
            }

            // The conference listing carries no recordings, so the single event is fetched
            // to reach its subtitle tracks. This only happens when someone searches.
            var hydrated = await _apiClient.GetEventAsync(scheduled.Event.Guid, cancellationToken).ConfigureAwait(false);
            var subtitles = FindSubtitles(hydrated);

            return subtitles
                .Select(r => new RemoteSubtitleInfo
                {
                    Name = BuildName(r.Language),
                    ProviderName = Name,
                    Format = "srt",
                    Id = r.Url,
                    ThreeLetterISOLanguageName = ToIso639(r.Language),
                    Comment = "media.ccc.de"
                })
                .ToList();
        }

        public async Task<SubtitleResponse?> GetSubtitles(string search, CancellationToken cancellationToken)
        {
            var payload = await DownloadSubtitleAsync(search, cancellationToken).ConfigureAwait(false);
            if (payload is null)
            {
                return null;
            }

            return new SubtitleResponse
            {
                Format = "srt",
                Stream = payload,
                Language = GuessLanguage(ExtractUrl(search)!)!
            };
        }

        /// <summary>
        /// media.ccc.de serves subtitles from a redirect to a community mirror, and the
        /// shared API client refuses redirects, so the hops are followed here. The first
        /// hop must stay on media.ccc.de; every later hop only has to be a public HTTPS
        /// address, which still blocks a redirect into the private network.
        /// </summary>
        internal async Task<MemoryStream?> DownloadSubtitleAsync(string? search, CancellationToken cancellationToken)
        {
            if (!IsAllowedSubtitleUrl(search))
            {
                return null;
            }

            var url = ExtractUrl(search)!;
            var httpClient = _httpClientFactory.CreateClient(SubtitleClientName);

            for (var hop = 0; hop <= MaxRedirects; hop++)
            {
                await RemoteUrlValidator
                    .ValidatePublicHttpsUrlAsync(url, cancellationToken)
                    .ConfigureAwait(false);

                using var response = await httpClient
                    .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                if (IsRedirect(response.StatusCode))
                {
                    var location = response.Headers.Location;
                    if (location is null)
                    {
                        return null;
                    }

                    url = location.IsAbsoluteUri ? location.ToString() : new Uri(new Uri(url), location).ToString();

                    if (hop == MaxRedirects || !IsSafeRedirectTarget(url))
                    {
                        return null;
                    }

                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var buffer = new MemoryStream();
                await source.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
                buffer.Position = 0;
                return buffer;
            }

            return null;
        }

        private static bool IsRedirect(HttpStatusCode statusCode)
        {
            return statusCode is HttpStatusCode.MovedPermanently
                or HttpStatusCode.Found
                or HttpStatusCode.SeeOther
                or HttpStatusCode.TemporaryRedirect
                or HttpStatusCode.PermanentRedirect;
        }

        /// <summary>
        /// After the first hop the target is a community mirror rather than media.ccc.de,
        /// so the host is not pinned. It must still be absolute HTTPS; the public-address
        /// check in the download loop is what keeps this from reaching the private network.
        /// </summary>
        internal static bool IsSafeRedirectTarget(string? url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri)
                && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        }

        internal static IEnumerable<Recording> FindSubtitles(EventDto? eventDto)
        {
            if (eventDto?.Recordings is not { Count: > 0 })
            {
                return Array.Empty<Recording>();
            }

            return eventDto.Recordings
                .Select(r => new Recording
                {
                    Language = r.Language,
                    MimeType = r.EffectiveMimeType ?? string.Empty,
                    Url = r.EffectiveUrl
                })
                .Where(r => string.Equals(r.MimeType, SubtitleMimeType, StringComparison.OrdinalIgnoreCase))
                .Where(r => !string.IsNullOrWhiteSpace(r.Url))
                .ToList();
        }

        internal static string BuildName(string? language)
        {
            var code = string.IsNullOrWhiteSpace(language) ? "und" : language.Trim();
            return code.ToUpperInvariant();
        }

        internal static string ToIso639(string? language)
        {
            if (string.IsNullOrWhiteSpace(language))
            {
                return "und";
            }

            var code = language.Trim();
            return code.Length == 2 ? code.ToLowerInvariant() : code.ToLowerInvariant();
        }

        /// <summary>
        /// Only subtitles served by media.ccc.de may be fetched, so a crafted search value
        /// cannot be used to reach an arbitrary host.
        /// </summary>
        internal static bool IsAllowedSubtitleUrl(string? search)
        {
            var url = ExtractUrl(search);

            if (url is null
                || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return uri.Host.EndsWith(AllowedSubtitleHostSuffix, StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Host, "media.ccc.de", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Jellyfin normally hands back the id that was offered, but it prefixes that id
        /// with its own hash in some responses, so the URL is taken from the first
        /// https:// onwards rather than assuming the whole value is one.
        /// </summary>
        internal static string? ExtractUrl(string? search)
        {
            if (string.IsNullOrWhiteSpace(search))
            {
                return null;
            }

            var start = search.IndexOf("https://", StringComparison.OrdinalIgnoreCase);
            return start < 0 ? search.Trim() : search[start..].Trim();
        }

        private static string GuessLanguage(string url)
        {
            var name = Path.GetFileNameWithoutExtension(url);
            foreach (var candidate in new[] { "deu", "eng", "fra", "spa", "ita", "por", "rus", "pol", "ces", "nld", "jpn", "chi", "kor" })
            {
                if (name.Contains("-" + candidate + "-", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith("-" + candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            return "und";
        }

        private static string? ResolveSlug(SubtitleSearchRequest request)
        {
            if (request.ProviderIds.TryGetValue(MediaCccEpisodeProvider.ProviderIdKey, out var stored)
                && !string.IsNullOrWhiteSpace(stored))
            {
                return stored.Trim();
            }

            if (string.IsNullOrWhiteSpace(request.MediaPath))
            {
                return null;
            }

            var name = Path.GetFileNameWithoutExtension(request.MediaPath);
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
    }
}
