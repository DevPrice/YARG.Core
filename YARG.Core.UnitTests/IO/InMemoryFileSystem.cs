using YARG.Core.IO;

namespace YARG.Core.UnitTests.IO;

/// <summary>
/// An <see cref="IYARGFileSystem"/> over in-memory files. Paths compare case-insensitively and split on either
/// separator without consulting System.IO.Path, so tests can use UNC-style roots on every OS.
/// </summary>
public sealed class InMemoryFileSystem : IYARGFileSystem
{
    public static readonly DateTime DEFAULT_TIME = new(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    private readonly record struct FileData(byte[] Bytes, DateTime LastWriteTimeUtc, DateTime CreationTimeUtc);

    private readonly Dictionary<string, FileData> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);

    public List<string> OpenedPaths { get; } = [];
    public List<string> StattedPaths { get; } = [];
    public List<string> EnumeratedDirectories { get; } = [];

    public void AddFile(string path, byte[] bytes, DateTime? lastWriteTimeUtc = null, DateTime? creationTimeUtc = null)
    {
        path = Normalize(path);
        _files[path] = new FileData(bytes, lastWriteTimeUtc ?? DEFAULT_TIME, creationTimeUtc ?? DEFAULT_TIME);
        AddParents(path);
    }

    public void AddDirectory(string path)
    {
        path = Normalize(path);
        _directories.Add(path);
        AddParents(path);
    }

    public bool TryStat(string path, out YARGFileStat stat)
    {
        path = Normalize(path);
        StattedPaths.Add(path);
        return TryStatNormalized(path, out stat);
    }

    public IEnumerable<YARGFileSystemEntry> Enumerate(string directory)
    {
        directory = Normalize(directory);
        EnumeratedDirectories.Add(directory);
        if (!_directories.Contains(directory))
        {
            throw new DirectoryNotFoundException(directory);
        }

        var entries = new List<YARGFileSystemEntry>();
        foreach (string path in _directories.Concat(_files.Keys))
        {
            int separator = LastSeparator(path);
            if (separator > 0 && string.Equals(path[..separator], directory, StringComparison.OrdinalIgnoreCase))
            {
                TryStatNormalized(path, out var stat);
                entries.Add(new YARGFileSystemEntry(path[(separator + 1)..], path, in stat));
            }
        }
        return entries;
    }

    public Stream OpenRead(string path, int bufferSize = YARGFileSystem.DEFAULT_BUFFER_SIZE)
    {
        path = Normalize(path);
        OpenedPaths.Add(path);
        if (!_files.TryGetValue(path, out var file))
        {
            throw new FileNotFoundException(null, path);
        }
        return new MemoryStream(file.Bytes, writable: false);
    }

    public bool FileExists(string path)
    {
        return _files.ContainsKey(Normalize(path));
    }

    public bool DirectoryExists(string path)
    {
        return _directories.Contains(Normalize(path));
    }

    private bool TryStatNormalized(string path, out YARGFileStat stat)
    {
        if (_files.TryGetValue(path, out var file))
        {
            stat = new YARGFileStat(false, file.Bytes.Length, file.LastWriteTimeUtc, file.CreationTimeUtc);
            return true;
        }

        if (_directories.Contains(path))
        {
            stat = new YARGFileStat(true, 0, DEFAULT_TIME, DEFAULT_TIME);
            return true;
        }

        stat = default;
        return false;
    }

    private void AddParents(string path)
    {
        for (int separator = LastSeparator(path); separator > 0; separator = LastSeparator(path))
        {
            path = path[..separator];
            if (!_directories.Add(path))
            {
                return;
            }
        }
    }

    private static string Normalize(string path)
    {
        return path.TrimEnd('\\', '/');
    }

    private static int LastSeparator(string path)
    {
        return path.LastIndexOfAny(['\\', '/']);
    }
}
