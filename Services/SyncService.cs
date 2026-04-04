using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaCccDe.Api;
using Jellyfin.Plugin.MediaCccDe.Models;
using MediaBrowser.Model.Plugins;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediaCccDe.Services
{
    public class SyncService : IHostedService
    {
        private readonly IMediaCccApiClient _apiClient;
        private readonly IStrmGenerator _strmGenerator;
        private readonly ISyncLogger _syncLogger;
        private readonly PluginConfiguration _configuration;
        private readonly ILogger<SyncService> _logger;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private Task? _backgroundTask;

        public SyncService(
            IMediaCccApiClient apiClient,
            IStrmGenerator strmGenerator,
            ISyncLogger syncLogger,
            PluginConfiguration configuration,
            ILogger<SyncService> logger)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _strmGenerator = strmGenerator ?? throw new ArgumentNullException(nameof(strmGenerator));
            _syncLogger = syncLogger ?? throw new ArgumentNullException(nameof(syncLogger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting conference sync background service");
            
            _backgroundTask = ExecuteAsync(_cts.Token);
            
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
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
                    await SyncConferencesAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during conference sync");
                }

                var intervalHours = _configuration.SyncIntervalHours;
                if (intervalHours <= 0)
                {
                    continue;
                }

                try
                {
                    await Task.Delay(TimeSpan.FromHours(intervalHours), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task SyncConferencesAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting conference sync");

            try
            {
                var conferences = await _apiClient.GetConferencesAsync(cancellationToken).ConfigureAwait(false);

                if (conferences == null || !conferences.Any())
                {
                    _logger.LogInformation("No conferences found");
                    return;
                }

                int processedCount = 0;
                int createdCount = 0;
                var syncTimestamp = DateTime.UtcNow;

                foreach (var conference in conferences)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    if (!_strmGenerator.StrmFilesExistForConference(conference))
                    {
                        await _strmGenerator.CreateStrmFilesForConference(conference, cancellationToken).ConfigureAwait(false);
                        createdCount++;
                        _logger.LogInformation("Created strm files for conference: {Conference}", conference.Title);
                    }

                    processedCount++;
                }

                _logger.LogInformation("Conference sync completed. Processed: {Processed}, Created: {Created}", processedCount, createdCount);
                
                await _syncLogger.LogSyncCompletion(processedCount, createdCount, syncTimestamp).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "API request failed during conference sync");
                throw;
            }
        }
    }
}