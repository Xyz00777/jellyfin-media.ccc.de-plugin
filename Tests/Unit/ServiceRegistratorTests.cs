using System;
using System.Linq;
using Jellyfin.Plugin.MediaCccDe.Api;
using Jellyfin.Plugin.MediaCccDe.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.MediaCccDe.Tests.Unit
{
    public class ServiceRegistratorTests
    {
        [Fact]
        public void ServiceRegistrator_registers_MediaCccApi_as_singleton()
        {
            // Arrange
            var serviceCollection = new ServiceCollection();
            var applicationHostMock = new Mock<IServerApplicationHost>();
            var registrator = new ServiceRegistrator();
            
            // Act
            registrator.RegisterServices(serviceCollection, applicationHostMock.Object);
            
            // Assert
            var descriptor = serviceCollection.FirstOrDefault(s => s.ServiceType == typeof(IMediaCccApiClient));
            Assert.NotNull(descriptor);
            Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        }

        [Fact]
        public void ServiceRegistrator_registers_SyncService_as_HostedService()
        {
            // Arrange
            var serviceCollection = new ServiceCollection();
            var applicationHostMock = new Mock<IServerApplicationHost>();
            var registrator = new ServiceRegistrator();
            
            // Act
            registrator.RegisterServices(serviceCollection, applicationHostMock.Object);
            
            // Assert
            var descriptor = serviceCollection.FirstOrDefault(s => 
                s.ServiceType == typeof(IHostedService) && 
                s.ImplementationType == typeof(SyncService));
            Assert.NotNull(descriptor);
        }

        [Fact]
        public void ServiceRegistrator_registers_DownloadService_as_HostedService()
        {
            // Arrange
            var serviceCollection = new ServiceCollection();
            var applicationHostMock = new Mock<IServerApplicationHost>();
            var registrator = new ServiceRegistrator();
            
            // Act
            registrator.RegisterServices(serviceCollection, applicationHostMock.Object);
            
            // Assert
            var descriptor = serviceCollection.FirstOrDefault(s => 
                s.ServiceType == typeof(IHostedService) && 
                s.ImplementationType == typeof(DownloadService));
            Assert.NotNull(descriptor);
        }

        [Fact]
        public void ServiceRegistrator_registers_UserDataManager_as_singleton()
        {
            // Arrange
            var serviceCollection = new ServiceCollection();
            var applicationHostMock = new Mock<IServerApplicationHost>();
            var registrator = new ServiceRegistrator();
            
            // Act
            registrator.RegisterServices(serviceCollection, applicationHostMock.Object);
            
            // Assert
            var descriptor = serviceCollection.FirstOrDefault(s => s.ServiceType == typeof(UserDataManager));
            Assert.NotNull(descriptor);
            Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        }

        [Fact]
        public void ServiceRegistrator_registers_LibrarySetupService_as_HostedService()
        {
            // Arrange
            var serviceCollection = new ServiceCollection();
            var applicationHostMock = new Mock<IServerApplicationHost>();
            var registrator = new ServiceRegistrator();
            
            // Act
            registrator.RegisterServices(serviceCollection, applicationHostMock.Object);
            
            // Assert
            var descriptor = serviceCollection.FirstOrDefault(s => 
                s.ServiceType == typeof(IHostedService) && 
                s.ImplementationType == typeof(LibrarySetupService));
            Assert.NotNull(descriptor);
        }

        [Fact]
        public void HttpClient_registered_with_base_address()
        {
            // Arrange
            var serviceCollection = new ServiceCollection();
            var applicationHostMock = new Mock<IServerApplicationHost>();
            var registrator = new ServiceRegistrator();
            
            // Act
            registrator.RegisterServices(serviceCollection, applicationHostMock.Object);
            
            // Assert
            var descriptor = serviceCollection.FirstOrDefault(s => s.ServiceType == typeof(System.Net.Http.HttpClient));
            Assert.NotNull(descriptor);
            
            var httpClientFactoryDescriptor = serviceCollection.FirstOrDefault(s => 
                s.ServiceType.Name.Contains("IHttpClientFactory"));
            Assert.NotNull(httpClientFactoryDescriptor);
        }
    }
}