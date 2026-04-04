using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaCccDe.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.MediaCccDe.Tests.Unit
{
    public class LibrarySetupServiceTests
    {
        private readonly Mock<ILibraryManager> _libraryManagerMock;
        private readonly Mock<IApplicationPaths> _applicationPathsMock;
        private readonly Mock<ILogger<LibrarySetupService>> _loggerMock;

        public LibrarySetupServiceTests()
        {
            _libraryManagerMock = new Mock<ILibraryManager>(MockBehavior.Loose);
            _applicationPathsMock = new Mock<IApplicationPaths>(MockBehavior.Loose);
            _loggerMock = new Mock<ILogger<LibrarySetupService>>(MockBehavior.Loose);
        }

        [Fact]
        public void LibrarySetupService_implements_IHostedService()
        {
            // Arrange & Act
            var service = new LibrarySetupService(
                _libraryManagerMock.Object,
                _applicationPathsMock.Object,
                _loggerMock.Object);
            
            // Assert
            Assert.IsAssignableFrom<Microsoft.Extensions.Hosting.IHostedService>(service);
        }

        [Fact]
        public async Task StartAsync_does_not_throw()
        {
            // Arrange
            _applicationPathsMock.Setup(x => x.PluginConfigurationsPath).Returns("/tmp/plugins");
            _libraryManagerMock.Setup(x => x.GetVirtualFolders()).Returns(new List<VirtualFolderInfo>());
            _libraryManagerMock.Setup(x => x.AddVirtualFolder(
                It.IsAny<string>(),
                It.IsAny<CollectionTypeOptions?>(),
                It.IsAny<LibraryOptions>(),
                It.IsAny<bool>())).Returns(Task.CompletedTask);

            var service = new LibrarySetupService(
                _libraryManagerMock.Object,
                _applicationPathsMock.Object,
                _loggerMock.Object);
            
            // Act & Assert - no exception thrown
            await service.StartAsync(CancellationToken.None);
        }

        [Fact]
        public async Task StopAsync_completes_successfully()
        {
            // Arrange
            var service = new LibrarySetupService(
                _libraryManagerMock.Object,
                _applicationPathsMock.Object,
                _loggerMock.Object);
            
            // Act
            await service.StopAsync(CancellationToken.None);
            
            // Assert - no exception thrown
        }
    }
}