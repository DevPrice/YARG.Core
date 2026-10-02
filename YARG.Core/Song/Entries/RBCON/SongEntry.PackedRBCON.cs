using System;
using System.Collections.Generic;
using System.IO;
using YARG.Core.Extensions;
using YARG.Core.Song.Cache;
using YARG.Core.IO;
using YARG.Core.Venue;
using YARG.Core.Logging;

namespace YARG.Core.Song
{
    internal sealed class PackedRBCONEntry : RBCONEntry
    {
        private CONFileListing? _midiListing;
        private CONFileListing? _moggListing;
        private CONFileListing? _miloListing;
        private CONFileListing? _imgListing;
        private CONFileListing? _vocListing;
        private string          _psuedoDirectory;

        public override EntryType SubType => EntryType.CON;
        public override string SortBasedLocation => _psuedoDirectory;
        public override string ActualLocation => _root.FullName;
        protected override DateTime MidiLastWriteTime => _root.LastWriteTime;

        internal override void Serialize(MemoryStream stream, CacheWriteIndices node)
        {
            stream.Write(_subName);
            base.Serialize(stream, node);
        }

        #nullable disable
        public override YARGImage LoadAlbumData()
        {
            var image = LoadUpdateAlbumData();
            if (image == null && _imgListing != null)
            {
                using var bytes = CONFileStream.LoadFile(_root.FullName, _imgListing);
                image = YARGImage.TransferDXT(bytes);
            }
            return image;
        }
        #nullable restore

        public override BackgroundResult? LoadBackground(bool enableCensoring, bool excludeYarground = false)
        {
            if (_midiListing == null)
            {
                return null;
            }

            return LoadExternalBackground(_root.FullName, _subName, excludeYarground, enableCensoring);
        }

        internal static BackgroundResult? LoadExternalBackground(string conPath, string subName, bool excludeYarground, bool enableCensoring)
        {
            string actualDirectory = Path.GetDirectoryName(conPath)!;
            string conName = Path.GetFileName(conPath);
            string conNameWithoutExtension = Path.GetFileNameWithoutExtension(conPath);
            string censorSuffix = enableCensoring ? CLEAN_BACKGROUND_SUFFIX : EXPLICIT_BACKGROUND_SUFFIX;
            var files = GetFilesByName(actualDirectory);
            if (!excludeYarground)
            {
                foreach (var name in GetPackedConBackgroundNames(subName, conName, conNameWithoutExtension))
                {
                    if (files.TryGetValue(name + YARGROUND_EXTENSION, out var venue))
                    {
                        var stream = YARGFileSystem.OpenRead(venue);
                        return new BackgroundResult(BackgroundType.Yarground, stream);
                    }
                }

                // Exactly ".yarground", not "*.yarground": that is what the longstanding
                // Directory.GetFiles(dir, ".yarground") lookup matched.
                if (files.TryGetValue(YARGROUND_EXTENSION, out var directoryVenue))
                {
                    var stream = YARGFileSystem.OpenRead(directoryVenue);
                    return new BackgroundResult(BackgroundType.Yarground, stream);
                }
            }

            foreach (var name in GetPackedBackgroundNames(subName, conName, conNameWithoutExtension, includeVideo: true))
            {
                foreach (var ext in VIDEO_EXTENSIONS)
                {
                    if (files.TryGetValue(name + censorSuffix + ext, out var censoredPath))
                    {
                        var stream = YARGFileSystem.OpenRead(censoredPath);
                        return new BackgroundResult(BackgroundType.Video, stream);
                    }
                    if (files.TryGetValue(name + ext, out var backgroundPath))
                    {
                        var stream = YARGFileSystem.OpenRead(backgroundPath);
                        return new BackgroundResult(BackgroundType.Video, stream);
                    }
                }
            }

            foreach (var name in GetPackedBackgroundNames(subName, conName, conNameWithoutExtension, includeVideo: false))
            {
                foreach (var ext in IMAGE_EXTENSIONS)
                {
                    if (files.TryGetValue(name + censorSuffix + ext, out var censoredPath))
                    {
                        var image = YARGImage.Load(censoredPath);
                        if (image != null)
                        {
                            return new BackgroundResult(image);
                        }
                    }
                    if (files.TryGetValue(name + ext, out var backgroundPath))
                    {
                        var image = YARGImage.Load(backgroundPath);
                        if (image != null)
                        {
                            return new BackgroundResult(image);
                        }
                    }
                }
            }
            return null;
        }

        // Case-insensitive to match File.Exists on Windows and SMB.
        private static Dictionary<string, string> GetFilesByName(string directory)
        {
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in YARGFileSystem.Enumerate(directory))
            {
                if (!entry.Stat.IsDirectory)
                {
                    files.TryAdd(entry.Name, entry.FullName);
                }
            }
            return files;
        }

        private static IEnumerable<string> GetPackedConBackgroundNames(string subName, string conName, string conNameWithoutExtension)
        {
            yield return subName;
            yield return conName;

            if (conNameWithoutExtension != conName)
            {
                yield return conNameWithoutExtension;
            }
        }

        private static IEnumerable<string> GetPackedBackgroundNames(string subName, string conName, string conNameWithoutExtension, bool includeVideo)
        {
            foreach (var name in GetPackedConBackgroundNames(subName, conName, conNameWithoutExtension))
            {
                yield return name;
            }

            yield return "bg";
            yield return "background";

            if (includeVideo)
            {
                yield return "video";
            }
        }

        public override FixedArray<byte>? LoadMiloData()
        {
            var data = LoadUpdateMiloData();
            if (data == null && _miloListing != null)
            {
                data = CONFileStream.LoadFile(_root.FullName, _miloListing);
            }
            return data;
        }

        public override FixedArray<byte>? LoadVocData()
        {
            var data = LoadUpdateVocData();
            if (data == null && _vocListing != null)
            {
                data = CONFileStream.LoadFile(_root.FullName, _vocListing);
            }
            return data;
        }

        protected override FixedArray<byte>? GetMainMidiData()
        {
            return _midiListing != null
                ? CONFileStream.LoadFile(_root.FullName, _midiListing)
                : null;
        }

        protected override Stream? GetMoggStream()
        {
            var stream = LoadUpdateMoggStream();
            if (stream == null && _moggListing != null)
            {
                stream = CONFileStream.CreateStream(_root.FullName, _moggListing);
            }
            return stream;
        }

        private PackedRBCONEntry(in AbridgedFileInfo root, string nodeName)
            : base(in root, nodeName)
        {
            _midiListing = null!;
            _psuedoDirectory = string.Empty;
        }

        public static ScanExpected<RBCONEntry> Create(in RBScanParameters parameters, List<CONFileListing> listings, Stream stream)
        {
            try
            {
                var entry = new PackedRBCONEntry(in parameters.Root, parameters.NodeName)
                {
                    _updateDirectoryAndDtaLastWrite = parameters.UpdateDirectory,
                    _updateMidiLastWrite = parameters.UpdateMidi,
                    _upgrade = parameters.Upgrade
                };
                entry._metadata.Playlist = parameters.DefaultPlaylist;

                var location = ProcessDTAs(entry, parameters.BaseDta, parameters.UpdateDta, parameters.UpgradeDta);
                if (!location)
                {
                    return new ScanUnexpected(location.Error);
                }

                if (!listings.FindListing(location.Value + ".mid", out entry._midiListing))
                {
                    return new ScanUnexpected(ScanResult.MissingCONMidi);
                }

                if (!listings.FindListing(location.Value + ".mogg", out entry._moggListing))
                {
                    return new ScanUnexpected(ScanResult.MoggError);
                }

                FixedArray<byte> mainMidi;

                long moggLocation = CONFileStream.CalculateBlockLocation(entry._moggListing.BlockOffset, entry._moggListing.Shift);
                lock (stream)
                {
                    var moggResult = stream.Seek(moggLocation, SeekOrigin.Begin) == moggLocation
                        ? ValidateMoggHeader(stream)
                        : ScanResult.MoggError;
                    if (moggResult != ScanResult.Success)
                    {
                        return new ScanUnexpected(moggResult);
                    }
                    mainMidi = CONFileStream.LoadFile(stream, entry._midiListing);
                }

                var result = ScanMidis(entry, mainMidi);
                mainMidi.Dispose();
                if (result != ScanResult.Success)
                {
                    return new ScanUnexpected(result);
                }
                entry._psuedoDirectory = Path.Combine(parameters.Root.FullName, listings[entry._midiListing.PathIndex].Name);
                entry._subName = location.Value[6..location.Value.IndexOf('/', 6)];

                string genPath = $"songs/{entry._subName}/gen/{entry._subName}";
                listings.FindListing(genPath + ".milo_xbox", out entry._miloListing);
                listings.FindListing(genPath + "_keep.png_xbox", out entry._imgListing);
                listings.FindListing(location.Value + ".voc", out entry._vocListing);
                entry.SetSortStrings();
                return entry;
            }
            catch (Exception e)
            {
                YargLogger.LogException(e);
                return new ScanUnexpected(ScanResult.DTAError);
            }
        }

        public static PackedRBCONEntry? TryDeserialize(List<CONFileListing> listings, in AbridgedFileInfo conInfo, string nodeName, ref FixedArrayStream stream, CacheReadStrings strings)
        {
            string subname = stream.ReadString();
            string location = $"songs/{subname}/{subname}";
            if (!listings.FindListing(location + ".mid", out var midiListing))
            {
                return null;
            }

            if (!listings.FindListing(location + ".mogg", out var moggListing))
            {
                return null;
            }

            var entry = new PackedRBCONEntry(conInfo, nodeName)
            {
                _subName = subname,
                _midiListing = midiListing,
                _moggListing = moggListing,
                _psuedoDirectory = Path.Combine(conInfo.FullName, listings[midiListing.PathIndex].Name)
            };
            entry.Deserialize(ref stream, strings);

            string genPath = $"songs/{entry._subName}/gen/{entry._subName}";
            listings.FindListing(genPath + ".milo_xbox", out entry._miloListing);
            listings.FindListing(genPath + "_keep.png_xbox", out entry._imgListing);
            listings.FindListing(location + ".voc", out entry._vocListing);
            return entry;
        }

        public static PackedRBCONEntry ForceDeserialize(List<CONFileListing>? listings, in AbridgedFileInfo conInfo, string nodeName, ref FixedArrayStream stream, CacheReadStrings strings)
        {
            var entry = new PackedRBCONEntry(conInfo, nodeName)
            {
                _subName = stream.ReadString(),
            };
            entry.Deserialize(ref stream, strings);

            entry._psuedoDirectory = Path.Combine(conInfo.FullName, $"songs/{entry._subName}");
            if (listings != null)
            {
                string location = $"songs/{entry._subName}/{entry._subName}";
                listings.FindListing(location + ".mid", out entry._midiListing);
                listings.FindListing(location + ".mogg", out entry._moggListing);
                listings.FindListing(location + ".voc", out entry._vocListing);


                string genPath = $"songs/{entry._subName}/gen/{entry._subName}";
                listings.FindListing(genPath + ".milo_xbox", out entry._miloListing);
                listings.FindListing(genPath + "_keep.png_xbox", out entry._imgListing);
            }
            return entry;
        }
    }
}
