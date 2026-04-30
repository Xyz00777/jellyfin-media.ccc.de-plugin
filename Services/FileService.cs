using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediaCccDe.Services
{
    public class FileService : IFileService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<FileService> _logger;
        private const int BufferSize = 65536;
        private const string HttpClientName = "MediaCccApi";
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = new(StringComparer.OrdinalIgnoreCase);

        public FileService(IHttpClientFactory httpClientFactory, ILogger<FileService> logger)
        {
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        private SemaphoreSlim GetFileLock(string destinationPath)
        {
            return _fileLocks.GetOrAdd(destinationPath, _ => new SemaphoreSlim(1, 1));
        }

        public async Task<string> DownloadFileAsync(string url, string destinationPath, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(url))
            {
                throw new ArgumentException("URL cannot be null or empty", nameof(url));
            }
            if (string.IsNullOrEmpty(destinationPath))
            {
                throw new ArgumentException("Destination path cannot be null or empty", nameof(destinationPath));
            }

            cancellationToken.ThrowIfCancellationRequested();

            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directory))
            {
                EnsureDirectoryExists(directory);
            }

            var fileLock = GetFileLock(destinationPath);

            await fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var httpClient = _httpClientFactory.CreateClient(HttpClientName);
                var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"HTTP request failed with status code {response.StatusCode}");
                }

                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                var downloadedBytes = 0L;
                var lastReportedProgress = 0.0;

                await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous);
                await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var buffer = new byte[BufferSize];
                int bytesRead;

                progress?.Report(0.0);

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, BufferSize, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
                    downloadedBytes += bytesRead;

                    if (totalBytes > 0 && progress != null)
                    {
                        var currentProgress = (double)downloadedBytes / totalBytes;
                        if (currentProgress - lastReportedProgress >= 0.01 || downloadedBytes == totalBytes)
                        {
                            progress.Report(currentProgress);
                            lastReportedProgress = currentProgress;
                        }
                    }
                }

                if (progress != null && totalBytes <= 0)
                {
                    progress.Report(1.0);
                }
            }
            finally
            {
                fileLock.Release();
            }

            return destinationPath;
        }

        public async Task<long> GetFileSizeAsync(string url, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(url))
            {
                throw new ArgumentException("URL cannot be null or empty", nameof(url));
            }

            var httpClient = _httpClientFactory.CreateClient(HttpClientName);
            var request = new HttpRequestMessage(HttpMethod.Head, url);
            var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"HTTP request failed with status code {response.StatusCode}");
            }

            return response.Content.Headers.ContentLength ?? -1;
        }

        public void DeleteFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                throw new ArgumentException("File path cannot be null or empty", nameof(filePath));
            }

            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Failed to delete file {FilePath}", filePath);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Access denied when deleting file {FilePath}", filePath);
            }
        }

        public bool FileExists(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                throw new ArgumentException("File path cannot be null or empty", nameof(filePath));
            }

            return File.Exists(filePath);
        }

        public void EnsureDirectoryExists(string directoryPath)
        {
            if (string.IsNullOrEmpty(directoryPath))
            {
                throw new ArgumentException("Directory path cannot be null or empty", nameof(directoryPath));
            }

            try
            {
                if (!Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Failed to create directory {DirectoryPath}", directoryPath);
                throw;
            }
        }
    }
}