using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaCccDe.Api;
using Jellyfin.Plugin.MediaCccDe.Models;
using Jellyfin.Plugin.MediaCccDe.Providers;
using Jellyfin.Plugin.MediaCccDe.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.MediaCccDe.Tests.Unit
{
    public class MediaCccEpisodeProviderTests
    {
        private readonly Mock<IMediaCccApiClient> _mockApiClient;
        private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
        private readonly ConferenceScheduleCache _scheduleCache;
        private readonly MediaCccEpisodeProvider _provider;

        public MediaCccEpisodeProviderTests()
        {
            _mockApiClient = new Mock<IMediaCccApiClient>();
            _mockHttpClientFactory = new Mock<IHttpClientFactory>();
            _scheduleCache = new ConferenceScheduleCache(_mockApiClient.Object);
            _provider = new MediaCccEpisodeProvider(
                _mockApiClient.Object,
                _scheduleCache,
                _mockHttpClientFactory.Object);
        }

        private const string ArchiveRoot = "/data/archive";

        private static EpisodeInfo InfoFor(string acronym, string seasonFolder, string slug)
        {
            return new EpisodeInfo
            {
                Name = slug,
                Path = $"{ArchiveRoot}/{acronym}/{seasonFolder}/{slug}.strm"
            };
        }

        private static EventDto Talk(string slug, string title, string date, string? poster = null)
        {
            return new EventDto
            {
                Guid = "guid-" + slug,
                Slug = slug,
                Title = title,
                Date = date,
                Length = 3600,
                Description = "A talk about things.",
                PosterUrl = poster
            };
        }

        private void GivenConference(string acronym, params EventDto[] events)
        {
            _mockApiClient
                .Setup(x => x.GetConferenceAsync(acronym, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ConferenceDto
                {
                    Acronym = acronym,
                    Title = acronym.ToUpperInvariant(),
                    LogoUrl = $"https://static.media.ccc.de/media/congress/2023/{acronym}.png",
                    Events = events.ToList()
                });
        }

        #region Season = conference day

        [Fact]
        public async Task First_conference_day_is_season_1()
        {
            GivenConference("37c3", Talk("a", "Opening", "2023-12-27T10:30:00+01:00"));

            var result = await _provider.GetMetadata(InfoFor("37c3", "Season 01", "a"), CancellationToken.None);

            Assert.True(result.HasMetadata);
            Assert.Equal(1, result.Item.ParentIndexNumber);
        }

        [Fact]
        public async Task Fourth_conference_day_is_season_4()
        {
            GivenConference(
                "37c3",
                Talk("a", "Opening", "2023-12-27T10:30:00+01:00"),
                Talk("b", "Talk", "2023-12-30T10:30:00+01:00"));

            var result = await _provider.GetMetadata(InfoFor("37c3", "Season 04", "b"), CancellationToken.None);

            Assert.Equal(4, result.Item.ParentIndexNumber);
        }

        [Fact]
        public async Task Season_is_relative_to_conference_start_not_day_of_month()
        {
            // A July conference: day-of-month math previously produced "Season 5" from the
            // 5th and mis-numbered a five day camp as seasons 5..9.
            GivenConference(
                "camp2023",
                Talk("a", "Day1", "2023-07-03T10:00:00+02:00"),
                Talk("b", "Day2", "2023-07-04T10:00:00+02:00"),
                Talk("c", "Day3", "2023-07-05T10:00:00+02:00"));

            var result = await _provider.GetMetadata(InfoFor("camp2023", "Season 03", "c"), CancellationToken.None);

            Assert.Equal(3, result.Item.ParentIndexNumber);
        }

        [Fact]
        public async Task Season_spanning_month_boundary_is_contiguous()
        {
            // A three day conference running 30 Mar - 1 Apr must still be seasons 1..3.
            GivenConference(
                "gpn",
                Talk("a", "Day1", "2024-03-30T10:00:00+01:00"),
                Talk("b", "Day2", "2024-03-31T10:00:00+02:00"),
                Talk("c", "Day3", "2024-04-01T10:00:00+02:00"));

            Assert.Equal(1, (await _provider.GetMetadata(InfoFor("gpn", "Season 01", "a"), CancellationToken.None)).Item.ParentIndexNumber);
            Assert.Equal(2, (await _provider.GetMetadata(InfoFor("gpn", "Season 02", "b"), CancellationToken.None)).Item.ParentIndexNumber);
            Assert.Equal(3, (await _provider.GetMetadata(InfoFor("gpn", "Season 03", "c"), CancellationToken.None)).Item.ParentIndexNumber);
        }

        [Fact]
        public async Task Season_follows_actual_days_not_a_contiguous_counter()
        {
            // A gap day must not be invented: if nothing happens on 31 Mar then 1 Apr is
            // day 3. This matches the on-disk Season folders, which use the same offset.
            GivenConference(
                "gpn",
                Talk("a", "Day1", "2024-03-29T10:00:00+01:00"),
                Talk("b", "Day3", "2024-04-01T10:00:00+02:00"));

            Assert.Equal(1, (await _provider.GetMetadata(InfoFor("gpn", "Season 01", "a"), CancellationToken.None)).Item.ParentIndexNumber);
            Assert.Equal(4, (await _provider.GetMetadata(InfoFor("gpn", "Season 04", "b"), CancellationToken.None)).Item.ParentIndexNumber);
        }

        [Fact]
        public async Task Season_is_null_when_conference_cannot_be_resolved()
        {
            // No path and no provider id: Jellyfin falls back to the folder name, which is
            // already correct on disk.
            _mockApiClient
                .Setup(x => x.GetEventAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Talk("a", "Opening", "2023-12-27T10:30:00+01:00"));

            var result = await _provider.GetMetadata(new EpisodeInfo { Name = "guid-a" }, CancellationToken.None);

            Assert.True(result.HasMetadata);
            Assert.Null(result.Item.ParentIndexNumber);
        }

        #endregion

        #region Episode = running order within the day

        [Fact]
        public async Task Episode_numbers_follow_start_time_within_a_day()
        {
            // Deliberately supplied out of order, and with a later slug sorting first.
            GivenConference(
                "37c3",
                Talk("zzz_late", "Third", "2023-12-27T15:00:00+01:00"),
                Talk("aaa_early", "First", "2023-12-27T10:00:00+01:00"),
                Talk("mmm_mid", "Second", "2023-12-27T12:00:00+01:00"));

            var first = await _provider.GetMetadata(InfoFor("37c3", "Season 01", "aaa_early"), CancellationToken.None);
            var second = await _provider.GetMetadata(InfoFor("37c3", "Season 01", "mmm_mid"), CancellationToken.None);
            var third = await _provider.GetMetadata(InfoFor("37c3", "Season 01", "zzz_late"), CancellationToken.None);

            Assert.Equal(1, first.Item.IndexNumber);
            Assert.Equal(2, second.Item.IndexNumber);
            Assert.Equal(3, third.Item.IndexNumber);
        }

        [Fact]
        public async Task Episode_numbering_restarts_on_each_day()
        {
            GivenConference(
                "37c3",
                Talk("d1a", "D1A", "2023-12-27T10:00:00+01:00"),
                Talk("d1b", "D1B", "2023-12-27T12:00:00+01:00"),
                Talk("d2a", "D2A", "2023-12-28T10:00:00+01:00"));

            var day1a = await _provider.GetMetadata(InfoFor("37c3", "Season 01", "d1a"), CancellationToken.None);
            var day1b = await _provider.GetMetadata(InfoFor("37c3", "Season 01", "d1b"), CancellationToken.None);
            var day2a = await _provider.GetMetadata(InfoFor("37c3", "Season 02", "d2a"), CancellationToken.None);

            Assert.Equal(1, day1a.Item.IndexNumber);
            Assert.Equal(2, day1b.Item.IndexNumber);
            Assert.Equal(1, day2a.Item.IndexNumber);
        }

        [Fact]
        public async Task Episode_numbers_are_stable_across_repeated_calls()
        {
            GivenConference(
                "37c3",
                Talk("a", "A", "2023-12-27T10:00:00+01:00"),
                Talk("b", "B", "2023-12-27T11:00:00+01:00"));

            var info = InfoFor("37c3", "Season 01", "b");

            var r1 = await _provider.GetMetadata(info, CancellationToken.None);
            var r2 = await _provider.GetMetadata(info, CancellationToken.None);
            var r3 = await _provider.GetMetadata(info, CancellationToken.None);

            Assert.Equal(r1.Item.IndexNumber, r2.Item.IndexNumber);
            Assert.Equal(r2.Item.IndexNumber, r3.Item.IndexNumber);
            Assert.Equal(2, r1.Item.IndexNumber);
        }

        [Fact]
        public async Task Simultaneous_start_times_are_broken_deterministically()
        {
            // 37C3 runs parallel tracks, so identical start times are the normal case.
            GivenConference(
                "37c3",
                Talk("bbb", "B", "2023-12-27T11:00:00+01:00"),
                Talk("aaa", "A", "2023-12-27T11:00:00+01:00"));

            var a = await _provider.GetMetadata(InfoFor("37c3", "Season 01", "aaa"), CancellationToken.None);
            var b = await _provider.GetMetadata(InfoFor("37c3", "Season 01", "bbb"), CancellationToken.None);

            Assert.Equal(1, a.Item.IndexNumber);
            Assert.Equal(2, b.Item.IndexNumber);
        }

        [Fact]
        public async Task Schedule_lookup_does_not_refetch_each_event()
        {
            GivenConference(
                "37c3",
                Talk("a", "A", "2023-12-27T10:00:00+01:00"),
                Talk("b", "B", "2023-12-27T11:00:00+01:00"));

            await _provider.GetMetadata(InfoFor("37c3", "Season 01", "a"), CancellationToken.None);
            await _provider.GetMetadata(InfoFor("37c3", "Season 01", "b"), CancellationToken.None);

            _mockApiClient.Verify(
                x => x.GetConferenceAsync("37c3", It.IsAny<CancellationToken>()),
                Times.Once);
            _mockApiClient.Verify(
                x => x.GetEventAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        #endregion

        #region Identity and mapping

        [Fact]
        public async Task Provider_id_is_the_slug_not_the_guid()
        {
            GivenConference("37c3", Talk("37c3-12746-opening_ceremony", "Opening", "2023-12-27T10:00:00+01:00"));

            var result = await _provider.GetMetadata(
                InfoFor("37c3", "Season 01", "37c3-12746-opening_ceremony"),
                CancellationToken.None);

            Assert.Equal("37c3-12746-opening_ceremony", result.Item.ProviderIds["MediaCccDe"]);
        }

        [Fact]
        public async Task Legacy_guid_provider_ids_still_resolve()
        {
            // Libraries created before the slug switch store the GUID as the provider id.
            var talk = Talk("37c3-12746-opening_ceremony", "Opening Ceremony", "2023-12-27T10:00:00+01:00");
            GivenConference("37c3", talk);

            var info = InfoFor("37c3", "Season 01", "37c3-12746-opening_ceremony");
            info.ProviderIds["MediaCccDe"] = talk.Guid;

            var result = await _provider.GetMetadata(info, CancellationToken.None);

            Assert.True(result.HasMetadata);
            Assert.Equal("Opening Ceremony", result.Item.Name);
            Assert.Equal(1, result.Item.IndexNumber);
        }

        [Fact]
        public async Task Falls_back_to_event_lookup_when_schedule_misses()
        {
            _mockApiClient
                .Setup(x => x.GetConferenceAsync("37c3", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ConferenceDto { Acronym = "37c3", Events = new List<EventDto>() });
            _mockApiClient
                .Setup(x => x.GetEventAsync("guid-x", It.IsAny<CancellationToken>()))
                .ReturnsAsync(Talk("x", "Direct Fetch", "2023-12-27T10:00:00+01:00"));

            var info = InfoFor("37c3", "Season 01", "x");
            info.ProviderIds["MediaCccDe"] = "guid-x";

            var result = await _provider.GetMetadata(info, CancellationToken.None);

            Assert.True(result.HasMetadata);
            Assert.Equal("Direct Fetch", result.Item.Name);
        }

        [Fact]
        public async Task Event_without_a_slug_is_not_mapped()
        {
            var info = new EpisodeInfo { Name = "guid-noslug", Path = $"{ArchiveRoot}/37c3/Season 01/x.strm" };
            _mockApiClient
                .Setup(x => x.GetConferenceAsync("37c3", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ConferenceDto { Acronym = "37c3", Events = new List<EventDto>() });
            _mockApiClient
                .Setup(x => x.GetEventAsync("guid-noslug", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new EventDto { Guid = "guid-noslug", Title = "No slug", Date = "2023-12-27T10:00:00+01:00" });

            var result = await _provider.GetMetadata(info, CancellationToken.None);

            Assert.False(result.HasMetadata);
        }

        [Fact]
        public async Task Maps_title_length_and_premiere_date()
        {
            var talk = Talk("a", "Opening Ceremony", "2023-12-27T10:30:00+01:00");
            talk.Length = 7200;
            GivenConference("37c3", talk);

            var result = await _provider.GetMetadata(InfoFor("37c3", "Season 01", "a"), CancellationToken.None);

            Assert.Equal("Opening Ceremony", result.Item.Name);
            Assert.Equal(7200L * 10_000_000L, result.Item.RunTimeTicks);
            Assert.NotNull(result.Item.PremiereDate);
            Assert.Equal(2023, result.Item.PremiereDate!.Value.Year);
            Assert.Equal(12, result.Item.PremiereDate.Value.Month);
            Assert.Equal(27, result.Item.PremiereDate.Value.Day);
        }

        [Fact]
        public async Task Unknown_event_reports_no_metadata()
        {
            _mockApiClient
                .Setup(x => x.GetConferenceAsync("37c3", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ConferenceDto { Acronym = "37c3", Events = new List<EventDto>() });
            _mockApiClient
                .Setup(x => x.GetEventAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((EventDto?)null);

            var result = await _provider.GetMetadata(InfoFor("37c3", "Season 01", "nope"), CancellationToken.None);

            Assert.False(result.HasMetadata);
            Assert.Null(result.Item);
        }

        #endregion

        #region Overview truncation

        [Fact]
        public async Task Overview_is_truncated_to_four_lines()
        {
            var talk = Talk("a", "Long", "2023-12-27T10:00:00+01:00");
            talk.Description = string.Join(" ", Enumerable.Range(0, 400).Select(i => "word" + i));
            GivenConference("37c3", talk);

            var result = await _provider.GetMetadata(InfoFor("37c3", "Season 01", "a"), CancellationToken.None);

            var lines = result.Item.Overview!.Split('\n');
            Assert.Equal(4, lines.Length);
        }

        [Fact]
        public async Task Overview_strips_html_and_entities()
        {
            var talk = Talk("a", "Html", "2023-12-27T10:00:00+01:00");
            talk.Description = "<p>Talk about <b>bins</b> &amp; things</p>";
            GivenConference("37c3", talk);

            var result = await _provider.GetMetadata(InfoFor("37c3", "Season 01", "a"), CancellationToken.None);

            Assert.Equal("Talk about bins & things", result.Item.Overview);
        }

        [Fact]
        public async Task Short_overview_is_left_intact()
        {
            var talk = Talk("a", "Short", "2023-12-27T10:00:00+01:00");
            talk.Description = "Just one line.";
            GivenConference("37c3", talk);

            var result = await _provider.GetMetadata(InfoFor("37c3", "Season 01", "a"), CancellationToken.None);

            Assert.Equal("Just one line.", result.Item.Overview);
        }

        [Fact]
        public async Task Null_overview_stays_null()
        {
            var talk = Talk("a", "None", "2023-12-27T10:00:00+01:00");
            talk.Description = null;
            GivenConference("37c3", talk);

            var result = await _provider.GetMetadata(InfoFor("37c3", "Season 01", "a"), CancellationToken.None);

            Assert.Null(result.Item.Overview);
        }

        #endregion

        #region Artwork

        [Fact]
        public async Task Poster_is_offered_as_a_remote_image()
        {
            var poster = "https://static.media.ccc.de/media/congress/2023/1234-abc_preview.jpg";
            GivenConference("37c3", Talk("a", "Poster", "2023-12-27T10:00:00+01:00", poster));

            var result = await _provider.GetMetadata(InfoFor("37c3", "Season 01", "a"), CancellationToken.None);

            var image = Assert.Single(result.RemoteImages);
            Assert.Equal(poster, image.Url);
            Assert.Equal(ImageType.Primary, image.Type);
        }

        [Fact]
        public async Task No_poster_means_no_remote_images()
        {
            GivenConference("37c3", Talk("a", "NoPoster", "2023-12-27T10:00:00+01:00"));

            var result = await _provider.GetMetadata(InfoFor("37c3", "Season 01", "a"), CancellationToken.None);

            Assert.Empty(result.RemoteImages);
        }

        [Fact]
        public void Provider_supports_episode_images_only()
        {
            Assert.True(_provider.Supports(new Episode()));
            Assert.False(_provider.Supports(new Series()));
            Assert.Contains(ImageType.Primary, _provider.SupportedImageTypes);
        }

        #endregion

        #region Search

        [Fact]
        public async Task GetSearchResults_echoes_a_known_provider_id()
        {
            var info = new EpisodeInfo { Name = "Opening" };
            info.ProviderIds["MediaCccDe"] = "37c3-12746-opening_ceremony";

            var results = (await _provider.GetSearchResults(info, CancellationToken.None)).ToList();

            Assert.Single(results);
            Assert.Equal("37c3-12746-opening_ceremony", results[0].ProviderIds["MediaCccDe"]);
        }

        [Fact]
        public async Task GetSearchResults_is_empty_without_a_provider_id()
        {
            var results = await _provider.GetSearchResults(new EpisodeInfo { Name = "Opening" }, CancellationToken.None);

            Assert.Empty(results);
        }

        #endregion

        #region Acronym resolution from the archive layout

        [Theory]
        [InlineData("/data/archive/37C3/Season 04/talk.strm", "37C3")]
        [InlineData("/data/archive/37C3/Season 1/talk.strm", "37C3")]
        [InlineData("/data/archive/camp2023/Season 02/talk.strm", "camp2023")]
        public void Resolves_acronym_from_archive_path(string path, string expected)
        {
            Assert.Equal(expected, MediaCccEpisodeProvider.ResolveConferenceAcronym(path));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void Acronym_is_null_for_a_missing_path(string? path)
        {
            Assert.Null(MediaCccEpisodeProvider.ResolveConferenceAcronym(path));
        }

        #endregion
    }
}
