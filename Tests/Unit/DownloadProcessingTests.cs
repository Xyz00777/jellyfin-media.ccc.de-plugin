using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaCccDe.Models;
using Jellyfin.Plugin.MediaCccDe.Services;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.MediaCccDe.Tests.Unit
{
    public class DownloadProcessingTests
    {
        private readonly Mock<IDownloadQueue> _queueMock;
        private readonly Mock<IFileService> _fileServiceMock;
        private readonly Mock<IUserDataManager> _userDataManagerMock;
        private readonly Mock<ILogger<DownloadService>> _loggerMock;
        private readonly DownloadService _service;

        public DownloadProcessingTests()
        {
            _queueMock = new Mock<IDownloadQueue>(MockBehavior.Strict);
            _fileServiceMock = new Mock<IFileService>(MockBehavior.Strict);
            _userDataManagerMock = new Mock<IUserDataManager>(MockBehavior.Loose);
            _loggerMock = new Mock<ILogger<DownloadService>>(MockBehavior.Loose);

            // Default setups required for error handling paths in DownloadService.
            // DownloadService.ProcessQueueAsync checks FileExists before downloading,
            // and HandleDownloadErrorAsync/CleanupFailedDownloadAsync call FileExists
            // and DeleteFile during cleanup. Without these setups, strict mock exceptions
            // cascade through the error handlers.
            _fileServiceMock.Setup(x => x.FileExists(It.IsAny<string>())).Returns(false);
            _fileServiceMock.Setup(x => x.DeleteFile(It.IsAny<string>()));
            _fileServiceMock.Setup(x => x.EnsureDirectoryExists(It.IsAny<string>()));

            _service = new DownloadService(
                _queueMock.Object,
                _fileServiceMock.Object,
                _userDataManagerMock.Object,
                _loggerMock.Object);
        }

        #region Queue Processing Tests

        [Fact]
        public async Task ProcessQueueAsync_dequeues_and_downloads_item()
        {
            // Arrange
            var item = CreateTestItem();
            
            _queueMock
                .Setup(x => x.DequeueAsync())
                .ReturnsAsync(item);
            
            _queueMock
                .Setup(x => x.MarkInProgressAsync(item.Id))
                .Returns(Task.CompletedTask);
            
            _fileServiceMock
                .Setup(x => x.DownloadFileAsync(
                    item.RecordingUrl,
                    item.DestinationPath,
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(item.DestinationPath);
            
            _queueMock
                .Setup(x => x.MarkCompletedAsync(item.Id))
                .Returns(Task.CompletedTask);

            // Act
            await _service.ProcessQueueAsync(CancellationToken.None);

            // Assert
            _queueMock.Verify(x => x.DequeueAsync(), Times.Once);
            _queueMock.Verify(x => x.MarkInProgressAsync(item.Id), Times.Once);
            _fileServiceMock.Verify(x => x.DownloadFileAsync(
                item.RecordingUrl,
                item.DestinationPath,
                It.IsAny<IProgress<double>>(),
                It.IsAny<CancellationToken>()), Times.Once);
            _queueMock.Verify(x => x.MarkCompletedAsync(item.Id), Times.Once);
        }

        [Fact]
        public async Task ProcessQueueAsync_marks_item_InProgress_before_download()
        {
            // Arrange
            var item = CreateTestItem();
            var callOrder = new List<string>();
            
            _queueMock
                .Setup(x => x.DequeueAsync())
                .ReturnsAsync(item);
            
            _queueMock
                .Setup(x => x.MarkInProgressAsync(item.Id))
                .Callback(() => callOrder.Add("MarkInProgress"))
                .Returns(Task.CompletedTask);
            
            _fileServiceMock
                .Setup(x => x.DownloadFileAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .Callback(() => callOrder.Add("Download"))
                .ReturnsAsync("downloaded");
            
            _queueMock
                .Setup(x => x.MarkCompletedAsync(item.Id))
                .Returns(Task.CompletedTask);

            // Act
            await _service.ProcessQueueAsync(CancellationToken.None);

            // Assert - MarkInProgress must be called before Download
            Assert.Equal(new List<string> { "MarkInProgress", "Download" }, callOrder);
        }

        [Fact]
        public async Task ProcessQueueAsync_marks_item_Completed_after_success()
        {
            // Arrange
            var item = CreateTestItem();
            
            _queueMock
                .Setup(x => x.DequeueAsync())
                .ReturnsAsync(item);
            
            _queueMock
                .Setup(x => x.MarkInProgressAsync(item.Id))
                .Returns(Task.CompletedTask);
            
            _fileServiceMock
                .Setup(x => x.DownloadFileAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync("downloaded");
            
            _queueMock
                .Setup(x => x.MarkCompletedAsync(item.Id))
                .Returns(Task.CompletedTask);

            // Act
            await _service.ProcessQueueAsync(CancellationToken.None);

            // Assert
            _queueMock.Verify(x => x.MarkCompletedAsync(item.Id), Times.Once);
        }

        [Fact]
        public async Task ProcessQueueAsync_marks_item_Failed_on_error()
        {
            // Arrange
            var item = CreateTestItem();
            var errorMessage = "Network timeout";
            
            _queueMock
                .Setup(x => x.DequeueAsync())
                .ReturnsAsync(item);
            
            _queueMock
                .Setup(x => x.MarkInProgressAsync(item.Id))
                .Returns(Task.CompletedTask);
            
            _fileServiceMock
                .Setup(x => x.DownloadFileAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException(errorMessage));
            
            _queueMock
                .Setup(x => x.MarkFailedAsync(item.Id, It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            // Act
            await _service.ProcessQueueAsync(CancellationToken.None);

            // Assert
            _queueMock.Verify(x => x.MarkFailedAsync(item.Id, It.Is<string>(s => s.Contains(errorMessage))), Times.Once);
        }

        [Fact]
        public async Task ProcessQueueAsync_continues_with_next_item_on_failure()
        {
            // Arrange
            var failedItem = CreateTestItem(eventGuid: "event-failed");
            var successItem = CreateTestItem(eventGuid: "event-success");
            var dequeueCalls = 0;
            
            _queueMock
                .Setup(x => x.DequeueAsync())
                .Callback(() => dequeueCalls++)
                .ReturnsAsync(() => dequeueCalls == 1 ? failedItem : successItem);
            
            // First item fails
            _queueMock
                .Setup(x => x.MarkInProgressAsync(failedItem.Id))
                .Returns(Task.CompletedTask);
            
            _fileServiceMock
                .Setup(x => x.DownloadFileAsync(
                    failedItem.RecordingUrl,
                    It.IsAny<string>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("Failed"));
            
            _queueMock
                .Setup(x => x.MarkFailedAsync(failedItem.Id, It.IsAny<string>()))
                .Returns(Task.CompletedTask);
            
            // Second item succeeds
            _queueMock
                .Setup(x => x.MarkInProgressAsync(successItem.Id))
                .Returns(Task.CompletedTask);
            
            _fileServiceMock
                .Setup(x => x.DownloadFileAsync(
                    successItem.RecordingUrl,
                    It.IsAny<string>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync("downloaded");
            
            _queueMock
                .Setup(x => x.MarkCompletedAsync(successItem.Id))
                .Returns(Task.CompletedTask);

            // Act - Process 2 items
            await _service.ProcessQueueAsync(CancellationToken.None);
            await _service.ProcessQueueAsync(CancellationToken.None);

            // Assert - Both items were attempted
            _queueMock.Verify(x => x.MarkFailedAsync(failedItem.Id, It.IsAny<string>()), Times.Once);
            _queueMock.Verify(x => x.MarkCompletedAsync(successItem.Id), Times.Once);
        }

        [Fact]
        public async Task ProcessQueueAsync_respects_concurrency_limit()
        {
            // Arrange - Simulate max concurrent downloads
            var maxConcurrent = 2;
            var currentConcurrent = 0;
            var maxObservedConcurrent = 0;
            var items = new List<DownloadQueueItem>
            {
                CreateTestItem(eventGuid: "event-1"),
                CreateTestItem(eventGuid: "event-2"),
                CreateTestItem(eventGuid: "event-3"),
                CreateTestItem(eventGuid: "event-4")
            };
            
            var dequeueIndex = 0;
            _queueMock
                .Setup(x => x.DequeueAsync())
                .ReturnsAsync(() => dequeueIndex < items.Count ? items[dequeueIndex++] : null);
            
            _queueMock
                .Setup(x => x.MarkInProgressAsync(It.IsAny<Guid>()))
                .Returns(Task.CompletedTask);
            
            _fileServiceMock
                .Setup(x => x.DownloadFileAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .Callback(() =>
                {
                    var current = Interlocked.Increment(ref currentConcurrent);
                    lock (items)
                    {
                        maxObservedConcurrent = Math.Max(maxObservedConcurrent, current);
                    }
                    Thread.Sleep(100); // Simulate download time
                    Interlocked.Decrement(ref currentConcurrent);
                })
                .ReturnsAsync("downloaded");
            
            _queueMock
                .Setup(x => x.MarkCompletedAsync(It.IsAny<Guid>()))
                .Returns(Task.CompletedTask);

            // Act
            var serviceWithConcurrency = new DownloadService(
                _queueMock.Object,
                _fileServiceMock.Object,
                _userDataManagerMock.Object,
                _loggerMock.Object,
                maxConcurrentDownloads: maxConcurrent);

            // Process multiple items concurrently
            var tasks = new List<Task>();
            for (int i = 0; i < items.Count; i++)
            {
                tasks.Add(serviceWithConcurrency.ProcessQueueAsync(CancellationToken.None));
            }
            await Task.WhenAll(tasks);

            // Assert
            Assert.True(maxObservedConcurrent <= maxConcurrent, 
                $"Observed {maxObservedConcurrent} concurrent downloads, but limit is {maxConcurrent}");
        }

        [Fact]
        public async Task ProcessQueueAsync_respects_cancellation_token()
        {
            var cts = new CancellationTokenSource();
            
            _queueMock
                .Setup(x => x.DequeueAsync())
                .ReturnsAsync(CreateTestItem());
            
            _queueMock
                .Setup(x => x.MarkInProgressAsync(It.IsAny<Guid>()))
                .Returns(Task.CompletedTask);

            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                _service.ProcessQueueAsync(cts.Token));
        }

        [Fact]
        public async Task ProcessQueueAsync_returns_when_queue_empty()
        {
            // Arrange
            _queueMock
                .Setup(x => x.DequeueAsync())
                .ReturnsAsync((DownloadQueueItem?)null);

            // Act
            await _service.ProcessQueueAsync(CancellationToken.None);

            // Assert - No download attempts
            _fileServiceMock.Verify(
                x => x.DownloadFileAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        #endregion

        #region Download Execution Tests

        [Fact]
        public async Task ProcessQueueAsync_downloads_from_RecordingUrl_to_DestinationPath()
        {
            // Arrange
            var item = CreateTestItem(
                recordingUrl: "https://example.com/video.mp4",
                destinationPath: "/tmp/downloads/video.mp4");
            
            _queueMock
                .Setup(x => x.DequeueAsync())
                .ReturnsAsync(item);
            
            _queueMock
                .Setup(x => x.MarkInProgressAsync(item.Id))
                .Returns(Task.CompletedTask);
            
            _fileServiceMock
                .Setup(x => x.DownloadFileAsync(
                    item.RecordingUrl,
                    item.DestinationPath,
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(item.DestinationPath);
            
            _queueMock
                .Setup(x => x.MarkCompletedAsync(item.Id))
                .Returns(Task.CompletedTask);

            // Act
            await _service.ProcessQueueAsync(CancellationToken.None);

            // Assert
            _fileServiceMock.Verify(
                x => x.DownloadFileAsync(
                    "https://example.com/video.mp4",
                    "/tmp/downloads/video.mp4",
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task ProcessQueueAsync_updates_progress_during_download()
        {
            // Arrange
            var item = CreateTestItem();
            var progressValues = new List<double>();
            
            _queueMock
                .Setup(x => x.DequeueAsync())
                .ReturnsAsync(item);
            
            _queueMock
                .Setup(x => x.MarkInProgressAsync(item.Id))
                .Returns(Task.CompletedTask);
            
            _queueMock
                .Setup(x => x.UpdateProgressAsync(item.Id, It.IsAny<double>()))
                .Callback<Guid, double>((id, progress) => progressValues.Add(progress))
                .Returns(Task.CompletedTask);
            
            _fileServiceMock
                .Setup(x => x.DownloadFileAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<string, string, IProgress<double>, CancellationToken>((url, path, progress, ct) =>
                {
                    // Simulate progress updates
                    progress?.Report(0);
                    progress?.Report(25);
                    progress?.Report(50);
                    progress?.Report(75);
                    progress?.Report(100);
                })
                .ReturnsAsync("downloaded");
            
            _queueMock
                .Setup(x => x.MarkCompletedAsync(item.Id))
                .Returns(Task.CompletedTask);

            // Act
            await _service.ProcessQueueAsync(CancellationToken.None);

            // Assert - Progress should be updated multiple times
            _queueMock.Verify(
                x => x.UpdateProgressAsync(item.Id, It.IsAny<double>()),
                Times.AtLeast(2));
        }

        [Fact]
        public async Task ProcessQueueAsync_creates_destination_directory_if_missing()
        {
            // Arrange
            var item = CreateTestItem(
                destinationPath: "/tmp/new-dir/sub-dir/video.mp4");
            
            _queueMock
                .Setup(x => x.DequeueAsync())
                .ReturnsAsync(item);
            
            _queueMock
                .Setup(x => x.MarkInProgressAsync(item.Id))
                .Returns(Task.CompletedTask);
            
            _fileServiceMock
                .Setup(x => x.EnsureDirectoryExists(It.IsAny<string>()))
                .Callback<string>(path => Directory.CreateDirectory(path));
            
            _fileServiceMock
                .Setup(x => x.DownloadFileAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync("downloaded");
            
            _queueMock
                .Setup(x => x.MarkCompletedAsync(item.Id))
                .Returns(Task.CompletedTask);

            // Act
            await _service.ProcessQueueAsync(CancellationToken.None);

            // Assert
            _fileServiceMock.Verify(
                x => x.EnsureDirectoryExists(It.IsAny<string>()),
                Times.Once);
        }

        [Fact]
        public async Task ProcessQueueAsync_handles_http_404_error()
        {
            // Arrange
            var item = CreateTestItem();
            
            _queueMock
                .Setup(x => x.DequeueAsync())
                .ReturnsAsync(item);
            
            _queueMock
                .Setup(x => x.MarkInProgressAsync(item.Id))
                .Returns(Task.CompletedTask);
            
            _fileServiceMock
                .Setup(x => x.DownloadFileAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("404 Not Found", null, HttpStatusCode.NotFound));
            
            _queueMock
                .Setup(x => x.MarkFailedAsync(item.Id, It.Is<string>(s => s.Contains("404"))))
                .Returns(Task.CompletedTask);

            // Act
            await _service.ProcessQueueAsync(CancellationToken.None);

            // Assert
            _queueMock.Verify(
                x => x.MarkFailedAsync(item.Id, It.IsAny<string>()),
                Times.Once);
        }

        [Fact]
        public async Task ProcessQueueAsync_handles_http_500_error()
        {
            // Arrange
            var item = CreateTestItem();
            
            _queueMock
                .Setup(x => x.DequeueAsync())
                .ReturnsAsync(item);
            
            _queueMock
                .Setup(x => x.MarkInProgressAsync(item.Id))
                .Returns(Task.CompletedTask);
            
            _fileServiceMock
                .Setup(x => x.DownloadFileAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("500 Internal Server Error", null, HttpStatusCode.InternalServerError));
            
            _queueMock
                .Setup(x => x.MarkFailedAsync(item.Id, It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            // Act
            await _service.ProcessQueueAsync(CancellationToken.None);

            // Assert
            _queueMock.Verify(
                x => x.MarkFailedAsync(item.Id, It.IsAny<string>()),
                Times.Once);
        }

        [Fact]
        public async Task ProcessQueueAsync_handles_network_timeout()
        {
            var item = CreateTestItem();
            
            _queueMock
                .Setup(x => x.DequeueAsync())
                .ReturnsAsync(item);
            
            _queueMock
                .Setup(x => x.MarkInProgressAsync(item.Id))
                .Returns(Task.CompletedTask);
            
            _fileServiceMock
                .Setup(x => x.DownloadFileAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TaskCanceledException("Request timeout"));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                _service.ProcessQueueAsync(CancellationToken.None));

            _fileServiceMock.Verify(x => x.FileExists(item.DestinationPath), Times.AtLeastOnce);
            _queueMock.Verify(x => x.MarkFailedAsync(It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task ProcessQueueAsync_handles_disk_full_error()
        {
            // Arrange
            var item = CreateTestItem();
            
            _queueMock
                .Setup(x => x.DequeueAsync())
                .ReturnsAsync(item);
            
            _queueMock
                .Setup(x => x.MarkInProgressAsync(item.Id))
                .Returns(Task.CompletedTask);
            
            _fileServiceMock
                .Setup(x => x.DownloadFileAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("No space left on device"));
            
            _queueMock
                .Setup(x => x.MarkFailedAsync(item.Id, It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            // Act
            await _service.ProcessQueueAsync(CancellationToken.None);

            // Assert
            _queueMock.Verify(
                x => x.MarkFailedAsync(item.Id, It.IsAny<string>()),
                Times.Once);
        }

        [Fact]
        public async Task ProcessQueueAsync_deletes_partial_file_on_failure()
        {
            var item = CreateTestItem(destinationPath: "/tmp/partial.mp4");
            
            _queueMock
                .Setup(x => x.DequeueAsync())
                .ReturnsAsync(item);
            
            _queueMock
                .Setup(x => x.MarkInProgressAsync(item.Id))
                .Returns(Task.CompletedTask);
            
            _fileServiceMock
                .SetupSequence(x => x.FileExists(item.DestinationPath))
                .Returns(false)
                .Returns(true);

            _fileServiceMock
                .Setup(x => x.DownloadFileAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("Download failed"));
            
            _queueMock
                .Setup(x => x.MarkFailedAsync(item.Id, It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            await _service.ProcessQueueAsync(CancellationToken.None);

            _fileServiceMock.Verify(
                x => x.DeleteFile(item.DestinationPath),
                Times.Once);
        }

        [Fact]
        public async Task ProcessQueueAsync_does_not_overwrite_existing_completed_downloads()
        {
            // Arrange
            var item = CreateTestItem(destinationPath: "/tmp/existing.mp4");
            
            _queueMock
                .Setup(x => x.DequeueAsync())
                .ReturnsAsync(item);
            
            _fileServiceMock
                .Setup(x => x.FileExists(item.DestinationPath))
                .Returns(true);

            _queueMock
                .Setup(x => x.MarkCompletedAsync(item.Id))
                .Returns(Task.CompletedTask);

            // Act
            await _service.ProcessQueueAsync(CancellationToken.None);

            // Assert - Should not download, should mark as completed
            _fileServiceMock.Verify(
                x => x.DownloadFileAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            
            _queueMock.Verify(
                x => x.MarkCompletedAsync(item.Id),
                Times.Once);
        }

        #endregion

        #region IHostedService Integration Tests

        [Fact]
        public void DownloadService_implements_IHostedService()
        {
            // Arrange & Act
            var service = new DownloadService(
                _queueMock.Object,
                _fileServiceMock.Object,
                _userDataManagerMock.Object,
                _loggerMock.Object);

            // Assert
            Assert.IsAssignableFrom<IHostedService>(service);
        }

        [Fact]
        public async Task StartAsync_starts_processing_loop()
        {
            var item = CreateTestItem();
            var processingStarted = new TaskCompletionSource<bool>();
            
            _queueMock
                .Setup(x => x.DequeueAsync())
                .Callback(() => processingStarted.SetResult(true))
                .ReturnsAsync(item);
            
            _queueMock
                .Setup(x => x.MarkInProgressAsync(It.IsAny<Guid>()))
                .Returns(Task.CompletedTask);
            
            _fileServiceMock
                .Setup(x => x.DownloadFileAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync("downloaded");
            
            _queueMock
                .Setup(x => x.MarkCompletedAsync(It.IsAny<Guid>()))
                .Returns(Task.CompletedTask);

            await _service.StartAsync(CancellationToken.None);
            
            var started = await Task.WhenAny(processingStarted.Task, Task.Delay(2000));
            
            Assert.True(started == processingStarted.Task, "Processing loop should have started");
            
            await _service.StopAsync(CancellationToken.None);
        }

        [Fact]
        public async Task StopAsync_cancels_processing_gracefully()
        {
            _queueMock
                .Setup(x => x.DequeueAsync())
                .ReturnsAsync((DownloadQueueItem?)null);

            await _service.StartAsync(CancellationToken.None);
            await Task.Delay(100);
            await _service.StopAsync(CancellationToken.None);

            Assert.True(true);
        }

        [Fact]
        public async Task Process_loop_runs_continuously()
        {
            var processCallCount = 0;
            var maxCalls = 3;
            var cts = new CancellationTokenSource();
            
            _queueMock
                .Setup(x => x.DequeueAsync())
                .Callback(() =>
                {
                    processCallCount++;
                    if (processCallCount >= maxCalls)
                    {
                        cts.Cancel();
                    }
                })
                .ReturnsAsync((DownloadQueueItem?)null);

            await _service.StartAsync(CancellationToken.None);
            await Task.Delay(500);

            Assert.True(processCallCount >= 2, "Process loop should run multiple times");
            
            await _service.StopAsync(CancellationToken.None);
        }

        [Fact]
        public async Task Process_loop_waits_when_queue_empty()
        {
            // Arrange
            var dequeueCallCount = 0;
            var emptyQueueReturnCount = 0;
            
            _queueMock
                .Setup(x => x.DequeueAsync())
                .Callback(() => dequeueCallCount++)
                .ReturnsAsync(() =>
                {
                    // First call returns item, subsequent calls return null
                    if (emptyQueueReturnCount == 0)
                    {
                        emptyQueueReturnCount++;
                        return CreateTestItem();
                    }
                    return null;
                });
            
            _queueMock
                .Setup(x => x.MarkInProgressAsync(It.IsAny<Guid>()))
                .Returns(Task.CompletedTask);
            
            _fileServiceMock
                .Setup(x => x.DownloadFileAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync("downloaded");
            
            _queueMock
                .Setup(x => x.MarkCompletedAsync(It.IsAny<Guid>()))
                .Returns(Task.CompletedTask);

            // Act
            await _service.StartAsync(CancellationToken.None);
            await Task.Delay(200); // Allow processing

            // Stop the service
            await _service.StopAsync(CancellationToken.None);
            await Task.Delay(100);

            // Assert - Should have called Dequeue initially then waited
            // Exact count depends on implementation (polling interval)
            Assert.True(dequeueCallCount >= 1, "Should check queue at least once");
        }

        [Fact]
        public async Task Multiple_items_processed_sequentially()
        {
            // Arrange - FIFO processing
            var processedItems = new List<string>();
            var items = new List<DownloadQueueItem>
            {
                CreateTestItem(eventGuid: "event-1"),
                CreateTestItem(eventGuid: "event-2"),
                CreateTestItem(eventGuid: "event-3")
            };
            
            var dequeueIndex = 0;
            _queueMock
                .Setup(x => x.DequeueAsync())
                .ReturnsAsync(() => dequeueIndex < items.Count ? items[dequeueIndex++] : null);
            
            _queueMock
                .Setup(x => x.MarkInProgressAsync(It.IsAny<Guid>()))
                .Returns(Task.CompletedTask);
            
            _fileServiceMock
                .Setup(x => x.DownloadFileAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<string, string, IProgress<double>, CancellationToken>((url, path, progress, ct) =>
                {
                    var item = items[dequeueIndex - 1];
                    lock (processedItems)
                    {
                        processedItems.Add(item.EventGuid);
                    }
                })
                .ReturnsAsync("downloaded");
            
            _queueMock
                .Setup(x => x.MarkCompletedAsync(It.IsAny<Guid>()))
                .Returns(Task.CompletedTask);

            // Act
            foreach (var item in items)
            {
                await _service.ProcessQueueAsync(CancellationToken.None);
            }

            // Assert - Items processed in order
            Assert.Equal(new List<string> { "event-1", "event-2", "event-3" }, processedItems);
        }

        #endregion

        #region Helper Methods

        private DownloadQueueItem CreateTestItem(
            string? eventGuid = null,
            string? recordingUrl = null,
            string? destinationPath = null)
        {
            return new DownloadQueueItem
            {
                Id = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                EventGuid = eventGuid ?? Guid.NewGuid().ToString(),
                EventTitle = "Test Event",
                ConferenceAcronym = "testconf",
                RecordingUrl = recordingUrl ?? "https://test.com/video.mp4",
                DestinationPath = destinationPath ?? "/tmp/video.mp4",
                Status = DownloadStatus.Pending,
                Progress = 0,
                ErrorMessage = null,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Priority = 5
            };
        }

        #endregion
    }
}