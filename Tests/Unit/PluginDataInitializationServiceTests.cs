using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaCccDe.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.MediaCccDe.Tests.Unit
{
    public class PluginDataInitializationServiceTests
    {
        private readonly string _pluginConfigPath;

        public PluginDataInitializationServiceTests()
        {
            _pluginConfigPath = Path.Combine(
                Path.GetTempPath(),
                "ccc-init-tests",
                Guid.NewGuid().ToString());
        }

        [Fact]
        public async Task StartAsync_never_advertises_the_token_as_a_query_parameter()
        {
            // Arrange
            Directory.CreateDirectory(_pluginConfigPath);
            try
            {
                var warnings = new List<string>();
                var service = CreateService(warnings);

                // Act
                await service.StartAsync(CancellationToken.None);

                // Assert
                var line = string.Join("\n", warnings);
                Assert.Contains("/media_ccc/settings", line, StringComparison.Ordinal);

                // The controller only reads the token from the unlock form, so telling an
                // administrator to append ?token= would send them to a page that stays locked
                // without explaining why. A token in a URL also leaks into browser history and
                // into server access logs.
                Assert.DoesNotContain("?token=", line, StringComparison.Ordinal);
            }
            finally
            {
                if (Directory.Exists(_pluginConfigPath))
                {
                    Directory.Delete(_pluginConfigPath, true);
                }
            }
        }

        [Fact]
        public async Task StartAsync_points_at_the_token_file_without_printing_the_token()
        {
            Directory.CreateDirectory(_pluginConfigPath);
            try
            {
                var warnings = new List<string>();
                var service = CreateService(warnings);

                await service.StartAsync(CancellationToken.None);

                var line = string.Join("\n", warnings);

                // The token is a reusable bearer credential for settings access. Logs get
                // exported, shipped to support, and archived, so the value must never be
                // written there; the administrator reads it from the file instead.
                var tokenStore = new SettingsAccessTokenStore(_pluginConfigPath);
                var token = await tokenStore.GetAsync(CancellationToken.None);
                Assert.DoesNotContain(token, line, StringComparison.Ordinal);
                Assert.DoesNotContain("Token: ", line, StringComparison.Ordinal);
                Assert.Contains(tokenStore.FilePath, line, StringComparison.Ordinal);
            }
            finally
            {
                if (Directory.Exists(_pluginConfigPath))
                {
                    Directory.Delete(_pluginConfigPath, true);
                }
            }
        }

        /// <summary>
        /// Captures warning-and-above messages so a test can assert on the text an
        /// administrator will actually read in the Jellyfin log.
        /// </summary>
        private sealed class WarningRecorder : ILogger<PluginDataInitializationService>
        {
            private readonly List<string> _messages;

            public WarningRecorder(List<string> messages)
            {
                _messages = messages;
            }

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (logLevel >= LogLevel.Warning)
                {
                    _messages.Add(formatter(state, exception));
                }
            }
        }

        private PluginDataInitializationService CreateService(List<string> warnings)
        {
            var syncLogger = new Mock<ISyncLogger>(MockBehavior.Loose);
            syncLogger.Setup(logger => logger.LoadAsync()).Returns(Task.CompletedTask);

            var queue = new Mock<IDownloadQueue>(MockBehavior.Loose);
            queue.Setup(q => q.GetQueueLengthAsync()).ReturnsAsync(0);

            return new PluginDataInitializationService(
                syncLogger.Object,
                queue.Object,
                new SettingsAccessTokenStore(_pluginConfigPath),
                new WarningRecorder(warnings));
        }
    }
}
