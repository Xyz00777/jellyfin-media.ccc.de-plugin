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
    public sealed class DownloadHardeningTests : SecurityTestBase
    {
        #region Downloads cannot exhaust server storage

        [Fact]
        public async Task Queue_refuses_more_unfinished_downloads_than_one_user_may_hold()
        {
            var queue = CreateQueue(Path.Combine(_root, "queue-user"));
            var userId = Guid.NewGuid();

            for (var i = 0; i < DownloadLimits.MaxActiveItemsPerUser; i++)
            {
                await queue.EnqueueAsync(CreateQueueItem(userId, $"event-{i}"));
            }

            await Assert.ThrowsAsync<DownloadQuotaExceededException>(() =>
                queue.EnqueueAsync(CreateQueueItem(userId, "event-over-limit")));
        }

        [Fact]
        public async Task Queue_refuses_more_unfinished_downloads_than_the_server_allows()
        {
            var queue = CreateQueue(Path.Combine(_root, "queue-total"));

            for (var i = 0; i < DownloadLimits.MaxActiveItemsTotal; i++)
            {
                await queue.EnqueueAsync(CreateQueueItem(Guid.NewGuid(), $"event-{i}"));
            }

            await Assert.ThrowsAsync<DownloadQuotaExceededException>(() =>
                queue.EnqueueAsync(CreateQueueItem(Guid.NewGuid(), "event-over-limit")));
        }

        [Fact]
        public async Task Queue_keeps_only_a_bounded_number_of_finished_items()
        {
            var queue = CreateQueue(Path.Combine(_root, "queue-history"));

            for (var i = 0; i < DownloadLimits.MaxRetainedFinishedItems + 25; i++)
            {
                var item = CreateQueueItem(Guid.NewGuid(), $"event-{i}");
                await queue.EnqueueAsync(item);
                await queue.MarkCompletedAsync(item.Id);
            }

            var persisted = await File.ReadAllTextAsync(
                Path.Combine(_root, "queue-history", "plugins", "ccc-media", "data", "download-queue.json"));

            using var document = JsonDocument.Parse(persisted);
            var retained = document.RootElement.GetArrayLength();

            Assert.True(
                retained <= DownloadLimits.MaxRetainedFinishedItems,
                $"Expected at most {DownloadLimits.MaxRetainedFinishedItems} retained items but found {retained}.");
        }

        [Fact]
        public async Task File_download_is_refused_when_the_declared_length_exceeds_the_per_download_cap()
        {
            var service = new FileService(
                CreateHttpFactory(DownloadLimits.MaxRecordingBytes + 1).Object,
                new Mock<ILogger<FileService>>(MockBehavior.Loose).Object,
                new FakeStorageGuard { AvailableBytes = long.MaxValue });

            var destination = Path.Combine(_root, "too-big.mp4");

            await Assert.ThrowsAsync<DownloadQuotaExceededException>(() =>
                service.DownloadFileAsync("https://media.ccc.de/big.mp4", destination, null, CancellationToken.None));

            Assert.False(File.Exists(destination));
            Assert.False(File.Exists(destination + ".tmp"));
        }

        [Fact]
        public async Task File_download_is_refused_when_the_volume_has_no_reserved_headroom()
        {
            var service = new FileService(
                CreateHttpFactory(null).Object,
                new Mock<ILogger<FileService>>(MockBehavior.Loose).Object,
                new FakeStorageGuard { AvailableBytes = DownloadLimits.ReservedFreeSpaceBytes });

            var destination = Path.Combine(_root, "no-room.mp4");

            await Assert.ThrowsAsync<DownloadQuotaExceededException>(() =>
                service.DownloadFileAsync("https://media.ccc.de/v.mp4", destination, null, CancellationToken.None));

            Assert.False(File.Exists(destination));
        }

        [Fact]
        public async Task File_download_succeeds_while_the_reserve_is_still_intact()
        {
            var service = new FileService(
                CreateHttpFactory(16).Object,
                new Mock<ILogger<FileService>>(MockBehavior.Loose).Object,
                new FakeStorageGuard { AvailableBytes = DownloadLimits.ReservedFreeSpaceBytes + 1024 });

            var destination = Path.Combine(_root, "room-enough.mp4");

            await service.DownloadFileAsync("https://media.ccc.de/v.mp4", destination, null, CancellationToken.None);

            Assert.True(File.Exists(destination));
        }

        [Fact]
        public async Task Enqueue_is_refused_when_the_users_library_is_already_full()
        {
            var user = CreateUser(disabled: false);
            var service = CreateDownloadService(
                user,
                Path.Combine(_root, "library-full"),
                new FakeStorageGuard
                {
                    UsedBytes = DownloadLimits.MaxUserLibraryBytes,
                    AvailableBytes = long.MaxValue
                });

            await Assert.ThrowsAsync<DownloadQuotaExceededException>(() =>
                service.EnqueueAsync(user.Id, "event-1", CancellationToken.None));
        }

        [Fact]
        public async Task Enqueue_is_refused_when_the_volume_has_no_reserved_headroom()
        {
            var user = CreateUser(disabled: false);
            var service = CreateDownloadService(
                user,
                Path.Combine(_root, "library-noroom"),
                new FakeStorageGuard
                {
                    UsedBytes = 0,
                    AvailableBytes = DownloadLimits.ReservedFreeSpaceBytes
                });

            await Assert.ThrowsAsync<DownloadQuotaExceededException>(() =>
                service.EnqueueAsync(user.Id, "event-1", CancellationToken.None));
        }

        [Fact]
        public async Task A_quota_refusal_does_not_delete_a_previously_downloaded_file()
        {
            // The refusal happens before any byte is written, so treating it like a broken
            // transfer and deleting the destination would destroy a copy that had already
            // downloaded successfully under the same deterministic name.
            var directory = Path.Combine(_root, "existing");
            Directory.CreateDirectory(directory);
            var destination = Path.Combine(directory, "talk.mp4");
            await File.WriteAllTextAsync(destination, "already downloaded");

            var item = CreateQueueItem(Guid.NewGuid(), "event-1");
            item.DestinationPath = destination;

            var queue = new Mock<IDownloadQueue>(MockBehavior.Loose);
            queue.Setup(q => q.DequeueAsync()).ReturnsAsync(item);
            queue.Setup(q => q.MarkInProgressAsync(item.Id)).Returns(Task.CompletedTask);
            queue.Setup(q => q.MarkFailedAsync(item.Id, It.IsAny<string>())).Returns(Task.CompletedTask);

            var files = new Mock<IFileService>(MockBehavior.Loose);
            files.SetupSequence(f => f.FileExists(It.IsAny<string>()))
                .Returns(false)
                .Returns(true);
            files.Setup(f => f.DownloadFileAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new DownloadQuotaExceededException("refused"));

            var service = new DownloadService(
                queue.Object,
                files.Object,
                CreateUserDataManager().Object,
                new Mock<ILogger<DownloadService>>(MockBehavior.Loose).Object);

            await service.ProcessQueueAsync(CancellationToken.None);

            Assert.True(File.Exists(destination));
            Assert.Equal("already downloaded", await File.ReadAllTextAsync(destination));
            files.Verify(f => f.DeleteFile(It.IsAny<string>()), Times.Never);
            queue.Verify(q => q.MarkFailedAsync(item.Id, "refused"), Times.Once);
        }

        [Fact]
        public async Task A_broken_transfer_still_removes_its_partial_file()
        {
            var directory = Path.Combine(_root, "partial");
            Directory.CreateDirectory(directory);
            var destination = Path.Combine(directory, "talk.mp4");

            var item = CreateQueueItem(Guid.NewGuid(), "event-1");
            item.DestinationPath = destination;

            var queue = new Mock<IDownloadQueue>(MockBehavior.Loose);
            queue.Setup(q => q.DequeueAsync()).ReturnsAsync(item);
            queue.Setup(q => q.MarkInProgressAsync(item.Id)).Returns(Task.CompletedTask);
            queue.Setup(q => q.MarkFailedAsync(item.Id, It.IsAny<string>())).Returns(Task.CompletedTask);

            var files = new Mock<IFileService>(MockBehavior.Loose);
            files.SetupSequence(f => f.FileExists(It.IsAny<string>()))
                .Returns(false)
                .Returns(true);
            files.Setup(f => f.DownloadFileAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("connection reset"));

            var service = new DownloadService(
                queue.Object,
                files.Object,
                CreateUserDataManager().Object,
                new Mock<ILogger<DownloadService>>(MockBehavior.Loose).Object);

            await service.ProcessQueueAsync(CancellationToken.None);

            files.Verify(f => f.DeleteFile(destination), Times.Once);
        }

        [Fact]
        public async Task Enqueue_succeeds_while_the_user_stays_within_every_limit()
        {
            var user = CreateUser(disabled: false);
            var service = CreateDownloadService(
                user,
                Path.Combine(_root, "library-ok"),
                new FakeStorageGuard
                {
                    UsedBytes = 1024,
                    AvailableBytes = long.MaxValue
                });

            var item = await service.EnqueueAsync(user.Id, "event-1", CancellationToken.None);

            Assert.NotNull(item);
        }

        #endregion

        #region Redirects are followed only after re-validation

        private sealed class RedirectingHandler : HttpMessageHandler
        {
            private readonly Queue<(HttpStatusCode Status, string Location, byte[] Body)> _responses = new();

            public void Enqueue(HttpStatusCode status, string? location, byte[] body)
            {
                _responses.Enqueue((status, location!, body));
            }

            public List<string?> RequestedUrls { get; } = new();

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                RequestedUrls.Add(request.RequestUri?.AbsoluteUri);

                var (status, location, body) = _responses.Dequeue();
                var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(body) };
                if (location is not null)
                {
                    // A leading slash parses as an absolute file:// URI on Unix, so decide
                    // on the scheme instead of asking Uri whether it is absolute.
                    response.Headers.Location = location.Contains("://", StringComparison.Ordinal)
                        ? new Uri(location)
                        : new Uri(location, UriKind.Relative);
                }

                return Task.FromResult(response);
            }
        }

        private static IFileService CreateDownloadService(HttpMessageHandler handler, IStorageGuard? storageGuard = null)
        {
            var factory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(handler));
            return new FileService(
                factory.Object,
                new Mock<ILogger<FileService>>(MockBehavior.Loose).Object,
                storageGuard ?? new FakeStorageGuard { AvailableBytes = long.MaxValue });
        }

        [Fact]
        public async Task A_download_follows_a_redirect_to_the_media_mirror()
        {
            // cdn.media.ccc.de answers with a 302 to a regional mirror, so refusing
            // redirects meant the recording could never be fetched at all.
            var handler = new RedirectingHandler();
            handler.Enqueue(HttpStatusCode.Found, "https://93.184.216.35/mirror/talk.webm", new byte[0]);
            handler.Enqueue(HttpStatusCode.OK, null, new byte[64]);

            var destination = Path.Combine(_root, "followed.webm");
            await CreateDownloadService(handler)
                .DownloadFileAsync("https://93.184.216.34/talk.webm", destination, null, CancellationToken.None);

            Assert.True(File.Exists(destination));
            Assert.Equal(64, new FileInfo(destination).Length);
            Assert.Equal(2, handler.RequestedUrls.Count);
        }

        [Fact]
        public async Task A_redirect_into_a_private_address_is_refused()
        {
            var handler = new RedirectingHandler();
            handler.Enqueue(HttpStatusCode.Found, "https://127.0.0.1/secret", new byte[0]);

            var destination = Path.Combine(_root, "ssrf.mp4");

            await Assert.ThrowsAsync<ArgumentException>(() =>
                CreateDownloadService(handler)
                    .DownloadFileAsync("https://93.184.216.34/talk.mp4", destination, null, CancellationToken.None));

            Assert.False(File.Exists(destination));
        }

        [Fact]
        public async Task A_redirect_loop_is_bounded()
        {
            var handler = new RedirectingHandler();
            for (var i = 0; i < 12; i++)
            {
                handler.Enqueue(HttpStatusCode.Found, $"https://93.184.216.{100 + i}/talk.mp4", new byte[0]);
            }

            await Assert.ThrowsAsync<HttpRequestException>(() =>
                CreateDownloadService(handler).DownloadFileAsync(
                    "https://93.184.216.34/talk.mp4",
                    Path.Combine(_root, "loop.mp4"),
                    null,
                    CancellationToken.None));
        }

        [Fact]
        public async Task A_relative_redirect_is_resolved_against_the_current_url()
        {
            var handler = new RedirectingHandler();
            handler.Enqueue(HttpStatusCode.Found, "/mirror/talk.webm", new byte[0]);
            handler.Enqueue(HttpStatusCode.OK, null, new byte[16]);

            var destination = Path.Combine(_root, "relative.webm");
            await CreateDownloadService(handler)
                .DownloadFileAsync("https://93.184.216.34/a/b/talk.webm", destination, null, CancellationToken.None);

            Assert.Equal("https://93.184.216.34/mirror/talk.webm", handler.RequestedUrls[1]);
            Assert.True(File.Exists(destination));
        }

        #endregion

        #region Downloads must not reuse the JSON API client

        [Fact]
        public void The_download_client_is_distinct_from_the_json_api_client()
        {
            // The API client advertises Accept: application/json, and cdn.media.ccc.de answers
            // that with a JSON file descriptor instead of the recording.
            Assert.NotEqual("MediaCccApi", FileService.DownloadClientName);
            Assert.False(FileService.DownloadClientName.Contains("Api", StringComparison.Ordinal));
        }

        #endregion

        #region First download per user must not fail

        [Fact]
        public async Task A_library_that_appears_after_registration_is_still_found()
        {
            // AddVirtualFolder registers asynchronously. Looking once and throwing made the
            // very first download of every account fail, and the caller's retry then left a
            // duplicate library behind.
            var libraryManager = new Mock<ILibraryManager>(MockBehavior.Loose);
            var appearAfter = 3;
            libraryManager
                .Setup(m => m.GetVirtualFolders())
                .Returns(() =>
                {
                    if (appearAfter-- > 0)
                    {
                        return new List<VirtualFolderInfo>();
                    }

                    return new List<VirtualFolderInfo>
                    {
                        new()
                        {
                            Name = "someone's Watchlist",
                            ItemId = Guid.NewGuid().ToString("N"),
                            Locations = new[] { _root }
                        }
                    };
                });

            var service = new UserLibraryService(
                libraryManager.Object,
                CreateUserManager(CreateUser(disabled: false)).Object,
                CreateApplicationPaths().Object,
                new Mock<ILogger<UserLibraryService>>(MockBehavior.Loose).Object);

            var library = await service.GetOrCreateUserLibraryAsync(Guid.NewGuid(), "someone");

            Assert.Equal(_root, library.Path);
            libraryManager.Verify(
                m => m.AddVirtualFolder(It.IsAny<string>(), It.IsAny<CollectionTypeOptions>(), It.IsAny<LibraryOptions>(), It.IsAny<bool>()),
                Times.Once);
        }

        [Fact]
        public async Task An_existing_library_is_reused_without_registering_another()
        {
            var libraryManager = new Mock<ILibraryManager>(MockBehavior.Loose);
            libraryManager
                .Setup(m => m.GetVirtualFolders())
                .Returns(new List<VirtualFolderInfo>
                {
                    new()
                    {
                        Name = "someone's Watchlist",
                        ItemId = Guid.NewGuid().ToString("N"),
                        Locations = new[] { _root }
                    }
                });

            var service = new UserLibraryService(
                libraryManager.Object,
                CreateUserManager(CreateUser(disabled: false)).Object,
                CreateApplicationPaths().Object,
                new Mock<ILogger<UserLibraryService>>(MockBehavior.Loose).Object);

            var library = await service.GetOrCreateUserLibraryAsync(Guid.NewGuid(), "someone");

            Assert.Equal(_root, library.Path);
            libraryManager.Verify(
                m => m.AddVirtualFolder(It.IsAny<string>(), It.IsAny<CollectionTypeOptions>(), It.IsAny<LibraryOptions>(), It.IsAny<bool>()),
                Times.Never);
        }

        #endregion
    }
}
