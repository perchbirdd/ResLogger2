using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Dalamud.Logging;

namespace ResLogger2.Plugin
{
    public class IndexValidator
    {
        private readonly Dictionary<uint, HashSet<uint>> _indexes = new();

        public IndexValidator(string gamePath)
        {
            Initialize(gamePath);
        }

        public IndexValidator()
        {
            var gamePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(gamePath))
                return;
            Initialize(gamePath);
        }

        private void Initialize(string gamePath)
        {
            gamePath = Path.GetDirectoryName(gamePath);
            var indexes = Directory.GetFiles(gamePath, "*.index2", SearchOption.AllDirectories);

            foreach (var index in indexes)
                ReadIndex(index);
        }

        private void ReadIndex(string path)
        {
            var indexIdStr = Path.GetFileNameWithoutExtension(path).Replace(".win32", "");
            var indexId = uint.Parse(indexIdStr, NumberStyles.HexNumber);
            var br = new BinaryReader(new MemoryStream(File.ReadAllBytes(path)));
            br.BaseStream.Seek(12, SeekOrigin.Begin);
            var skip = br.ReadUInt32();
            br.BaseStream.Seek(skip + 8, SeekOrigin.Begin);
            var seekTo = br.ReadUInt32();
            var hashCount = br.ReadUInt32() / 8; //8 for index2
            br.BaseStream.Seek(seekTo, SeekOrigin.Begin);

            for (int i = 0; i < hashCount; i++)
            {
                var fullHash = br.ReadUInt32();
                if (_indexes.ContainsKey(indexId))
                    _indexes[indexId].Add(fullHash);
                else
                    _indexes.Add(indexId, new HashSet<uint> { fullHash });
                br.BaseStream.Seek(4, SeekOrigin.Current);
            }

            PluginLog.Information($"Loaded index2 {indexId} {indexId:x6} with {hashCount} hashes");
        }

        // This code has been adapted to C# from FFXIV itself :)
        private uint GetCategoryIdForPath(string gamePath)
        {
            return gamePath switch
            {
                _ when gamePath.StartsWith("com") => 0x000000,
                _ when gamePath.StartsWith("bgc") => 0x010000,
                _ when gamePath.StartsWith("bg/") => GetBgSubCategoryId(gamePath) | (0x02 << 16),
                _ when gamePath.StartsWith("cut") => GetNonBgSubCategoryId(gamePath, 4) | (0x03 << 16),
                _ when gamePath.StartsWith("cha") => 0x040000,
                _ when gamePath.StartsWith("sha") => 0x050000,
                _ when gamePath.StartsWith("ui/") => 0x060000,
                _ when gamePath.StartsWith("sou") => 0x070000,
                _ when gamePath.StartsWith("vfx") => 0x080000,
                _ when gamePath.StartsWith("ui_") => 0x090000,
                _ when gamePath.StartsWith("exd") => 0x0A0000,
                _ when gamePath.StartsWith("gam") => 0x0B0000,
                _ when gamePath.StartsWith("mus") => GetNonBgSubCategoryId(gamePath, 6) | (0x0C << 16),
                _ when gamePath.StartsWith("_sq") => 0x110000,
                _ when gamePath.StartsWith("_de") => 0x120000,
                _ => 0
            };
        }

        // This code has been adapted to C# from FFXIV itself :)
        private uint GetBgSubCategoryId(string gamePath)
        {
            var segmentIdIndex = 3;
            uint expacId = 0;

            // Check if this is an ex* path
            if (gamePath[3] != 'e')
                return 0;

            // Check if our expac ID has one or two digits
            if (gamePath[6] == '/')
            {
                expacId = uint.Parse(gamePath[5..6]) << 8;
                segmentIdIndex = 7;
            }
            else if (gamePath[7] == '/')
            {
                expacId = uint.Parse(gamePath[5..7]) << 8;
                segmentIdIndex = 8;
            }
            else
            {
                expacId = 0;
            }

            // Parse the segment id for this bg path
            var segmentIdStr = gamePath.Substring(segmentIdIndex, 2);
            var segmentId = uint.Parse(segmentIdStr);

            return expacId + segmentId;
        }

        // This code has been adapted to C# from FFXIV itself :)
        private uint GetNonBgSubCategoryId(string gamePath, int firstDirLen)
        {
            if (gamePath[firstDirLen] != 'e')
                return 0;

            if (gamePath[firstDirLen + 3] == '/')
                return uint.Parse(gamePath.Substring(firstDirLen + 2, 1)) << 8;

            if (gamePath[firstDirLen + 4] == '/')
                return uint.Parse(gamePath.Substring(firstDirLen + 2, 2)) << 8;

            return 0;
        }

        public ExistsResult Exists(string gamePath)
        {
            try
            {
                var lowerPath = gamePath.ToLower();
                var indexId = GetCategoryIdForPath(lowerPath);
                var fullHash = Lumina.Misc.Crc32.Get(lowerPath);
                if (_indexes.TryGetValue(indexId, out var hashes))
                {
                    if (hashes.Contains(fullHash))
                    {
                        return new ExistsResult
                        {
                            FullText = gamePath,
                            FullExists = true,
                            IndexId = indexId
                        };
                    }
                }

                return new ExistsResult
                {
                    FullText = gamePath,
                    FullExists = false,
                    IndexId = uint.MaxValue
                };
            }
            catch (Exception e)
            {
                PluginLog.Error(e, "An error occurred in ResLogger2.");
            }

            return default;
        }
    }
}