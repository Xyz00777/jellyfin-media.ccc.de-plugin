using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaCccDe.Api;
using Jellyfin.Plugin.MediaCccDe.Models;

namespace Jellyfin.Plugin.MediaCccDe.Services
{
    public readonly record struct EventPosition(int? Season, int Episode);

    public sealed class ScheduledEvent
    {
        public required EventPosition Position { get; init; }

        public required EventDto Event { get; init; }
    }

    public sealed class ConferenceSchedule
    {
        public string? LogoUrl { get; init; }

        public IReadOnlyDictionary<string, ScheduledEvent> BySlug { get; init; } =
            new Dictionary<string, ScheduledEvent>(StringComparer.OrdinalIgnoreCase)!;

        public IReadOnlyDictionary<string, ScheduledEvent> ByGuid { get; init; } =
            new Dictionary<string, ScheduledEvent>(StringComparer.OrdinalIgnoreCase)!;

        public bool TryGet(string? slug, string? guid, out ScheduledEvent? scheduled)
        {
            if (!string.IsNullOrWhiteSpace(slug) && BySlug.TryGetValue(slug, out scheduled))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(guid) && ByGuid.TryGetValue(guid, out scheduled))
            {
                return true;
            }

            scheduled = null;
            return false;
        }
    }

    public interface IConferenceScheduleCache
    {
        Task<ConferenceSchedule?> GetAsync(string conferenceIdentifier, CancellationToken cancellationToken);
    }

    /// <summary>
    /// One API call per conference yields every event's start timestamp, which is all
    /// that is needed to place a talk on the conference timeline. The result is cached
    /// because a metadata provider is invoked once per library item.
    /// </summary>
    public sealed class ConferenceScheduleCache : IConferenceScheduleCache
    {
        private readonly IMediaCccApiClient _apiClient;
        private readonly ConcurrentDictionary<string, CacheEntry> _cache =
            new(StringComparer.OrdinalIgnoreCase);

        internal static TimeSpan CacheDuration = TimeSpan.FromHours(6);

        public ConferenceScheduleCache(IMediaCccApiClient apiClient)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        }

        public async Task<ConferenceSchedule?> GetAsync(string conferenceIdentifier, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(conferenceIdentifier))
            {
                return null;
            }

            var key = conferenceIdentifier.Trim();
            if (_cache.TryGetValue(key, out var cached) && DateTime.UtcNow < cached.Expiry)
            {
                return cached.Schedule;
            }

            var conference = await _apiClient.GetConferenceAsync(key, cancellationToken).ConfigureAwait(false);
            var schedule = conference is { Events.Count: > 0 } ? Build(conference) : null;
            _cache[key] = new CacheEntry(schedule, DateTime.UtcNow + CacheDuration);
            return schedule;
        }

        private static ConferenceSchedule Build(ConferenceDto conference)
        {
            var events = conference.Events!
                .Where(e => TryParseInstant(e.Date, out _))
                .ToList();

            var bySlug = new Dictionary<string, ScheduledEvent>(StringComparer.OrdinalIgnoreCase);
            var byGuid = new Dictionary<string, ScheduledEvent>(StringComparer.OrdinalIgnoreCase);

            if (events.Count == 0)
            {
                return new ConferenceSchedule { LogoUrl = conference.LogoUrl, BySlug = bySlug, ByGuid = byGuid };
            }

            var firstDay = events.Min(e => TryParseInstant(e.Date, out var d) ? d.Date : DateTime.MinValue).Date;
            var firstDayString = firstDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            var byDay = events
                .GroupBy(e => (TryParseInstant(e.Date, out var parsed) ? parsed.Date : DateTime.MinValue))
                .OrderBy(g => g.Key);

            foreach (var day in byDay)
            {
                var season = StrmHelper.ExtractDayNumber(day.First().Date, firstDayString);

                var ordered = day
                    .OrderBy(e => TryParseInstant(e.Date, out var instant) ? instant : DateTime.MinValue)
                    .ThenBy(e => e.Slug, StringComparer.Ordinal)
                    .ToList();

                for (var i = 0; i < ordered.Count; i++)
                {
                    var scheduled = new ScheduledEvent
                    {
                        Position = new EventPosition(season, i + 1),
                        Event = ordered[i]
                    };

                    if (!string.IsNullOrWhiteSpace(ordered[i].Slug))
                    {
                        bySlug[ordered[i].Slug] = scheduled;
                    }

                    if (!string.IsNullOrWhiteSpace(ordered[i].Guid))
                    {
                        byGuid[ordered[i].Guid] = scheduled;
                    }
                }
            }

            return new ConferenceSchedule
            {
                LogoUrl = conference.LogoUrl,
                BySlug = bySlug,
                ByGuid = byGuid
            };
        }

        private static bool TryParseInstant(string? value, out DateTimeOffset instant)
        {
            return DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out instant);
        }

        private readonly record struct CacheEntry(ConferenceSchedule? Schedule, DateTime Expiry);
    }
}
