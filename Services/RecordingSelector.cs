using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.MediaCccDe.Models;

namespace Jellyfin.Plugin.MediaCccDe.Services
{
    /// <summary>
    /// Selects the best recording from available options based on user preferences.
    /// Priority order: video only &gt; Language &gt; multi-language &gt; MP4/H.264 &gt; Quality &gt; Format &gt; Resolution &gt; File Size &gt; Bitrate.
    /// </summary>
    public class RecordingSelector : IRecordingSelector
    {
        /// <summary>
        /// Selects the best recording from a list based on user preferences.
        /// </summary>
        /// <param name="recordings">Available recordings to choose from.</param>
        /// <param name="preferences">User preferences for language, quality, and format.</param>
        /// <returns>The best matching recording, or null if no recordings available.</returns>
        public Recording? SelectBestRecording(
            IEnumerable<Recording> recordings,
            RecordingPreferences? preferences)
        {
            var recordingList = recordings.Where(IsPlayableVideo).ToList();
            if (recordingList.Count == 0)
                return null;

            if (preferences == null)
            {
                return GetBestFallback(recordingList, new List<string>());
            }

            var preferredLanguages = preferences.PreferredLanguages ?? new List<string>();
            var qualityPreference = preferences.QualityPreference ?? "hd";
            var preferredFormat = preferences.PreferredFormat;

            var candidates = recordingList.AsEnumerable();

            candidates = FilterByLanguage(candidates, preferredLanguages);
            candidates = FilterByQuality(candidates, qualityPreference);
            candidates = FilterByFormat(candidates, preferredFormat);
            candidates = OrderByQualityMetrics(candidates);

            return candidates.FirstOrDefault() ??
                   GetBestFallback(recordingList, preferredLanguages);
        }

        /// <summary>
        /// The API returns audio-only renditions (mp3/opus) and subtitle files (srt/vtt)
        /// alongside the video files, and some of them are flagged high quality. Writing
        /// any of those into a .strm produces an unplayable entry, so they are excluded
        /// before ranking. A .strm holds a single URL, so a combined-language file is the
        /// only way Jellyfin can offer an audio track choice.
        /// </summary>
        private static bool IsPlayableVideo(Recording recording)
        {
            if (!string.IsNullOrWhiteSpace(recording.MimeType))
            {
                return recording.MimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase);
            }

            var format = recording.Format ?? string.Empty;
            return !format.Equals("mp3", StringComparison.OrdinalIgnoreCase)
                && !format.Equals("opus", StringComparison.OrdinalIgnoreCase)
                && !format.Equals("srt", StringComparison.OrdinalIgnoreCase)
                && !format.Equals("vtt", StringComparison.OrdinalIgnoreCase)
                && !format.Equals("ttml", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsMultiLanguage(Recording recording)
        {
            return !string.IsNullOrWhiteSpace(recording.Language)
                && recording.Language.Contains('-', StringComparison.Ordinal);
        }

        private static bool IsMp4(Recording recording)
        {
            return recording.Format?.Equals("mp4", StringComparison.OrdinalIgnoreCase) == true;
        }

        private IEnumerable<Recording> FilterByLanguage(
            IEnumerable<Recording> recordings,
            List<string> preferredLanguages)
        {
            if (preferredLanguages == null || preferredLanguages.Count == 0)
                return recordings;

            foreach (var lang in preferredLanguages)
            {
                var matches = recordings.Where(r =>
                    r.Language.Equals(lang, StringComparison.OrdinalIgnoreCase) ||
                    (r.Language != null && r.Language.StartsWith(lang + "-", StringComparison.OrdinalIgnoreCase)));

                if (matches.Any())
                    return matches;
            }

            return recordings;
        }

        private IEnumerable<Recording> FilterByQuality(
            IEnumerable<Recording> recordings,
            string qualityPreference)
        {
            if (string.IsNullOrEmpty(qualityPreference))
                return recordings;

            bool preferHd = qualityPreference.Equals("hd", StringComparison.OrdinalIgnoreCase);

            var filtered = recordings.Where(r =>
                r.HighQuality.GetValueOrDefault() == preferHd);

            return filtered.Any() ? filtered : recordings;
        }

        private IEnumerable<Recording> FilterByFormat(
            IEnumerable<Recording> recordings,
            string? preferredFormat)
        {
            if (string.IsNullOrEmpty(preferredFormat))
            {
                var mp4 = recordings.Where(r =>
                    r.Format?.Equals("mp4", StringComparison.OrdinalIgnoreCase) == true);
                return mp4.Any() ? mp4 : recordings;
            }

            var filtered = recordings.Where(r =>
                r.Format?.Equals(preferredFormat, StringComparison.OrdinalIgnoreCase) == true);

            return filtered.Any() ? filtered : recordings;
        }

        private IEnumerable<Recording> OrderByQualityMetrics(IEnumerable<Recording> recordings)
        {
            return recordings
                .OrderByDescending(r => IsMultiLanguage(r) ? 1 : 0)
                .ThenByDescending(r => IsMp4(r) ? 1 : 0)
                .ThenByDescending(r => r.Width ?? 0)
                .ThenByDescending(r => r.FileSize ?? r.Size)
                .ThenByDescending(r => r.Bitrate ?? 0);
        }

        private Recording? GetBestFallback(
            List<Recording> recordings,
            List<string> preferredLanguages)
        {
            var candidates = recordings.AsEnumerable();

            if (preferredLanguages != null && preferredLanguages.Count > 0)
            {
                candidates = FilterByLanguage(candidates, preferredLanguages);
            }

            return OrderByQualityMetrics(candidates).FirstOrDefault();
        }
    }
}
