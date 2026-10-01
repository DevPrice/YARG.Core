using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace YARG.Core.IO
{
    /// <summary>
    /// Routes each path to the backend registered for the longest matching root, or to
    /// <see cref="LocalFileSystem"/> when none matches.
    /// </summary>
    public static class YARGFileSystem
    {
        public const int DEFAULT_BUFFER_SIZE = 4096;

        private readonly struct Mount
        {
            public readonly string Root;
            public readonly IYARGFileSystem FileSystem;

            public Mount(string root, IYARGFileSystem fileSystem)
            {
                Root = root;
                FileSystem = fileSystem;
            }
        }

        private static readonly object MOUNT_LOCK = new();

        // Replaced wholesale under the lock and never mutated, so lookups read it without locking.
        // Sorted longest root first, so the first match is the longest.
        private static Mount[] _mounts = Array.Empty<Mount>();

        /// <summary>
        /// Sends every path equal to <paramref name="root"/>, or under it, to <paramref name="fileSystem"/>.
        /// Matching ignores case and respects separator boundaries: "\\nas\media" does not match "\\nas\media2".
        /// Registering the same root again replaces its backend.
        /// </summary>
        public static void Register(string root, IYARGFileSystem fileSystem)
        {
            if (fileSystem == null)
            {
                throw new ArgumentNullException(nameof(fileSystem));
            }

            root = NormalizeRoot(root);
            lock (MOUNT_LOCK)
            {
                var mounts = new List<Mount>(_mounts.Length + 1);
                foreach (var mount in _mounts)
                {
                    if (!string.Equals(mount.Root, root, StringComparison.OrdinalIgnoreCase))
                    {
                        mounts.Add(mount);
                    }
                }
                mounts.Add(new Mount(root, fileSystem));
                mounts.Sort((lhs, rhs) => rhs.Root.Length.CompareTo(lhs.Root.Length));
                Volatile.Write(ref _mounts, mounts.ToArray());
            }
        }

        /// <returns>Whether a backend was registered for the root</returns>
        public static bool Unregister(string root)
        {
            root = NormalizeRoot(root);
            lock (MOUNT_LOCK)
            {
                var mounts = new List<Mount>(_mounts);
                int index = mounts.FindIndex(mount => string.Equals(mount.Root, root, StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                {
                    return false;
                }
                mounts.RemoveAt(index);
                Volatile.Write(ref _mounts, mounts.ToArray());
                return true;
            }
        }

        public static IYARGFileSystem Resolve(string path)
        {
            var mounts = Volatile.Read(ref _mounts);
            foreach (var mount in mounts)
            {
                if (IsUnderRoot(path, mount.Root))
                {
                    return mount.FileSystem;
                }
            }
            return LocalFileSystem.Instance;
        }

        public static bool TryStat(string path, out YARGFileStat stat)
        {
            return Resolve(path).TryStat(path, out stat);
        }

        public static IEnumerable<YARGFileSystemEntry> Enumerate(string directory)
        {
            return Resolve(directory).Enumerate(directory);
        }

        /// <inheritdoc cref="IYARGFileSystem.OpenRead"/>
        public static Stream OpenRead(string path, int bufferSize = DEFAULT_BUFFER_SIZE)
        {
            return Resolve(path).OpenRead(path, bufferSize);
        }

        public static bool FileExists(string path)
        {
            return Resolve(path).FileExists(path);
        }

        public static bool DirectoryExists(string path)
        {
            return Resolve(path).DirectoryExists(path);
        }

        private static string NormalizeRoot(string root)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            root = root.TrimEnd('\\', '/');
            if (root.Length == 0)
            {
                throw new ArgumentException("Root must name a directory", nameof(root));
            }
            return root;
        }

        private static bool IsUnderRoot(string path, string root)
        {
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            return path.Length == root.Length || IsSeparator(path[root.Length]);
        }

        private static bool IsSeparator(char c)
        {
            return c == '\\' || c == '/';
        }
    }
}
