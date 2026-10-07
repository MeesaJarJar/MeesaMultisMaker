using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MeesaMultisMaker.Mul
{
    /// <summary>
    /// Reads and writes UOP (Ultima Online Package) files for art data.
    /// UOP is a container format that wraps MUL-style data with compression and hashing.
    /// 
    /// UOP File Structure:
    /// - Header (28 bytes): Magic, Version, Timestamp, NextBlockAddress, BlockSize, FileCount
    /// - File Table Blocks: Each block contains entries pointing to data
    /// - Data Blocks: Compressed or uncompressed file data
    /// 
    /// For artLegacyMUL.uop, each entry corresponds to an art index (land tiles 0x0000-0x3FFF, statics 0x4000+)
    /// The entry hash is computed from "build/artlegacymul/{index:D8}.tga"
    /// </summary>
    public static class UopArtWriter
    {
        // UOP Magic number "MYP\0"
        private const uint UOP_MAGIC = 0x0050594D;
        private const int UOP_VERSION = 5;

        // Hash format for art entries
        private const string ART_HASH_FORMAT = "build/artlegacymul/{0:D8}.tga";

        /// <summary>
        /// Update art entries in a UOP file
        /// </summary>
        /// <param name="uopPath">Path to the UOP file</param>
        /// <param name="items">Dictionary of item IDs and their encoded MUL data</param>
        /// <returns>Number of items successfully updated</returns>
        public static int UpdateUopArt(string uopPath, Dictionary<int, byte[]> items)
        {
            // Filter to standard MUL format items (<= 0xFFFF)
            var standardItems = new Dictionary<ushort, byte[]>();
            foreach (var kvp in items)
            {
                if (kvp.Key <= 0xFFFF)
                {
                    standardItems[(ushort)kvp.Key] = kvp.Value;
                }
            }
            
            if (standardItems.Count == 0)
                return 0;
                
            return UpdateUopArtInternal(uopPath, standardItems);
        }

        private static int UpdateUopArtInternal(string uopPath, Dictionary<ushort, byte[]> items)
        {
            if (!File.Exists(uopPath))
            {
                System.Diagnostics.Debug.WriteLine($"UopArtWriter: UOP file not found: {uopPath}");
                return 0;
            }

            if (items == null || items.Count == 0)
            {
                return 0;
            }

            try
            {
                // Read existing UOP structure
                var uopData = ReadUopFile(uopPath);
                if (uopData == null)
                {
                    System.Diagnostics.Debug.WriteLine("UopArtWriter: Failed to read UOP file");
                    return 0;
                }

                int updatedCount = 0;
                int skippedCount = 0;

                // For each item to update, find its entry and update the data
                foreach (var kvp in items)
                {
                    ushort itemId = kvp.Key;
                    byte[] newData = kvp.Value;

                    // Calculate the index in the UOP file (statics start at 0x4000)
                    int uopIndex = itemId + 0x4000;

                    // Calculate hash for this entry
                    ulong hash = HashLittle2(string.Format(ART_HASH_FORMAT, uopIndex));

                    // Find the entry with this hash
                    var entry = uopData.Entries.FirstOrDefault(e => e.Hash == hash);
                    if (entry != null)
                    {
                        // Update entry with new data (uncompressed)
                        entry.DecompressedSize = newData.Length;
                        entry.CompressedSize = newData.Length;
                        entry.CompressionType = 0; // No compression
                        entry.Data = newData;
                        entry.Modified = true;
                        updatedCount++;

                        System.Diagnostics.Debug.WriteLine($"UopArtWriter: Updated entry for item 0x{itemId:X4} (index {uopIndex})");
                    }
                    else
                    {
                        skippedCount++;
                        System.Diagnostics.Debug.WriteLine($"UopArtWriter: Entry not found for item 0x{itemId:X4} (hash 0x{hash:X16}) — skipped, UOP append not supported");
                    }
                }

                if (updatedCount > 0)
                {
                    // Rebuild the UOP file with updated data
                    WriteUopFile(uopPath, uopData);
                    System.Diagnostics.Debug.WriteLine($"UopArtWriter: Successfully updated {updatedCount} entries in UOP file");
                }
                // updatedCount counts only actually-written entries; skipped IDs (no UOP
                // slot to append to) are excluded so callers never over-report success.
                if (skippedCount > 0)
                    System.Diagnostics.Debug.WriteLine($"UopArtWriter: WARNING — {skippedCount} item(s) skipped (beyond UOP range, append not supported)");

                return updatedCount;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UopArtWriter: Error updating UOP: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Check if a UOP file exists for art data
        /// </summary>
        public static bool UopFileExists(string mulFolder)
        {
            string uopPath = Path.Combine(mulFolder, "artLegacyMUL.uop");
            return File.Exists(uopPath);
        }

        /// <summary>
        /// Get the path to the art UOP file
        /// </summary>
        public static string GetUopPath(string mulFolder)
        {
            return Path.Combine(mulFolder, "artLegacyMUL.uop");
        }

        /// <summary>
        /// Backup the UOP file
        /// </summary>
        public static bool BackupUopFile(string mulFolder, string backupSuffix = null)
        {
            if (string.IsNullOrEmpty(backupSuffix))
            {
                backupSuffix = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            }

            string uopPath = GetUopPath(mulFolder);
            if (!File.Exists(uopPath))
            {
                return true; // Nothing to backup
            }

            try
            {
                string backupPath = $"{uopPath}.{backupSuffix}.bak";
                if (!File.Exists(backupPath))
                {
                    File.Copy(uopPath, backupPath);
                    System.Diagnostics.Debug.WriteLine($"UopArtWriter: Backed up UOP to {backupPath}");
                }
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UopArtWriter: Backup failed: {ex.Message}");
                return false;
            }
        }

        #region UOP File Structure

        private class UopFileData
        {
            public uint Magic;
            public int Version;
            public uint Timestamp;
            public long NextBlockAddress;
            public int BlockSize;
            public int FileCount;
            public List<UopEntry> Entries = new List<UopEntry>();
        }

        private class UopEntry
        {
            public long DataOffset;
            public int HeaderLength;
            public int CompressedSize;
            public int DecompressedSize;
            public ulong Hash;
            public uint Checksum;
            public short CompressionType; // 0 = none, 1 = zlib
            public bool IsCompressed => CompressionType != 0;
            public byte[] Data;
            public bool Modified;
        }

        #endregion

        #region UOP Reading

        private static UopFileData ReadUopFile(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var reader = new BinaryReader(stream))
            {
                var uop = new UopFileData();

                // Read header
                uop.Magic = reader.ReadUInt32();
                if (uop.Magic != UOP_MAGIC)
                {
                    System.Diagnostics.Debug.WriteLine($"UopArtWriter: Invalid UOP magic: 0x{uop.Magic:X8}");
                    return null;
                }

                uop.Version = reader.ReadInt32();
                uop.Timestamp = reader.ReadUInt32();
                uop.NextBlockAddress = reader.ReadInt64();
                uop.BlockSize = reader.ReadInt32();
                uop.FileCount = reader.ReadInt32();

                System.Diagnostics.Debug.WriteLine($"UopArtWriter: Reading UOP - Version {uop.Version}, {uop.FileCount} files, BlockSize {uop.BlockSize}");

                // Read all file table blocks
                long nextBlock = uop.NextBlockAddress;
                while (nextBlock != 0)
                {
                    stream.Seek(nextBlock, SeekOrigin.Begin);

                    int filesInBlock = reader.ReadInt32();
                    nextBlock = reader.ReadInt64();

                    for (int i = 0; i < filesInBlock; i++)
                    {
                        var entry = new UopEntry
                        {
                            DataOffset = reader.ReadInt64(),
                            HeaderLength = reader.ReadInt32(),
                            CompressedSize = reader.ReadInt32(),
                            DecompressedSize = reader.ReadInt32(),
                            Hash = reader.ReadUInt64(),
                            Checksum = reader.ReadUInt32(),
                            CompressionType = reader.ReadInt16()
                        };

                        if (entry.DataOffset != 0)
                        {
                            uop.Entries.Add(entry);
                        }
                    }
                }

                // Read data for all entries
                foreach (var entry in uop.Entries)
                {
                    if (entry.DataOffset > 0 && entry.CompressedSize > 0)
                    {
                        stream.Seek(entry.DataOffset + entry.HeaderLength, SeekOrigin.Begin);
                        entry.Data = reader.ReadBytes(entry.CompressedSize);

                        // Decompress if needed
                        if (entry.IsCompressed && entry.Data.Length > 0)
                        {
                            entry.Data = Decompress(entry.Data, entry.DecompressedSize);
                        }
                    }
                }

                System.Diagnostics.Debug.WriteLine($"UopArtWriter: Read {uop.Entries.Count} entries from UOP");
                return uop;
            }
        }

        #endregion

        #region UOP Writing

        private static void WriteUopFile(string path, UopFileData uop)
        {
            string tempPath = path + ".tmp";

            using (var stream = File.Create(tempPath))
            using (var writer = new BinaryWriter(stream))
            {
                // Write header (we'll come back to update NextBlockAddress)
                long headerPos = stream.Position;
                writer.Write(UOP_MAGIC);
                writer.Write(uop.Version);
                writer.Write(uop.Timestamp);
                writer.Write((long)0); // NextBlockAddress - placeholder
                writer.Write(uop.BlockSize);
                writer.Write(uop.Entries.Count);

                // Write all data blocks first, collecting new offsets
                var dataOffsets = new List<long>();
                foreach (var entry in uop.Entries)
                {
                    if (entry.Data != null && entry.Data.Length > 0)
                    {
                        dataOffsets.Add(stream.Position);

                        // Write entry header (length varies, but typically 0 for art)
                        // Then write the data
                        writer.Write(entry.Data);
                    }
                    else
                    {
                        dataOffsets.Add(0);
                    }
                }

                // Calculate number of blocks needed
                int entriesPerBlock = uop.BlockSize > 0 ? uop.BlockSize : 100;
                int numBlocks = (uop.Entries.Count + entriesPerBlock - 1) / entriesPerBlock;

                // Write file table blocks
                long firstBlockAddress = stream.Position;

                for (int blockIndex = 0; blockIndex < numBlocks; blockIndex++)
                {
                    int startEntry = blockIndex * entriesPerBlock;
                    int entriesInBlock = Math.Min(entriesPerBlock, uop.Entries.Count - startEntry);

                    // Calculate next block address
                    long nextBlockAddr = 0;
                    if (blockIndex < numBlocks - 1)
                    {
                        // We need to calculate where the next block will be
                        // Each entry in table is 34 bytes, plus 12 bytes for block header
                        nextBlockAddr = stream.Position + 12 + (entriesInBlock * 34);
                    }

                    // Write block header
                    writer.Write(entriesInBlock);
                    writer.Write(nextBlockAddr);

                    // Write entries
                    for (int i = 0; i < entriesInBlock; i++)
                    {
                        int entryIndex = startEntry + i;
                        var entry = uop.Entries[entryIndex];

                        writer.Write(dataOffsets[entryIndex]); // DataOffset (8 bytes)
                        writer.Write(0); // HeaderLength (4 bytes)

                        int dataSize = entry.Data?.Length ?? 0;
                        writer.Write(dataSize); // CompressedSize (4 bytes)
                        writer.Write(dataSize); // DecompressedSize (4 bytes)
                        writer.Write(entry.Hash); // Hash (8 bytes)
                        writer.Write((uint)0); // Checksum (4 bytes)
                        writer.Write((short)0); // CompressionType - none (2 bytes)
                    }
                }

                // Go back and update the NextBlockAddress in header
                stream.Seek(headerPos + 12, SeekOrigin.Begin);
                writer.Write(firstBlockAddress);
            }

            // Replace original atomically, keeping a .bak backup.
            string backupPath = path + ".bak";
            try
            {
                if (File.Exists(path) && !File.Exists(backupPath))
                    File.Copy(path, backupPath);
            }
            catch { }
            File.Replace(tempPath, path, backupPath);
        }

        #endregion

        #region Compression

        private static byte[] Decompress(byte[] data, int decompressedSize)
        {
            try
            {
                // Skip first 2 bytes (zlib header)
                using (var input = new MemoryStream(data, 2, data.Length - 2))
                using (var deflate = new System.IO.Compression.DeflateStream(input, System.IO.Compression.CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    deflate.CopyTo(output);
                    return output.ToArray();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UopArtWriter: Decompression failed: {ex.Message}");
                return data; // Return original if decompression fails
            }
        }

        #endregion

        #region Hash Function

        /// <summary>
        /// Jenkins hash function used by UO for file lookups
        /// This is the "HashLittle2" variant
        /// </summary>
        private static ulong HashLittle2(string s)
        {
            uint a, b, c;
            int length = s.Length;

            a = b = c = 0xDEADBEEF + (uint)length;

            int i = 0;

            while (length > 12)
            {
                a += (uint)(s[i] + (s[i + 1] << 8) + (s[i + 2] << 16) + (s[i + 3] << 24));
                b += (uint)(s[i + 4] + (s[i + 5] << 8) + (s[i + 6] << 16) + (s[i + 7] << 24));
                c += (uint)(s[i + 8] + (s[i + 9] << 8) + (s[i + 10] << 16) + (s[i + 11] << 24));

                a -= c; a ^= RotateLeft(c, 4); c += b;
                b -= a; b ^= RotateLeft(a, 6); a += c;
                c -= b; c ^= RotateLeft(b, 8); b += a;
                a -= c; a ^= RotateLeft(c, 16); c += b;
                b -= a; b ^= RotateLeft(a, 19); a += c;
                c -= b; c ^= RotateLeft(b, 4); b += a;

                length -= 12;
                i += 12;
            }

            if (length > 0)
            {
                switch (length)
                {
                    case 12: c += (uint)(s[i + 11] << 24); goto case 11;
                    case 11: c += (uint)(s[i + 10] << 16); goto case 10;
                    case 10: c += (uint)(s[i + 9] << 8); goto case 9;
                    case 9: c += s[i + 8]; goto case 8;
                    case 8: b += (uint)(s[i + 7] << 24); goto case 7;
                    case 7: b += (uint)(s[i + 6] << 16); goto case 6;
                    case 6: b += (uint)(s[i + 5] << 8); goto case 5;
                    case 5: b += s[i + 4]; goto case 4;
                    case 4: a += (uint)(s[i + 3] << 24); goto case 3;
                    case 3: a += (uint)(s[i + 2] << 16); goto case 2;
                    case 2: a += (uint)(s[i + 1] << 8); goto case 1;
                    case 1: a += s[i]; break;
                }

                c ^= b; c -= RotateLeft(b, 14);
                a ^= c; a -= RotateLeft(c, 11);
                b ^= a; b -= RotateLeft(a, 25);
                c ^= b; c -= RotateLeft(b, 16);
                a ^= c; a -= RotateLeft(c, 4);
                b ^= a; b -= RotateLeft(a, 14);
                c ^= b; c -= RotateLeft(b, 24);
            }

            return ((ulong)b << 32) | c;
        }

        private static uint RotateLeft(uint value, int count)
        {
            return (value << count) | (value >> (32 - count));
        }

        #endregion
    }
}
