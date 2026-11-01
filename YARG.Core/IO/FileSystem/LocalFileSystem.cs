using System.Collections.Generic;
using System.IO;

namespace YARG.Core.IO
{
    /// <summary>
    /// The OS filesystem through System.IO; the default backend for every path.
    /// </summary>
    public sealed class LocalFileSystem : IYARGFileSystem
    {
        public static readonly LocalFileSystem Instance = new();

        // Attribute maps to Remote Storage files (ex. oneDrive) that are not locally present
        private const FileAttributes RECALL_ON_DATA_ACCESS = (FileAttributes) 0x00400000;
        internal static readonly EnumerationOptions ENUMERATION_OPTIONS = new()
        {
            MatchType = MatchType.Win32,
            AttributesToSkip = RECALL_ON_DATA_ACCESS,
            IgnoreInaccessible = false,
        };

        private LocalFileSystem() {}

        public bool TryStat(string path, out YARGFileStat stat)
        {
            var file = new FileInfo(path);
            if (file.Exists)
            {
                stat = new YARGFileStat(false, file.Length, file.LastWriteTimeUtc, file.CreationTimeUtc);
                return true;
            }

            var directory = new DirectoryInfo(path);
            if (directory.Exists)
            {
                stat = new YARGFileStat(true, 0, directory.LastWriteTimeUtc, directory.CreationTimeUtc);
                return true;
            }

            stat = default;
            return false;
        }

        public IEnumerable<YARGFileSystemEntry> Enumerate(string directory)
        {
            foreach (var info in new DirectoryInfo(directory).EnumerateFileSystemInfos("*", ENUMERATION_OPTIONS))
            {
                var stat = info is FileInfo file
                    ? new YARGFileStat(false, file.Length, file.LastWriteTimeUtc, file.CreationTimeUtc)
                    : new YARGFileStat(true, 0, info.LastWriteTimeUtc, info.CreationTimeUtc);
                yield return new YARGFileSystemEntry(info.Name, info.FullName, in stat);
            }
        }

        public Stream OpenRead(string path, int bufferSize = YARGFileSystem.DEFAULT_BUFFER_SIZE)
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize);
        }

        public bool FileExists(string path)
        {
            return File.Exists(path);
        }

        public bool DirectoryExists(string path)
        {
            return Directory.Exists(path);
        }
    }
}
