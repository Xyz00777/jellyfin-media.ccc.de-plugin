using System;
using System.IO;
using System.Net.Http.Headers;
using Jellyfin.Plugin.MediaCccDe.Api;
using Jellyfin.Plugin.MediaCccDe.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.MediaCccDe;

public class ServiceRegistrator : IPluginServiceRegistrator
{
    private const string ArchiveFolderName = "archive";

    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddHttpClient("MediaCccApi", client => 
        {
            client.BaseAddress = new Uri("https://api.media.ccc.de/public/");
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });
        
        serviceCollection.AddSingleton<IMediaCccApiClient, MediaCccApi>();
        serviceCollection.AddSingleton<ILanguageSelector, LanguageSelector>();
        serviceCollection.AddSingleton<IRecordingSelector, RecordingSelector>();
        serviceCollection.AddSingleton<IStrmGenerator>(sp =>
        {
            var apiClient = sp.GetRequiredService<IMediaCccApiClient>();
            var recordingSelector = sp.GetRequiredService<IRecordingSelector>();
            var appPaths = sp.GetRequiredService<IApplicationPaths>();
            var archivePath = Path.Combine(appPaths.PluginConfigurationsPath, ArchiveFolderName);
            return new StrmGenerator(apiClient, recordingSelector, archivePath);
        });
        serviceCollection.AddSingleton<IStrmFileGenerator>(sp =>
        {
            var recordingSelector = sp.GetRequiredService<IRecordingSelector>();
            return new StrmFileGeneratorAdapter(recordingSelector);
        });
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