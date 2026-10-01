using System;
using System.Collections.Generic;
using System.IO;

namespace YARG.Core.Song.Cache
{
    /// <summary>
    /// Path identity for song folders, cache paths and scan bookkeeping. UNC paths (\\server\share\...) compare
    /// ignoring case, because SMB shares are case-insensitive and the same folder can arrive spelled differently
    /// from user input, the cache and enumeration. Every other path compares ordinally, so local folders behave as
    /// they always have.
    /// </summary>
    internal static class SongPaths
    {
        public static readonly IEqualityComparer<string> Comparer = new PathComparer();

        public static bool IsUnc(string path)
        {
            return path.Length >= 2 && path[0] == '\\' && path[1] == '\\';
        }

        public static bool AreEqual(string lhs, string rhs)
        {
            return string.Equals(lhs, rhs, GetComparison(lhs));
        }

        /// <returns>Whether <paramref name="path"/> is <paramref name="directory"/> itself or lies beneath it</returns>
        public static bool IsUnderDirectory(string path, string directory)
        {
            if (directory.Length == 0 || !path.StartsWith(directory, GetComparison(directory)))
            {
                return false;
            }
            return path.Length == directory.Length
                || IsSeparator(directory, directory[^1])
                || IsSeparator(directory, path[directory.Length]);
        }

        /// <summary>
        /// Slices the part of <paramref name="path"/> below <paramref name="directory"/> without consulting
        /// Path.GetFullPath, which only understands UNC strings on Windows.
        /// </summary>
        /// <returns>False if the path is not under the directory</returns>
        public static bool TryGetRelativePath(string directory, string path, out string relative)
        {
            if (!IsUnderDirectory(path, directory))
            {
                relative = string.Empty;
                return false;
            }

            int start = TrimEndSeparators(directory).Length + 1;
            relative = start < path.Length ? path[start..] : string.Empty;
            return true;
        }

        public static string TrimEndSeparators(string path)
        {
            int length = path.Length;
            while (length > 0 && IsSeparator(path, path[length - 1]))
            {
                --length;
            }
            return path[..length];
        }

        private static StringComparison GetComparison(string path)
        {
            return IsUnc(path) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        }

        private static bool IsSeparator(string path, char c)
        {
            return c == Path.DirectorySeparatorChar || (IsUnc(path) && (c == '\\' || c == '/'));
        }

        // A UNC string can only equal another UNC string, so choosing the comparison by either side is symmetric
        private sealed class PathComparer : IEqualityComparer<string>
        {
            public bool Equals(string? lhs, string? rhs)
            {
                if (lhs == null || rhs == null)
                {
                    return lhs == rhs;
                }
                return AreEqual(lhs, rhs);
            }

            public int GetHashCode(string path)
            {
                return IsUnc(path)
                    ? StringComparer.OrdinalIgnoreCase.GetHashCode(path)
                    : StringComparer.Ordinal.GetHashCode(path);
            }
        }
    }
}
