using System;
using System.Collections.Generic;
using System.Linq;
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
        public async Task GetMetadata_maps_conference_acronym_to_OriginalTitle()
        {
            var conference = CreateTestConference(
                acronym: "37c3",
                title: "37C3: UnLocked",
                slug: "congress/2023/37c3"  // Different from acronym
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
        public async Task GetMetadata_sets_Genre_to_Conference()
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
            Assert.Contains("Conference", result.Item.Genres);
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
                slug: "congress/2023/37c3",
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

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => 
                provider.GetMetadata(seriesId, cts.Token));
        }

        #endregion

        #region Cache Expiry Tests

        [Fact]
        public async Task GetMetadata_fetches_fresh_data_after_cache_expiry()
        {
            var oldConference = CreateTestConference(acronym: "37c3", title: "37C3: Old Title");
            var newConference = CreateTestConference(acronym: "37c3", title: "37C3: Updated Title");

            var mockApi = CreateMockApiClient();
            var callCount = 0;
            mockApi.Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(() =>
                   {
                       callCount++;
                       return callCount == 1
                           ? new List<ConferenceDto> { oldConference }
                           : new List<ConferenceDto> { newConference };
                   });

            var originalCacheDuration = MediaCccSeriesProvider.CacheDuration;
            MediaCccSeriesProvider.CacheDuration = TimeSpan.FromMilliseconds(50);
            try
            {
            var provider = CreateProvider(apiClient: mockApi.Object);

            var seriesId = new SeriesInfo { Name = "37c3" };

            var result1 = await provider.GetMetadata(seriesId, CancellationToken.None);
            Assert.Equal("37C3: Old Title", result1.Item.Name);

            await Task.Delay(80);

            var result2 = await provider.GetMetadata(seriesId, CancellationToken.None);
            Assert.Equal("37C3: Updated Title", result2.Item.Name);

            mockApi.Verify(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()), Times.AtLeast(2));
            }
            finally
            {
                MediaCccSeriesProvider.CacheDuration = originalCacheDuration;
            }
        }

        [Fact]
        public async Task GetMetadata_returns_cached_data_before_expiry()
        {
            var conference = CreateTestConference();

            var mockApi = CreateMockApiClient();
            mockApi.Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new List<ConferenceDto> { conference });

            var provider = CreateProvider(apiClient: mockApi.Object);
            MediaCccSeriesProvider.CacheDuration = TimeSpan.FromMinutes(30);

            var seriesId = new SeriesInfo { Name = "37c3" };

            await provider.GetMetadata(seriesId, CancellationToken.None);
            await provider.GetMetadata(seriesId, CancellationToken.None);

            mockApi.Verify(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        #endregion

        #region Thread Safety Tests

        [Fact]
        public async Task GetMetadata_concurrent_access_does_not_corrupt_cache()
        {
            var conference = CreateTestConference(acronym: "37c3", title: "37C3: UnLocked");

            var mockApi = CreateMockApiClient();
            var apiCallCount = 0;
            mockApi.Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                   .Returns(async (CancellationToken ct) =>
                   {
                       Interlocked.Increment(ref apiCallCount);
                       await Task.Delay(50, ct);
                       return (IReadOnlyList<ConferenceDto>)new List<ConferenceDto> { conference };
                   });

            var provider = CreateProvider(apiClient: mockApi.Object);

            var seriesId = new SeriesInfo { Name = "37c3" };
            var tasks = new List<Task<MetadataResult<Series>>>();

            for (var i = 0; i < 10; i++)
            {
                tasks.Add(provider.GetMetadata(seriesId, CancellationToken.None));
            }

            var results = await Task.WhenAll(tasks);

            foreach (var result in results)
            {
                Assert.True(result.HasMetadata);
                Assert.Equal("37C3: UnLocked", result.Item.Name);
            }

            Assert.True(apiCallCount <= 2, $"API was called {apiCallCount} times; expected at most 2 due to concurrent cache misses");
        }

        #endregion

        #region GetSearchResults Tests

        [Fact]
        public async Task GetSearchResults_returns_results_for_known_conference()
        {
            var conference = CreateTestConference(acronym: "37c3", title: "37C3: UnLocked");

            var mockApi = CreateMockApiClient();
            mockApi.Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new List<ConferenceDto> { conference });

            var provider = CreateProvider(apiClient: mockApi.Object);
            var searchInfo = new SeriesInfo();
            searchInfo.ProviderIds["MediaCccDe"] = "37c3";

            var results = await provider.GetSearchResults(searchInfo, CancellationToken.None);

            Assert.NotEmpty(results);
            var result = results.First();
            Assert.Equal("37C3: UnLocked", result.Name);
            Assert.Equal("37c3", result.ProviderIds["MediaCccDe"]);
        }

        [Fact]
        public async Task GetSearchResults_searches_by_name()
        {
            var conference = CreateTestConference(acronym: "37c3", title: "37C3: UnLocked");

            var mockApi = CreateMockApiClient();
            mockApi.Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new List<ConferenceDto> { conference });

            var provider = CreateProvider(apiClient: mockApi.Object);
            var searchInfo = new SeriesInfo { Name = "37c3" };

            var results = await provider.GetSearchResults(searchInfo, CancellationToken.None);

            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task GetSearchResults_returns_empty_for_no_match()
        {
            var conference = CreateTestConference(acronym: "37c3", title: "37C3: UnLocked");

            var mockApi = CreateMockApiClient();
            mockApi.Setup(x => x.GetConferencesAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new List<ConferenceDto> { conference });

            var provider = CreateProvider(apiClient: mockApi.Object);
            var searchInfo = new SeriesInfo { Name = "nonexistent" };

            var results = await provider.GetSearchResults(searchInfo, CancellationToken.None);

            Assert.Empty(results);
        }

        #endregion
    }
}