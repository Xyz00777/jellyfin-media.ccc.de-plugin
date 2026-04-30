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
        private const int DefaultIntervalHours = 6;
        private const int MaxBackoffMinutes = 30;

        private readonly IMediaCccApiClient _apiClient;
        private readonly IStrmGenerator _strmGenerator;
        private readonly ISyncLogger _syncLogger;
        private readonly Func<PluginConfiguration> _configurationProvider;
        private readonly ILogger<SyncService> _logger;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly SemaphoreSlim _syncTrigger = new SemaphoreSlim(0, int.MaxValue);
        private Task? _backgroundTask;
        private int _consecutiveErrors;

        public SyncService(
            IMediaCccApiClient apiClient,
            IStrmGenerator strmGenerator,
            ISyncLogger syncLogger,
            Func<PluginConfiguration> configurationProvider,
            ILogger<SyncService> logger)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _strmGenerator = strmGenerator ?? throw new ArgumentNullException(nameof(strmGenerator));
            _syncLogger = syncLogger ?? throw new ArgumentNullException(nameof(syncLogger));
            _configurationProvider = configurationProvider ?? throw new ArgumentNullException(nameof(configurationProvider));
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

        public Task TriggerSyncAsync(CancellationToken cancellationToken)
        {
            _syncTrigger.Release();
            return Task.CompletedTask;
        }

        private async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await SyncConferencesAsync(cancellationToken).ConfigureAwait(false);
                    _consecutiveErrors = 0;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _consecutiveErrors++;
                    var backoffMinutes = Math.Min(Math.Pow(2, _consecutiveErrors - 1), MaxBackoffMinutes);
                    _logger.LogError(ex, "Error during conference sync (attempt {Attempt}), retrying in {Minutes:F1} minutes",
                        _consecutiveErrors, backoffMinutes);

                    try
                    {
                        var backoffDelay = TimeSpan.FromMinutes(backoffMinutes);
                        var maxDelay = TimeSpan.FromMilliseconds(int.MaxValue);
                        if (backoffDelay > maxDelay)
                        {
                            backoffDelay = maxDelay;
                        }

                        var triggered = await _syncTrigger.WaitAsync(
                            backoffDelay, cancellationToken).ConfigureAwait(false);

                        if (triggered)
                        {
                            while (_syncTrigger.Wait(TimeSpan.Zero)) { }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    continue;
                }

                var intervalHours = _configurationProvider().SyncIntervalHours;
                if (intervalHours <= 0)
                {
                    _logger.LogWarning("SyncIntervalHours is {Value}, defaulting to {Default} hours",
                        intervalHours, DefaultIntervalHours);
                    intervalHours = DefaultIntervalHours;
                }

                _logger.LogInformation("Next sync in {Interval} hours", intervalHours);

                try
                {
                    var delay = TimeSpan.FromHours(intervalHours);
                    var maxDelay = TimeSpan.FromMilliseconds(int.MaxValue);
                    if (delay > maxDelay)
                    {
                        delay = maxDelay;
                    }

                    var triggered = await _syncTrigger.WaitAsync(
                        delay, cancellationToken).ConfigureAwait(false);

                    if (triggered)
                    {
                        while (_syncTrigger.Wait(TimeSpan.Zero)) { }
                    }
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