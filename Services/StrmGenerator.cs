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
    public interface IStrmGenerator
    {
        Task<StrmResult?> GenerateStrmAsync(Conference conference, Event evt, CancellationToken cancellationToken);
        Task<List<StrmResult>> GenerateSeriesStrmTreeAsync(CancellationToken cancellationToken);
        Task CreateStrmFilesForConference(ConferenceDto conference, CancellationToken cancellationToken);
        bool StrmFilesExistForConference(ConferenceDto conference);
    }

    public class StrmResult
    {
        public string FilePath { get; set; } = string.Empty;
        public string RecordingUrl { get; set; } = string.Empty;
        public string ConferenceAcronym { get; set; } = string.Empty;
        public string EventSlug { get; set; } = string.Empty;
    }

    public class StrmGenerator : IStrmGenerator
    {
        private readonly IMediaCccApiClient _apiClient;
        private readonly IRecordingSelector _recordingSelector;
        private readonly string _archivePath;

        public StrmGenerator(IMediaCccApiClient apiClient, IRecordingSelector recordingSelector, string archivePath)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _recordingSelector = recordingSelector ?? throw new ArgumentNullException(nameof(recordingSelector));
            _archivePath = archivePath ?? throw new ArgumentNullException(nameof(archivePath));
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

            var recording = _recordingSelector.SelectBestRecording(evt.Recordings, null);
            if (recording == null)
            {
                return null;
            }

            var filePath = BuildStrmFilePath(conference, evt, conferenceFirstDay);
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(filePath, recording.Url, cancellationToken).ConfigureAwait(false);

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
                var events = await _apiClient.GetEventsAsync(conferenceDto.Id, cancellationToken).ConfigureAwait(false);

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
                Size = dto.Size,
                Length = dto.Length,
                MimeType = dto.MimeType ?? string.Empty,
                Language = dto.Language ?? string.Empty,
                Url = dto.Url ?? string.Empty,
                Format = dto.Format,
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
            var events = await _apiClient.GetEventsAsync(conferenceDto.Id, cancellationToken).ConfigureAwait(false);

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

                if (File.Exists(filePath))
                {
                    continue;
                }

                await GenerateStrmAsync(conference, evt, conferenceFirstDayStr, cancellationToken).ConfigureAwait(false);
            }
        }

        public bool StrmFilesExistForConference(ConferenceDto conferenceDto)
        {
            var conference = MapToConference(conferenceDto);
            var conferenceDir = Path.Combine(_archivePath, StrmHelper.NormalizeConferenceDirectory(conference.Acronym));
            return Directory.Exists(conferenceDir);
        }
    }
}