using System;
using System.IO;
using System.Net.Http.Headers;
using Jellyfin.Plugin.MediaCccDe.Api;
using Jellyfin.Plugin.MediaCccDe.Controllers;
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

    private const int LocalPort = 8096;

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
        serviceCollection.AddSingleton<PluginInstanceResolver, PluginInstanceResolver>();
        serviceCollection.AddSingleton(sp => new JellyfinIdentityVerifier(
            sp.GetRequiredService<IHttpClientFactory>(),
            $"http://127.0.0.1:{LocalPort}"));
        serviceCollection.AddHttpClient(JellyfinIdentityVerifier.VerificationClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        serviceCollection.AddSingleton(sp => new SettingsAccessTokenStore(
            sp.GetRequiredService<IApplicationPaths>().PluginConfigurationsPath));
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
                var local = pluginManager.GetPlugin(Plugin.PluginGuid, Plugin.PluginVersion);
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
        // image providers are only ever discovered through DI, and only IRemoteImageProvider
        // exposes the GetImages call that Jellyfin uses to discover artwork per item.
        serviceCollection.TryAddEnumerable(
            ServiceDescriptor.Singleton<IRemoteMetadataProvider<Series, SeriesInfo>, MediaCccSeriesProvider>());
        serviceCollection.TryAddEnumerable(
            ServiceDescriptor.Singleton<IRemoteMetadataProvider<Episode, EpisodeInfo>, MediaCccEpisodeProvider>());
        serviceCollection.TryAddEnumerable(
            ServiceDescriptor.Singleton<IRemoteImageProvider, MediaCccSeriesProvider>());
        serviceCollection.TryAddEnumerable(
            ServiceDescriptor.Singleton<IRemoteImageProvider, MediaCccEpisodeProvider>());
    }
}
