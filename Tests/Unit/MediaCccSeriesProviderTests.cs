using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using Moq;
using Moq.Protected;
using Xunit;
using Jellyfin.Plugin.MediaCccDe.Api;
using Jellyfin.Plugin.MediaCccDe.Models;
using Jellyfin.Plugin.MediaCccDe.Providers;

namespace Jellyfin.Plugin.MediaCccDe.Tests.Unit
{
    public class MediaCccSeriesProviderTests
    {
        private const string BaseUrl = "https://api.media.ccc.de/public/";
        
        #region Helper Methods

        private static Mock<IMediaCccApiClient> CreateMockApiClient()
        {
            return new Mock<IMediaCccApiClient>();
        }

        private static Mock<IHttpClientFactory> CreateMockHttpClientFactory()
        {
            return new Mock<IHttpClientFactory>();
        }

        private static ConferenceDto CreateTestConference(
            string acronym = "37c3",
            string title = "37C3: UnLocked",
            string slug = "37c3",
            string? description = "The 37th Chaos Communication Congress",
            string? url = "https://events.ccc.de/congress/2023/",
            DateTime? updatedAt = null)
        {
            return new ConferenceDto
            {
                Acronym = acronym,
                Title = title,
                Slug = slug,
                Description = description,
                Url = url,
                UpdatedAt = updatedAt ?? DateTime.Parse("2024-01-02"),
                ScheduleUrl = "https://events.ccc.de/congress/2023/schedule/"
            };
        }

        private static HttpClient CreateHttpClientWithImageResponse(byte[] imageData)
        {
            var handlerMock = new Mock<HttpMessageHandler>();
            handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(imageData)
                    {
                        Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg") }
                    }
                });

            return new HttpClient(handlerMock.Object);
        }

        private static HttpClient CreateHttpClientWithNotFoundResponse()
        {
            var handlerMock = new Mock<HttpMessageHandler>();
            handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NotFound));

            return new HttpClient(handlerMock.Object);
        }

        private static MediaCccSeriesProvider CreateProvider(
            IMediaCccApiClient? apiClient = null,
            IHttpClientFactory? httpClientFactory = null)
        {
            var mockApi = apiClient ?? CreateMockApiClient().Object;
            var mockHttpFactory = httpClientFactory ?? CreateMockHttpClientFactory().Object;
            return new MediaCccSeriesProvider(mockApi, mockHttpFactory);
        }

        #endregion

        #region GetMetadata Tests

        [Fact]
        public async Task GetMetadata_returns_MetadataResult_for_conference()
        {
            var conference = CreateTestConference();
            var mockApi = CreateMockApiClient();
            mockApi.Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new List<ConferenceDto> { conference });

            var provider = CreateProvider(apiClient: mockApi.Object);
            var seriesId = new SeriesInfo { Name = "37c3" };

            var result = await provider.GetMetadata(seriesId, CancellationToken.None);

            Assert.NotNull(result);
            Assert.True(result.HasMetadata);
            Assert.NotNull(result.Item);
            Assert.IsType<Series>(result.Item);
        }

        [Fact]
        public async Task GetMetadata_maps_conference_title_to_Name()
        {
            var conference = CreateTestConference(
                acronym: "37c3",
                title: "37C3: UnLocked",
                slug: "37c3"
            );
            
            var mockApi = CreateMockApiClient();
            mockApi.Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new List<ConferenceDto> { conference });

            var provider = CreateProvider(apiClient: mockApi.Object);
            var seriesId = new SeriesInfo { Name = "37c3" };

            var result = await provider.GetMetadata(seriesId, CancellationToken.None);

            Assert.True(result.HasMetadata);
            Assert.Equal("37C3: UnLocked", result.Item.Name);
        }

        [Fact]
        public async Task GetMetadata_maps_conference_slug_to_OriginalTitle()
        {
            var conference = CreateTestConference(
                acronym: "37c3",
                title: "37C3: UnLocked",
                slug: "37c3"
            );
            
            var mockApi = CreateMockApiClient();
            mockApi.Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new List<ConferenceDto> { conference });

            var provider = CreateProvider(apiClient: mockApi.Object);
            var seriesId = new SeriesInfo { Name = "37c3" };

            var result = await provider.GetMetadata(seriesId, CancellationToken.None);

            Assert.True(result.HasMetadata);
            Assert.Equal("37c3", result.Item.OriginalTitle);
        }

        [Fact]
        public async Task GetMetadata_fetches_poster_from_conference_url()
        {
            var conference = CreateTestConference(
                url: "https://events.ccc.de/congress/2023/"
            );
            
            var posterData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
            
            var mockApi = CreateMockApiClient();
            mockApi.Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new List<ConferenceDto> { conference });
            
            var httpClient = CreateHttpClientWithImageResponse(posterData);
            var mockHttpFactory = new Mock<IHttpClientFactory>();
            mockHttpFactory.Setup(x => x.CreateClient(It.IsAny<string>()))
                          .Returns(httpClient);

            var provider = CreateProvider(apiClient: mockApi.Object, httpClientFactory: mockHttpFactory.Object);
            var seriesId = new SeriesInfo { Name = "37c3" };

            var result = await provider.GetMetadata(seriesId, CancellationToken.None);

            Assert.True(result.HasMetadata);
            Assert.NotNull(result.Item.ImageInfos);
        }

        [Fact]
        public async Task GetMetadata_sets_Overview_from_conference_description()
        {
            var conference = CreateTestConference(
                description: "The 37th Chaos Communication Congress - exploring technology, society, and utopia"
            );
            
            var mockApi = CreateMockApiClient();
            mockApi.Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new List<ConferenceDto> { conference });

            var provider = CreateProvider(apiClient: mockApi.Object);
            var seriesId = new SeriesInfo { Name = "37c3" };

            var result = await provider.GetMetadata(seriesId, CancellationToken.None);

            Assert.True(result.HasMetadata);
            Assert.Contains("37th Chaos Communication Congress", result.Item.Overview);
        }

        [Fact]
        public async Task GetMetadata_sets_Genre_to_Talk()
        {
            var conference = CreateTestConference();
            
            var mockApi = CreateMockApiClient();
            mockApi.Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new List<ConferenceDto> { conference });

            var provider = CreateProvider(apiClient: mockApi.Object);
            var seriesId = new SeriesInfo { Name = "37c3" };

            var result = await provider.GetMetadata(seriesId, CancellationToken.None);

            Assert.True(result.HasMetadata);
            Assert.NotNull(result.Item.Genres);
            Assert.Contains("Talk", result.Item.Genres);
        }

        [Fact]
        public async Task GetMetadata_returns_empty_result_for_unknown_conference()
        {
            var mockApi = CreateMockApiClient();
            mockApi.Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new List<ConferenceDto>());

            var provider = CreateProvider(apiClient: mockApi.Object);
            var seriesId = new SeriesInfo { Name = "unknown-conference" };

            var result = await provider.GetMetadata(seriesId, CancellationToken.None);

            Assert.False(result.HasMetadata);
            Assert.Null(result.Item);
        }

        [Fact]
        public async Task GetMetadata_handles_missing_poster_gracefully()
        {
            var conference = CreateTestConference(url: "https://events.ccc.de/congress/2023/");
            
            var mockApi = CreateMockApiClient();
            mockApi.Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new List<ConferenceDto> { conference });
            
            var httpClient = CreateHttpClientWithNotFoundResponse();
            var mockHttpFactory = new Mock<IHttpClientFactory>();
            mockHttpFactory.Setup(x => x.CreateClient(It.IsAny<string>()))
                          .Returns(httpClient);

            var provider = CreateProvider(apiClient: mockApi.Object, httpClientFactory: mockHttpFactory.Object);
            var seriesId = new SeriesInfo { Name = "37c3" };

            var result = await provider.GetMetadata(seriesId, CancellationToken.None);
            
            Assert.True(result.HasMetadata);
        }

        [Fact]
        public async Task GetMetadata_matches_conference_by_acronym_case_insensitive()
        {
            var conference = CreateTestConference(acronym: "37c3");
            
            var mockApi = CreateMockApiClient();
            mockApi.Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new List<ConferenceDto> { conference });

            var provider = CreateProvider(apiClient: mockApi.Object);
            
            var seriesId = new SeriesInfo { Name = "37C3" };

            var result = await provider.GetMetadata(seriesId, CancellationToken.None);

            Assert.True(result.HasMetadata);
            Assert.Equal("37C3: UnLocked", result.Item.Name);
        }

        [Fact]
        public async Task GetMetadata_handles_multiple_conferences()
        {
            var conference1 = CreateTestConference(acronym: "37c3", title: "37C3: UnLocked");
            var conference2 = CreateTestConference(acronym: "36c3", title: "36C3: Resource Overflow");
            
            var mockApi = CreateMockApiClient();
            mockApi.Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new List<ConferenceDto> { conference1, conference2 });

            var provider = CreateProvider(apiClient: mockApi.Object);
            var seriesId = new SeriesInfo { Name = "36c3" };

            var result = await provider.GetMetadata(seriesId, CancellationToken.None);

            Assert.True(result.HasMetadata);
            Assert.Equal("36C3: Resource Overflow", result.Item.Name);
        }

        [Fact]
        public async Task GetMetadata_caches_conference_list()
        {
            var conference = CreateTestConference();
            
            var mockApi = CreateMockApiClient();
            mockApi.Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new List<ConferenceDto> { conference });

            var provider = CreateProvider(apiClient: mockApi.Object);
            var seriesId = new SeriesInfo { Name = "37c3" };

            await provider.GetMetadata(seriesId, CancellationToken.None);
            await provider.GetMetadata(seriesId, CancellationToken.None);

            mockApi.Verify(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        #endregion

        #region Edge Cases

        [Fact]
        public async Task GetMetadata_handles_API_error_gracefully()
        {
            var mockApi = CreateMockApiClient();
            mockApi.Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                   .ThrowsAsync(new HttpRequestException("API Error"));

            var provider = CreateProvider(apiClient: mockApi.Object);
            var seriesId = new SeriesInfo { Name = "37c3" };

            var result = await provider.GetMetadata(seriesId, CancellationToken.None);
            Assert.False(result.HasMetadata);
            Assert.Null(result.Item);
        }

        [Fact]
        public async Task GetMetadata_handles_empty_conference_list()
        {
            var mockApi = CreateMockApiClient();
            mockApi.Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new List<ConferenceDto>());

            var provider = CreateProvider(apiClient: mockApi.Object);
            var seriesId = new SeriesInfo { Name = "37c3" };

            var result = await provider.GetMetadata(seriesId, CancellationToken.None);

            Assert.False(result.HasMetadata);
            Assert.Null(result.Item);
        }

        [Fact]
        public async Task GetMetadata_handles_null_conference_url()
        {
            var conference = CreateTestConference(url: null);
            
            var mockApi = CreateMockApiClient();
            mockApi.Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new List<ConferenceDto> { conference });

            var provider = CreateProvider(apiClient: mockApi.Object);
            var seriesId = new SeriesInfo { Name = "37c3" };

            var result = await provider.GetMetadata(seriesId, CancellationToken.None);

            Assert.True(result.HasMetadata);
        }

        [Fact]
        public async Task GetMetadata_preserves_provider_id()
        {
            var conference = CreateTestConference();
            
            var mockApi = CreateMockApiClient();
            mockApi.Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new List<ConferenceDto> { conference });

            var provider = CreateProvider(apiClient: mockApi.Object);
            var seriesId = new SeriesInfo { Name = "37c3" };

            var result = await provider.GetMetadata(seriesId, CancellationToken.None);

            Assert.True(result.HasMetadata);
            Assert.Equal("37c3", result.Item.ProviderIds["MediaCccDe"]);
        }

        [Fact]
        public async Task GetMetadata_sets_conference_metadata_properties()
        {
            var conference = CreateTestConference(
                acronym: "37c3",
                title: "37C3: UnLocked",
                slug: "37c3",
                description: "Congress description",
                url: "https://events.ccc.de/congress/2023/",
                updatedAt: new DateTime(2024, 1, 2)
            );
            
            var mockApi = CreateMockApiClient();
            mockApi.Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new List<ConferenceDto> { conference });

            var provider = CreateProvider(apiClient: mockApi.Object);
            var seriesId = new SeriesInfo { Name = "37c3" };

            var result = await provider.GetMetadata(seriesId, CancellationToken.None);

            Assert.True(result.HasMetadata);
            Assert.Equal("37C3: UnLocked", result.Item.Name);
            Assert.Equal("37c3", result.Item.OriginalTitle);
            Assert.NotNull(result.Item.Overview);
            Assert.Contains("37c3", result.Item.ProviderIds["MediaCccDe"]);
        }

        #endregion

        #region Cancellation Tests

        [Fact]
        public async Task GetMetadata_respects_cancellation_token()
        {
            var cts = new CancellationTokenSource();
            var conference = CreateTestConference();
            
            var mockApi = CreateMockApiClient();
            mockApi.Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                   .Returns(async (CancellationToken token) =>
                   {
                        cts.Cancel();
                        await Task.Delay(100, token);
                        return new List<ConferenceDto> { conference };
                   });

            var provider = CreateProvider(apiClient: mockApi.Object);
            var seriesId = new SeriesInfo { Name = "37c3" };

            await Assert.ThrowsAsync<OperationCanceledException>(() => 
                provider.GetMetadata(seriesId, cts.Token));
        }

        #endregion
    }
}