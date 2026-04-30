using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaCccDe.Api;
using Jellyfin.Plugin.MediaCccDe.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediaCccDe.Services
{
    /// <summary>
    /// Service that generates directory tree structure for Media CCC archive.
    /// Creates:
    /// - Series folder for each conference
    /// - Season folder for each day
    /// - Episode .strm file for each event
    /// </summary>
    public class StrmTreeGenerator
    {
        private readonly IMediaCccApiClient _apiClient;
        private readonly IStrmFileGenerator _strmGenerator;
        private readonly ILogger<StrmTreeGenerator> _logger;

        public StrmTreeGenerator(
            IMediaCccApiClient apiClient,
            IStrmFileGenerator strmGenerator,
            ILogger<StrmTreeGenerator> logger)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _strmGenerator = strmGenerator ?? throw new ArgumentNullException(nameof(strmGenerator));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<TreeGenerationResult> GenerateTreeAsync(string archivePath, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(archivePath))
            {
                throw new ArgumentNullException(nameof(archivePath));
            }

            var result = new TreeGenerationResult();

            if (!Directory.Exists(archivePath))
            {
                Directory.CreateDirectory(archivePath);
            }

            IReadOnlyList<ConferenceDto> conferences;
            try
            {
                conferences = await _apiClient.GetConferencesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to fetch conferences");
                throw;
            }

            if (conferences == null || conferences.Count == 0)
            {
                return result;
            }

            foreach (var conference in conferences)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (conference.Id <= 0)
                {
                    continue;
                }

                try
                {
                    await ProcessConferenceAsync(archivePath, conference, result, cancellationToken).ConfigureAwait(false);
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogError(ex, "Failed to process conference {Acronym}", conference.Acronym);
                    result.FailedConferences++;
                }
            }

            return result;
        }

        private async Task ProcessConferenceAsync(
            string archivePath,
            ConferenceDto conference,
            TreeGenerationResult result,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation("Processing conference: {Acronym}", conference.Acronym);

            EventDto[] events;
            try
            {
                events = await _apiClient.GetEventsAsync(conference.Id, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Failed to fetch events for conference {Acronym}", conference.Acronym);
                throw;
            }

            if (events == null || events.Length == 0)
            {
                return;
            }

            var sanitizedAcronym = StrmHelper.NormalizeConferenceDirectory(conference.Acronym ?? "unknown");
            var seriesPath = Path.Combine(archivePath, sanitizedAcronym);

            if (!Directory.Exists(seriesPath))
            {
                Directory.CreateDirectory(seriesPath);
                result.SeriesFoldersCreated++;
            }

            var conferenceFirstDay = events
                .Where(e => !string.IsNullOrEmpty(e.Date) && DateTime.TryParse(e.Date, out _))
                .Select(e => DateTime.Parse(e.Date!))
                .OrderBy(d => d)
                .FirstOrDefault();

            var conferenceFirstDayStr = conferenceFirstDay != default
                ? conferenceFirstDay.ToString("yyyy-MM-dd")
                : null;

            var eventsByDay = events
                .GroupBy(e => StrmHelper.ExtractDayNumber(e.Date, conferenceFirstDayStr))
                .OrderBy(g => g.Key);

            var existingFiles = Directory.Exists(seriesPath)
                ? Directory.GetFiles(seriesPath, "*.strm", SearchOption.AllDirectories)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var currentFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var dayGroup in eventsByDay)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var dayNumber = dayGroup.Key;
                string seasonPath;

                if (dayNumber.HasValue)
                {
                    var seasonFolder = $"Season {dayNumber.Value:D2}";
                    seasonPath = Path.Combine(seriesPath, seasonFolder);
                    
                    if (!Directory.Exists(seasonPath))
                    {
                        Directory.CreateDirectory(seasonPath);
                        result.SeasonsCreated++;
                    }
                }
                else
                {
                    seasonPath = seriesPath;
                }

                foreach (var evt in dayGroup)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        await _strmGenerator.GenerateStrmAsync(seasonPath, evt, cancellationToken).ConfigureAwait(false);
                        result.FilesCreated++;

                        var sanitizedSlug = StrmHelper.SanitizeFileName(evt.Slug ?? evt.Guid ?? "unknown");
                        var fileName = $"{sanitizedSlug}.strm";
                        var filePath = Path.Combine(seasonPath, fileName);
                        currentFiles.Add(filePath);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogError(ex, "Failed to generate strm file for event {Guid}", evt.Guid);
                    }
                }
            }

            // Remove stale files (files that existed before but are no longer in current API response)
            foreach (var existingFile in existingFiles)
            {
                if (!currentFiles.Contains(existingFile))
                {
                    try
                    {
                        File.Delete(existingFile);
                        _logger.LogInformation("Removed stale file: {FilePath}", existingFile);
                    }
                    catch (FileNotFoundException)
                    {
                    }
                    catch (DirectoryNotFoundException)
                    {
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to remove stale file: {FilePath}", existingFile);
                    }
                }
            }

            result.ConferencesProcessed++;
        }

    }
}