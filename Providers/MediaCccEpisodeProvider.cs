using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
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
    /// <summary>
    /// Provides metadata for episodes (talks) from media.ccc.de API.
    /// Episode = Talk from a conference.
    /// Season = conference day, IndexNumber = running order within that day.
    /// </summary>
    public class MediaCccEpisodeProvider : IRemoteMetadataProvider<Episode, EpisodeInfo>, IRemoteImageProvider
    {
        internal const string ProviderIdKey = "MediaCccDe";
        private const int OverviewMaxLines = 4;
        private const int OverviewLineWidth = 90;

        private static readonly Regex HtmlTag = new("<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex SeasonFolder = new(
            @"^season\s*\d+$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly IMediaCccApiClient _apiClient;
        private readonly IConferenceScheduleCache _scheduleCache;
        private readonly IHttpClientFactory _httpClientFactory;

        public string Name => "MediaCccDe Episode";

        public MediaCccEpisodeProvider(
            IMediaCccApiClient apiClient,
            IConferenceScheduleCache scheduleCache,
            IHttpClientFactory httpClientFactory)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _scheduleCache = scheduleCache ?? throw new ArgumentNullException(nameof(scheduleCache));
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        }

        public async Task<MetadataResult<Episode>> GetMetadata(EpisodeInfo info, CancellationToken cancellationToken)
        {
            info.ProviderIds.TryGetValue(ProviderIdKey, out var storedId);
            var identifier = !string.IsNullOrWhiteSpace(storedId) ? storedId : info.Name;

            if (string.IsNullOrWhiteSpace(identifier))
            {
                return NotFound();
            }

            var acronym = ResolveConferenceAcronym(info.Path);
            var schedule = acronym is null
                ? null
                : await _scheduleCache.GetAsync(acronym, cancellationToken).ConfigureAwait(false);

            // The conference listing already carries title, description, date and poster
            // for every talk, so a cached schedule resolves the event without an
            // additional per-episode request.
            var scheduled = ResolveFromSchedule(schedule, identifier);
            var eventDto = scheduled?.Event
                ?? await FetchEventAsync(identifier, cancellationToken).ConfigureAwait(false);

            if (eventDto == null)
            {
                return NotFound();
            }

            var episode = MapEventToEpisode(eventDto, scheduled?.Position);
            if (episode == null)
            {
                return NotFound();
            }

            return new MetadataResult<Episode>
            {
                HasMetadata = true,
                Item = episode,
                RemoteImages = BuildRemoteImages(eventDto.PosterUrl)
            };
        }

        internal static List<(string Url, ImageType Type)> BuildRemoteImages(string? posterUrl)
        {
            return string.IsNullOrWhiteSpace(posterUrl)
                ? new List<(string, ImageType)>()
                : new List<(string, ImageType)> { (posterUrl, ImageType.Primary) };
        }

        public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(EpisodeInfo searchInfo, CancellationToken cancellationToken)
        {
            var results = new List<RemoteSearchResult>();

            if (searchInfo.ProviderIds.TryGetValue(ProviderIdKey, out var id) && !string.IsNullOrWhiteSpace(id))
            {
                results.Add(new RemoteSearchResult
                {
                    Name = searchInfo.Name,
                    ProviderIds = new Dictionary<string, string> { [ProviderIdKey] = id }
                });
            }

            return Task.FromResult<IEnumerable<RemoteSearchResult>>(results);
        }

        public bool Supports(BaseItem item)
        {
            return item is Episode;
        }

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            return new[] { ImageType.Primary };
        }

        /// <summary>
        /// Jellyfin asks the registered image providers for artwork per item, so the
        /// poster is resolved here from the slug rather than pushed through
        /// MetadataResult.RemoteImages, which no provider consumes on its own.
        /// </summary>
        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            if (item is not Episode episode)
            {
                return Array.Empty<RemoteImageInfo>();
            }

            var slug = episode.ProviderIds.TryGetValue(ProviderIdKey, out var storedId)
                ? storedId
                : episode.OriginalTitle;

            if (string.IsNullOrWhiteSpace(slug))
            {
                return Array.Empty<RemoteImageInfo>();
            }

            var acronym = ResolveConferenceAcronym(episode.Path);
            if (acronym is null)
            {
                return Array.Empty<RemoteImageInfo>();
            }

            var schedule = await _scheduleCache.GetAsync(acronym, cancellationToken).ConfigureAwait(false);
            if (schedule is null || !schedule.TryGet(slug, slug, out var scheduled) || scheduled is null)
            {
                return Array.Empty<RemoteImageInfo>();
            }

            var posterUrl = scheduled.Event.PosterUrl;
            if (string.IsNullOrWhiteSpace(posterUrl))
            {
                return Array.Empty<RemoteImageInfo>();
            }

            return new[]
            {
                new RemoteImageInfo
                {
                    Url = posterUrl,
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

        internal static Episode? MapEventToEpisode(EventDto eventDto, EventPosition? position)
        {
            if (string.IsNullOrWhiteSpace(eventDto.Slug))
            {
                return null;
            }

            var episode = new Episode
            {
                Name = eventDto.Title ?? string.Empty,
                Overview = TruncateOverview(eventDto.Description),
                ProviderIds = new Dictionary<string, string>
                {
                    { ProviderIdKey, eventDto.Slug }
                }
            };

            if (eventDto.Length > 0)
            {
                episode.RunTimeTicks = eventDto.Length * 10_000_000L;
            }

            if (TryParseInstant(eventDto.Date, out var premiere))
            {
                episode.PremiereDate = premiere.LocalDateTime;
            }

            episode.OriginalTitle = eventDto.Slug;

            if (position.HasValue)
            {
                episode.ParentIndexNumber = position.Value.Season;
                episode.IndexNumber = position.Value.Episode;
            }

            return episode;
        }

        /// <summary>
        /// Collapses the talk description into at most four wrapped lines so the episode
        /// overview stays readable instead of rendering a wall of text.
        /// </summary>
        internal static string? TruncateOverview(string? description)
        {
            if (string.IsNullOrWhiteSpace(description))
            {
                return description;
            }

            var text = WebUtility.HtmlDecode(HtmlTag.Replace(description, " "));
            var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
            {
                return text.Trim();
            }

            var builder = new StringBuilder();
            var line = new StringBuilder();

            foreach (var word in words)
            {
                if (line.Length > 0 && line.Length + 1 + word.Length > OverviewLineWidth)
                {
                    AppendLine(builder, line);
                    if (LineCount(builder) >= OverviewMaxLines)
                    {
                        return builder.ToString().TrimEnd();
                    }

                    line.Clear();
                }

                if (line.Length > 0)
                {
                    line.Append(' ');
                }

                line.Append(word);
            }

            if (line.Length > 0)
            {
                AppendLine(builder, line);
            }

            return builder.ToString().TrimEnd();
        }

        private static void AppendLine(StringBuilder builder, StringBuilder line)
        {
            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(line);
        }

        private static int LineCount(StringBuilder builder)
        {
            var count = 1;
            for (var i = 0; i < builder.Length; i++)
            {
                if (builder[i] == '\n')
                {
                    count++;
                }
            }

            return count;
        }

        private static ScheduledEvent? ResolveFromSchedule(ConferenceSchedule? schedule, string identifier)
        {
            if (schedule is null)
            {
                return null;
            }

            return schedule.TryGet(identifier, identifier, out var scheduled) ? scheduled : null;
        }

        private async Task<EventDto?> FetchEventAsync(string identifier, CancellationToken cancellationToken)
        {
            try
            {
                return await _apiClient.GetEventAsync(identifier, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                return null;
            }
        }

        /// <summary>
        /// The archive layout is &lt;root&gt;/&lt;acronym&gt;/Season NN/&lt;slug&gt;.strm, so the
        /// conference is two directories above the file unless the item sits directly in
        /// a season folder.
        /// </summary>
        internal static string? ResolveConferenceAcronym(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var directory = System.IO.Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory))
            {
                return null;
            }

            var parent = System.IO.Directory.GetParent(directory)?.Name;
            if (!string.IsNullOrWhiteSpace(parent) && !SeasonFolder.IsMatch(parent))
            {
                return parent;
            }

            var grandParent = System.IO.Directory.GetParent(directory)?.Parent?.Name;
            return string.IsNullOrWhiteSpace(grandParent) ? null : grandParent;
        }

        private static bool TryParseInstant(string? value, out DateTimeOffset instant)
        {
            return DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out instant);
        }

        private static MetadataResult<Episode> NotFound()
        {
            return new MetadataResult<Episode>
            {
                HasMetadata = false,
                Item = null!
            };
        }
    }
}
