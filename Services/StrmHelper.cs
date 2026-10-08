using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Jellyfin.Plugin.MediaCccDe.Services
{
    internal static class StrmHelper
    {
        private const int MaxGapDaysWithinConference = 3;

        /// <summary>
        /// Picks the day that counts as "day 1" of a conference. Anchoring on the earliest
        /// event is fragile: media.ccc.de carries a few placeholder dates, and CCC Camp 2023
        /// for example has one talk dated three months before the camp, which pushed every
        /// real day out to seasons 69-76. Instead the busiest run of consecutive days wins,
        /// tolerating a rest day inside the run.
        /// </summary>
        internal static string? ResolveConferenceFirstDay(IEnumerable<string?> eventDates)
        {
            var eventsPerDay = new Dictionary<DateTime, int>();

            foreach (var value in eventDates)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
                {
                    eventsPerDay[parsed.Date] = eventsPerDay.TryGetValue(parsed.Date, out var count) ? count + 1 : 1;
                }
            }

            if (eventsPerDay.Count == 0)
            {
                return null;
            }

            var days = eventsPerDay.Keys.OrderBy(d => d).ToList();
            var bestStart = days[0];
            var bestTotal = 0;
            var runStart = days[0];
            var runTotal = eventsPerDay[days[0]];

            for (var i = 1; i < days.Count; i++)
            {
                if ((days[i] - days[i - 1]).Days <= MaxGapDaysWithinConference)
                {
                    runTotal += eventsPerDay[days[i]];
                }
                else
                {
                    if (runTotal > bestTotal)
                    {
                        bestTotal = runTotal;
                        bestStart = runStart;
                    }

                    runStart = days[i];
                    runTotal = eventsPerDay[days[i]];
                }
            }

            if (runTotal > bestTotal)
            {
                bestStart = runStart;
            }

            return bestStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        internal static int? ExtractDayNumber(string? date, string? conferenceFirstDay = null)
        {
            if (string.IsNullOrEmpty(date) || !DateTime.TryParse(date, out var eventDate))
                return null;

            if (!string.IsNullOrEmpty(conferenceFirstDay) && DateTime.TryParse(conferenceFirstDay, out var firstDay))
            {
                var dayOffset = (eventDate.Date - firstDay.Date).Days + 1;
                return Math.Max(dayOffset, 1);
            }

            if (eventDate.Month == 12 && eventDate.Day >= 27)
            {
                return eventDate.Day - 27 + 1;
            }

            return eventDate.Day;
        }

        internal static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "unknown";

            var invalid = Path.GetInvalidFileNameChars()
                .Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' })
                .ToHashSet();

            return new string(name.Where(c => !invalid.Contains(c)).ToArray())
                .Trim('.', ' ');
        }

        internal static string NormalizeConferenceDirectory(string acronym)
        {
            return SanitizeFileName(acronym);
        }
    }
}