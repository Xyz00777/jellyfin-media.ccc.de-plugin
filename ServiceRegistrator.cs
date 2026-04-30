using System;
using Jellyfin.Plugin.MediaCccDe.Api;
using Jellyfin.Plugin.MediaCccDe.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.MediaCccDe;

public class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddHttpClient("MediaCccApi", client => 
        {
            client.BaseAddress = new Uri("https://api.media.ccc.de/public/");
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        
        serviceCollection.AddSingleton<IMediaCccApiClient, MediaCccApi>();
        serviceCollection.AddSingleton<ILanguageSelector, LanguageSelector>();
        serviceCollection.AddSingleton<IRecordingSelector, RecordingSelector>();
        serviceCollection.AddSingleton<IStrmGenerator, StrmGenerator>();
        serviceCollection.AddSingleton<StrmTreeGenerator>();
        serviceCollection.AddSingleton<ISyncLogger, SyncLogger>();
        serviceCollection.AddSingleton<IUserDataManager, UserDataManager>();
        serviceCollection.AddSingleton<IUserLibraryService, UserLibraryService>();
        serviceCollection.AddSingleton<IDownloadQueue, DownloadQueue>();
        serviceCollection.AddSingleton<IFileService, FileService>();
        
        serviceCollection.AddHostedService<LibrarySetupService>();
        serviceCollection.AddHostedService<SyncService>();
        serviceCollection.AddHostedService<DownloadService>();
    }
}