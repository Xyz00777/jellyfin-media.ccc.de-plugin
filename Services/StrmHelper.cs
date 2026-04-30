using System;
using System.IO;
using System.Linq;

namespace Jellyfin.Plugin.MediaCccDe.Services
{
    internal static class StrmHelper
    {
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