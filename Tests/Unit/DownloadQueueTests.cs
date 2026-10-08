using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaCccDe.Models;
using Jellyfin.Plugin.MediaCccDe.Services;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.MediaCccDe.Tests.Unit
{
    /// <summary>
    /// RED phase tests for DownloadQueue service.
    /// These tests WILL FAIL until implementation exists.
    /// </summary>
    public class DownloadQueueTests
    {
        private readonly Mock<IApplicationPaths> _applicationPathsMock;
        private readonly Mock<ILogger<DownloadQueue>> _loggerMock;
        private readonly string _testDataPath;
        private readonly DownloadQueue _queue;

        public DownloadQueueTests()
        {
            _applicationPathsMock = new Mock<IApplicationPaths>(MockBehavior.Strict);
            _loggerMock = new Mock<ILogger<DownloadQueue>>(MockBehavior.Loose);
            _testDataPath = Path.Combine(Path.GetTempPath(), "ccc-media-tests", Guid.NewGuid().ToString());
            _applicationPathsMock.Setup(x => x.DataPath).Returns(_testDataPath);
            _queue = new DownloadQueue(_applicationPathsMock.Object, _loggerMock.Object);
        }

        #region IDownloadQueue Interface Tests

        [Fact]
        public async Task EnqueueAsync_adds_item_to_queue()
        {
            var item = CreateTestItem();

            await _queue.EnqueueAsync(item);

            var queueLength = await _queue.GetQueueLengthAsync();
            Assert.Equal(1, queueLength);
        }

        [Fact]
        public async Task EnqueueAsync_does_not_duplicate_same_event_for_same_user()
        {
            var userId = Guid.NewGuid();
            var item1 = CreateTestItem(userId: userId, eventGuid: "event-1");
            var item2 = CreateTestItem(userId: userId, eventGuid: "event-1");

            await _queue.EnqueueAsync(item1);
            await _queue.EnqueueAsync(item2);

            var userQueue = await _queue.GetUserQueueAsync(userId);
            Assert.Single(userQueue);
        }

        [Fact]
        public async Task EnqueueAsync_allows_same_event_for_different_users()
        {
            var user1 = Guid.NewGuid();
            var user2 = Guid.NewGuid();
            var item1 = CreateTestItem(userId: user1, eventGuid: "event-1");
            var item2 = CreateTestItem(userId: user2, eventGuid: "event-1");

            await _queue.EnqueueAsync(item1);
            await _queue.EnqueueAsync(item2);

            var queueLength = await _queue.GetQueueLengthAsync();
            Assert.Equal(2, queueLength);
        }

        [Fact]
        public async Task DequeueAsync_returns_oldest_pending_item_FIFO()
        {
            var userId = Guid.NewGuid();
            var item1 = CreateTestItem(userId: userId, eventGuid: "event-1");
            var item2 = CreateTestItem(userId: userId, eventGuid: "event-2");

            await _queue.EnqueueAsync(item1);
            await _queue.EnqueueAsync(item2);

            var dequeuedItem = await _queue.DequeueAsync();

            Assert.NotNull(dequeuedItem);
            Assert.Equal(item1.EventGuid, dequeuedItem!.EventGuid);
        }

        [Fact]
        public async Task DequeueAsync_returns_null_if_queue_empty()
        {
            var dequeuedItem = await _queue.DequeueAsync();

            Assert.Null(dequeuedItem);
        }

        [Fact]
        public async Task DequeueAsync_respects_priority_ordering()
        {
            var userId = Guid.NewGuid();
            var item1 = CreateTestItem(userId: userId, eventGuid: "event-1", priority: 10);
            var item2 = CreateTestItem(userId: userId, eventGuid: "event-2", priority: 1);
            var item3 = CreateTestItem(userId: userId, eventGuid: "event-3", priority: 5);

            await _queue.EnqueueAsync(item1);
            await _queue.EnqueueAsync(item2);
            await _queue.EnqueueAsync(item3);

            var dequeuedItem = await _queue.DequeueAsync();

            Assert.NotNull(dequeuedItem);
            Assert.Equal("event-2", dequeuedItem!.EventGuid);
        }

        [Fact]
        public async Task DequeueAsync_sets_status_to_InProgress()
        {
            var item = CreateTestItem();
            await _queue.EnqueueAsync(item);

            var dequeuedItem = await _queue.DequeueAsync();

            Assert.NotNull(dequeuedItem);
            Assert.Equal(DownloadStatus.InProgress, dequeuedItem!.Status);
        }

        [Fact]
        public async Task GetUserQueueAsync_returns_only_user_items()
        {
            var user1 = Guid.NewGuid();
            var user2 = Guid.NewGuid();

            var item1 = CreateTestItem(userId: user1, eventGuid: "event-1");
            var item2 = CreateTestItem(userId: user1, eventGuid: "event-2");
            var item3 = CreateTestItem(userId: user2, eventGuid: "event-3");

            await _queue.EnqueueAsync(item1);
            await _queue.EnqueueAsync(item2);
            await _queue.EnqueueAsync(item3);

            var userQueue = await _queue.GetUserQueueAsync(user1);

            Assert.Equal(2, userQueue.Count());
            Assert.All(userQueue, item => Assert.Equal(user1, item.UserId));
        }

        [Fact]
        public async Task GetItemAsync_returns_item_by_id()
        {
            var item = CreateTestItem();
            await _queue.EnqueueAsync(item);

            var retrievedItem = await _queue.GetItemAsync(item.Id);

            Assert.NotNull(retrievedItem);
            Assert.Equal(item.Id, retrievedItem!.Id);
        }

        [Fact]
        public async Task GetItemAsync_returns_null_if_not_found()
        {
            var nonExistentId = Guid.NewGuid();

            var retrievedItem = await _queue.GetItemAsync(nonExistentId);

            Assert.Null(retrievedItem);
        }

        #endregion

        #region Persistence Tests

        [Fact]
        public async Task Queue_persists_to_json_file()
        {
            var item = CreateTestItem();
            await _queue.EnqueueAsync(item);

            var filePath = Path.Combine(
                _testDataPath,
                "plugins",
                "ccc-media",
                "data",
                "download-queue.json"
            );

            Assert.True(File.Exists(filePath));
        }

        [Fact]
        public async Task MarkInProgressAsync_updates_status()
        {
            var item = CreateTestItem();
            await _queue.EnqueueAsync(item);

            await _queue.MarkInProgressAsync(item.Id);

            var updatedItem = await _queue.GetItemAsync(item.Id);
            Assert.NotNull(updatedItem);
            Assert.Equal(DownloadStatus.InProgress, updatedItem!.Status);
        }

        [Fact]
        public async Task Queue_loads_from_json_file_on_startup()
        {
            var item = CreateTestItem();
            await _queue.EnqueueAsync(item);

            var secondQueue = new DownloadQueue(_applicationPathsMock.Object, _loggerMock.Object);
            var retrievedItem = await secondQueue.GetItemAsync(item.Id);

            Assert.NotNull(retrievedItem);
            Assert.Equal(item.EventGuid, retrievedItem!.EventGuid);
        }

        [Fact]
        public async Task Queue_persist_after_each_modification()
        {
            var item = CreateTestItem();
            await _queue.EnqueueAsync(item);

            await _queue.MarkInProgressAsync(item.Id);

            var filePath = Path.Combine(
                _testDataPath,
                "plugins",
                "ccc-media",
                "data",
                "download-queue.json"
            );

            var jsonContent = await File.ReadAllTextAsync(filePath);
            Assert.Contains("InProgress", jsonContent);
        }

        [Fact]
        public async Task LoadAsync_requeues_items_left_InProgress_by_a_previous_process()
        {
            var item = CreateTestItem();
            await _queue.EnqueueAsync(item);
            await _queue.MarkInProgressAsync(item.Id);

            var reloaded = new DownloadQueue(_applicationPathsMock.Object, _loggerMock.Object);

            var recovered = await reloaded.GetItemAsync(item.Id);
            Assert.NotNull(recovered);
            Assert.Equal(DownloadStatus.Pending, recovered!.Status);
            Assert.Equal(0, recovered.Progress);

            var dequeued = await reloaded.DequeueAsync();
            Assert.NotNull(dequeued);
        }

        [Fact]
        public async Task LoadAsync_keeps_completed_items_completed()
        {
            var item = CreateTestItem();
            await _queue.EnqueueAsync(item);
            await _queue.MarkCompletedAsync(item.Id);

            var reloaded = new DownloadQueue(_applicationPathsMock.Object, _loggerMock.Object);

            var restored = await reloaded.GetItemAsync(item.Id);
            Assert.NotNull(restored);
            Assert.Equal(DownloadStatus.Completed, restored!.Status);
        }

        [Fact]
        public void Queue_handles_corrupt_json_gracefully()
        {
            var filePath = Path.Combine(
                _testDataPath,
                "plugins",
                "ccc-media",
                "data",
                "download-queue.json"
            );
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, "{ corrupt json }");

            var exception = Record.Exception(() =>
                new DownloadQueue(_applicationPathsMock.Object, _loggerMock.Object));

            Assert.Null(exception);
        }

        #endregion

        #region Thread Safety Tests

        [Fact]
        public async Task Concurrent_enqueue_is_thread_safe()
        {
            var tasks = new List<Task>();
            var userId = Guid.NewGuid();

            for (int i = 0; i < 100; i++)
            {
                var index = i;
                tasks.Add(Task.Run(async () =>
                {
                    var item = CreateTestItem(
                        userId: userId,
                        eventGuid: $"event-{index}"
                    );
                    await _queue.EnqueueAsync(item);
                }));
            }

            await Task.WhenAll(tasks);

            var queueLength = await _queue.GetQueueLengthAsync();
            Assert.Equal(100, queueLength);
        }

        [Fact]
        public async Task Concurrent_enqueue_and_dequeue_is_thread_safe()
        {
            var tasks = new List<Task>();
            var userId = Guid.NewGuid();
            var dequeueCount = 0;

            for (int i = 0; i < 50; i++)
            {
                var index = i;
                tasks.Add(Task.Run(async () =>
                {
                    var item = CreateTestItem(
                        userId: userId,
                        eventGuid: $"event-{index}"
                    );
                    await _queue.EnqueueAsync(item);
                }));
            }

            for (int i = 0; i < 50; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    var item = await _queue.DequeueAsync();
                    if (item != null)
                    {
                        Interlocked.Increment(ref dequeueCount);
                    }
                }));
            }

            await Task.WhenAll(tasks);

            var expectedTotal = 50;
            var queueLength = await _queue.GetQueueLengthAsync();
            Assert.True(queueLength + dequeueCount <= expectedTotal);
        }

        #endregion

        #region Atomic Write Tests

        [Fact]
        public async Task PersistAsync_writes_atomically_with_temp_and_rename()
        {
            var item = CreateTestItem();
            await _queue.EnqueueAsync(item);

            var filePath = Path.Combine(
                _testDataPath,
                "plugins",
                "ccc-media",
                "data",
                "download-queue.json"
            );
            var tempFile = filePath + ".tmp";

            Assert.True(File.Exists(filePath));
            Assert.False(File.Exists(tempFile));

            var json = await File.ReadAllTextAsync(filePath);
            Assert.Contains("Pending", json);
            Assert.DoesNotContain("\"Status\": 0", json);

            var options = new JsonSerializerOptions
            {
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            };
            var items = JsonSerializer.Deserialize<List<DownloadQueueItem>>(json, options);
            Assert.NotNull(items);
            Assert.Single(items!);
            Assert.Equal(DownloadStatus.Pending, items![0].Status);
        }

        [Fact]
        public async Task PersistAsync_no_temp_file_remains_after_write()
        {
            var item = CreateTestItem();
            await _queue.EnqueueAsync(item);

            var filePath = Path.Combine(
                _testDataPath,
                "plugins",
                "ccc-media",
                "data",
                "download-queue.json"
            );
            var tempFile = filePath + ".tmp";

            Assert.False(File.Exists(tempFile));
            Assert.True(File.Exists(filePath));
        }

        [Fact]
        public async Task ReportProgress_updates_memory_without_persisting()
        {
            var item = CreateTestItem();
            await _queue.EnqueueAsync(item);

            _queue.ReportProgress(item.Id, 0.42);

            var stored = await _queue.GetItemAsync(item.Id);
            Assert.NotNull(stored);
            Assert.Equal(0.42, stored!.Progress);
        }

        [Fact]
        public void ReportProgress_ignores_unknown_item()
        {
            var exception = Record.Exception(() => _queue.ReportProgress(Guid.NewGuid(), 0.5));

            Assert.Null(exception);
        }

        [Fact]
        public async Task PersistAsync_serializes_snapshot_and_write_together()
        {
            await _queue.EnqueueAsync(CreateTestItem(eventGuid: "first"));

            // Hold the first writer right after it snapshots, add another item
            // underneath it, then let it finish. The writer must publish the newer
            // state rather than the snapshot it captured beforehand.
            var snapshotTaken = new TaskCompletionSource();
            var release = new TaskCompletionSource();

            var firstPersist = _queue.PersistAsync(async () =>
            {
                snapshotTaken.SetResult();
                await release.Task;
            });

            await snapshotTaken.Task;
            var mutation = _queue.EnqueueAsync(CreateTestItem(eventGuid: "second"));
            await Task.Delay(200);
            release.SetResult();
            await Task.WhenAll(firstPersist, mutation);

            var filePath = Path.Combine(
                _testDataPath,
                "plugins",
                "ccc-media",
                "data",
                "download-queue.json");
            var persisted = await File.ReadAllTextAsync(filePath);
            Assert.Contains("first", persisted, StringComparison.Ordinal);
            Assert.Contains("second", persisted, StringComparison.Ordinal);
        }

        [Fact]
        public async Task GetUserQueueAsync_returns_snapshots_not_live_items()
        {
            var item = CreateTestItem();
            await _queue.EnqueueAsync(item);

            var first = (await _queue.GetUserQueueAsync(item.UserId)).Single();
            await _queue.UpdateProgressAsync(item.Id, 0.9);

            Assert.Equal(0, first.Progress);
        }

        #endregion

        #region Helper Methods

        private DownloadQueueItem CreateTestItem(
            Guid? userId = null,
            string? eventGuid = null,
            int priority = 5)
        {
            return new DownloadQueueItem
            {
                Id = Guid.NewGuid(),
                UserId = userId ?? Guid.NewGuid(),
                EventGuid = eventGuid ?? Guid.NewGuid().ToString(),
                EventTitle = "Test Event",
                ConferenceAcronym = "testconf",
                RecordingUrl = "https://test.com/video.mp4",
                DestinationPath = "/test/video.mp4",
                Status = DownloadStatus.Pending,
                Progress = 0,
                ErrorMessage = null,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Priority = priority
            };
        }

        #endregion
    }
}
