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
        private readonly IMediaCccApiClient _apiClient;
        private readonly IRecordingSelector _recordingSelector;
        private readonly string _archivePath;
        private readonly Func<PluginConfiguration>? _configurationProvider;

        public StrmGenerator(
            IMediaCccApiClient apiClient,
            IRecordingSelector recordingSelector,
            string archivePath,
            Func<PluginConfiguration>? configurationProvider = null)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _recordingSelector = recordingSelector ?? throw new ArgumentNullException(nameof(recordingSelector));
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

                var conferenceFirstDay = events
                    .Where(e => !string.IsNullOrEmpty(e.Date) && DateTime.TryParse(e.Date, out _))
                    .Select(e => DateTime.Parse(e.Date!))
                    .OrderBy(d => d)
                    .FirstOrDefault();

                var conferenceFirstDayStr = conferenceFirstDay != default
                    ? conferenceFirstDay.ToString("yyyy-MM-dd")
                    : null;

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

            var conferenceFirstDay = events
                .Where(e => !string.IsNullOrEmpty(e.Date) && DateTime.TryParse(e.Date, out _))
                .Select(e => DateTime.Parse(e.Date!))
                .OrderBy(d => d)
                .FirstOrDefault();

            var conferenceFirstDayStr = conferenceFirstDay != default
                ? conferenceFirstDay.ToString("yyyy-MM-dd")
                : null;

            foreach (var eventDto in events)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var evt = MapToEvent(eventDto);
                var filePath = BuildStrmFilePath(conference, evt, conferenceFirstDayStr);

                await GenerateStrmAsync(conference, evt, conferenceFirstDayStr, cancellationToken).ConfigureAwait(false);
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
