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
            // script-free form, so the settings page is unlocked with a token the
            // administrator reads from the plugin configuration directory. The token is
            // never logged: it is a reusable bearer credential for settings access, and
            // logs are routinely exported, shipped to support, or kept for years.
            _ = await _settingsTokenStore.GetAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogWarning(
                "Media.CCC.de settings: open {Path} and paste the access token from {TokenFile} "
                    + "into the unlock form. The token is deliberately not accepted as a query parameter, so that it "
                    + "does not leak into browser history or server access logs. Keep this token private. To rotate "
                    + "it, delete that file and restart Jellyfin.",
                "/media_ccc/settings",
                _settingsTokenStore.FilePath);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
