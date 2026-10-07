using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Moq;
using Moq.Protected;
using Xunit;
using Jellyfin.Plugin.MediaCccDe.Models;
using Jellyfin.Plugin.MediaCccDe.Api;

namespace Jellyfin.Plugin.MediaCccDe.Tests.Unit
{
    public class MediaCccApiTests
    {
        private const string BaseUrl = "https://api.media.ccc.de/public/";

        #region Constructor Tests

        [Fact]
        public void Constructor_creates_client_from_factory()
        {
            var mockFactory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
            var handlerMock = new Mock<HttpMessageHandler>();
            handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}")
                });

            var httpClient = new HttpClient(handlerMock.Object) { BaseAddress = new Uri(BaseUrl) };
            mockFactory.Setup(x => x.CreateClient("MediaCccApi")).Returns(httpClient);

            var apiClient = new MediaCccApi(mockFactory.Object);

            Assert.NotNull(apiClient);
        }

        [Fact]
        public void Constructor_throws_on_null_HttpClientFactory()
        {
            Assert.Throws<ArgumentNullException>(() => new MediaCccApi(null!));
        }

        #endregion

        #region GetAsync Tests

        [Fact]
        public async Task GetAsync_deserializes_response()
        {
            // Arrange
            var testData = new TestResponse { Name = "Test", Id = 42 };
            var json = JsonSerializer.Serialize(testData);
            
            var handlerMock = CreateHttpMessageHandlerMock(json, HttpStatusCode.OK);
            var httpClient = new HttpClient(handlerMock.Object);
            var apiClient = CreateApiClient(httpClient);

            // Act
            var result = await apiClient.GetAsync<TestResponse>("test/endpoint");

            // Assert
            Assert.NotNull(result);
            Assert.Equal("Test", result.Name);
            Assert.Equal(42, result.Id);
        }

        [Fact]
        public async Task GetAsync_handles_not_found()
        {
            // Arrange
            var handlerMock = CreateHttpMessageHandlerMock("Not found", HttpStatusCode.NotFound);
            var httpClient = new HttpClient(handlerMock.Object);
            var apiClient = CreateApiClient(httpClient);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<HttpRequestException>(
                () => apiClient.GetAsync<TestResponse>("nonexistent"));
            
            Assert.Contains("404", exception.Message);
        }

        [Fact]
        public async Task GetAsync_handles_http_errors()
        {
            // Arrange
            var errorCodes = new[] { HttpStatusCode.InternalServerError, 
                                      HttpStatusCode.BadGateway, 
                                      HttpStatusCode.ServiceUnavailable,
                                      HttpStatusCode.BadRequest };

            foreach (var errorCode in errorCodes)
            {
                var handlerMock = CreateHttpMessageHandlerMock($"Error {(int)errorCode}", errorCode);
                var httpClient = new HttpClient(handlerMock.Object);
                var apiClient = CreateApiClient(httpClient);

                // Act & Assert
                var exception = await Assert.ThrowsAsync<HttpRequestException>(
                    () => apiClient.GetAsync<TestResponse>("error/endpoint"));
                
                Assert.Contains(((int)errorCode).ToString(), exception.Message);
            }
        }

        [Fact]
        public async Task GetAsync_throws_on_network_error()
        {
            // Arrange
            var handlerMock = new Mock<HttpMessageHandler>();
            handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ThrowsAsync(new HttpRequestException("Network error"));

            var httpClient = new HttpClient(handlerMock.Object);
            var apiClient = CreateApiClient(httpClient);

            // Act & Assert
            await Assert.ThrowsAsync<HttpRequestException>(
                () => apiClient.GetAsync<TestResponse>("network/error"));
        }

        [Fact]
        public async Task GetAsync_cancels_on_timeout()
        {
            // Arrange - Create a handler that never completes
            var handlerMock = new Mock<HttpMessageHandler>();
            handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .Returns(async (HttpRequestMessage request, CancellationToken token) =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(60), token);
                    return new HttpResponseMessage(HttpStatusCode.OK);
                });

            using var httpClient = new HttpClient(handlerMock.Object);
            httpClient.Timeout = TimeSpan.FromMilliseconds(100);
            var apiClient = CreateApiClient(httpClient);

            // Act & Assert
            await Assert.ThrowsAsync<TaskCanceledException>(
                () => apiClient.GetAsync<TestResponse>("timeout/endpoint"));
        }

        [Fact]
        public async Task GetAsync_uses_client_configured_with_accept_header()
        {
            // Arrange
            HttpRequestMessage? capturedRequest = null;
            var handlerMock = new Mock<HttpMessageHandler>();
            handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"name\":\"Test\"}")
                });

            var httpClient = new HttpClient(handlerMock.Object);
            httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            var apiClient = CreateApiClient(httpClient);

            // Act
            await apiClient.GetAsync<TestResponse>("test/endpoint");

            // Assert
            Assert.NotNull(capturedRequest);
            Assert.Contains("application/json", capturedRequest.Headers.Accept.ToString());
        }

        [Fact]
        public async Task GetAsync_concats_to_base_url()
        {
            // Arrange
            var testData = new TestResponse { Name = "OK", Id = 1 };
            var json = JsonSerializer.Serialize(testData);
            Uri? capturedUri = null;
            
            var handlerMock = new Mock<HttpMessageHandler>();
            handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedUri = req.RequestUri)
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json)
                });

            var httpClient = new HttpClient(handlerMock.Object);
            httpClient.BaseAddress = new Uri(BaseUrl);
            var apiClient = CreateApiClient(httpClient);

            // Act
            await apiClient.GetAsync<TestResponse>("conferences");

            // Assert
            Assert.NotNull(capturedUri);
            Assert.Equal("https://api.media.ccc.de/public/conferences", capturedUri.ToString());
        }

        #endregion

        #region URL Construction Tests

        [Fact]
        public void BuildUrl_encodes_parameters()
        {
            // Arrange
            var httpClient = new HttpClient();
            var apiClient = CreateApiClient(httpClient);

            // Act
            var url = apiClient.BuildUrl("search", new { q = "test query with spaces" });

            // Assert
            Assert.Contains("q=test%20query%20with%20spaces", url);
            Assert.Contains("search", url);
        }

        [Fact]
        public void BuildUrl_appends_path_correctly()
        {
            // Arrange
            var httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri(BaseUrl);
            var apiClient = CreateApiClient(httpClient);

            // Act
            var url = apiClient.BuildUrl("conferences/123/events");

            // Assert
            Assert.Equal("https://api.media.ccc.de/public/conferences/123/events", url);
        }

        [Fact]
        public void BuildUrl_handles_existing_query_string()
        {
            // Arrange
            var httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri(BaseUrl);
            var apiClient = CreateApiClient(httpClient);

            // Act
            var url = apiClient.BuildUrl("conferences?limit=10", new { offset = 20 });

            // Assert
            Assert.Contains("limit=10", url);
            Assert.Contains("offset=20", url);
            Assert.Equal("https://api.media.ccc.de/public/conferences?limit=10&offset=20", url);
        }

        [Fact]
        public void BuildUrl_encodes_special_characters()
        {
            // Arrange
            var httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri(BaseUrl);
            var apiClient = CreateApiClient(httpClient);

            // Act
            var url = apiClient.BuildUrl("events", new { slug = "event-2024-äöü" });

            // Assert
            Assert.Contains("event-2024-%C3%A4%C3%B6%C3%BC", url);
        }

        [Fact]
        public void BuildUrl_prepends_endpoint_to_base()
        {
            // Arrange
            var httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri(BaseUrl);
            var apiClient = CreateApiClient(httpClient);

            // Act
            var url = apiClient.BuildUrl("/test/endpoint");

            // Assert  
            Assert.StartsWith(BaseUrl, url);
            Assert.EndsWith("test/endpoint", url);
        }

        #endregion

        #region Edge Cases

        [Fact]
        public async Task GetAsync_returns_empty_object_on_empty_json()
        {
            // Arrange
            var handlerMock = CreateHttpMessageHandlerMock("{}", HttpStatusCode.OK);
            var httpClient = new HttpClient(handlerMock.Object);
            var apiClient = CreateApiClient(httpClient);

            // Act
            var result = await apiClient.GetAsync<TestObject>("empty");

            // Assert
            Assert.NotNull(result);
        }

        [Fact]
        public async Task GetAsync_throws_on_invalid_json()
        {
            // Arrange
            var handlerMock = CreateHttpMessageHandlerMock("Invalid JSON", HttpStatusCode.OK);
            var httpClient = new HttpClient(handlerMock.Object);
            var apiClient = CreateApiClient(httpClient);

            // Act & Assert
            await Assert.ThrowsAsync<JsonException>(
                () => apiClient.GetAsync<TestObject>("invalid"));
        }

        [Fact]
        public async Task GetEventAsync_accepts_recording_with_null_length()
        {
            var json = "{\"guid\":\"event-1\",\"recordings\":[{\"id\":1,\"length\":null,\"width\":null,\"height\":null,\"size\":null,\"recording_url\":\"https://media.example/event-1.mp4\"}]}";
            var handlerMock = CreateHttpMessageHandlerMock(json, HttpStatusCode.OK);
            var apiClient = CreateApiClient(handlerMock);

            var result = await apiClient.GetEventAsync("event-1");

            Assert.NotNull(result);
            var recording = Assert.Single(result.Recordings!);
            Assert.Null(recording.Length);
            Assert.Null(recording.Width);
            Assert.Null(recording.Height);
            Assert.Null(recording.Size);
            Assert.Equal("https://media.example/event-1.mp4", recording.EffectiveUrl);
        }

        #endregion

        #region Helper Methods

        private static IMediaCccApiClient CreateApiClient(HttpClient httpClient)
        {
            if (httpClient.BaseAddress == null)
            {
                httpClient.BaseAddress = new Uri(BaseUrl);
            }

            var mockFactory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
            mockFactory
                .Setup(x => x.CreateClient("MediaCccApi"))
                .Returns(httpClient);
            return new MediaCccApi(mockFactory.Object);
        }

        private static IMediaCccApiClient CreateApiClient(Mock<HttpMessageHandler> handlerMock)
        {
            var httpClient = new HttpClient(handlerMock.Object) { BaseAddress = new Uri(BaseUrl) };
            var mockFactory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
            mockFactory
                .Setup(x => x.CreateClient("MediaCccApi"))
                .Returns(httpClient);
            return new MediaCccApi(mockFactory.Object);
        }



        private static Mock<HttpMessageHandler> CreateHttpMessageHandlerMock(string content, HttpStatusCode statusCode)
        {
            var handlerMock = new Mock<HttpMessageHandler>();
            handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage(statusCode)
                {
                    Content = new StringContent(content)
                });
            return handlerMock;
        }

        #endregion

        #region GetConferences Tests

        [Fact]
        public async Task GetConferences_returns_list_of_conferences()
        {
            // Arrange
            var json = @"[
                {""title"":""37C3"",""acronym"":""37c3"",""slug"":""37c3"",""aspect_ratio"":""16:9"",""updated_at"":""2024-01-01T00:00:00Z""},
                {""title"":""36C3"",""acronym"":""36c3"",""slug"":""36c3"",""aspect_ratio"":""16:9"",""updated_at"":""2023-01-01T00:00:00Z""}
            ]";
            
            var handlerMock = CreateHttpMessageHandlerMock(json, HttpStatusCode.OK);
            var httpClient = new HttpClient(handlerMock.Object);
            var apiClient = CreateApiClient(httpClient);

            // Act
            var result = await apiClient.GetConferencesAsync();

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, result.Count);
            Assert.Equal("37C3", result[0].Title);
            Assert.Equal("37c3", result[0].Acronym);
            Assert.Equal("36C3", result[1].Title);
            Assert.Equal("36c3", result[1].Acronym);
        }

        [Fact]
        public async Task GetConferences_handles_empty_response()
        {
            // Arrange
            var handlerMock = CreateHttpMessageHandlerMock("[]", HttpStatusCode.OK);
            var httpClient = new HttpClient(handlerMock.Object);
            var apiClient = CreateApiClient(httpClient);

            // Act
            var result = await apiClient.GetConferencesAsync();

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public async Task GetConferences_deserializes_conference_properties()
        {
            // Arrange - Complete conference with all properties
            var json = @"[
                {
                    ""title"": ""DEFCON 32"",
                    ""acronym"": ""defcon32"",
                    ""slug"": ""defcon32"",
                    ""aspect_ratio"": ""16:9"",
                    ""updated_at"": ""2024-08-15T12:30:45Z"",
                    ""url"": ""https://media.ccc.de/c/defcon32"",
                    ""schedule_url"": ""https://defcon.org/schedule""
                }
            ]";
            
            var handlerMock = CreateHttpMessageHandlerMock(json, HttpStatusCode.OK);
            var httpClient = new HttpClient(handlerMock.Object);
            var apiClient = CreateApiClient(httpClient);

            // Act
            var result = await apiClient.GetConferencesAsync();

            // Assert
            Assert.Single(result);
            var conference = result[0];
            Assert.Equal("DEFCON 32", conference.Title);
            Assert.Equal("defcon32", conference.Acronym);
            Assert.Equal("defcon32", conference.Slug);
            Assert.Equal("16:9", conference.AspectRatio);
            Assert.NotNull(conference.UpdatedAt);
            Assert.Equal(new DateTime(2024, 8, 15, 12, 30, 45, DateTimeKind.Utc), conference.UpdatedAt.Value.ToUniversalTime());
            Assert.Equal("https://media.ccc.de/c/defcon32", conference.Url);
            Assert.Equal("https://defcon.org/schedule", conference.ScheduleUrl);
        }

        [Fact]
        public async Task GetConferences_deserializes_current_wrapped_api_response()
        {
            var json = @"{
                ""conferences"": [
                    {""title"": ""37C3"", ""acronym"": ""37c3"", ""slug"": ""37c3""}
                ]
            }";

            var handlerMock = CreateHttpMessageHandlerMock(json, HttpStatusCode.OK);
            var httpClient = new HttpClient(handlerMock.Object);
            var apiClient = CreateApiClient(httpClient);

            // Act
            var result = await apiClient.GetConferencesAsync();

            // Assert
            var conference = Assert.Single(result);
            Assert.Equal("37C3", conference.Title);
            Assert.Equal("37c3", conference.Acronym);
        }

        [Fact]
        public async Task GetEvents_by_conference_identifier_hydrates_recordings()
        {
            var handlerMock = new Mock<HttpMessageHandler>();
            handlerMock.Protected()
                .SetupSequence<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(@"{
                        ""acronym"": ""37c3"",
                        ""events"": [{""guid"": ""event-1"", ""title"": ""Opening""}]
                    }")
                })
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(@"{
                        ""guid"": ""event-1"",
                        ""title"": ""Opening"",
                        ""recordings"": [{""recording_url"": ""https://cdn.example.test/opening.mp4""}]
                    }")
                });

            var apiClient = CreateApiClient(new HttpClient(handlerMock.Object));

            var result = await apiClient.GetEventsAsync("37c3");

            var evt = Assert.Single(result);
            Assert.Equal("event-1", evt.Guid);
            Assert.Single(evt.Recordings!);
            Assert.Equal("https://cdn.example.test/opening.mp4", evt.Recordings[0].EffectiveUrl);
        }

        [Fact]
        public async Task GetConferences_fetches_from_correct_endpoint()
        {
            // Arrange
            Uri? capturedUri = null;
            var handlerMock = new Mock<HttpMessageHandler>();
            handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedUri = req.RequestUri)
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                });

            var httpClient = new HttpClient(handlerMock.Object);
            httpClient.BaseAddress = new Uri(BaseUrl);
            var apiClient = CreateApiClient(httpClient);

            // Act
            await apiClient.GetConferencesAsync();

            // Assert
            Assert.NotNull(capturedUri);
            Assert.Equal("https://api.media.ccc.de/public/conferences", capturedUri.ToString());
        }

        [Fact]
        public async Task GetConferences_handles_http_error()
        {
            // Arrange
            var errorCodes = new[] { 
                HttpStatusCode.InternalServerError, 
                HttpStatusCode.BadGateway,
                HttpStatusCode.ServiceUnavailable 
            };

            foreach (var errorCode in errorCodes)
            {
                var handlerMock = CreateHttpMessageHandlerMock($"Error {(int)errorCode}", errorCode);
                var httpClient = new HttpClient(handlerMock.Object);
                var apiClient = CreateApiClient(httpClient);

                // Act & Assert
                var exception = await Assert.ThrowsAsync<HttpRequestException>(
                    () => apiClient.GetConferencesAsync());
                
                Assert.Contains(((int)errorCode).ToString(), exception.Message);
            }
        }

        [Fact]
        public async Task GetConferences_handles_not_found()
        {
            // Arrange
            var handlerMock = CreateHttpMessageHandlerMock("Not found", HttpStatusCode.NotFound);
            var httpClient = new HttpClient(handlerMock.Object);
            var apiClient = CreateApiClient(httpClient);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<HttpRequestException>(
                () => apiClient.GetConferencesAsync());
            
            Assert.Contains("404", exception.Message);
        }

        [Fact]
        public async Task GetConferences_throws_on_invalid_json()
        {
            // Arrange
            var handlerMock = CreateHttpMessageHandlerMock("Invalid JSON", HttpStatusCode.OK);
            var httpClient = new HttpClient(handlerMock.Object);
            var apiClient = CreateApiClient(httpClient);

            // Act & Assert
            await Assert.ThrowsAsync<JsonException>(
                () => apiClient.GetConferencesAsync());
        }

        #endregion

        #region GetEvents Tests

        [Fact]
        public async Task GetEvents_returns_events_for_conference()
        {
            // Arrange
            var eventsJson = @"[
                {
                    ""guid"": ""abc123"",
                    ""title"": ""Opening Event"",
                    ""slug"": ""37c3-12746-opening_event"",
                    ""conference_id"": 123,
                    ""recordings"": []
                },
                {
                    ""guid"": ""def456"",
                    ""title"": ""Closing Event"",
                    ""slug"": ""37c3-12747-closing_event"",
                    ""conference_id"": 123,
                    ""recordings"": []
                }
            ]";
            
            var handlerMock = CreateHttpMessageHandlerMock(eventsJson, HttpStatusCode.OK);
            var httpClient = new HttpClient(handlerMock.Object);
            var apiClient = CreateApiClient(httpClient);

            // Act
            var result = await apiClient.GetEventsAsync(123);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, result.Length);
            Assert.Equal("abc123", result[0].Guid);
            Assert.Equal("Opening Event", result[0].Title);
            Assert.Equal("def456", result[1].Guid);
            Assert.Equal("Closing Event", result[1].Title);
        }

        [Fact]
        public async Task GetEvents_handles_empty_conference()
        {
            // Arrange
            var emptyJson = "[]";
            var handlerMock = CreateHttpMessageHandlerMock(emptyJson, HttpStatusCode.OK);
            var httpClient = new HttpClient(handlerMock.Object);
            var apiClient = CreateApiClient(httpClient);

            // Act
            var result = await apiClient.GetEventsAsync(999);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public async Task GetEvents_includes_recordings_array()
        {
            // Arrange
            var eventWithRecordings = @"[{
                ""guid"": ""abc123"",
                ""title"": ""Test Event"",
                ""slug"": ""test-event"",
                ""conference_id"": 456,
                ""recordings"": [
                    {
                        ""language"": ""en"",
                        ""format"": ""mp4"",
                        ""high_quality"": true,
                        ""width"": 1920,
                        ""height"": 1080,
                        ""size"": 1024,
                        ""url"": ""https://example.com/video.mp4""
                    }
                ]
            }]";
            
            var handlerMock = CreateHttpMessageHandlerMock(eventWithRecordings, HttpStatusCode.OK);
            var httpClient = new HttpClient(handlerMock.Object);
            var apiClient = CreateApiClient(httpClient);

            // Act
            var result = await apiClient.GetEventsAsync(456);

            // Assert
            Assert.Single(result);
            Assert.NotNull(result[0].Recordings);
            Assert.Single(result[0].Recordings);
            Assert.Equal("en", result[0].Recordings[0].Language);
            Assert.Equal("mp4", result[0].Recordings[0].Format);
        }

        [Fact]
        public async Task GetEvents_fetches_from_correct_endpoint()
        {
            // Arrange
            Uri? capturedUri = null;
            var handlerMock = new Mock<HttpMessageHandler>();
            handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedUri = req.RequestUri)
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                });

            var httpClient = new HttpClient(handlerMock.Object) { BaseAddress = new Uri(BaseUrl) };
            var apiClient = CreateApiClient(httpClient);

            // Act
            await apiClient.GetEventsAsync(789);

            // Assert
            Assert.NotNull(capturedUri);
            Assert.Equal("https://api.media.ccc.de/public/conferences/789/events", capturedUri!.ToString());
        }

        [Fact]
        public async Task GetEvents_handles_conference_not_found()
        {
            // Arrange
            var handlerMock = CreateHttpMessageHandlerMock("Conference not found", HttpStatusCode.NotFound);
            var httpClient = new HttpClient(handlerMock.Object);
            var apiClient = CreateApiClient(httpClient);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<HttpRequestException>(
                () => apiClient.GetEventsAsync(404));
            
            Assert.Contains("404", exception.Message);
        }

        [Fact]
        public async Task GetEvents_validates_conference_id()
        {
            // Arrange
            var httpClient = new HttpClient();
            var apiClient = CreateApiClient(httpClient);

            // Act & Assert - Should throw ArgumentException for invalid conference_id
            await Assert.ThrowsAsync<ArgumentException>(
                () => apiClient.GetEventsAsync(0));
        }

        [Fact]
        public async Task GetEvents_validates_negative_conference_id()
        {
            // Arrange
            var httpClient = new HttpClient();
            var apiClient = CreateApiClient(httpClient);

            // Act & Assert - Should throw ArgumentException for negative conference_id
            await Assert.ThrowsAsync<ArgumentException>(
                () => apiClient.GetEventsAsync(-1));
        }

        #endregion

        #region GetEvent Tests

        [Fact]
        public async Task GetEvent_returns_single_event_with_recordings()
        {
            // Arrange
            var eventJson = @"{
                ""guid"": ""abc123-def456-ghi789"",
                ""title"": ""Opening Event"",
                ""slug"": ""37c3-12746-opening_event"",
                ""description"": ""Conference opening ceremony"",
                ""link"": ""https://media.ccc.de/v/37c3-12746-opening_event"",
                ""date"": ""2023-12-27"",
                ""length"": 3600,
                ""conference_id"": 123,
                ""recordings"": [
                    {
                        ""language"": ""en"",
                        ""format"": ""mp4"",
                        ""high_quality"": true,
                        ""width"": 1920,
                        ""height"": 1080,
                        ""size"": 1500000000,
                        ""url"": ""https://cdn.media.ccc.de/congress/2023/h264-hd/37c3-12746-deu-slides.mp4""
                    }
                ]
            }";
            
            var handlerMock = CreateHttpMessageHandlerMock(eventJson, HttpStatusCode.OK);
            var httpClient = new HttpClient(handlerMock.Object);
            var apiClient = CreateApiClient(httpClient);

            // Act
            var result = await apiClient.GetEventAsync("abc123-def456-ghi789");

            // Assert
            Assert.NotNull(result);
            Assert.Equal("abc123-def456-ghi789", result.Guid);
            Assert.Equal("Opening Event", result.Title);
            Assert.Equal("37c3-12746-opening_event", result.Slug);
            Assert.Equal("Conference opening ceremony", result.Description);
            Assert.Equal("https://media.ccc.de/v/37c3-12746-opening_event", result.Link);
            Assert.Equal("2023-12-27", result.Date);
            Assert.Equal(3600, result.Length);
            Assert.Equal(123, result.ConferenceId);
            Assert.NotNull(result.Recordings);
            Assert.Single(result.Recordings);
            
            var recording = result.Recordings[0];
            Assert.Equal("en", recording.Language);
            Assert.Equal("mp4", recording.Format);
            Assert.True(recording.HighQuality);
            Assert.Equal(1920, recording.Width);
            Assert.Equal(1080, recording.Height);
            Assert.Equal(1500000000, recording.Size);
            Assert.Equal("https://cdn.media.ccc.de/congress/2023/h264-hd/37c3-12746-deu-slides.mp4", recording.Url);
        }

        [Fact]
        public async Task GetEvent_handles_event_not_found()
        {
            // Arrange
            var handlerMock = CreateHttpMessageHandlerMock("Event not found", HttpStatusCode.NotFound);
            var httpClient = new HttpClient(handlerMock.Object);
            var apiClient = CreateApiClient(httpClient);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<HttpRequestException>(
                () => apiClient.GetEventAsync("nonexistent-guid"));
            
            Assert.Contains("404", exception.Message);
        }

        [Fact]
        public async Task GetEvent_validates_guid_format()
        {
            // Arrange
            var httpClient = new HttpClient();
            var apiClient = CreateApiClient(httpClient);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(
                () => apiClient.GetEventAsync(""));
            
            await Assert.ThrowsAsync<ArgumentException>(
                () => apiClient.GetEventAsync("   "));
            
            await Assert.ThrowsAsync<ArgumentException>(
                () => apiClient.GetEventAsync(null!));
        }

        [Fact]
        public async Task GetEvent_fetches_from_correct_endpoint()
        {
            // Arrange
            var eventJson = @"{""guid"": ""test-guid"", ""title"": ""Test Event""}";
            Uri? capturedUri = null;
            
            var handlerMock = new Mock<HttpMessageHandler>();
            handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedUri = req.RequestUri)
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(eventJson)
                });

            var httpClient = new HttpClient(handlerMock.Object);
            httpClient.BaseAddress = new Uri(BaseUrl);
            var apiClient = CreateApiClient(httpClient);

            // Act
            await apiClient.GetEventAsync("abc123-def456-ghi789");

            // Assert
            Assert.NotNull(capturedUri);
            Assert.Equal("https://api.media.ccc.de/public/events/abc123-def456-ghi789", capturedUri.ToString());
        }

        [Fact]
        public async Task GetEvent_deserializes_nested_recordings()
        {
            // Arrange - Event with multiple recordings
            var eventJson = @"{
                ""guid"": ""multi-recording-event"",
                ""title"": ""Multi Recording Event"",
                ""slug"": ""37c3-multi"",
                ""description"": ""Event with multiple recordings"",
                ""link"": ""https://media.ccc.de/v/37c3-multi"",
                ""date"": ""2023-12-28"",
                ""length"": 5400,
                ""conference_id"": 456,
                ""recordings"": [
                    {
                        ""language"": ""en"",
                        ""format"": ""mp4"",
                        ""high_quality"": true,
                        ""width"": 1920,
                        ""height"": 1080,
                        ""size"": 2000000000,
                        ""url"": ""https://cdn.media.ccc.de/hd.mp4""
                    },
                    {
                        ""language"": ""en"",
                        ""format"": ""webm"",
                        ""high_quality"": false,
                        ""width"": 1280,
                        ""height"": 720,
                        ""size"": 800000000,
                        ""url"": ""https://cdn.media.ccc.de/sd.webm""
                    },
                    {
                        ""language"": ""de"",
                        ""format"": ""mp4"",
                        ""high_quality"": true,
                        ""width"": 1920,
                        ""height"": 1080,
                        ""size"": 2100000000,
                        ""url"": ""https://cdn.media.ccc.de/hd-de.mp4""
                    }
                ]
            }";
            
            var handlerMock = CreateHttpMessageHandlerMock(eventJson, HttpStatusCode.OK);
            var httpClient = new HttpClient(handlerMock.Object);
            var apiClient = CreateApiClient(httpClient);

            // Act
            var result = await apiClient.GetEventAsync("multi-recording-event");

            // Assert
            Assert.NotNull(result);
            Assert.NotNull(result.Recordings);
            Assert.Equal(3, result.Recordings.Count);
            
            // First recording (HD English)
            Assert.Equal("en", result.Recordings[0].Language);
            Assert.Equal("mp4", result.Recordings[0].Format);
            Assert.True(result.Recordings[0].HighQuality);
            
            // Second recording (SD English)
            Assert.Equal("en", result.Recordings[1].Language);
            Assert.Equal("webm", result.Recordings[1].Format);
            Assert.False(result.Recordings[1].HighQuality);
            
            // Third recording (HD German)
            Assert.Equal("de", result.Recordings[2].Language);
            Assert.Equal("mp4", result.Recordings[2].Format);
            Assert.True(result.Recordings[2].HighQuality);
        }

        [Fact]
        public async Task GetEvent_returns_null_for_not_found()
        {
            // Arrange
            var handlerMock = CreateHttpMessageHandlerMock("Not found", HttpStatusCode.NotFound);
            var httpClient = new HttpClient(handlerMock.Object);
            var apiClient = CreateApiClient(httpClient);

            // Act & Assert - Should throw HttpRequestException for 404
            var exception = await Assert.ThrowsAsync<HttpRequestException>(
                () => apiClient.GetEventAsync("nonexistent-guid"));
            
            Assert.NotNull(exception);
        }

        [Fact]
        public async Task GetEvent_handles_empty_recordings_array()
        {
            // Arrange
            var eventJson = @"{
                ""guid"": ""no-recordings"",
                ""title"": ""Event Without Recordings"",
                ""slug"": ""37c3-no-recordings"",
                ""description"": ""Event with no recordings"",
                ""link"": ""https://media.ccc.de/v/37c3-no-recordings"",
                ""date"": ""2023-12-29"",
                ""length"": 0,
                ""conference_id"": 789,
                ""recordings"": []
            }";
            
            var handlerMock = CreateHttpMessageHandlerMock(eventJson, HttpStatusCode.OK);
            var httpClient = new HttpClient(handlerMock.Object);
            var apiClient = CreateApiClient(httpClient);

            // Act
            var result = await apiClient.GetEventAsync("no-recordings");

            // Assert
            Assert.NotNull(result);
            Assert.NotNull(result.Recordings);
            Assert.Empty(result.Recordings);
        }

        [Fact]
        public async Task GetEvent_handles_missing_recordings_property()
        {
            // Arrange - Event without recordings property
            var eventJson = @"{
                ""guid"": ""missing-recordings"",
                ""title"": ""Event Missing Recordings"",
                ""slug"": ""37c3-missing"",
                ""description"": ""Event without recordings property"",
                ""link"": ""https://media.ccc.de/v/37c3-missing"",
                ""date"": ""2023-12-30"",
                ""length"": 1800,
                ""conference_id"": 999
            }";
            
            var handlerMock = CreateHttpMessageHandlerMock(eventJson, HttpStatusCode.OK);
            var httpClient = new HttpClient(handlerMock.Object);
            var apiClient = CreateApiClient(httpClient);

            // Act
            var result = await apiClient.GetEventAsync("missing-recordings");

            // Assert
            Assert.NotNull(result);
            // Recordings should be null or empty when not present in JSON
            Assert.True(result.Recordings == null || result.Recordings.Count == 0);
        }

        #endregion

        #region GetRecent Tests

        [Fact]
        public async Task GetRecent_includes_recordings()
        {
            // Arrange
            var json = @"[
                {
                    ""guid"": ""recent-001"",
                    ""title"": ""Recent Talk"",
                    ""date"": ""2024-01-15"",
                    ""slug"": ""recent-talk"",
                    ""conference_id"": 100,
                    ""recordings"": [
                        {
                            ""language"": ""en"",
                            ""format"": ""webm"",
                            ""high_quality"": true,
                            ""width"": 1920,
                            ""height"": 1080,
                            ""size"": 2048000,
                            ""url"": ""https://example.com/video.webm""
                        },
                        {
                            ""language"": ""de"",
                            ""format"": ""mp4"",
                            ""high_quality"": false,
                            ""width"": 1280,
                            ""height"": 720,
                            ""size"": 1024000,
                            ""url"": ""https://example.com/video.mp4""
                        }
                    ]
                }
            ]";
            
            var handlerMock = CreateHttpMessageHandlerMock(json, HttpStatusCode.OK);
            var httpClient = new HttpClient(handlerMock.Object);
            var apiClient = CreateApiClient(httpClient);

            // Act
            var result = await apiClient.GetRecentAsync();

            // Assert
            Assert.Single(result);
            Assert.NotNull(result[0].Recordings);
            Assert.Equal(2, result[0].Recordings!.Count);
            Assert.Equal("en", result[0].Recordings[0].Language);
            Assert.Equal("webm", result[0].Recordings[0].Format);
            Assert.True(result[0].Recordings[0].HighQuality);
            Assert.Equal("de", result[0].Recordings[1].Language);
            Assert.Equal("mp4", result[0].Recordings[1].Format);
            Assert.False(result[0].Recordings[1].HighQuality);
        }

        [Fact]
        public async Task GetRecent_returns_recently_released_events()
        {
            // Arrange
            var json = @"[
                {
                    ""guid"": ""recent-001"",
                    ""title"": ""Recent Talk 1"",
                    ""date"": ""2024-01-15"",
                    ""slug"": ""recent-talk-1"",
                    ""conference_id"": 100,
                    ""recordings"": []
                },
                {
                    ""guid"": ""recent-002"",
                    ""title"": ""Recent Talk 2"",
                    ""date"": ""2024-01-14"",
                    ""slug"": ""recent-talk-2"",
                    ""conference_id"": 101,
                    ""recordings"": []
                }
            ]";
            
            var handlerMock = CreateHttpMessageHandlerMock(json, HttpStatusCode.OK);
            var httpClient = new HttpClient(handlerMock.Object);
            var apiClient = CreateApiClient(httpClient);

            // Act
            var result = await apiClient.GetRecentAsync();

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, result.Length);
            Assert.Equal("recent-001", result[0].Guid);
            Assert.Equal("Recent Talk 1", result[0].Title);
            Assert.Equal("recent-002", result[1].Guid);
            Assert.Equal("Recent Talk 2", result[1].Title);
        }

        [Fact]
        public async Task GetRecent_limits_results()
        {
            // Arrange - API returns 5 events, but we only request 3
            var json = @"[
                {""guid"": ""r1"", ""title"": ""Talk 1"", ""date"": ""2024-01-15"", ""slug"": ""t1"", ""conference_id"": 1, ""recordings"": []},
                {""guid"": ""r2"", ""title"": ""Talk 2"", ""date"": ""2024-01-14"", ""slug"": ""t2"", ""conference_id"": 1, ""recordings"": []},
                {""guid"": ""r3"", ""title"": ""Talk 3"", ""date"": ""2024-01-13"", ""slug"": ""t3"", ""conference_id"": 1, ""recordings"": []},
                {""guid"": ""r4"", ""title"": ""Talk 4"", ""date"": ""2024-01-12"", ""slug"": ""t4"", ""conference_id"": 1, ""recordings"": []},
                {""guid"": ""r5"", ""title"": ""Talk 5"", ""date"": ""2024-01-11"", ""slug"": ""t5"", ""conference_id"": 1, ""recordings"": []}
            ]";
            
            Uri? capturedUri = null;
            var handlerMock = new Mock<HttpMessageHandler>();
            handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedUri = req.RequestUri)
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json)
                });

            var httpClient = new HttpClient(handlerMock.Object) { BaseAddress = new Uri(BaseUrl) };
            var apiClient = CreateApiClient(httpClient);

            // Act
            var result = await apiClient.GetRecentAsync(limit: 3);

            // Assert
            Assert.NotNull(capturedUri);
            Assert.Equal(3, result.Length);
            Assert.Contains("limit=3", capturedUri!.Query);
        }

        [Fact]
        public async Task GetRecent_handles_empty_response()
        {
            // Arrange
            var handlerMock = CreateHttpMessageHandlerMock("[]", HttpStatusCode.OK);
            var httpClient = new HttpClient(handlerMock.Object);
            var apiClient = CreateApiClient(httpClient);

            // Act
            var result = await apiClient.GetRecentAsync();

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public async Task GetRecent_fetches_from_correct_endpoint()
        {
            // Arrange
            Uri? capturedUri = null;
            var handlerMock = new Mock<HttpMessageHandler>();
            handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedUri = req.RequestUri)
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                });

            var httpClient = new HttpClient(handlerMock.Object) { BaseAddress = new Uri(BaseUrl) };
            var apiClient = CreateApiClient(httpClient);

            // Act
            await apiClient.GetRecentAsync();

            // Assert
            Assert.NotNull(capturedUri);
            Assert.Equal(
                "https://api.media.ccc.de/public/events/recent?limit=50",
                capturedUri!.ToString());
        }

        [Fact]
        public async Task GetRecent_returns_ordered_by_date()
        {
            // Arrange - Events with different dates (not sorted)
            var json = @"[
                {
                    ""guid"": ""event-3"",
                    ""title"": ""Event Three"",
                    ""date"": ""2024-01-10"",
                    ""slug"": ""event-3"",
                    ""conference_id"": 1,
                    ""recordings"": []
                },
                {
                    ""guid"": ""event-1"",
                    ""title"": ""Event One"",
                    ""date"": ""2024-01-15"",
                    ""slug"": ""event-1"",
                    ""conference_id"": 1,
                    ""recordings"": []
                },
                {
                    ""guid"": ""event-2"",
                    ""title"": ""Event Two"",
                    ""date"": ""2024-01-12"",
                    ""slug"": ""event-2"",
                    ""conference_id"": 1,
                    ""recordings"": []
                }
            ]";
            
            var handlerMock = CreateHttpMessageHandlerMock(json, HttpStatusCode.OK);
            var httpClient = new HttpClient(handlerMock.Object);
            var apiClient = CreateApiClient(httpClient);

            // Act
            var result = await apiClient.GetRecentAsync();

            // Assert
            Assert.NotNull(result);
            Assert.Equal(3, result.Length);
            Assert.Equal("event-1", result[0].Guid); // 2024-01-15
            Assert.Equal("event-2", result[1].Guid); // 2024-01-12
            Assert.Equal("event-3", result[2].Guid); // 2024-01-10
        }

        [Fact]
        public async Task GetRecent_handles_error_response()
        {
            // Arrange
            var handlerMock = CreateHttpMessageHandlerMock("Internal Server Error", HttpStatusCode.InternalServerError);
            var httpClient = new HttpClient(handlerMock.Object);
            var apiClient = CreateApiClient(httpClient);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<HttpRequestException>(
                () => apiClient.GetRecentAsync());
            
            Assert.Contains("500", exception.Message);
        }

        [Fact]
        public async Task GetRecent_with_limit_adds_query_parameter()
        {
            // Arrange
            Uri? capturedUri = null;
            var handlerMock = new Mock<HttpMessageHandler>();
            handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedUri = req.RequestUri)
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                });

            var httpClient = new HttpClient(handlerMock.Object) { BaseAddress = new Uri(BaseUrl) };
            var apiClient = CreateApiClient(httpClient);

            // Act
            await apiClient.GetRecentAsync(limit: 10);

            // Assert
            Assert.NotNull(capturedUri);
            Assert.Contains("events/recent", capturedUri!.PathAndQuery);
            Assert.Contains("limit=10", capturedUri.Query);
        }

        [Fact]
        public async Task GetRecent_without_limit_applies_default_page_size()
        {
            // Arrange
            Uri? capturedUri = null;
            var handlerMock = new Mock<HttpMessageHandler>();
            handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedUri = req.RequestUri)
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                });

            var httpClient = new HttpClient(handlerMock.Object) { BaseAddress = new Uri(BaseUrl) };
            var apiClient = CreateApiClient(httpClient);

            // Act
            await apiClient.GetRecentAsync();

            // Assert - unbounded pages would fan out into one hydration request per event
            Assert.NotNull(capturedUri);
            Assert.Equal("https://api.media.ccc.de/public/events/recent?limit=50", capturedUri!.ToString());
        }

        [Fact]
        public async Task GetRecent_clamps_limit_to_maximum()
        {
            // Arrange
            Uri? capturedUri = null;
            var handlerMock = new Mock<HttpMessageHandler>();
            handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedUri = req.RequestUri)
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                });

            var httpClient = new HttpClient(handlerMock.Object) { BaseAddress = new Uri(BaseUrl) };
            var apiClient = CreateApiClient(httpClient);

            // Act
            await apiClient.GetRecentAsync(100000);

            // Assert
            Assert.NotNull(capturedUri);
            Assert.Equal("https://api.media.ccc.de/public/events/recent?limit=200", capturedUri!.ToString());
        }

        #endregion

        #region Test Data Classes

        private class TestResponse
        {
            public string Name { get; set; } = string.Empty;
            public int Id { get; set; }
        }

        private class TestObject
        {
            public string? Value { get; set; }
        }

        #endregion
    }
}
