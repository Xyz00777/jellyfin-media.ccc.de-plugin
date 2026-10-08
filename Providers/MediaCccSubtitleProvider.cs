using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;
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

        /// <summary>
        /// Jellyfin 12.2 throws a NullReferenceException in SubtitleManager.TrySaveSubtitle
        /// for these items, so its download endpoint answers 204 and writes nothing. See
        /// https://github.com/jellyfin/jellyfin/issues/18352. Everything below is a
        /// temporary workaround and must be removed once that is fixed upstream.
        /// </summary>
        internal const string UpstreamWorkaround =
            "TEMPORARY: Jellyfin 12.2 cannot save subtitles for remote .strm items " +
            "(NullReferenceException in SubtitleManager.TrySaveSubtitle, issue 18352). " +
            "Media.CCC.de writes the sidecar itself as a workaround; remove once fixed upstream.";
        private const string AllowedSubtitleHostSuffix = ".media.ccc.de";

        private readonly IMediaCccApiClient _apiClient;
        private readonly IConferenceScheduleCache _scheduleCache;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILibraryMonitor? _libraryMonitor;
        private readonly ILogger<MediaCccSubtitleProvider>? _logger;

        public string Name => "Media.CCC.de";

        public IEnumerable<VideoContentType> SupportedMediaTypes => new[] { VideoContentType.Episode, VideoContentType.Movie };

        public MediaCccSubtitleProvider(
            IMediaCccApiClient apiClient,
            IConferenceScheduleCache scheduleCache,
            IHttpClientFactory httpClientFactory,
            ILibraryMonitor? libraryMonitor = null,
            ILogger<MediaCccSubtitleProvider>? logger = null)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _scheduleCache = scheduleCache ?? throw new ArgumentNullException(nameof(scheduleCache));
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _libraryMonitor = libraryMonitor;
            _logger = logger;
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

            var results = subtitles
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

            // Workaround, see UpstreamWorkaround. On Jellyfin 12 the dashboard cannot run
            // the plugin's own pages, so searching for subtitles is the only user-driven
            // hook left to obtain one.
            foreach (var recording in subtitles)
            {
                await SaveSidecarAsync(recording.Url, request.MediaPath, cancellationToken).ConfigureAwait(false);
            }

            return results;
        }

        /// <summary>
        /// Writes the subtitle next to the .strm and tells the library monitor about it, so
        /// the scanner registers it as a local subtitle. Skipped when a usable sidecar is
        /// already there.
        /// </summary>
        internal async Task<bool> SaveSidecarAsync(string subtitleUrl, string? mediaPath, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(mediaPath) || !IsAllowedSubtitleUrl(subtitleUrl))
            {
                return false;
            }

            var target = Path.ChangeExtension(mediaPath, "srt");
            if (File.Exists(target) && new FileInfo(target).Length > 0)
            {
                return true;
            }

            var payload = await DownloadSubtitleAsync(subtitleUrl, cancellationToken).ConfigureAwait(false);
            if (payload is null)
            {
                return false;
            }

            await File.WriteAllBytesAsync(target, payload.ToArray(), cancellationToken).ConfigureAwait(false);

            // Without this the scanner never learns about the file and the playback probe
            // drops it again, which is what issue 15882 describes.
            if (_libraryMonitor is not null)
            {
                try
                {
                    _libraryMonitor.ReportFileSystemChangeBeginning(target);
                    _libraryMonitor.ReportFileSystemChangeComplete(target, true);
                }
                catch (IOException)
                {
                }
            }

            _logger?.LogWarning(UpstreamWorkaround);
            _logger?.LogInformation("Saved subtitle sidecar {Path}", target);
            return true;
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

            // media.ccc.de answers 302 to a community mirror. The default client follows
            // redirects itself and is the same one the metadata providers already use for
            // artwork, so its egress is known good; hand-rolling the hops here only added
            // ways to fail silently.
            await RemoteUrlValidator
                .ValidatePublicHttpsUrlAsync(url, cancellationToken)
                .ConfigureAwait(false);

            var httpClient = _httpClientFactory.CreateClient();

            using var response = await httpClient
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogWarning(
                    "{Workaround} Subtitle fetch returned {Status} for {Url}",
                    UpstreamWorkaround,
                    (int)response.StatusCode,
                    url);
                return null;
            }

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var buffer = new MemoryStream();
            await source.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            buffer.Position = 0;
            return buffer;
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
