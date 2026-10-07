using System;
using System.IO;
using System.Net.Http.Headers;
using Jellyfin.Plugin.MediaCccDe.Api;
using Jellyfin.Plugin.MediaCccDe.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Providers;
using Jellyfin.Plugin.MediaCccDe.Providers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Jellyfin.Plugin.MediaCccDe;

public class ServiceRegistrator : IPluginServiceRegistrator
{
    private const string ArchiveFolderName = "archive";

    private static readonly Guid PluginId = Guid.Parse("e225c91a-ef11-41ca-b913-6491f15c2992");

    private static readonly Version PluginVersion = typeof(Plugin).Assembly.GetName().Version
        ?? new Version(0, 0, 0, 0);

    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddHttpClient("MediaCccApi", client =>
        {
            client.BaseAddress = new Uri("https://api.media.ccc.de/public/");
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false
        });
        
        serviceCollection.AddSingleton<IMediaCccApiClient, MediaCccApi>();
        serviceCollection.AddSingleton<IConferenceScheduleCache, ConferenceScheduleCache>();
        serviceCollection.AddSingleton<IRecordingSelector, RecordingSelector>();
        serviceCollection.AddSingleton<IStrmGenerator>(sp =>
        {
            var apiClient = sp.GetRequiredService<IMediaCccApiClient>();
            var recordingSelector = sp.GetRequiredService<IRecordingSelector>();
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var appPaths = sp.GetRequiredService<IApplicationPaths>();
            var archivePath = Path.Combine(appPaths.PluginConfigurationsPath, ArchiveFolderName);
            var configurationProvider = sp.GetRequiredService<Func<PluginConfiguration>>();
            return new StrmGenerator(apiClient, recordingSelector, httpClientFactory, archivePath, configurationProvider);
        });
        serviceCollection.AddSingleton<IStrmFileGenerator>(sp =>
        {
            var recordingSelector = sp.GetRequiredService<IRecordingSelector>();
            return new StrmFileGeneratorAdapter(recordingSelector);
        });
        serviceCollection.AddSingleton<ISyncLogger, SyncLogger>();
        serviceCollection.AddSingleton<IUserDataManager, UserDataManager>();
        serviceCollection.AddSingleton<IUserLibraryService, UserLibraryService>();
        serviceCollection.AddSingleton<IDownloadQueue, DownloadQueue>();
        serviceCollection.AddSingleton<IFileService, FileService>();
        serviceCollection.AddSingleton<IWatchlistDownloadService, WatchlistDownloadService>();
        serviceCollection.AddSingleton<SyncService>();
        serviceCollection.AddSingleton<ISyncTrigger>(sp => sp.GetRequiredService<SyncService>());
        
        serviceCollection.AddSingleton<Func<PluginConfiguration>>(sp =>
        {
            var pluginManager = sp.GetRequiredService<IPluginManager>();
            return () =>
            {
                var local = pluginManager.GetPlugin(PluginId, PluginVersion);
                return (local?.Instance as Plugin)?.Configuration ?? new PluginConfiguration();
            };
        });

        serviceCollection.AddHostedService<PluginDataInitializationService>();
        serviceCollection.AddHostedService<LibrarySetupService>();
        serviceCollection.AddHostedService(sp => sp.GetRequiredService<SyncService>());
        serviceCollection.AddHostedService<DownloadService>();

        RegisterProviders(serviceCollection);
    }

    private static void RegisterProviders(IServiceCollection serviceCollection)
    {
        // Jellyfin instantiates IRemoteMetadataProvider implementations on its own, but
        // IImageProvider implementations are only ever discovered through DI, so without
        // these the artwork returned in MetadataResult.RemoteImages is never fetched.
        serviceCollection.TryAddEnumerable(
            ServiceDescriptor.Singleton<IRemoteMetadataProvider<Series, SeriesInfo>, MediaCccSeriesProvider>());
        serviceCollection.TryAddEnumerable(
            ServiceDescriptor.Singleton<IRemoteMetadataProvider<Episode, EpisodeInfo>, MediaCccEpisodeProvider>());
        serviceCollection.TryAddEnumerable(
            ServiceDescriptor.Singleton<IImageProvider, MediaCccSeriesProvider>());
        serviceCollection.TryAddEnumerable(
            ServiceDescriptor.Singleton<IImageProvider, MediaCccEpisodeProvider>());
    }
}
