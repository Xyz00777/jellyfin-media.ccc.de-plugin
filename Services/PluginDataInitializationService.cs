using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.MediaCccDe.Services
{
    public sealed class PluginDataInitializationService : IHostedService
    {
        private readonly ISyncLogger _syncLogger;
        private readonly IDownloadQueue _downloadQueue;

        public PluginDataInitializationService(ISyncLogger syncLogger, IDownloadQueue downloadQueue)
        {
            _syncLogger = syncLogger ?? throw new ArgumentNullException(nameof(syncLogger));
            _downloadQueue = downloadQueue ?? throw new ArgumentNullException(nameof(downloadQueue));
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await _syncLogger.LoadAsync().ConfigureAwait(false);
            await _downloadQueue.GetQueueLengthAsync().ConfigureAwait(false);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
