using System;
using System.Linq;
using Jellyfin.Plugin.MediaCccDe.Api;
using Jellyfin.Plugin.MediaCccDe.Services;
using MediaBrowser.Common.Configuration;
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
        public void ServiceRegistrator_registers_SyncService_as_HostedService()
        {
            var serviceCollection = new ServiceCollection();
            var applicationHostMock = new Mock<IServerApplicationHost>();
            var registrator = new ServiceRegistrator();

            registrator.RegisterServices(serviceCollection, applicationHostMock.Object);

            var descriptor = serviceCollection.FirstOrDefault(s =>
                s.ServiceType == typeof(IHostedService) &&
                s.ImplementationType == typeof(SyncService));
            Assert.NotNull(descriptor);
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
            serviceCollection.AddLogging();

            var provider = serviceCollection.BuildServiceProvider();
            var strmGenerator = provider.GetService<IStrmGenerator>();

            Assert.NotNull(strmGenerator);
            Assert.IsType<StrmGenerator>(strmGenerator);
        }
    }
}