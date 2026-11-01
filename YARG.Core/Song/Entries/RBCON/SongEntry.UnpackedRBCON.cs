using System;
using System.IO;
using YARG.Core.Extensions;
using YARG.Core.IO;
using YARG.Core.Logging;

namespace YARG.Core.Song
{
    internal sealed class UnpackedRBCONEntry : UnpackedConsolePackageEntry
    {
        protected override YARGImage DXTImageLoader(string path) => YARGImage.LoadDXT(path);

        private UnpackedRBCONEntry(in AbridgedFileInfo root, string nodeName)
            : base(in root, nodeName) {}

        public static ScanExpected<RBCONEntry> Create(in RBScanParameters parameters)
        {
            try
            {
                var entry = new UnpackedRBCONEntry(in parameters.Root, parameters.NodeName)
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

                entry._subName = location.Value[6..location.Value.IndexOf('/', 6)];

                string songDirectory = Path.Combine(parameters.Root.FullName, entry._subName);
                string midiPath = Path.Combine(songDirectory, entry._subName + ".mid");
                if (!AbridgedFileInfo.TryStatFile(midiPath, out var midiStat))
                {
                    return new ScanUnexpected(ScanResult.MissingCONMidi);
                }

                string moggPath = Path.Combine(songDirectory, entry._subName + ".mogg");
                if (YARGFileSystem.FileExists(moggPath))
                {
                    using var moggStream = YARGFileSystem.OpenRead(moggPath, 1);
                    var moggResult = ValidateMoggHeader(moggStream);
                    if (moggResult != ScanResult.Success)
                    {
                        return new ScanUnexpected(moggResult);
                    }
                }
                else
                {
                    return new ScanUnexpected(ScanResult.MoggError);
                }

                using var mainMidi = FixedArray.LoadFile(midiPath);

                var result = ScanMidis(entry, mainMidi);
                if (result != ScanResult.Success)
                {
                    return new ScanUnexpected(result);
                }
                entry._midiLastWrite = AbridgedFileInfo.NormalizedLastWrite(in midiStat);
                entry.SetSortStrings();
                return entry;
            }
            catch (Exception e)
            {
                YargLogger.LogException(e);
                return new ScanUnexpected(ScanResult.DTAError);
            }
        }

        public static UnpackedRBCONEntry? TryDeserialize(in AbridgedFileInfo root, string nodeName, ref FixedArrayStream stream, CacheReadStrings strings)
        {
            string subname = stream.ReadString();
            string midiPath = Path.Combine(root.FullName, subname, subname + ".mid");
            if (!AbridgedFileInfo.TryStatFile(midiPath, out var midiStat))
            {
                return null;
            }

            var midiLastWrite = DateTime.FromBinary(stream.Read<long>(Endianness.Little));
            if (midiLastWrite != AbridgedFileInfo.NormalizedLastWrite(in midiStat))
            {
                return null;
            }

            var entry = new UnpackedRBCONEntry(in root, nodeName)
            {
                _subName = subname,
                _midiLastWrite = midiLastWrite,
            };
            entry.Deserialize(ref stream, strings);
            return entry;
        }

        public static UnpackedRBCONEntry ForceDeserialize(in AbridgedFileInfo root, string nodeName, ref FixedArrayStream stream, CacheReadStrings strings)
        {
            var entry = new UnpackedRBCONEntry(in root, nodeName)
            {
                _subName = stream.ReadString(),
                _midiLastWrite = DateTime.FromBinary(stream.Read<long>(Endianness.Little)),
            };
            entry.Deserialize(ref stream, strings);
            return entry;
        }

        public override YARGImage? LoadAlbumData() => LoadAlbumData(".png_xbox");

        public override FixedArray<byte>? LoadMiloData() => LoadMiloData(".milo_xbox");

        protected override FixedArray<byte>? GetMainMidiData() => GetMainMidiData(".mid");
    }
}
