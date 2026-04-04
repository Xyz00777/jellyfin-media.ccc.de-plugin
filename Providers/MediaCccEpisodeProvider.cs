using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaCccDe.Api;
using Jellyfin.Plugin.MediaCccDe.Models;
using Jellyfin.Plugin.MediaCccDe.Services;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.MediaCccDe.Providers
{
    /// <summary>
    /// Provides metadata for episodes (talks) from media.ccc.de API.
    /// Episode = Talk from a conference.
    /// IndexNumber = episode order within day.
    /// ParentIndexNumber = day number within conference (season).
    /// </summary>
    public class MediaCccEpisodeProvider : IRemoteMetadataProvider<Episode, EpisodeInfo>
    {
        private readonly IMediaCccApiClient _apiClient;
        private readonly RecordingSelector _recordingSelector;

        public string Name => "MediaCccDe Episode";

        public MediaCccEpisodeProvider(IMediaCccApiClient apiClient, RecordingSelector recordingSelector)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _recordingSelector = recordingSelector ?? throw new ArgumentNullException(nameof(recordingSelector));
        }

        public async Task<MetadataResult<Episode>> GetMetadata(EpisodeInfo info, CancellationToken cancellationToken)
        {
            var eventGuid = info.Name;
            var eventDto = await _apiClient.GetEventAsync(eventGuid, cancellationToken).ConfigureAwait(false);

            if (eventDto == null)
            {
                return new MetadataResult<Episode>
                {
                    HasMetadata = false,
                    Item = null
                };
            }

            var episode = MapEventToEpisode(eventDto);
            
            return new MetadataResult<Episode>
            {
                HasMetadata = true,
                Item = episode
            };
        }

        public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(EpisodeInfo searchInfo, CancellationToken cancellationToken)
        {
            return Task.FromResult(Enumerable.Empty<RemoteSearchResult>());
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            throw new NotImplementedException("Image response not implemented for episode provider");
        }

        private Episode MapEventToEpisode(EventDto eventDto)
        {
            var episode = new Episode
            {
                Name = eventDto.Title ?? string.Empty,
                Overview = eventDto.Description,
                ProviderIds = new Dictionary<string, string>
                {
                    { "MediaCccDe", eventDto.Guid }
                }
            };

            episode.RunTimeTicks = eventDto.Length > 0 
                ? eventDto.Length * 10_000_000L 
                : 0;

            if (!string.IsNullOrEmpty(eventDto.Date) && DateTime.TryParse(eventDto.Date, out var premiereDate))
            {
                episode.PremiereDate = premiereDate;
            }

            episode.OriginalTitle = eventDto.Slug;
            episode.IndexNumber = DeriveIndexNumber(eventDto);
            episode.ParentIndexNumber = DeriveParentIndexNumber(eventDto);

            return episode;
        }

        private int DeriveIndexNumber(EventDto eventDto)
        {
            if (string.IsNullOrEmpty(eventDto.Slug))
            {
                return 1;
            }

            var parts = eventDto.Slug.Split('-');
            if (parts.Length >= 2 && int.TryParse(parts[1], out var eventId))
            {
                return (eventId % 100) + 1;
            }

            return Math.Abs(eventDto.Slug.GetHashCode()) % 100 + 1;
        }

        private int? DeriveParentIndexNumber(EventDto eventDto)
        {
            if (string.IsNullOrEmpty(eventDto.Date) || !DateTime.TryParse(eventDto.Date, out var eventDate))
            {
                return null;
            }

            var dayOfMonth = eventDate.Day;
            
            if (dayOfMonth >= 27 && dayOfMonth <= 31)
            {
                return dayOfMonth - 26;
            }

            return Math.Min(dayOfMonth, 10);
        }
    }
}