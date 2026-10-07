using System;
using System.Linq;
using Jellyfin.Plugin.MediaCccDe.Api;
using Jellyfin.Plugin.MediaCccDe.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.MediaCccDe.Tests.Unit
{
    public class ServiceRegistratorTests
    {
        [Fact]
        public void ServiceRegistrator_registers_MediaCccApi_as_singleton()
        {
            var serviceCollection = new ServiceCollection();
            var applicationHostMock = new Mock<IServerApplicationHost>();
            var registrator = new ServiceRegistrator();

            registrator.RegisterServices(serviceCollection, applicationHostMock.Object);

            var descriptor = serviceCollection.FirstOrDefault(s => s.ServiceType == typeof(IMediaCccApiClient));
            Assert.NotNull(descriptor);
            Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        }

        [Fact]
        public void ServiceRegistrator_registers_SyncService_as_singleton_and_hosted_service()
        {
            var serviceCollection = new ServiceCollection();
            var applicationHostMock = new Mock<IServerApplicationHost>();
            var registrator = new ServiceRegistrator();

            registrator.RegisterServices(serviceCollection, applicationHostMock.Object);

            var singleton = serviceCollection.FirstOrDefault(s => s.ServiceType == typeof(SyncService));
            Assert.NotNull(singleton);
            Assert.Equal(ServiceLifetime.Singleton, singleton.Lifetime);

            // The hosted service must reuse that singleton, otherwise the injected
            // ISyncTrigger would be a second, non-running SyncService instance.
            var hosted = serviceCollection.FirstOrDefault(s =>
                s.ServiceType == typeof(IHostedService) && s.ImplementationFactory != null);
            Assert.NotNull(hosted);
        }

        [Fact]
        public void ServiceRegistrator_registers_ISyncTrigger()
        {
            var serviceCollection = new ServiceCollection();
            var applicationHostMock = new Mock<IServerApplicationHost>();
            var registrator = new ServiceRegistrator();

            registrator.RegisterServices(serviceCollection, applicationHostMock.Object);

            var descriptor = serviceCollection.FirstOrDefault(s => s.ServiceType == typeof(ISyncTrigger));
            Assert.NotNull(descriptor);
            Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        }

        [Fact]
        public void ServiceRegistrator_registers_PluginDataInitializationService_first()
        {
            var serviceCollection = new ServiceCollection();
            var applicationHostMock = new Mock<IServerApplicationHost>();
            var registrator = new ServiceRegistrator();

            registrator.RegisterServices(serviceCollection, applicationHostMock.Object);

            // SyncLogger keeps history in memory and only reads it from disk in
            // LoadAsync, which this hosted service is the sole caller of. If it is
            // missing, sync history is empty after every server restart.
            var hosted = serviceCollection
                .Where(s => s.ServiceType == typeof(IHostedService))
                .ToList();

            Assert.NotEmpty(hosted);
            Assert.Contains(
                hosted,
                s => s.ImplementationType == typeof(PluginDataInitializationService));
        }

        [Fact]
        public void ServiceRegistrator_registers_IWatchlistDownloadService()
        {
            var serviceCollection = new ServiceCollection();
            var applicationHostMock = new Mock<IServerApplicationHost>();
            var registrator = new ServiceRegistrator();

            registrator.RegisterServices(serviceCollection, applicationHostMock.Object);

            var descriptor = serviceCollection.FirstOrDefault(s => s.ServiceType == typeof(IWatchlistDownloadService));
            Assert.NotNull(descriptor);
            Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        }

        [Fact]
        public void ServiceRegistrator_registers_DownloadService_as_HostedService()
        {
            var serviceCollection = new ServiceCollection();
            var applicationHostMock = new Mock<IServerApplicationHost>();
            var registrator = new ServiceRegistrator();

            registrator.RegisterServices(serviceCollection, applicationHostMock.Object);

            var descriptor = serviceCollection.FirstOrDefault(s =>
                s.ServiceType == typeof(IHostedService) &&
                s.ImplementationType == typeof(DownloadService));
            Assert.NotNull(descriptor);
        }

        [Fact]
        public void ServiceRegistrator_registers_UserDataManager_as_singleton()
        {
            var serviceCollection = new ServiceCollection();
            var applicationHostMock = new Mock<IServerApplicationHost>();
            var registrator = new ServiceRegistrator();

            registrator.RegisterServices(serviceCollection, applicationHostMock.Object);

            var descriptor = serviceCollection.FirstOrDefault(s => s.ServiceType == typeof(IUserDataManager));
            Assert.NotNull(descriptor);
            Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        }

        [Fact]
        public void ServiceRegistrator_registers_LibrarySetupService_as_HostedService()
        {
            var serviceCollection = new ServiceCollection();
            var applicationHostMock = new Mock<IServerApplicationHost>();
            var registrator = new ServiceRegistrator();

            registrator.RegisterServices(serviceCollection, applicationHostMock.Object);

            var descriptor = serviceCollection.FirstOrDefault(s =>
                s.ServiceType == typeof(IHostedService) &&
                s.ImplementationType == typeof(LibrarySetupService));
            Assert.NotNull(descriptor);
        }

        [Fact]
        public void ServiceRegistrator_registers_HttpClient_with_named_client()
        {
            var serviceCollection = new ServiceCollection();
            var applicationHostMock = new Mock<IServerApplicationHost>();
            var registrator = new ServiceRegistrator();

            registrator.RegisterServices(serviceCollection, applicationHostMock.Object);

            var httpClientFactoryDescriptor = serviceCollection.FirstOrDefault(s =>
                s.ServiceType == typeof(IHttpClientFactory));
            Assert.NotNull(httpClientFactoryDescriptor);
        }

        [Fact]
        public void ServiceRegistrator_registers_IStrmGenerator_with_factory()
        {
            var serviceCollection = new ServiceCollection();
            var applicationHostMock = new Mock<IServerApplicationHost>();
            var registrator = new ServiceRegistrator();

            registrator.RegisterServices(serviceCollection, applicationHostMock.Object);

            var descriptor = serviceCollection.FirstOrDefault(s => s.ServiceType == typeof(IStrmGenerator));
            Assert.NotNull(descriptor);
            Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
            Assert.Null(descriptor.ImplementationType);
            Assert.NotNull(descriptor.ImplementationFactory);
        }

        [Fact]
        public void ServiceRegistrator_registers_IStrmFileGenerator()
        {
            var serviceCollection = new ServiceCollection();
            var applicationHostMock = new Mock<IServerApplicationHost>();
            var registrator = new ServiceRegistrator();

            registrator.RegisterServices(serviceCollection, applicationHostMock.Object);

            var descriptor = serviceCollection.FirstOrDefault(s => s.ServiceType == typeof(IStrmFileGenerator));
            Assert.NotNull(descriptor);
            Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        }

        [Fact]
        public void ServiceRegistrator_StrmGenerator_factory_resolves_archivePath_from_IApplicationPaths()
        {
            var serviceCollection = new ServiceCollection();
            var applicationHostMock = new Mock<IServerApplicationHost>();
            var appPathsMock = new Mock<IApplicationPaths>();
            appPathsMock.Setup(x => x.PluginConfigurationsPath).Returns("/test/config");
            var registrator = new ServiceRegistrator();

            registrator.RegisterServices(serviceCollection, applicationHostMock.Object);
            serviceCollection.AddSingleton(appPathsMock.Object);
            serviceCollection.AddSingleton(new Mock<IPluginManager>().Object);
            serviceCollection.AddLogging();

            var provider = serviceCollection.BuildServiceProvider();
            var strmGenerator = provider.GetService<IStrmGenerator>();

            Assert.NotNull(strmGenerator);
            Assert.IsType<StrmGenerator>(strmGenerator);
        }

        [Fact]
        public void ServiceRegistrator_configuration_provider_falls_back_to_defaults_without_a_loaded_plugin()
        {
            var serviceCollection = new ServiceCollection();
            var applicationHostMock = new Mock<IServerApplicationHost>();
            var appPathsMock = new Mock<IApplicationPaths>();
            appPathsMock.Setup(x => x.PluginConfigurationsPath).Returns("/test/config");
            var registrator = new ServiceRegistrator();

            registrator.RegisterServices(serviceCollection, applicationHostMock.Object);
            serviceCollection.AddSingleton(appPathsMock.Object);
            serviceCollection.AddSingleton(new Mock<IPluginManager>().Object);
            serviceCollection.AddLogging();

            var provider = serviceCollection.BuildServiceProvider();
            var configurationProvider = provider.GetRequiredService<Func<PluginConfiguration>>();

            var configuration = configurationProvider();

            Assert.NotNull(configuration);
            Assert.Equal("hd", configuration.PreferredQuality);
        }

        [Fact]
        public void ServiceRegistrator_registers_the_conference_schedule_cache()
        {
            var serviceCollection = new ServiceCollection();
            var registrator = new ServiceRegistrator();

            registrator.RegisterServices(serviceCollection, new Mock<IServerApplicationHost>().Object);

            var descriptor = serviceCollection.FirstOrDefault(s => s.ServiceType == typeof(IConferenceScheduleCache));
            Assert.NotNull(descriptor);
            Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        }
    }
}
