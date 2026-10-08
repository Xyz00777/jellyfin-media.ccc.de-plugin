using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaCccDe.Api;
using Jellyfin.Plugin.MediaCccDe.Models;
using Jellyfin.Plugin.MediaCccDe.Providers;
using Jellyfin.Plugin.MediaCccDe.Services;
using MediaBrowser.Controller.Subtitles;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.MediaCccDe.Tests.Unit
{
    public class MediaCccSubtitleProviderTests
    {
        private const string SrtUrl = "https://cdn.media.ccc.de/congress/2023/DRAFT_37c3-57926-deu-Talk.en_DRAFT.srt";

        private static EventDto EventWithSubtitles()
        {
            return new EventDto
            {
                Guid = "guid-1",
                Slug = "37c3-57926-talk",
                Date = "2023-12-29T10:00:00+01:00",
                Recordings = new List<RecordingDto>
                {
                    new() { Language = "deu", RecordingUrl = "https://cdn.media.ccc.de/v/talk-deu.mp4", CurrentMimeType = "video/mp4" },
                    new() { Language = "eng", RecordingUrl = SrtUrl, CurrentMimeType = "application/x-subrip" },
                    new() { Language = "deu", RecordingUrl = "https://cdn.media.ccc.de/v/audio.mp3", CurrentMimeType = "audio/mpeg" }
                }
            };
        }

        private sealed class RedirectHandler : System.Net.Http.HttpMessageHandler
        {
            private readonly HttpStatusCode _first;
            private readonly Uri? _location;
            private readonly HttpStatusCode _second;
            private readonly string _body;

            private int _redirectTimes = 1;

            public RedirectHandler(HttpStatusCode first, Uri? location, HttpStatusCode second, string body)
            {
                _first = first;
                _location = location;
                _second = second;
                _body = body;
            }

            public RedirectHandler RedirectingForever()
            {
                _redirectTimes = 99;
                return this;
            }

            public List<string> Requests { get; } = new List<string>();

            protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(
                System.Net.Http.HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Add(request.RequestUri!.ToString());

                if (_location is not null && Requests.Count <= _redirectTimes)
                {
                    var moved = new System.Net.Http.HttpResponseMessage(_first);
                    moved.Headers.Location = _location;
                    return Task.FromResult(moved);
                }

                return Task.FromResult(new System.Net.Http.HttpResponseMessage(_second)
                {
                    Content = new System.Net.Http.StringContent(_body)
                });
            }
        }

        private sealed class StatusHandler : System.Net.Http.HttpMessageHandler
        {
            private readonly System.Net.HttpStatusCode _status;
            public StatusHandler(System.Net.HttpStatusCode status) => _status = status;

            protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(
                System.Net.Http.HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(new System.Net.Http.HttpResponseMessage(_status)
                {
                    Content = new System.Net.Http.StringContent(string.Empty)
                });
        }

        private sealed class SingleHandlerFactory : System.Net.Http.IHttpClientFactory
        {
            private readonly System.Net.Http.HttpMessageHandler _handler;

            public SingleHandlerFactory(System.Net.Http.HttpMessageHandler handler) => _handler = handler;

            public System.Net.Http.HttpClient CreateClient(string name) =>
                new System.Net.Http.HttpClient(_handler, disposeHandler: false);
        }

        private static MediaCccSubtitleProvider BuildWithHandler(System.Net.Http.HttpMessageHandler handler)
        {
            var api = new Mock<IMediaCccApiClient>();
            return new MediaCccSubtitleProvider(
                api.Object,
                new ConferenceScheduleCache(api.Object),
                new SingleHandlerFactory(handler));
        }

        private static (MediaCccSubtitleProvider Provider, Mock<IMediaCccApiClient> Api) Build(EventDto? hydrated)
        {
            var api = new Mock<IMediaCccApiClient>();
            api.Setup(x => x.GetConferenceAsync("37c3", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ConferenceDto
                {
                    Acronym = "37c3",
                    Events = new List<EventDto>
                    {
                        new() { Guid = "guid-1", Slug = "37c3-57926-talk", Date = "2023-12-29T10:00:00+01:00" }
                    }
                });
            api.Setup(x => x.GetEventAsync("guid-1", It.IsAny<CancellationToken>())).ReturnsAsync(hydrated);

            var handler = new StatusHandler(System.Net.HttpStatusCode.NotFound);
            var provider = new MediaCccSubtitleProvider(
                api.Object,
                new ConferenceScheduleCache(api.Object),
                new SingleHandlerFactory(handler));

            return (provider, api);
        }

        private static SubtitleSearchRequest Request()
        {
            return new SubtitleSearchRequest
            {
                MediaPath = "/archive/37c3/Season 03/37c3-57926-talk.strm",
                ProviderIds = new Dictionary<string, string> { ["MediaCccDe"] = "37c3-57926-talk" }
            };
        }

        [Fact]
        public async Task Search_returns_only_subtitle_tracks()
        {
            var (provider, _) = Build(EventWithSubtitles());

            var results = (await provider.Search(Request(), CancellationToken.None)).ToList();

            var only = Assert.Single(results);
            Assert.Equal(SrtUrl, only.Id);
            Assert.Equal("srt", only.Format);
            Assert.Equal("Media.CCC.de", only.ProviderName);
        }

        [Fact]
        public async Task Search_returns_nothing_for_an_event_without_subtitles()
        {
            var (provider, _) = Build(new EventDto
            {
                Guid = "guid-1",
                Slug = "37c3-57926-talk",
                Recordings = new List<RecordingDto>
                {
                    new() { RecordingUrl = "https://cdn.media.ccc.de/v/talk.mp4", CurrentMimeType = "video/mp4" }
                }
            });

            Assert.Empty(await provider.Search(Request(), CancellationToken.None));
        }

        [Fact]
        public async Task Search_falls_back_to_the_file_name_when_no_provider_id_is_present()
        {
            var (provider, _) = Build(EventWithSubtitles());
            var request = new SubtitleSearchRequest
            {
                MediaPath = "/archive/37c3/Season 03/37c3-57926-talk.strm"
            };

            Assert.Single(await provider.Search(request, CancellationToken.None));
        }

        [Fact]
        public async Task Search_returns_nothing_for_a_foreign_path()
        {
            var (provider, _) = Build(EventWithSubtitles());
            var request = new SubtitleSearchRequest { MediaPath = "/movies/some-film.mkv" };

            Assert.Empty(await provider.Search(request, CancellationToken.None));
        }

        [Fact]
        public async Task Search_returns_nothing_when_the_event_is_unknown()
        {
            var (provider, _) = Build(EventWithSubtitles());
            var request = new SubtitleSearchRequest
            {
                MediaPath = "/archive/37c3/Season 03/unknown.strm",
                ProviderIds = new Dictionary<string, string> { ["MediaCccDe"] = "unknown" }
            };

            Assert.Empty(await provider.Search(request, CancellationToken.None));
        }

        [Theory]
        [InlineData("https://cdn.media.ccc.de/congress/2023/a.srt", true)]
        [InlineData("https://static.media.ccc.de/media/a.srt", true)]
        [InlineData("https://media.ccc.de/a.srt", true)]
        [InlineData("http://cdn.media.ccc.de/a.srt", false)]
        [InlineData("https://evil.example.com/a.srt", false)]
        [InlineData("https://cdn.media.ccc.de.evil.example/a.srt", false)]
        [InlineData("https://notmedia.ccc.de.attacker.net/a.srt", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void Only_media_ccc_subtitle_urls_are_allowed(string? url, bool expected)
        {
            Assert.Equal(expected, MediaCccSubtitleProvider.IsAllowedSubtitleUrl(url));
        }

        [Fact]
        public void A_hashed_id_from_jellyfin_still_resolves_to_the_url()
        {
            var hashed = "a4945f938ffb8bcd44141901dcbebcce_" + SrtUrl;

            Assert.Equal(SrtUrl, MediaCccSubtitleProvider.ExtractUrl(hashed));
            Assert.True(MediaCccSubtitleProvider.IsAllowedSubtitleUrl(hashed));
        }

        [Fact]
        public async Task Download_returns_null_when_the_first_hop_is_not_media_ccc()
        {
            var handler = new RedirectHandler(HttpStatusCode.OK, null, HttpStatusCode.OK, "x");
            var provider = BuildWithHandler(handler);

            Assert.Null(await provider.DownloadSubtitleAsync("https://evil.example.com/a.srt", CancellationToken.None));
            Assert.Empty(handler.Requests);
        }

        [Fact]
        public void FindSubtitles_ignores_video_and_audio()
        {
            var found = MediaCccSubtitleProvider.FindSubtitles(EventWithSubtitles()).ToList();

            Assert.Single(found);
            Assert.Equal(SrtUrl, found[0].Url);
        }

        [Fact]
        public void FindSubtitles_handles_a_missing_recording_list()
        {
            Assert.Empty(MediaCccSubtitleProvider.FindSubtitles(null));
            Assert.Empty(MediaCccSubtitleProvider.FindSubtitles(new EventDto()));
        }

        [Theory]
        [InlineData("eng", "ENG")]
        [InlineData("deu", "DEU")]
        [InlineData(null, "UND")]
        public void Display_names_are_derived_from_the_language(string? language, string expected)
        {
            Assert.Equal(expected, MediaCccSubtitleProvider.BuildName(language));
        }

        [Fact]
        public void Provider_advertises_video_content_types()
        {
            var (provider, _) = Build(null);

            Assert.Equal("Media.CCC.de", provider.Name);
            Assert.Contains(MediaBrowser.Controller.Providers.VideoContentType.Episode, provider.SupportedMediaTypes);
        }
    }
}
