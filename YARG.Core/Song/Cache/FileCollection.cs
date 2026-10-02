using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using YARG.Core.IO;

namespace YARG.Core.Song.Cache
{
    internal readonly struct FileCollection : IEnumerable<KeyValuePair<string, YARGFileSystemEntry>>
    {
        private readonly Dictionary<string, YARGFileSystemEntry> _entries;
        public readonly string Directory;
        public readonly bool ContainedDupes;

        /// <param name="directory">A full path, used as given for <see cref="Directory"/> and child paths</param>
        public FileCollection(string directory)
        {
            Directory = directory;
            _entries = new Dictionary<string, YARGFileSystemEntry>(StringComparer.Ordinal);
            var dupes = new HashSet<string>();

            foreach (var entry in YARGFileSystem.Enumerate(directory))
            {
                string name = entry.Name.ToLowerInvariant();
                if (!_entries.TryAdd(name, entry))
                {
                    dupes.Add(name);
                }
            }

            // Removes any sort of ambiguity from duplicates
            ContainedDupes = dupes.Count > 0;
            foreach (var dupe in dupes)
            {
                _entries.Remove(dupe);
            }
        }

        public bool FindFile(string name, out YARGFileSystemEntry file)
        {
            return _entries.TryGetValue(name, out file) && !file.Stat.IsDirectory;
        }

        public bool FindDirectory(string name, out YARGFileSystemEntry directory)
        {
            return _entries.TryGetValue(name, out directory) && directory.Stat.IsDirectory;
        }

        public bool ContainsDirectory()
        {
            foreach (var entry in _entries)
            {
                if (entry.Value.Stat.IsDirectory)
                {
                    return true;
                }
            }
            return false;
        }

        public bool ContainsAudio()
        {
            foreach (var entry in _entries)
            {
                if (IniAudio.IsAudioFile(entry.Key))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Stats a file outside of any collection, as new FileInfo(path) would: FullName is the full path, and a
        /// directory at the path does not count as existing
        /// </summary>
        public static bool TryGetFile(string path, out YARGFileSystemEntry file)
        {
            if (!AbridgedFileInfo.TryStatFile(path, out var stat))
            {
                file = default;
                return false;
            }
            file = new YARGFileSystemEntry(Path.GetFileName(path), Path.GetFullPath(path), in stat);
            return true;
        }

        public Dictionary<string, YARGFileSystemEntry>.Enumerator GetEnumerator()
        {
            return _entries.GetEnumerator();
        }

        IEnumerator<KeyValuePair<string, YARGFileSystemEntry>> IEnumerable<KeyValuePair<string, YARGFileSystemEntry>>.GetEnumerator()
        {
            return _entries.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _entries.GetEnumerator();
        }
    }
}
