using System;
using System.Collections.Generic;
using System.IO;

namespace YARG.Core.IO
{
    /// <summary>
    /// Read-only access to song files, implemented by <see cref="LocalFileSystem"/> and by any backend
    /// registered with <see cref="YARGFileSystem.Register"/>. Paths are plain strings, as accepted by System.IO.
    /// </summary>
    public interface IYARGFileSystem
    {
        /// <returns>Whether a file or directory exists at the path</returns>
        bool TryStat(string path, out YARGFileStat stat);

        /// <summary>
        /// Lists the immediate children of a directory, with their metadata, in one listing.
        /// </summary>
        /// <exception cref="DirectoryNotFoundException">The directory does not exist; may be deferred until enumeration</exception>
        IEnumerable<YARGFileSystemEntry> Enumerate(string directory);

        /// <summary>
        /// Opens a file for reading. The stream is seekable, its Length is known, and every Read fills the
        /// requested count unless it reaches the end of the file.
        /// </summary>
        /// <param name="bufferSize">A hint for the backend's read buffer; 1 disables FileStream buffering</param>
        Stream OpenRead(string path, int bufferSize = YARGFileSystem.DEFAULT_BUFFER_SIZE);

        bool FileExists(string path);

        bool DirectoryExists(string path);
    }

    /// <summary>
    /// File or directory metadata. Times are UTC. <see cref="AbridgedFileInfo"/> keys the song cache on local
    /// times (FileInfo.LastWriteTime), so callers building cache keys must use ToLocalTime() to keep the cache
    /// bytes identical.
    /// </summary>
    public readonly struct YARGFileStat
    {
        public readonly bool IsDirectory;

        /// <summary>
        /// Size of a file in bytes; 0 for directories
        /// </summary>
        public readonly long Length;

        public readonly DateTime LastWriteTimeUtc;
        public readonly DateTime CreationTimeUtc;

        public YARGFileStat(bool isDirectory, long length, DateTime lastWriteTimeUtc, DateTime creationTimeUtc)
        {
            IsDirectory = isDirectory;
            Length = length;
            LastWriteTimeUtc = lastWriteTimeUtc;
            CreationTimeUtc = creationTimeUtc;
        }
    }

    public readonly struct YARGFileSystemEntry
    {
        /// <summary>
        /// The file or directory name, without its parent path
        /// </summary>
        public readonly string Name;

        public readonly string FullName;
        public readonly YARGFileStat Stat;

        public YARGFileSystemEntry(string name, string fullName, in YARGFileStat stat)
        {
            Name = name;
            FullName = fullName;
            Stat = stat;
        }
    }
}
