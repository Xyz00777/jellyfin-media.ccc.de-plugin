using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.MediaCccDe.Services
{
    /// <summary>
    /// Jellyfin 12's dashboard renders plugin pages without executing their scripts, and
    /// authenticates every API call with a header that only scripted requests can send, so
    /// a configuration form served from a plugin page can neither run nor authenticate.
    /// A one-time token printed in the log is therefore the only bootstrap available: the
    /// administrator opens the settings URL with it once and receives a signed cookie.
    /// </summary>
    public sealed class SettingsAccessTokenStore
    {
        private const int TokenByteLength = 32;
        private const string FileName = "settings-access.txt";

        private readonly string _path;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private string? _cached;

        public SettingsAccessTokenStore(string pluginConfigurationPath)
        {
            _path = Path.Combine(pluginConfigurationPath, FileName);
        }

        public async Task<string> GetAsync(CancellationToken cancellationToken)
        {
            if (_cached is not null)
            {
                return _cached;
            }

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_cached is not null)
                {
                    return _cached;
                }

                _cached = File.Exists(_path)
                    ? (await File.ReadAllTextAsync(_path, cancellationToken).ConfigureAwait(false)).Trim()
                    : string.Empty;

                if (_cached.Length == 0)
                {
                    var directory = System.IO.Path.GetDirectoryName(_path);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    _cached = Convert.ToHexString(RandomNumberGenerator.GetBytes(TokenByteLength));
                    await File.WriteAllTextAsync(_path, _cached, cancellationToken).ConfigureAwait(false);
                }

                return _cached;
            }
            finally
            {
                _gate.Release();
            }
        }

        public bool Matches(string? candidate, string expected)
        {
            if (string.IsNullOrWhiteSpace(candidate) || candidate.Length != expected.Length)
            {
                return false;
            }

            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(candidate),
                Encoding.UTF8.GetBytes(expected));
        }
    }
}
