using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        #region DownloadQueueItem Tests

        [Fact]
        public void DownloadQueueItem_Has_Id_Property()
        {
            var item = new DownloadQueueItem
            {
                Id = Guid.NewGuid()
            };

            Assert.NotEqual(Guid.Empty, item.Id);
        }

        [Fact]
        public void DownloadQueueItem_Has_UserId_Property()
        {
            var userId = Guid.NewGuid();
            var item = new DownloadQueueItem
            {
                UserId = userId
            };

            Assert.Equal(userId, item.UserId);
        }

        [Fact]
        public void DownloadQueueItem_Has_EventGuid_Property()
        {
            var item = new DownloadQueueItem
            {
                EventGuid = "test-event-guid"
            };

            Assert.Equal("test-event-guid", item.EventGuid);
        }

        [Fact]
        public void DownloadQueueItem_Has_EventTitle_Property()
        {
            var item = new DownloadQueueItem
            {
                EventTitle = "Test Event Title"
            };

            Assert.Equal("Test Event Title", item.EventTitle);
        }

        [Fact]
        public void DownloadQueueItem_Has_ConferenceAcronym_Property()
        {
            var item = new DownloadQueueItem
            {
                ConferenceAcronym = "37c3"
            };

            Assert.Equal("37c3", item.ConferenceAcronym);
        }

        [Fact]
        public void DownloadQueueItem_Has_RecordingUrl_Property()
        {
            var item = new DownloadQueueItem
            {
                RecordingUrl = "https://example.com/video.mp4"
            };

            Assert.Equal("https://example.com/video.mp4", item.RecordingUrl);
        }

        [Fact]
        public void DownloadQueueItem_Has_DestinationPath_Property()
        {
            var item = new DownloadQueueItem
            {
                DestinationPath = "/path/to/destination.mp4"
            };

            Assert.Equal("/path/to/destination.mp4", item.DestinationPath);
        }

        [Fact]
        public void DownloadQueueItem_Has_Status_Property()
        {
            var item = new DownloadQueueItem
            {
                Status = DownloadStatus.Pending
            };

            Assert.Equal(DownloadStatus.Pending, item.Status);
        }

        [Fact]
        public void DownloadQueueItem_Has_Progress_Property()
        {
            var item = new DownloadQueueItem
            {
                Progress = 45.5
            };

            Assert.Equal(45.5, item.Progress);
        }

        [Fact]
        public void DownloadQueueItem_Has_ErrorMessage_Property()
        {
            var item = new DownloadQueueItem
            {
                ErrorMessage = "Download failed"
            };

            Assert.Equal("Download failed", item.ErrorMessage);
        }

        [Fact]
        public void DownloadQueueItem_Has_CreatedAt_Property()
        {
            var created = DateTime.UtcNow;
            var item = new DownloadQueueItem
            {
                CreatedAt = created
            };

            Assert.Equal(created, item.CreatedAt);
        }

        [Fact]
        public void DownloadQueueItem_Has_UpdatedAt_Property()
        {
            var updated = DateTime.UtcNow;
            var item = new DownloadQueueItem
            {
                UpdatedAt = updated
            };

            Assert.Equal(updated, item.UpdatedAt);
        }

        [Fact]
        public void DownloadQueueItem_Has_Priority_Property()
        {
            var item = new DownloadQueueItem
            {
                Priority = 10
            };

            Assert.Equal(10, item.Priority);
        }

        #endregion

        #region DownloadStatus Enum Tests

        [Fact]
        public void DownloadStatus_Has_Pending_Value()
        {
            Assert.Equal(0, (int)DownloadStatus.Pending);
        }

        [Fact]
        public void DownloadStatus_Has_InProgress_Value()
        {
            Assert.Equal(1, (int)DownloadStatus.InProgress);
        }

        [Fact]
        public void DownloadStatus_Has_Completed_Value()
        {
            Assert.Equal(2, (int)DownloadStatus.Completed);
        }

        [Fact]
        public void DownloadStatus_Has_Failed_Value()
        {
            Assert.Equal(3, (int)DownloadStatus.Failed);
        }

        #endregion

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
            await Task.Delay(10);
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