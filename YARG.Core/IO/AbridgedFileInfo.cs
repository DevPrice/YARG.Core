using System;
using System.IO;
using YARG.Core.Extensions;

namespace YARG.Core.IO
{
    /// <summary>
    /// A FileInfo structure that only contains the filename and time last added
    /// </summary>
    public readonly struct AbridgedFileInfo
    {
        /// <summary>
        /// The file path
        /// </summary>
        public readonly string FullName;

        /// <summary>
        /// The time the file was last written or created on OS - whichever came later
        /// </summary>
        public readonly DateTime LastWriteTime;

        public AbridgedFileInfo(string file)
        {
            FullName = Path.GetFullPath(file);
            LastWriteTime = YARGFileSystem.TryStat(file, out var stat)
                ? NormalizedLastWrite(in stat)
                : MissingFileTime();
        }

        public AbridgedFileInfo(FileInfo info)
        {
            FullName = info.FullName;
            LastWriteTime = NormalizedLastWrite(info);
        }

        public AbridgedFileInfo(in YARGFileSystemEntry entry)
        {
            FullName = entry.FullName;
            LastWriteTime = NormalizedLastWrite(in entry.Stat);
        }

        /// <summary>
        /// Only used when validation of the underlying file is not required
        /// </summary>
        public AbridgedFileInfo(ref FixedArrayStream stream)
        {
            FullName = stream.ReadString();
            LastWriteTime = DateTime.FromBinary(stream.Read<long>(Endianness.Little));
        }

        /// <summary>
        /// Only used when validation of the underlying file is not required
        /// </summary>
        public AbridgedFileInfo(string filename, ref FixedArrayStream stream)
        {
            FullName = filename;
            LastWriteTime = DateTime.FromBinary(stream.Read<long>(Endianness.Little));
        }

        public AbridgedFileInfo(string filename, in DateTime lastUpdatedTime)
        {
            FullName = filename;
            LastWriteTime = lastUpdatedTime;
        }

        public void Serialize(MemoryStream stream)
        {
            stream.Write(FullName);
            stream.Write(LastWriteTime.ToBinary(), Endianness.Little);
        }

        public bool Exists()
        {
            return YARGFileSystem.FileExists(FullName);
        }

        public bool IsStillValid()
        {
            return Validate(FullName, in LastWriteTime);
        }

        public static DateTime NormalizedLastWrite(FileInfo info)
        {
            return Normalize(info.LastWriteTimeUtc, info.CreationTimeUtc);
        }

        public static DateTime NormalizedLastWrite(in YARGFileStat stat)
        {
            return Normalize(stat.LastWriteTimeUtc, stat.CreationTimeUtc);
        }

        /// <summary>
        /// The local last-write time alone, as FileInfo.LastWriteTime reports it, for the cache checks that
        /// have always compared it rather than <see cref="NormalizedLastWrite(in YARGFileStat)"/>
        /// </summary>
        public static DateTime RawLastWrite(in YARGFileStat stat)
        {
            return stat.LastWriteTimeUtc.ToLocalTime();
        }

        // The cache stores local times (FileInfo.LastWriteTime) through ToBinary and compares them exactly, so
        // UTC stats convert first and compare as local times, exactly as FileInfo-derived values did.
        // FileInfo.LastWriteTime is LastWriteTimeUtc.ToLocalTime() on both .NET and Mono.
        private static DateTime Normalize(DateTime lastWriteUtc, DateTime creationUtc)
        {
            var lastWrite = lastWriteUtc.ToLocalTime();
            var creation = creationUtc.ToLocalTime();
            return lastWrite > creation ? lastWrite : creation;
        }

        // What FileInfo reports for every time of a missing file: 1601-01-01 UTC in local time
        private static DateTime MissingFileTime()
        {
            return DateTime.FromFileTimeUtc(0).ToLocalTime();
        }

        /// <summary>
        /// Used for cache validation
        /// </summary>
        public static bool TryParseInfo(ref FixedArrayStream stream, out AbridgedFileInfo abridged)
        {
            return TryParseInfo(stream.ReadString(), ref stream, out abridged);
        }

        /// <summary>
        /// Used for cache validation
        /// </summary>
        public static bool TryParseInfo(string file, ref FixedArrayStream stream, out AbridgedFileInfo abridged)
        {
            if (!TryStatFile(file, out var stat))
            {
                stream.Position += sizeof(long);
                abridged = default;
                return false;
            }

            abridged = new AbridgedFileInfo(Path.GetFullPath(file), NormalizedLastWrite(in stat));
            return abridged.LastWriteTime == DateTime.FromBinary(stream.Read<long>(Endianness.Little));
        }

        public static bool Validate(string file, in DateTime lastWrite)
        {
            return TryStatFile(file, out var stat) && NormalizedLastWrite(in stat) == lastWrite;
        }

        /// <returns>Whether a file, not a directory, exists at the path, as FileInfo.Exists reports</returns>
        public static bool TryStatFile(string path, out YARGFileStat stat)
        {
            return YARGFileSystem.TryStat(path, out stat) && !stat.IsDirectory;
        }
    }
}
