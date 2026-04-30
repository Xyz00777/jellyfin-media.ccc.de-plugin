using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
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
    /// IndexNumber = episode order within day (deterministic hash of GUID).
    /// ParentIndexNumber = day number within conference (season).
    /// </summary>
    public class MediaCccEpisodeProvider : IRemoteMetadataProvider<Episode, EpisodeInfo>
    {
        private readonly IMediaCccApiClient _apiClient;
        private readonly IRecordingSelector _recordingSelector;

        public string Name => "MediaCccDe Episode";

        public MediaCccEpisodeProvider(IMediaCccApiClient apiClient, IRecordingSelector recordingSelector)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _recordingSelector = recordingSelector ?? throw new ArgumentNullException(nameof(recordingSelector));
        }

        public async Task<MetadataResult<Episode>> GetMetadata(EpisodeInfo info, CancellationToken cancellationToken)
        {
            var eventGuid = info.ProviderIds.TryGetValue("MediaCccDe", out var existingId) 
                ? existingId 
                : info.Name;
            var eventDto = await _apiClient.GetEventAsync(eventGuid, cancellationToken).ConfigureAwait(false);

            if (eventDto == null)
            {
                return new MetadataResult<Episode>
                {
                    HasMetadata = false,
                    Item = null!
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
            var results = new List<RemoteSearchResult>();

            if (searchInfo.ProviderIds.TryGetValue("MediaCccDe", out var id))
            {
                results.Add(new RemoteSearchResult
                {
                    Name = searchInfo.Name,
                    ProviderIds = new Dictionary<string, string> { ["MediaCccDe"] = id }
                });
            }

            return Task.FromResult<IEnumerable<RemoteSearchResult>>(results);
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
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
            if (string.IsNullOrEmpty(eventDto.Guid))
            {
                return 1;
            }

            // Deterministic: SHA256 hash of the GUID, take first 4 bytes as int, mod 100 + 1
            // This is stable across process restarts unlike string.GetHashCode()
            var hashBytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(eventDto.Guid));
            var hashInt = BitConverter.ToInt32(hashBytes, 0);
            return Math.Abs(hashInt % 100) + 1;
        }

        private int? DeriveParentIndexNumber(EventDto eventDto)
        {
            if (string.IsNullOrEmpty(eventDto.Date) || !DateTime.TryParse(eventDto.Date, out var eventDate))
            {
                return null;
            }

            // CCC congresses start Dec 27: Dec 27 = Day 1, Dec 28 = Day 2, etc.
            if (eventDate.Month == 12 && eventDate.Day >= 27)
            {
                return eventDate.Day - 27 + 1;
            }

            // For all other conferences: use day-of-month as season number
            // (1-indexed, matching Jellyfin's season numbering convention)
            return eventDate.Day;
        }
    }
}