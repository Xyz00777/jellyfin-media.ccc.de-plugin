using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaCccDe.Api;
using Jellyfin.Plugin.MediaCccDe.Models;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.MediaCccDe.Services
{
    public sealed class WatchlistDownloadService : IWatchlistDownloadService
    {
        private readonly IMediaCccApiClient _apiClient;
        private readonly IDownloadQueue _downloadQueue;
        private readonly IUserDataManager _userDataManager;
        private readonly IUserLibraryService _userLibraryService;
        private readonly IUserManager _userManager;
        private readonly IRecordingSelector _recordingSelector;
        private readonly Func<PluginConfiguration> _configurationProvider;

        public WatchlistDownloadService(
            IMediaCccApiClient apiClient,
            IDownloadQueue downloadQueue,
            IUserDataManager userDataManager,
            IUserLibraryService userLibraryService,
            IUserManager userManager,
            IRecordingSelector recordingSelector,
            Func<PluginConfiguration> configurationProvider)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _downloadQueue = downloadQueue ?? throw new ArgumentNullException(nameof(downloadQueue));
            _userDataManager = userDataManager ?? throw new ArgumentNullException(nameof(userDataManager));
            _userLibraryService = userLibraryService ?? throw new ArgumentNullException(nameof(userLibraryService));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _recordingSelector = recordingSelector ?? throw new ArgumentNullException(nameof(recordingSelector));
            _configurationProvider = configurationProvider ?? throw new ArgumentNullException(nameof(configurationProvider));
        }

        public async Task<DownloadQueueItem?> EnqueueAsync(
            Guid userId,
            string eventGuid,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(eventGuid))
            {
                throw new ArgumentException("Event GUID cannot be empty", nameof(eventGuid));
            }

            var user = _userManager.GetUserById(userId)
                ?? throw new ArgumentException($"User with ID {userId} was not found.", nameof(userId));
            var eventDto = await _apiClient.GetEventAsync(eventGuid, cancellationToken).ConfigureAwait(false);
            if (eventDto == null)
            {
                return null;
            }

            await _userDataManager.EnsureLoadedAsync(userId).ConfigureAwait(false);
            var recordings = eventDto.Recordings?.Select(MapRecording).ToList() ?? new List<Recording>();
            var userData = _userDataManager.GetUserData(userId);
            var configuration = _configurationProvider();
            var recording = _recordingSelector.SelectBestRecording(
                recordings,
                new RecordingPreferences
                {
                    PreferredLanguages = userData.PreferredAudioLanguages.ToList(),
                    QualityPreference = configuration.PreferredQuality
                });

            if (recording == null || string.IsNullOrWhiteSpace(recording.Url))
            {
                return null;
            }

            var library = await _userLibraryService
                .GetOrCreateUserLibraryAsync(userId, user.Username)
                .ConfigureAwait(false);
            var destinationPath = BuildDestinationPath(library.Path, eventDto, recording);
            var item = new DownloadQueueItem
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                EventGuid = eventDto.Guid,
                EventTitle = eventDto.Title,
                ConferenceAcronym = eventDto.ConferenceId.ToString(),
                RecordingUrl = recording.Url,
                DestinationPath = destinationPath,
                Priority = 0
            };

            await _downloadQueue.EnqueueAsync(item).ConfigureAwait(false);
            return item;
        }

        public async Task<IReadOnlyList<DownloadQueueItem>> GetUserQueueAsync(Guid userId)
        {
            var items = await _downloadQueue.GetUserQueueAsync(userId).ConfigureAwait(false);
            return items.ToList();
        }

        private static string BuildDestinationPath(string libraryPath, EventDto eventDto, Recording recording)
        {
            var root = Path.GetFullPath(libraryPath);
            var baseName = StrmHelper.SanitizeFileName($"{eventDto.Slug}-{eventDto.Guid}");
            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = eventDto.Guid;
            }

            var format = StrmHelper.SanitizeFileName(recording.Format ?? string.Empty).TrimStart('.');
            var extension = string.IsNullOrWhiteSpace(format) ? "mp4" : format;
            var destinationPath = Path.GetFullPath(Path.Combine(root, $"{baseName}.{extension}"));
            var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
                ? root
                : root + Path.DirectorySeparatorChar;

            if (!destinationPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Download path escaped the user's watchlist library.");
            }

            return destinationPath;
        }

        private static Recording MapRecording(RecordingDto dto)
        {
            return new Recording
            {
                Id = dto.Id,
                Size = dto.Size ?? 0,
                Length = dto.Length ?? 0,
                MimeType = dto.EffectiveMimeType ?? string.Empty,
                Language = dto.Language ?? string.Empty,
                Url = dto.EffectiveUrl,
                Format = dto.EffectiveFormat,
                HighQuality = dto.HighQuality,
                Width = dto.Width,
                Height = dto.Height,
                FileSize = dto.FileSize,
                Bitrate = dto.Bitrate
            };
        }
    }
}
