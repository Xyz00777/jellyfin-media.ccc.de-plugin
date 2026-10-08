using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaCccDe.Api;
using Jellyfin.Plugin.MediaCccDe.Models;

namespace Jellyfin.Plugin.MediaCccDe.Services
{
    public class StrmGenerator : IStrmGenerator
    {
        private const string SubtitleMimeType = "application/x-subrip";

        private readonly IMediaCccApiClient _apiClient;
        private readonly IRecordingSelector _recordingSelector;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _archivePath;
        private readonly Func<PluginConfiguration>? _configurationProvider;

        public StrmGenerator(
            IMediaCccApiClient apiClient,
            IRecordingSelector recordingSelector,
            IHttpClientFactory httpClientFactory,
            string archivePath,
            Func<PluginConfiguration>? configurationProvider = null)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _recordingSelector = recordingSelector ?? throw new ArgumentNullException(nameof(recordingSelector));
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _archivePath = archivePath ?? throw new ArgumentNullException(nameof(archivePath));
            _configurationProvider = configurationProvider;
        }

        public async Task<StrmResult?> GenerateStrmAsync(Conference conference, Event evt, CancellationToken cancellationToken)
        {
            return await GenerateStrmAsync(conference, evt, null, cancellationToken);
        }

        private async Task<StrmResult?> GenerateStrmAsync(Conference conference, Event evt, string? conferenceFirstDay, CancellationToken cancellationToken)
        {
            if (evt.Recordings == null || !evt.Recordings.Any())
            {
                return null;
            }

            var configuration = _configurationProvider?.Invoke();
            var recording = _recordingSelector.SelectBestRecording(
                evt.Recordings,
                configuration == null
                    ? null
                    : new RecordingPreferences
                    {
                        PreferredLanguages = configuration.PreferredAudioLanguages,
                        QualityPreference = configuration.PreferredQuality
                    });
            if (recording == null)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(recording.Url))
            {
                return null;
            }

            var filePath = BuildStrmFilePath(conference, evt, conferenceFirstDay);
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (configuration is { DownloadSubtitles: true })
            {
                await DownloadSubtitleSidecarAsync(evt, filePath, configuration, cancellationToken).ConfigureAwait(false);
            }

            if (!File.Exists(filePath) || !string.Equals(await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false), recording.Url, StringComparison.Ordinal))
            {
                // Write to a sibling temp file and move into place, so an interrupted
                // write can never leave a truncated .strm that later runs skip.
                var tempPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await File.WriteAllTextAsync(tempPath, recording.Url, cancellationToken).ConfigureAwait(false);
                    File.Move(tempPath, filePath, overwrite: true);
                }
                catch
                {
                    try
                    {
                        if (File.Exists(tempPath))
                        {
                            File.Delete(tempPath);
                        }
                    }
                    catch (IOException)
                    {
                    }

                    throw;
                }
            }

            return new StrmResult
            {
                FilePath = filePath,
                RecordingUrl = recording.Url,
                ConferenceAcronym = conference.Acronym,
                EventSlug = evt.Slug
            };
        }

        /// <summary>
        /// Jellyfin only discovers subtitles that sit on local disk beside the media file,
        /// so an enabled setting means fetching the sidecar once and leaving it there.
        /// </summary>
        private async Task DownloadSubtitleSidecarAsync(
            Event evt,
            string strmPath,
            PluginConfiguration configuration,
            CancellationToken cancellationToken)
        {
            var subtitle = SelectSubtitle(evt.Recordings, configuration.PreferredSubtitleLanguages);
            if (subtitle == null || string.IsNullOrWhiteSpace(subtitle.Url))
            {
                return;
            }

            var sidecarPath = Path.ChangeExtension(strmPath, ".srt");
            if (File.Exists(sidecarPath) && new FileInfo(sidecarPath).Length > 0)
            {
                return;
            }

            await RemoteUrlValidator.ValidatePublicHttpsUrlAsync(subtitle.Url, cancellationToken).ConfigureAwait(false);

            var httpClient = _httpClientFactory.CreateClient();
            using var response = await httpClient.GetAsync(subtitle.Url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var target = File.Create(sidecarPath);
            await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        }

        internal static Recording? SelectSubtitle(IReadOnlyCollection<Recording> recordings, IReadOnlyList<string> preferredLanguages)
        {
            var candidates = recordings
                .Where(r => string.Equals(r.MimeType, SubtitleMimeType, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (candidates.Count == 0)
            {
                return null;
            }

            foreach (var language in preferredLanguages)
            {
                var match = candidates.FirstOrDefault(r =>
                    string.Equals(r.Language, language, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    return match;
                }
            }

            return candidates[0];
        }

        public async Task<List<StrmResult>> GenerateSeriesStrmTreeAsync(CancellationToken cancellationToken)
        {
            var results = new List<StrmResult>();

            var conferences = await _apiClient.GetConferencesAsync(cancellationToken).ConfigureAwait(false);
            if (conferences == null || !conferences.Any())
            {
                return results;
            }

            foreach (var conferenceDto in conferences)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var conference = MapToConference(conferenceDto);
                var events = await GetEventsAsync(conferenceDto, cancellationToken).ConfigureAwait(false);

                if (events == null || !events.Any())
                {
                    continue;
                }

                var conferenceFirstDayStr = StrmHelper.ResolveConferenceFirstDay(events.Select(e => e.Date));

                foreach (var eventDto in events)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var evt = MapToEvent(eventDto);
                    var filePath = BuildStrmFilePath(conference, evt, conferenceFirstDayStr);

                    if (File.Exists(filePath))
                    {
                        continue;
                    }

                    var result = await GenerateStrmAsync(conference, evt, conferenceFirstDayStr, cancellationToken).ConfigureAwait(false);
                    if (result != null)
                    {
                        results.Add(result);
                    }
                }
            }

            return results;
        }

        private string BuildStrmFilePath(Conference conference, Event evt, string? conferenceFirstDay = null)
        {
            var sanitizedAcronym = StrmHelper.NormalizeConferenceDirectory(conference.Acronym);
            var sanitizedSlug = StrmHelper.SanitizeFileName(evt.Slug);

            int? dayNumber = StrmHelper.ExtractDayNumber(evt.Date, conferenceFirstDay);

            string relativePath;
            if (dayNumber.HasValue)
            {
                relativePath = Path.Combine(sanitizedAcronym, $"Season {dayNumber.Value:D2}", $"{sanitizedSlug}.strm");
            }
            else
            {
                relativePath = Path.Combine(sanitizedAcronym, $"{sanitizedSlug}.strm");
            }

            return Path.Combine(_archivePath, relativePath);
        }

        private Conference MapToConference(ConferenceDto dto)
        {
            return new Conference
            {
                Title = dto.Title ?? string.Empty,
                Acronym = dto.Acronym ?? string.Empty,
                Slug = dto.Slug ?? string.Empty,
                AspectRatio = dto.AspectRatio ?? string.Empty,
                UpdatedAt = dto.UpdatedAt,
                Url = dto.Url,
                ScheduleUrl = dto.ScheduleUrl
            };
        }

        private Event MapToEvent(EventDto dto)
        {
            return new Event
            {
                Guid = dto.Guid ?? string.Empty,
                Title = dto.Title ?? string.Empty,
                Slug = dto.Slug ?? string.Empty,
                Description = dto.Description,
                Link = dto.Link,
                Date = dto.Date,
                Length = dto.Length,
                ConferenceId = dto.ConferenceId,
                Recordings = dto.Recordings?.Select(r => MapToRecording(r)).ToList() ?? new List<Recording>()
            };
        }

        private Recording MapToRecording(RecordingDto dto)
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

        public async Task CreateStrmFilesForConference(ConferenceDto conferenceDto, CancellationToken cancellationToken)
        {
            var conference = MapToConference(conferenceDto);
            var events = await GetEventsAsync(conferenceDto, cancellationToken).ConfigureAwait(false);

            if (events == null || !events.Any())
            {
                return;
            }

            var conferenceFirstDayStr = StrmHelper.ResolveConferenceFirstDay(events.Select(e => e.Date));

            foreach (var eventDto in events)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var evt = MapToEvent(eventDto);
                var filePath = BuildStrmFilePath(conference, evt, conferenceFirstDayStr);

                await GenerateStrmAsync(conference, evt, conferenceFirstDayStr, cancellationToken).ConfigureAwait(false);
            }

            RemoveMisplacedStrmFiles(conference, events, conferenceFirstDayStr);
        }

        /// <summary>
        /// Earlier releases placed talks in folders derived from a broken day number, and
        /// those files were never removed, so a talk could exist twice under two season
        /// folders. A misplaced copy is deleted only when the same talk is already present
        /// at its correct path, which keeps this safe for a conference whose API data is
        /// only partially available.
        /// </summary>
        internal void RemoveMisplacedStrmFiles(Conference conference, IReadOnlyCollection<EventDto> events, string? conferenceFirstDay)
        {
            var expectedPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var eventDto in events)
            {
                var evt = MapToEvent(eventDto);
                if (string.IsNullOrWhiteSpace(evt.Slug))
                {
                    continue;
                }

                expectedPaths[evt.Slug] = BuildStrmFilePath(conference, evt, conferenceFirstDay);
            }

            if (expectedPaths.Count == 0)
            {
                return;
            }

            var conferenceDirectory = Path.Combine(
                _archivePath,
                StrmHelper.NormalizeConferenceDirectory(conference.Acronym));

            if (!Directory.Exists(conferenceDirectory))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(conferenceDirectory, "*.strm", SearchOption.AllDirectories))
            {
                var slug = Path.GetFileNameWithoutExtension(file);
                if (!expectedPaths.TryGetValue(slug, out var expectedPath))
                {
                    continue;
                }

                if (!string.Equals(
                        Path.GetFullPath(file),
                        Path.GetFullPath(expectedPath),
                        StringComparison.Ordinal))
                {
                    TryDeleteFile(file);
                }
            }

            RemoveEmptyDirectories(conferenceDirectory);
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static void RemoveEmptyDirectories(string root)
        {
            var directories = Directory.GetDirectories(root, "Season *", SearchOption.AllDirectories)
                .OrderByDescending(d => d.Length)
                .ToList();

            foreach (var directory in directories)
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    try
                    {
                        Directory.Delete(directory);
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            }
        }

        public bool StrmFilesExistForConference(ConferenceDto conferenceDto)
        {
            var conference = MapToConference(conferenceDto);
            var conferenceDir = Path.Combine(_archivePath, StrmHelper.NormalizeConferenceDirectory(conference.Acronym));
            return Directory.Exists(conferenceDir);
        }

        private Task<EventDto[]> GetEventsAsync(ConferenceDto conference, CancellationToken cancellationToken)
        {
            if (conference.Id > 0)
            {
                return _apiClient.GetEventsAsync(conference.Id, cancellationToken);
            }

            return string.IsNullOrWhiteSpace(conference.Acronym)
                ? Task.FromResult(Array.Empty<EventDto>())
                : _apiClient.GetEventsAsync(conference.Acronym, cancellationToken);
        }
    }
}
