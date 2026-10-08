using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.MediaCccDe.Api;
using Jellyfin.Plugin.MediaCccDe.Controllers;
using Jellyfin.Plugin.MediaCccDe.Models;
using Jellyfin.Plugin.MediaCccDe.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Moq;
using Moq.Protected;
using Xunit;
using PluginUserData = Jellyfin.Plugin.MediaCccDe.Models.UserData;
using PluginUserDataManager = Jellyfin.Plugin.MediaCccDe.Services.IUserDataManager;

namespace Jellyfin.Plugin.MediaCccDe.Tests.Unit
{
    /// <summary>
    /// Scratch directory and fixtures shared by the security regression suites.
    /// </summary>
    public abstract class SecurityTestBase : IDisposable
    {
        protected readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "ccc-security-tests",
            Guid.NewGuid().ToString());

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, true);
                }
            }
            catch (IOException)
            {
            }
        }

        protected static HttpRequest CreateRequest(bool isHttps, string? remoteAddress)
        {
            var context = new DefaultHttpContext();
            if (remoteAddress is not null)
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(remoteAddress);
            }

            context.Request.Scheme = isHttps ? Uri.UriSchemeHttps : Uri.UriSchemeHttp;
            return context.Request;
        }

        protected static User CreateUser(bool disabled)
        {
            var user = new User("someone", "provider", "reset")
            {
                Id = Guid.NewGuid()
            };

            if (disabled)
            {
                user.Permissions.Add(new Permission(PermissionKind.IsDisabled, true));
            }

            return user;
        }

        protected static Mock<IUserManager> CreateUserManager(User? user)
        {
            var manager = new Mock<IUserManager>(MockBehavior.Loose);
            manager.Setup(m => m.GetUserById(It.IsAny<Guid>())).Returns(user);
            return manager;
        }

        protected SettingsController CreateSettingsController(HttpRequest request, SettingsAccessTokenStore store)
        {
            var pluginManager = new Mock<IPluginManager>(MockBehavior.Loose);
            return new SettingsController(
                new PluginInstanceResolver(pluginManager.Object),
                store,
                () => new PluginConfiguration())
            {
                ControllerContext = new ControllerContext { HttpContext = request.HttpContext }
            };
        }

        protected static void SetCookie(HttpRequest request, string name, string value)
        {
            request.Headers["Cookie"] = $"{name}={value}";
        }

        protected sealed class StubHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _status;

            public StubHandler(HttpStatusCode status) => _status = status;

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                var body = _status == HttpStatusCode.OK
                    ? "[{\"Id\":\"" + Guid.NewGuid() + "\",\"Name\":\"someone\"}]"
                    : string.Empty;

                return Task.FromResult(new HttpResponseMessage(_status)
                {
                    Content = new StringContent(body)
                });
            }
        }

        protected sealed class FakeStorageGuard : IStorageGuard
        {
            public long AvailableBytes { get; set; } = long.MaxValue;

            public long UsedBytes { get; set; }

            public long GetAvailableBytes(string path) => AvailableBytes;

            public long GetUsedBytesInDirectory(string path) => UsedBytes;
        }

        protected sealed class StubHttpFactory : IHttpClientFactory
        {
            private readonly HttpStatusCode _status;

            public StubHttpFactory(HttpStatusCode status) => _status = status;

            public HttpClient CreateClient(string name)
            {
                var handler = new StubHandler(_status);
                return new HttpClient(handler, disposeHandler: false);
            }
        }

        protected static JellyfinIdentityVerifier CreateIdentityVerifier(HttpStatusCode status)
        {
            return new JellyfinIdentityVerifier(new StubHttpFactory(status), "http://127.0.0.1:8096");
        }

        protected static Mock<PluginUserDataManager> CreateUserDataManager()
        {
            var manager = new Mock<PluginUserDataManager>(MockBehavior.Loose);
            manager.Setup(m => m.GetUserData(It.IsAny<Guid>())).Returns(new PluginUserData());
            manager.Setup(m => m.GetUserName(It.IsAny<Guid>())).Returns("someone");
            return manager;
        }

        protected Mock<IApplicationPaths> CreateApplicationPaths()
        {
            var paths = new Mock<IApplicationPaths>(MockBehavior.Loose);
            paths.Setup(p => p.PluginConfigurationsPath).Returns(_root);
            paths.Setup(p => p.DataPath).Returns(_root);
            return paths;
        }

        protected static DownloadQueue CreateQueue(string dataPath)
        {
            var paths = new Mock<IApplicationPaths>(MockBehavior.Strict);
            paths.Setup(p => p.DataPath).Returns(dataPath);
            return new DownloadQueue(
                paths.Object,
                new Mock<ILogger<DownloadQueue>>(MockBehavior.Loose).Object);
        }

        protected static DownloadQueueItem CreateQueueItem(Guid userId, string eventGuid) => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            EventGuid = eventGuid,
            EventTitle = "Talk",
            ConferenceAcronym = "conf",
            RecordingUrl = "https://media.ccc.de/talk.mp4",
            DestinationPath = "/library/talk.mp4",
            Status = DownloadStatus.Pending,
            Priority = 0
        };

        protected static Mock<IHttpClientFactory> CreateHttpFactory(long? contentLength)
        {
            var handler = new Mock<HttpMessageHandler>();
            handler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(() =>
                {
                    var response = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(new byte[16])
                    };

                    if (contentLength.HasValue)
                    {
                        response.Content.Headers.ContentLength = contentLength.Value;
                    }

                    return response;
                });

            var factory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(handler.Object));
            return factory;
        }

        protected WatchlistDownloadService CreateDownloadService(
            User user,
            string libraryPath,
            IStorageGuard storageGuard)
        {
            Directory.CreateDirectory(libraryPath);

            var api = new Mock<IMediaCccApiClient>(MockBehavior.Loose);
            api.Setup(a => a.GetEventAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new EventDto
                {
                    Guid = "event-1",
                    Slug = "talk",
                    ConferenceId = 1,
                    Recordings = new List<RecordingDto>
                    {
                            new() { Url = "https://media.ccc.de/talk.mp4", Format = "mp4", MimeType = "video/mp4" }
                    }
                });

            var selector = new Mock<IRecordingSelector>(MockBehavior.Loose);
            selector.Setup(s => s.SelectBestRecording(It.IsAny<IEnumerable<Recording>>(), It.IsAny<RecordingPreferences>()))
                .Returns(new Recording
                {
                    Url = "https://media.ccc.de/talk.mp4",
                    Format = "mp4",
                    Size = 1024
                });

            var library = new Mock<IUserLibraryService>(MockBehavior.Loose);
            library.Setup(l => l.GetOrCreateUserLibraryAsync(It.IsAny<Guid>(), It.IsAny<string>()))
                .ReturnsAsync(new UserLibrary
                {
                    LibraryId = Guid.NewGuid(),
                    LibraryName = "someone's Watchlist",
                    Path = libraryPath,
                    UserId = user.Id
                });

            return new WatchlistDownloadService(
                api.Object,
                new Mock<IDownloadQueue>(MockBehavior.Loose).Object,
                CreateUserDataManager().Object,
                library.Object,
                CreateUserManager(user).Object,
                selector.Object,
                () => new PluginConfiguration(),
                storageGuard);
        }
    }
}
