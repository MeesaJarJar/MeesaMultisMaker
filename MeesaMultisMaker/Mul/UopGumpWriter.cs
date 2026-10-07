using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace MeesaMultisMaker.Mul
{
    /// <summary>
    /// Writes GUMP art to gumpartLegacyMUL.uop format.
    /// 
    /// UOP gump data format:
    /// - 4 bytes: width (int32)
    /// - 4 bytes: height (int32)
    /// - Followed by standard MUL-style RLE gump data (lookup table + row runs)
    /// 
    /// Each entry is hashed using "build/gumpartlegacymul/{index:D8}.tga"
    /// </summary>
    public static class UopGumpWriter
    {
        private const uint UOP_MAGIC = 0x0050594D; // "MYP\0"
        private const string GUMP_LEGACY_UOP_FILENAME = "gumpartLegacyMUL.uop";
        private const string GUMP_HASH_FORMAT = "build/gumpartlegacymul/{0:D8}.tga";

        /// <summary>
        /// Save a bitmap as a GUMP to the UOP file
        /// </summary>
        /// <param name="mulFolder">Path to folder containing gumpartLegacyMUL.uop</param>
        /// <param name="gumpId">GUMP ID to save</param>
        /// <param name="image">Bitmap to save</param>
        /// <returns>True if successful</returns>
        public static bool SaveGump(string mulFolder, int gumpId, Bitmap image)
        {
            if (string.IsNullOrEmpty(mulFolder) || image == null)
                return false;

            string uopPath = Path.Combine(mulFolder, GUMP_LEGACY_UOP_FILENAME);
            if (!File.Exists(uopPath))
                return false;

            try
            {
                // Encode image to UOP gump format (width + height + RLE data)
                byte[] gumpData = EncodeGumpForUop(image);
                if (gumpData == null)
                    return false;

                // Read existing UOP structure
                var uopData = ReadUopFile(uopPath);
                if (uopData == null)
                {
                    System.Diagnostics.Debug.WriteLine("UopGumpWriter: Failed to read UOP file");
                    return false;
                }

                // Calculate hash for this gump ID
                ulong hash = UopHashEngine.HashLittle2(string.Format(GUMP_HASH_FORMAT, gumpId));

                // Find the entry with this hash
                var entry = uopData.Entries.FirstOrDefault(e => e.Hash == hash);
                if (entry != null)
                {
                    // Update existing entry
                    entry.DecompressedSize = gumpData.Length;
                    entry.CompressedSize = gumpData.Length;
                    entry.CompressionType = 0;
                    entry.Data = gumpData;
                    entry.Modified = true;
                }
                else
                {
                    // Add new entry
                    uopData.Entries.Add(new UopEntry
                    {
                        Hash = hash,
                        DecompressedSize = gumpData.Length,
                        CompressedSize = gumpData.Length,
                        CompressionType = 0,
                        Data = gumpData,
                        Modified = true
                    });
                    uopData.FileCount = uopData.Entries.Count;
                }

                // Rebuild the UOP file
                WriteUopFile(uopPath, uopData);

                // Clear caches
                GumpArtReader.ClearCache();
                UopGumpReader.ClearCache();

                System.Diagnostics.Debug.WriteLine($"UopGumpWriter: Saved gump 0x{gumpId:X4} to UOP");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UopGumpWriter: Failed to save gump 0x{gumpId:X4}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Save multiple gumps to UOP in a single pass (more efficient)
        /// </summary>
        /// <param name="mulFolder">Path to folder containing gumpartLegacyMUL.uop</param>
        /// <param name="gumps">Dictionary of gump ID to Bitmap</param>
        /// <returns>Number of gumps successfully saved</returns>
        public static int SaveGumps(string mulFolder, Dictionary<int, Bitmap> gumps)
        {
            if (string.IsNullOrEmpty(mulFolder) || gumps == null || gumps.Count == 0)
                return 0;

            string uopPath = Path.Combine(mulFolder, GUMP_LEGACY_UOP_FILENAME);
            if (!File.Exists(uopPath))
                return 0;

            try
            {
                var uopData = ReadUopFile(uopPath);
                if (uopData == null)
                    return 0;

                int updatedCount = 0;

                foreach (var kvp in gumps)
                {
                    int gumpId = kvp.Key;
                    Bitmap image = kvp.Value;
                    if (image == null) continue;

                    byte[] gumpData = EncodeGumpForUop(image);
                    if (gumpData == null) continue;

                    ulong hash = UopHashEngine.HashLittle2(string.Format(GUMP_HASH_FORMAT, gumpId));
                    var entry = uopData.Entries.FirstOrDefault(e => e.Hash == hash);

                    if (entry != null)
                    {
                        entry.DecompressedSize = gumpData.Length;
                        entry.CompressedSize = gumpData.Length;
                        entry.CompressionType = 0;
                        entry.Data = gumpData;
                        entry.Modified = true;
                    }
                    else
                    {
                        uopData.Entries.Add(new UopEntry
                        {
                            Hash = hash,
                            DecompressedSize = gumpData.Length,
                            CompressedSize = gumpData.Length,
                            CompressionType = 0,
                            Data = gumpData,
                            Modified = true
                        });
                    }

                    updatedCount++;
                }

                if (updatedCount > 0)
                {
                    uopData.FileCount = uopData.Entries.Count;
                    WriteUopFile(uopPath, uopData);
                    GumpArtReader.ClearCache();
                    UopGumpReader.ClearCache();
                    System.Diagnostics.Debug.WriteLine($"UopGumpWriter: Saved {updatedCount} gumps to UOP");
                }

                return updatedCount;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UopGumpWriter: Batch save failed: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Check if UOP gump file exists
        /// </summary>
        public static bool UopFileExists(string mulFolder)
        {
            return File.Exists(Path.Combine(mulFolder, GUMP_LEGACY_UOP_FILENAME));
        }

        /// <summary>
        /// Backup the UOP gump file
        /// </summary>
        public static bool BackupUopFile(string mulFolder, string backupSuffix = null)
        {
            if (string.IsNullOrEmpty(backupSuffix))
            {
                backupSuffix = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            }

            string uopPath = Path.Combine(mulFolder, GUMP_LEGACY_UOP_FILENAME);
            if (!File.Exists(uopPath))
                return true;

            try
            {
                string backupPath = $"{uopPath}.{backupSuffix}.bak";
                if (!File.Exists(backupPath))
                {
                    File.Copy(uopPath, backupPath);
                    System.Diagnostics.Debug.WriteLine($"UopGumpWriter: Backed up UOP to {backupPath}");
                }
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UopGumpWriter: Backup failed: {ex.Message}");
                return false;
            }
        }

        #region Gump Encoding

        /// <summary>
        /// Encode a bitmap to UOP gump format (width + height header + RLE data)
        /// </summary>
        private static byte[] EncodeGumpForUop(Bitmap image)
        {
            int width = image.Width;
            int height = image.Height;

            if (width <= 0 || height <= 0 || width > 4096 || height > 4096)
                return null;

            // Get the RLE-encoded gump data (lookup table + row data)
            byte[] rleData = EncodeGumpRle(image);
            if (rleData == null)
                return null;

            // UOP gump data = [width:int32][height:int32] + RLE data
            using (var output = new MemoryStream())
            using (var writer = new BinaryWriter(output))
            {
                writer.Write(width);
                writer.Write(height);
                writer.Write(rleData);
                return output.ToArray();
            }
        }

        /// <summary>
        /// Encode bitmap to standard MUL-style RLE gump data (lookup table + row runs)
        /// </summary>
        private static byte[] EncodeGumpRle(Bitmap image)
        {
            int width = image.Width;
            int height = image.Height;

            // Read pixel data from bitmap
            var rect = new Rectangle(0, 0, width, height);
            var data = image.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            byte[] pixels;
            try
            {
                int byteCount = Math.Abs(data.Stride) * height;
                pixels = new byte[byteCount];
                Marshal.Copy(data.Scan0, pixels, 0, byteCount);
            }
            finally
            {
                image.UnlockBits(data);
            }

            // Encode each row using RLE
            var rowDataList = new byte[height][];

            for (int y = 0; y < height; y++)
            {
                var rowStream = new MemoryStream();
                using (var rowWriter = new BinaryWriter(rowStream))
                {
                    int x = 0;
                    while (x < width)
                    {
                        int pixelIndex = (y * width + x) * 4;
                        byte b = pixels[pixelIndex + 0];
                        byte g = pixels[pixelIndex + 1];
                        byte r = pixels[pixelIndex + 2];
                        byte a = pixels[pixelIndex + 3];

                        ushort color16;
                        if (a < 128)
                        {
                            color16 = 0;
                        }
                        else
                        {
                            int r5 = (r * 31 / 255) & 0x1F;
                            int g5 = (g * 31 / 255) & 0x1F;
                            int b5 = (b * 31 / 255) & 0x1F;
                            color16 = (ushort)((1 << 15) | (r5 << 10) | (g5 << 5) | b5);
                            if (color16 == 0) color16 = 1;
                        }

                        int runLength = 1;
                        int maxRun = Math.Min(width - x, ushort.MaxValue);

                        while (runLength < maxRun)
                        {
                            int nextPixelIndex = (y * width + x + runLength) * 4;
                            byte nb = pixels[nextPixelIndex + 0];
                            byte ng = pixels[nextPixelIndex + 1];
                            byte nr = pixels[nextPixelIndex + 2];
                            byte na = pixels[nextPixelIndex + 3];

                            ushort nextColor16;
                            if (na < 128)
                            {
                                nextColor16 = 0;
                            }
                            else
                            {
                                int nr5 = (nr * 31 / 255) & 0x1F;
                                int ng5 = (ng * 31 / 255) & 0x1F;
                                int nb5 = (nb * 31 / 255) & 0x1F;
                                nextColor16 = (ushort)((1 << 15) | (nr5 << 10) | (ng5 << 5) | nb5);
                                if (nextColor16 == 0) nextColor16 = 1;
                            }

                            if (nextColor16 != color16)
                                break;

                            runLength++;
                        }

                        rowWriter.Write(color16);
                        rowWriter.Write((ushort)runLength);

                        x += runLength;
                    }
                }

                rowDataList[y] = rowStream.ToArray();
            }

            // Build final data with lookup table
            var outputStream = new MemoryStream();
            using (var writer = new BinaryWriter(outputStream))
            {
                int currentOffset = height;
                for (int y = 0; y < height; y++)
                {
                    writer.Write(currentOffset);
                    currentOffset += rowDataList[y].Length / 4;
                    if (rowDataList[y].Length % 4 != 0)
                        currentOffset++;
                }

                for (int y = 0; y < height; y++)
                {
                    writer.Write(rowDataList[y]);

                    int padding = (4 - (rowDataList[y].Length % 4)) % 4;
                    for (int i = 0; i < padding; i++)
                        writer.Write((byte)0);
                }
            }

            return outputStream.ToArray();
        }

        #endregion

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
            public short CompressionType;
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

                uop.Magic = reader.ReadUInt32();
                if (uop.Magic != UOP_MAGIC)
                {
                    System.Diagnostics.Debug.WriteLine($"UopGumpWriter: Invalid UOP magic: 0x{uop.Magic:X8}");
                    return null;
                }

                uop.Version = reader.ReadInt32();
                uop.Timestamp = reader.ReadUInt32();
                uop.NextBlockAddress = reader.ReadInt64();
                uop.BlockSize = reader.ReadInt32();
                uop.FileCount = reader.ReadInt32();

                // Read all file table blocks
                long nextBlock = uop.NextBlockAddress;
                while (nextBlock != 0)
                {
                    if (nextBlock < 0 || nextBlock >= stream.Length)
                        break;

                    stream.Seek(nextBlock, SeekOrigin.Begin);

                    int filesInBlock = reader.ReadInt32();
                    nextBlock = reader.ReadInt64();

                    if (filesInBlock < 0 || filesInBlock > 1000)
                        break;

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

                        if (entry.CompressionType != 0 && entry.Data.Length > 0)
                        {
                            entry.Data = Decompress(entry.Data, entry.DecompressedSize);
                        }
                    }
                }

                System.Diagnostics.Debug.WriteLine($"UopGumpWriter: Read {uop.Entries.Count} entries from UOP");
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
                // Write header
                long headerPos = stream.Position;
                writer.Write(UOP_MAGIC);
                writer.Write(uop.Version);
                writer.Write(uop.Timestamp);
                writer.Write((long)0); // NextBlockAddress placeholder
                writer.Write(uop.BlockSize);
                writer.Write(uop.Entries.Count);

                // Write all data blocks, collecting new offsets
                var dataOffsets = new List<long>();
                foreach (var entry in uop.Entries)
                {
                    if (entry.Data != null && entry.Data.Length > 0)
                    {
                        dataOffsets.Add(stream.Position);
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

                    long nextBlockAddr = 0;
                    if (blockIndex < numBlocks - 1)
                    {
                        // 12 bytes block header + 34 bytes per entry
                        nextBlockAddr = stream.Position + 12 + (entriesInBlock * 34);
                    }

                    writer.Write(entriesInBlock);
                    writer.Write(nextBlockAddr);

                    for (int i = 0; i < entriesInBlock; i++)
                    {
                        int entryIndex = startEntry + i;
                        var entry = uop.Entries[entryIndex];

                        int dataSize = entry.Data?.Length ?? 0;

                        writer.Write(dataOffsets[entryIndex]); // DataOffset (8 bytes)
                        writer.Write(0);                       // HeaderLength (4 bytes)
                        writer.Write(dataSize);                // CompressedSize (4 bytes)
                        writer.Write(dataSize);                // DecompressedSize (4 bytes)
                        writer.Write(entry.Hash);              // Hash (8 bytes)
                        writer.Write((uint)0);                 // Checksum (4 bytes)
                        writer.Write((short)0);                // CompressionType - none (2 bytes)
                    }
                }

                // Update NextBlockAddress in header
                stream.Seek(headerPos + 12, SeekOrigin.Begin);
                writer.Write(firstBlockAddress);
            }

            // Replace original with new file
            File.Delete(path);
            File.Move(tempPath, path);
        }

        #endregion

        #region Compression

        private static byte[] Decompress(byte[] data, int decompressedSize)
        {
            try
            {
                using (var input = new MemoryStream(data, 2, data.Length - 2))
                using (var deflate = new System.IO.Compression.DeflateStream(input, System.IO.Compression.CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    deflate.CopyTo(output);
                    return output.ToArray();
                }
            }
            catch
            {
                return data;
            }
        }

        #endregion
    }
}
