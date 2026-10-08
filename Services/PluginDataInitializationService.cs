using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediaCccDe.Services
{
    public sealed class PluginDataInitializationService : IHostedService
    {
        private readonly ISyncLogger _syncLogger;
        private readonly IDownloadQueue _downloadQueue;
        private readonly SettingsAccessTokenStore _settingsTokenStore;
        private readonly ILogger<PluginDataInitializationService> _logger;

        public PluginDataInitializationService(
            ISyncLogger syncLogger,
            IDownloadQueue downloadQueue,
            SettingsAccessTokenStore settingsTokenStore,
            ILogger<PluginDataInitializationService> logger)
        {
            _syncLogger = syncLogger ?? throw new ArgumentNullException(nameof(syncLogger));
            _downloadQueue = downloadQueue ?? throw new ArgumentNullException(nameof(downloadQueue));
            _settingsTokenStore = settingsTokenStore ?? throw new ArgumentNullException(nameof(settingsTokenStore));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await _syncLogger.LoadAsync().ConfigureAwait(false);
            await _downloadQueue.GetQueueLengthAsync().ConfigureAwait(false);

            // Jellyfin 12 does not run plugin page scripts and will not authenticate a
            // script-free form, so the settings page is unlocked with a token that only
            // an administrator with log access can read.
            var token = await _settingsTokenStore.GetAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogWarning(
                "Media.CCC.de settings can be edited at {Path}?token={Token} appended to this server's base URL. Keep this token private.",
                "/media_ccc/settings",
                token);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
