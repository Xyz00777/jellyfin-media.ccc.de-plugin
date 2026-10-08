using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaCccDe.Services;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.MediaCccDe.Tests.Unit
{
    public class SyncLoggerTests
    {
        private readonly Mock<IApplicationPaths> _applicationPathsMock;
        private readonly Mock<ILogger<SyncLogger>> _loggerMock;
        private readonly string _testDataPath;

        public SyncLoggerTests()
        {
            _applicationPathsMock = new Mock<IApplicationPaths>(MockBehavior.Strict);
            _loggerMock = new Mock<ILogger<SyncLogger>>(MockBehavior.Loose);
            _testDataPath = Path.Combine(Path.GetTempPath(), "ccc-media-tests", Guid.NewGuid().ToString());
        }

        [Fact]
        public void LogSyncStart_creates_new_entry_with_timestamp()
        {
            // Arrange
            var syncLogger = CreateSyncLogger();
            var conferenceAcronym = "37c3";
            var startTime = DateTime.UtcNow;

            // Act
            syncLogger.LogSyncStart(conferenceAcronym, startTime);

            // Assert
            var history = syncLogger.GetSyncHistory();
            Assert.Single(history);
            var entry = history[0];
            Assert.Equal(conferenceAcronym, entry.ConferenceAcronym);
            Assert.Equal(startTime, entry.Timestamp);
            Assert.Equal(SyncStatus.Started, entry.Status);
        }

        [Fact]
        public void LogSyncComplete_updates_entry_with_status()
        {
            // Arrange
            var syncLogger = CreateSyncLogger();
            var conferenceAcronym = "37c3";
            var startTime = DateTime.UtcNow;

            syncLogger.LogSyncStart(conferenceAcronym, startTime);

            // Act
            syncLogger.LogSyncComplete(conferenceAcronym, eventsProcessed: 150, filesCreated: 148);

            // Assert
            var history = syncLogger.GetSyncHistory();
            Assert.Single(history);
            var entry = history[0];
            Assert.Equal(SyncStatus.Completed, entry.Status);
            Assert.Equal(150, entry.EventsProcessed);
            Assert.Equal(148, entry.FilesCreated);
            Assert.Null(entry.ErrorMessage);
        }

        [Fact]
        public void LogSyncFailure_records_error_message()
        {
            // Arrange
            var syncLogger = CreateSyncLogger();
            var conferenceAcronym = "37c3";
            var startTime = DateTime.UtcNow;
            var errorMessage = "API connection failed: timeout after 30s";

            syncLogger.LogSyncStart(conferenceAcronym, startTime);

            // Act
            syncLogger.LogSyncFailure(conferenceAcronym, errorMessage, eventsProcessed: 45, filesCreated: 43);

            // Assert
            var history = syncLogger.GetSyncHistory();
            Assert.Single(history);
            var entry = history[0];
            Assert.Equal(SyncStatus.Failed, entry.Status);
            Assert.Equal(errorMessage, entry.ErrorMessage);
            Assert.Equal(45, entry.EventsProcessed);
            Assert.Equal(43, entry.FilesCreated);
        }

        [Fact]
        public void GetSyncHistory_returns_entries_ordered_by_date_descending()
        {
            // Arrange
            var syncLogger = CreateSyncLogger();
            var now = DateTime.UtcNow;

            syncLogger.LogSyncStart("37c3", now.AddDays(-2));
            syncLogger.LogSyncStart("36c3", now.AddDays(-1));
            syncLogger.LogSyncStart("35c3", now);

            // Act
            var history = syncLogger.GetSyncHistory();

            // Assert
            Assert.Equal(3, history.Count);
            Assert.Equal("35c3", history[0].ConferenceAcronym);
            Assert.Equal("36c3", history[1].ConferenceAcronym);
            Assert.Equal("37c3", history[2].ConferenceAcronym);
        }

        [Fact]
        public void GetSyncHistory_limits_results_to_configured_max()
        {
            // Arrange
            var syncLogger = CreateSyncLogger(maxHistoryEntries: 5);
            var now = DateTime.UtcNow;

            // Add 10 entries - LogSyncStart inserts at index 0, so newest first
            for (int i = 1; i <= 10; i++)
            {
                syncLogger.LogSyncStart($"conf{i}", now.AddDays(-i));
            }

            // Act
            var history = syncLogger.GetSyncHistory();

            // Assert
            Assert.Equal(5, history.Count);
            // After trimming, the 5 most recent entries are kept (inserted at index 0 each time)
            Assert.Equal("conf10", history[0].ConferenceAcronym);
            Assert.Equal("conf6", history[4].ConferenceAcronym);
        }

        [Fact]
        public void GetSyncHistory_for_conference_returns_correct_entries()
        {
            // Arrange
            var syncLogger = CreateSyncLogger();
            var now = DateTime.UtcNow;

            syncLogger.LogSyncStart("37c3", now.AddDays(-2));
            syncLogger.LogSyncStart("36c3", now.AddDays(-1));
            syncLogger.LogSyncStart("37c3", now);

            // Act
            var history = syncLogger.GetSyncHistory(conferenceAcronym: "37c3");

            // Assert
            Assert.Equal(2, history.Count);
            Assert.All(history, entry => Assert.Equal("37c3", entry.ConferenceAcronym));
        }

        [Fact]
        public void ClearHistory_removes_all_entries()
        {
            // Arrange
            var syncLogger = CreateSyncLogger();
            var now = DateTime.UtcNow;

            syncLogger.LogSyncStart("37c3", now);
            syncLogger.LogSyncStart("36c3", now.AddDays(-1));

            // Act
            syncLogger.ClearHistory();

            // Assert
            var history = syncLogger.GetSyncHistory();
            Assert.Empty(history);
        }

        [Fact]
        public async Task Persist_saves_to_json_file()
        {
            // Arrange
            var syncLogger = CreateSyncLogger();
            var now = DateTime.UtcNow;

            syncLogger.LogSyncStart("37c3", now);
            syncLogger.LogSyncComplete("37c3", 150, 148);

            // Act
            await syncLogger.PersistAsync();

            // Assert
            var filePath = Path.Combine(_testDataPath, "sync-logs.json");
            Assert.True(File.Exists(filePath));

            var json = await File.ReadAllTextAsync(filePath);
            var entries = JsonSerializer.Deserialize<List<SyncLogEntry>>(json, new JsonSerializerOptions
            {
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            });

            Assert.NotNull(entries);
            Assert.Single(entries);
            Assert.Equal("37c3", entries![0].ConferenceAcronym);
            Assert.Equal(150, entries[0].EventsProcessed);
        }

        [Fact]
        public async Task Load_reads_from_json_file()
        {
            // Arrange
            var entries = new List<SyncLogEntry>
            {
                new SyncLogEntry
                {
                    Timestamp = DateTime.UtcNow,
                    ConferenceAcronym = "37c3",
                    Status = SyncStatus.Completed,
                    EventsProcessed = 150,
                    FilesCreated = 148,
                    ErrorMessage = null
                }
            };

            var filePath = Path.Combine(_testDataPath, "sync-logs.json");
            Directory.CreateDirectory(_testDataPath);
            var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions
            {
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            });
            await File.WriteAllTextAsync(filePath, json);

            // Act
            var syncLogger = CreateSyncLogger();
            await syncLogger.LoadAsync();

            // Assert
            var history = syncLogger.GetSyncHistory();
            Assert.Single(history);
            Assert.Equal("37c3", history[0].ConferenceAcronym);
            Assert.Equal(150, history[0].EventsProcessed);
        }

        [Fact]
        public async Task Handle_concurrent_log_writes_safely()
        {
            // Arrange
            var syncLogger = CreateSyncLogger();
            var tasks = new List<Task>();
            var now = DateTime.UtcNow;

            // Act - Simulate concurrent writes from multiple sync operations
            for (int i = 0; i < 10; i++)
            {
                var index = i;
                tasks.Add(Task.Run(() =>
                {
                    for (int j = 0; j < 5; j++)
                    {
                        syncLogger.LogSyncStart($"conf{index}_{j}", now.AddMinutes(-index));
                    }
                }));
            }

            await Task.WhenAll(tasks);

            // Assert - All entries should be recorded safely without corruption
            var history = syncLogger.GetSyncHistory();
            Assert.Equal(50, history.Count); // 10 tasks * 5 entries each
        }

        [Fact]
        public void SyncLogEntry_has_correct_default_values()
        {
            // Arrange & Act
            var entry = new SyncLogEntry();

            // Assert
            Assert.Equal(string.Empty, entry.ConferenceAcronym);
            Assert.Equal(SyncStatus.Started, entry.Status);
            Assert.Equal(0, entry.EventsProcessed);
            Assert.Equal(0, entry.FilesCreated);
            Assert.Null(entry.ErrorMessage);
        }

        [Fact]
        public void SyncLogEntry_can_be_created_with_all_properties()
        {
            // Arrange
            var timestamp = DateTime.UtcNow;
            var conferenceAcronym = "37c3";
            var status = SyncStatus.Completed;
            var eventsProcessed = 150;
            var filesCreated = 148;
            var errorMessage = "Test error";

            // Act
            var entry = new SyncLogEntry
            {
                Timestamp = timestamp,
                ConferenceAcronym = conferenceAcronym,
                Status = status,
                EventsProcessed = eventsProcessed,
                FilesCreated = filesCreated,
                ErrorMessage = errorMessage
            };

            // Assert
            Assert.Equal(timestamp, entry.Timestamp);
            Assert.Equal(conferenceAcronym, entry.ConferenceAcronym);
            Assert.Equal(status, entry.Status);
            Assert.Equal(eventsProcessed, entry.EventsProcessed);
            Assert.Equal(filesCreated, entry.FilesCreated);
            Assert.Equal(errorMessage, entry.ErrorMessage);
        }

        [Fact]
        public async Task Load_handles_missing_file_gracefully()
        {
            // Arrange - no file exists
            var syncLogger = CreateSyncLogger();

            // Act - Should not throw
            await syncLogger.LoadAsync();

            // Assert - Should have empty history
            var history = syncLogger.GetSyncHistory();
            Assert.Empty(history);
        }

        [Fact]
        public async Task Load_handles_corrupted_json_gracefully()
        {
            // Arrange
            var filePath = Path.Combine(_testDataPath, "sync-logs.json");
            Directory.CreateDirectory(_testDataPath);
            await File.WriteAllTextAsync(filePath, "{{ invalid json }}");

            var syncLogger = CreateSyncLogger();

            // Act - Should not throw
            await syncLogger.LoadAsync();

            // Assert - Should have empty history and log warning
            var history = syncLogger.GetSyncHistory();
            Assert.Empty(history);

            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once());
        }

        [Fact]
        public async Task PersistAsync_is_thread_safe_under_concurrent_modifications()
        {
            var syncLogger = CreateSyncLogger();
            var now = DateTime.UtcNow;

            for (int i = 0; i < 10; i++)
            {
                syncLogger.LogSyncStart($"conf{i}", now.AddMinutes(-i));
            }

            var tasks = new List<Task>();
            for (int i = 0; i < 20; i++)
            {
                var index = i;
                tasks.Add(Task.Run(async () =>
                {
                    await syncLogger.PersistAsync();
                }));
                tasks.Add(Task.Run(() =>
                {
                    syncLogger.LogSyncStart($"concurrent{index}", DateTime.UtcNow);
                }));
            }

            await Task.WhenAll(tasks);

            var filePath = Path.Combine(_testDataPath, "sync-logs.json");
            Assert.True(File.Exists(filePath));
            var json = await File.ReadAllTextAsync(filePath);
            var exception = Record.Exception(() =>
                JsonSerializer.Deserialize<List<SyncLogEntry>>(json, new JsonSerializerOptions
                {
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                }));
            Assert.Null(exception);
        }

        [Fact]
        public async Task PersistAsync_reads_history_inside_lock()
        {
            var syncLogger = CreateSyncLogger();

            syncLogger.LogSyncStart("37c3", DateTime.UtcNow);
            syncLogger.LogSyncComplete("37c3", 150, 148);

            var historyBefore = syncLogger.GetSyncHistory();
            Assert.Single(historyBefore);
            Assert.Equal(SyncStatus.Completed, historyBefore[0].Status);

            await syncLogger.PersistAsync();

            var filePath = Path.Combine(_testDataPath, "sync-logs.json");
            var json = await File.ReadAllTextAsync(filePath);

            var options = new JsonSerializerOptions
            {
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            };
            var deserialized = JsonSerializer.Deserialize<List<SyncLogEntry>>(json, options);
            Assert.NotNull(deserialized);
            Assert.Single(deserialized!);
            Assert.Equal(SyncStatus.Completed, deserialized[0].Status);
            Assert.Equal("Started", JsonSerializer.Serialize(SyncStatus.Started, options).Trim('"'));
        }

        [Fact]
        public async Task PersistAsync_uses_JsonStringEnumConverter_for_SyncStatus()
        {
            var syncLogger = CreateSyncLogger();
            syncLogger.LogSyncStart("37c3", DateTime.UtcNow);

            await syncLogger.PersistAsync();

            var filePath = Path.Combine(_testDataPath, "sync-logs.json");
            var json = await File.ReadAllTextAsync(filePath);
            Assert.Contains("Started", json);
            Assert.DoesNotContain("\"Status\": 0", json);
        }

        [Fact]
        public async Task LoadAsync_handles_IOException_gracefully()
        {
            var filePath = Path.Combine(_testDataPath, "sync-logs.json");
            Directory.CreateDirectory(_testDataPath);

            await File.WriteAllTextAsync(filePath, "test content");

            var syncLogger = CreateSyncLogger();

            File.Delete(filePath);
            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var exception = await Record.ExceptionAsync(() => syncLogger.LoadAsync());
                Assert.Null(exception);
            }

            var history = syncLogger.GetSyncHistory();
            Assert.Empty(history);
        }

        private SyncLogger CreateSyncLogger(int maxHistoryEntries = 100)
        {
            _applicationPathsMock
                .Setup(x => x.DataPath)
                .Returns(_testDataPath);

            return new SyncLogger(
                _applicationPathsMock.Object,
                _loggerMock.Object,
                maxHistoryEntries);
        }
    }
}
