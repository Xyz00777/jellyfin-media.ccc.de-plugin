using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.MediaCccDe.Models;

namespace Jellyfin.Plugin.MediaCccDe.Services
{
    /// <summary>
    /// Selects the best recording from available options based on user preferences.
    /// Priority order: Language > Quality > Format > Resolution > File Size > Bitrate.
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
            var recordingList = recordings.ToList();
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
                .OrderByDescending(r => r.Width ?? 0)
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

            candidates = candidates
                .OrderByDescending(r => r.HighQuality ?? false)
                .ThenByDescending(r => r.Width ?? 0)
                .ThenByDescending(r => r.FileSize ?? r.Size)
                .ThenByDescending(r => r.Bitrate ?? 0);

            return candidates.FirstOrDefault();
        }
    }
}