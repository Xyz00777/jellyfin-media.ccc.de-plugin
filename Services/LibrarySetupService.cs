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

        // The creation check and the creation itself must not interleave. A restart while
        // the library was still being added, or two starts racing, both saw "missing" and
        // produced a duplicate library that Jellyfin then renamed.
        private static readonly SemaphoreSlim CreationGate = new(1, 1);

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
            await CreationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Create the archive directory first: a library pointing at a path that does
                // not exist yet is what produced an empty library on a fresh install.
                var archivePath = Path.Combine(_applicationPaths.PluginConfigurationsPath, ArchiveFolderName);
                Directory.CreateDirectory(archivePath);

                if (FindExistingLibrary(archivePath) != null)
                {
                    _logger.LogInformation("[MediaCcc] Library '{LibraryName}' already exists, skipping creation", ArchiveLibraryName);
                    return;
                }

                var libraryOptions = new LibraryOptions
                {
                    PathInfos = new[] { new MediaPathInfo { Path = archivePath } }
                };

                // The sync queues its own scan once it has written files, so refreshing here
                // only scans an empty directory and delays startup.
                await _libraryManager.AddVirtualFolder(
                    ArchiveLibraryName,
                    CollectionTypeOptions.tvshows,
                    libraryOptions,
                    refreshLibrary: false
                ).ConfigureAwait(false);

                _logger.LogInformation("[MediaCcc] Created library '{LibraryName}' at path '{Path}'", ArchiveLibraryName, archivePath);
            }
            catch (Exception ex)
            {
                // Log error but don't fail startup - the plugin should still work without the library
                _logger.LogError(ex, "[MediaCcc] Failed to create library '{LibraryName}'. Manual setup may be required.", ArchiveLibraryName);
            }
            finally
            {
                CreationGate.Release();
            }
        }

        /// <summary>
        /// Matches on the archive path as well as the name, so a library that already serves
        /// the archive under a different name is reused instead of duplicated.
        /// </summary>
        internal VirtualFolderInfo? FindExistingLibrary(string archivePath)
        {
            var normalised = Path.GetFullPath(archivePath).TrimEnd(Path.DirectorySeparatorChar);

            foreach (var folder in _libraryManager.GetVirtualFolders())
            {
                if (string.Equals(folder.Name, ArchiveLibraryName, StringComparison.Ordinal))
                {
                    return folder;
                }

                var servesArchive = (folder.Locations ?? new string[0])
                    .Where(location => !string.IsNullOrWhiteSpace(location))
                    .Any(location => string.Equals(
                        Path.GetFullPath(location).TrimEnd(Path.DirectorySeparatorChar),
                        normalised,
                        StringComparison.Ordinal));

                if (servesArchive)
                {
                    return folder;
                }
            }

            return null;
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
