using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaCccDe.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediaCccDe.Services
{
    /// <summary>
    /// Background service that processes download queue items.
    /// </summary>
    public class DownloadService : IHostedService
    {
        private readonly IDownloadQueue _queue;
        private readonly IFileService _fileService;
        private readonly IUserDataManager _userDataManager;
        private readonly ILogger<DownloadService> _logger;
        private readonly int _maxConcurrentDownloads;
        private readonly SemaphoreSlim _concurrencyLimiter;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private Task? _backgroundTask;

        /// <summary>
        /// Initializes a new instance of DownloadService.
        /// </summary>
        /// <param name="queue">The download queue.</param>
        /// <param name="fileService">The file service.</param>
        /// <param name="userDataManager">The user data manager.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="maxConcurrentDownloads">Maximum concurrent downloads (default: 1 for sequential).</param>
        public DownloadService(
            IDownloadQueue queue,
            IFileService fileService,
            IUserDataManager userDataManager,
            ILogger<DownloadService> logger,
            int maxConcurrentDownloads = 1)
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
            _userDataManager = userDataManager ?? throw new ArgumentNullException(nameof(userDataManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _maxConcurrentDownloads = Math.Max(1, maxConcurrentDownloads);
            _concurrencyLimiter = new SemaphoreSlim(_maxConcurrentDownloads, _maxConcurrentDownloads);
        }

        /// <inheritdoc />
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting download service background processing");
            _backgroundTask = ExecuteAsync(_cts.Token);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping download service");
            _cts.Cancel();

            if (_backgroundTask != null)
            {
                await Task.WhenAny(_backgroundTask, Task.Delay(Timeout.Infinite, cancellationToken)).ConfigureAwait(false);
            }
        }

        private async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessQueueAsync(cancellationToken).ConfigureAwait(false);
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing download queue");
                    await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Processes one item from the download queue.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task ProcessQueueAsync(CancellationToken cancellationToken)
        {
            var item = await _queue.DequeueAsync().ConfigureAwait(false);

            if (item == null)
            {
                return;
            }

            await _concurrencyLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_fileService.FileExists(item.DestinationPath))
                {
                    _logger.LogInformation("File already exists, marking as completed: {Path}", item.DestinationPath);
                    await _queue.MarkCompletedAsync(item.Id).ConfigureAwait(false);
                    return;
                }

                await _queue.MarkInProgressAsync(item.Id).ConfigureAwait(false);

                var directory = Path.GetDirectoryName(item.DestinationPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    _fileService.EnsureDirectoryExists(directory);
                }

                var progress = new Progress<double>(p =>
                {
                    _ = _queue.UpdateProgressAsync(item.Id, p);
                });

                await _fileService.DownloadFileAsync(
                    item.RecordingUrl,
                    item.DestinationPath,
                    progress,
                    cancellationToken).ConfigureAwait(false);

                await _queue.MarkCompletedAsync(item.Id).ConfigureAwait(false);
                _logger.LogInformation("Download completed: {EventGuid}", item.EventGuid);
            }
            catch (OperationCanceledException)
            {
                await CleanupFailedDownloadAsync(item, "Download cancelled").ConfigureAwait(false);
                throw;
            }
            catch (HttpRequestException ex)
            {
                await HandleDownloadErrorAsync(item, ex).ConfigureAwait(false);
            }
            catch (TaskCanceledException ex)
            {
                await HandleDownloadErrorAsync(item, ex).ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                await HandleDownloadErrorAsync(item, ex).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await HandleDownloadErrorAsync(item, ex).ConfigureAwait(false);
            }
            finally
            {
                _concurrencyLimiter.Release();
            }
        }

        private async Task HandleDownloadErrorAsync(DownloadQueueItem item, Exception ex)
        {
            var errorMessage = ex.Message;
            _logger.LogError(ex, "Download failed for event {EventGuid}: {Error}", item.EventGuid, errorMessage);

            await CleanupFailedDownloadAsync(item, errorMessage).ConfigureAwait(false);
            await _queue.MarkFailedAsync(item.Id, errorMessage).ConfigureAwait(false);
        }

        private async Task CleanupFailedDownloadAsync(DownloadQueueItem item, string errorMessage)
        {
            try
            {
                if (_fileService.FileExists(item.DestinationPath))
                {
                    _fileService.DeleteFile(item.DestinationPath);
                    _logger.LogInformation("Deleted partial download: {Path}", item.DestinationPath);
                }
            }
            catch (Exception cleanupEx)
            {
                _logger.LogWarning(cleanupEx, "Failed to delete partial download: {Path}", item.DestinationPath);
            }
        }
    }
}