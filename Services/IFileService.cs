using System;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.MediaCccDe.Services
{
    /// <summary>
    /// Service for file download and management operations.
    /// </summary>
    public interface IFileService
    {
        /// <summary>
        /// Downloads a file from a URL to the specified destination path with progress tracking.
        /// </summary>
        /// <param name="url">The URL to download from.</param>
        /// <param name="destinationPath">The local file path to save to.</param>
        /// <param name="progress">Optional progress reporter (0.0 to 100.0 for percentage).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The path to the downloaded file.</returns>
        /// <exception cref="ArgumentException">Thrown when url or destinationPath is null.</exception>
        /// <exception cref="HttpRequestException">Thrown when HTTP request fails.</exception>
        /// <exception cref="OperationCanceledException">Thrown when cancellation is requested.</exception>
        Task<string> DownloadFileAsync(string url, string destinationPath, IProgress<double>? progress, CancellationToken cancellationToken);

        /// <summary>
        /// Gets the file size from a URL using HEAD request.
        /// </summary>
        /// <param name="url">The URL to check.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The file size in bytes, or -1 if not available.</returns>
        /// <exception cref="ArgumentException">Thrown when url is null.</exception>
        /// <exception cref="HttpRequestException">Thrown when HTTP request fails.</exception>
        Task<long> GetFileSizeAsync(string url, CancellationToken cancellationToken);

        /// <summary>
        /// Deletes a file. Does not throw if file doesn't exist.
        /// </summary>
        /// <param name="filePath">The file path to delete.</param>
        /// <exception cref="ArgumentException">Thrown when filePath is null.</exception>
        void DeleteFile(string filePath);

        /// <summary>
        /// Checks if a file exists.
        /// </summary>
        /// <param name="filePath">The file path to check.</param>
        /// <returns>True if file exists, false otherwise.</returns>
        /// <exception cref="ArgumentException">Thrown when filePath is null.</exception>
        bool FileExists(string filePath);

        /// <summary>
        /// Ensures a directory exists, creating it and parent directories if necessary.
        /// </summary>
        /// <param name="directoryPath">The directory path to create.</param>
        /// <exception cref="ArgumentException">Thrown when directoryPath is null.</exception>
        void EnsureDirectoryExists(string directoryPath);
    }
}
