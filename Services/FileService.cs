using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
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

        private static void ValidateUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                throw new ArgumentException("URL must be a valid absolute URI", nameof(url));
            }

            if (!string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Only HTTPS URLs are allowed", nameof(url));
            }

            if (IsPrivateOrLoopbackHost(uri.Host))
            {
                throw new ArgumentException("URLs pointing to private or loopback addresses are not allowed", nameof(url));
            }
        }

        private static bool IsPrivateOrLoopbackHost(string host)
        {
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (IPAddress.TryParse(host, out var address))
            {
                return IsPrivateOrLoopbackAddress(address);
            }

            if (Uri.CheckHostName(host) == UriHostNameType.IPv6 && IPAddress.TryParse(host, out var ipv6Addr))
            {
                return IsPrivateOrLoopbackAddress(ipv6Addr);
            }

            return false;
        }

        private static bool IsPrivateOrLoopbackAddress(IPAddress address)
        {
            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                var bytes = address.GetAddressBytes();
                // 127.0.0.0/8
                if (bytes[0] == 127) return true;
                // 10.0.0.0/8
                if (bytes[0] == 10) return true;
                // 172.16.0.0/12
                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
                // 192.168.0.0/16
                if (bytes[0] == 192 && bytes[1] == 168) return true;
                // 169.254.0.0/16
                if (bytes[0] == 169 && bytes[1] == 254) return true;
                return false;
            }

            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                return address.Equals(IPAddress.IPv6Loopback);
            }

            return false;
        }

        private static void ValidateDestinationPath(string destinationPath)
        {
            if (destinationPath.Contains(".."))
            {
                throw new ArgumentException("Destination path cannot contain path traversal sequences", nameof(destinationPath));
            }
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

            ValidateUrl(url);
            ValidateDestinationPath(destinationPath);
            cancellationToken.ThrowIfCancellationRequested();
            await RemoteUrlValidator.ValidatePublicHttpsUrlAsync(url, cancellationToken).ConfigureAwait(false);

            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directory))
            {
                EnsureDirectoryExists(directory);
            }

            var fileLock = GetFileLock(destinationPath);
            var tempPath = destinationPath + ".tmp";

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

                await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous);
                await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var buffer = new byte[BufferSize];
                int bytesRead;

                progress?.Report(0.0);

                while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
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

                await fileStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                fileStream.Close();

                File.Move(tempPath, destinationPath, overwrite: true);

                if (progress != null && totalBytes <= 0)
                {
                    progress.Report(1.0);
                }
            }
            catch
            {
                // Clean up temp file on failure
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                throw;
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

            ValidateUrl(url);

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
