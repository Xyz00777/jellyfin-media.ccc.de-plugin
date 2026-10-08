using System;
using System.IO;

namespace Jellyfin.Plugin.MediaCccDe.Services
{
    /// <summary>
    /// Reports how much room a destination has, so downloads can be refused before they
    /// fill a volume. Kept behind an interface so tests can drive the limits without
    /// depending on the real free space of the machine they run on.
    /// </summary>
    public interface IStorageGuard
    {
        /// <summary>
        /// Gets the bytes available on the volume holding <paramref name="path"/>.
        /// </summary>
        /// <param name="path">A path on the volume to inspect.</param>
        /// <returns>
        /// Available bytes, or <see cref="long.MaxValue"/> when the platform cannot
        /// report them, in which case callers must not treat the volume as full.
        /// </returns>
        long GetAvailableBytes(string path);

        /// <summary>
        /// Gets the total size of the files inside <paramref name="path"/>, recursively.
        /// </summary>
        /// <param name="path">A directory to measure.</param>
        /// <returns>The summed file sizes in bytes, or zero when the directory is absent.</returns>
        long GetUsedBytesInDirectory(string path);
    }

    /// <summary>
    /// <see cref="IStorageGuard"/> backed by the real filesystem.
    /// </summary>
    public sealed class StorageGuard : IStorageGuard
    {
        /// <inheritdoc />
        public long GetAvailableBytes(string path)
        {
            var root = ResolveVolumeRoot(path);
            if (root is null)
            {
                return long.MaxValue;
            }

            try
            {
                return new DriveInfo(root).AvailableFreeSpace;
            }
            catch (IOException)
            {
                // Network and virtual filesystems do not always report free space. The
                // per-user byte quota still applies, so refusing every download on an
                // unreadable volume would break working setups for no security gain.
                return long.MaxValue;
            }
            catch (UnauthorizedAccessException)
            {
                return long.MaxValue;
            }
        }

        /// <inheritdoc />
        public long GetUsedBytesInDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return 0;
            }

            long total = 0;
            var pending = new Stack<string>();
            pending.Push(path);

            while (pending.Count > 0)
            {
                var current = pending.Pop();

                string[] files;
                string[] directories;
                try
                {
                    files = Directory.GetFiles(current);
                    directories = Directory.GetDirectories(current);
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (var file in files)
                {
                    try
                    {
                        total += new FileInfo(file).Length;
                    }
                    catch (IOException)
                    {
                        // A file removed mid-walk simply does not count towards the quota.
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }

                foreach (var directory in directories)
                {
                    pending.Push(directory);
                }
            }

            return total;
        }

        private static string? ResolveVolumeRoot(string path)
        {
            string full;
            try
            {
                full = Path.GetFullPath(path);
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (PathTooLongException)
            {
                return null;
            }

            var best = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(best))
            {
                return null;
            }

            // Path.GetPathRoot hands back "/" for every absolute path on Unix, so a
            // library on its own mount would be measured against the root filesystem and a
            // nearly full volume would look empty. Longest containing mount wins instead.
            foreach (var drive in DriveInfo.GetDrives())
            {
                var root = drive.Name;
                if (string.IsNullOrEmpty(root)
                    || root.Length <= best.Length
                    || !IsUnder(full, root))
                {
                    continue;
                }

                best = root;
            }

            return best;
        }

        private static bool IsUnder(string fullPath, string root)
        {
            var trimmed = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (trimmed.Length == 0)
            {
                trimmed = Path.DirectorySeparatorChar.ToString();
            }

            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (!fullPath.StartsWith(trimmed, comparison))
            {
                return false;
            }

            if (fullPath.Length == trimmed.Length)
            {
                return true;
            }

            var boundary = fullPath[trimmed.Length];
            return boundary == Path.DirectorySeparatorChar || boundary == Path.AltDirectorySeparatorChar;
        }
    }
}
