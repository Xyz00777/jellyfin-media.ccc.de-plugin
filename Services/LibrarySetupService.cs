using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediaCccDe.Services
{
    /// <summary>
    /// Hosted service that initializes the CCC Archive library on startup.
    /// Implements IHostedService for automatic startup by Jellyfin's DI container.
    /// </summary>
    public class LibrarySetupService : IHostedService
    {
        private const string ArchiveLibraryName = "CCC Archive";
        private const string ArchiveFolderName = "archive";

        private readonly ILibraryManager _libraryManager;
        private readonly IApplicationPaths _applicationPaths;
        private readonly ILogger<LibrarySetupService> _logger;

        /// <summary>
        /// Initializes a new instance of the LibrarySetupService class.
        /// </summary>
        /// <param name="libraryManager">The library manager for managing media libraries.</param>
        /// <param name="applicationPaths">The application paths for accessing system directories.</param>
        /// <param name="logger">The logger instance for this service.</param>
        public LibrarySetupService(
            ILibraryManager libraryManager,
            IApplicationPaths applicationPaths,
            ILogger<LibrarySetupService> logger)
        {
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
            _applicationPaths = applicationPaths ?? throw new ArgumentNullException(nameof(applicationPaths));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Starts the service and initializes the CCC Archive library.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                // Check if library already exists (idempotency check)
                var existingLibrary = _libraryManager.GetVirtualFolders()
                    .FirstOrDefault(vf => string.Equals(vf.Name, ArchiveLibraryName, StringComparison.Ordinal));

                if (existingLibrary != null)
                {
                    _logger.LogInformation("[MediaCcc] Library '{LibraryName}' already exists, skipping creation", ArchiveLibraryName);
                    return;
                }

                // Create the archive directory path
                var archivePath = Path.Combine(_applicationPaths.PluginConfigurationsPath, ArchiveFolderName);

                // Ensure directory exists
                Directory.CreateDirectory(archivePath);

                // Create the library with TvShows content type
                // Path is set via LibraryOptions.PathInfos
                var libraryOptions = new LibraryOptions
                {
                    PathInfos = new[] { new MediaPathInfo { Path = archivePath } }
                };

                await _libraryManager.AddVirtualFolder(
                    ArchiveLibraryName,
                    CollectionTypeOptions.tvshows,
                    libraryOptions,
                    refreshLibrary: true
                ).ConfigureAwait(false);

                _logger.LogInformation("[MediaCcc] Created library '{LibraryName}' at path '{Path}'", ArchiveLibraryName, archivePath);
            }
            catch (Exception ex)
            {
                // Log error but don't fail startup - the plugin should still work without the library
                _logger.LogError(ex, "[MediaCcc] Failed to create library '{LibraryName}'. Manual setup may be required.", ArchiveLibraryName);
            }
        }

        /// <summary>
        /// Stops the service. No cleanup required.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A completed task.</returns>
        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}