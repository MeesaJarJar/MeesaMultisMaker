using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace MeesaMultisMaker.Mul
{
    /// <summary>
    /// Writes static item art to art.mul/artidx.mul AND artLegacyMUL.uop
    /// Static items start at index 0x4000 in the art files (land tiles are 0x0000-0x3FFF)
    /// 
    /// Static item format in art.mul:
    /// - uint32 header (usually 0x00000000)
    /// - ushort width
    /// - ushort height
    /// - ushort[] lookupTable (height entries - offset to each row from start of pixel data)
    /// - ushort[] pixelData (run-length encoded rows)
    /// 
    /// Run-length encoding per row:
    /// - ushort xOffset (pixels from left edge)
    /// - ushort runLength (number of pixels)
    /// - ushort[] colors (runLength pixels in RGB555 format)
    /// - Repeat until end of row, then xOffset=0, runLength=0 to terminate
    /// </summary>
    public static class StaticArtWriter
    {
        private const int STATIC_OFFSET = 0x4000; // Static items start at index 0x4000

        /// <summary>
        /// Save multiple static items to art.mul AND/OR artLegacyMUL.uop by rebuilding the files.
        /// Supports MUL-only, UOP-only, and MUL+UOP configurations.
        /// </summary>
        public static int SaveMultipleStaticArts(string mulFolder, Dictionary<int, Bitmap> items)
        {
            // Filter to only items that fit in standard MUL format (<= 0xFFFF)
            var standardItems = new Dictionary<ushort, byte[]>();
            foreach (var kvp in items)
            {
                if (kvp.Key <= 0xFFFF)
                {
                    byte[] encoded = EncodeStaticArt(kvp.Value);
                    if (encoded != null && encoded.Length > 0)
                    {
                        standardItems[(ushort)kvp.Key] = encoded;
                    }
                }
            }
            
            if (standardItems.Count == 0)
            {
                return 0;
            }

            return SaveMultipleStaticArtsInternal(mulFolder, standardItems);
        }

        /// <summary>
        /// Internal method for standard MUL format (ushort keys)
        /// </summary>
        private static int SaveMultipleStaticArtsInternal(string mulFolder, Dictionary<ushort, byte[]> encodedItems)
        {
            string artIdxPath = Path.Combine(mulFolder, "artidx.mul");
            string artMulPath = Path.Combine(mulFolder, "art.mul");
            string uopPath = UopArtWriter.GetUopPath(mulFolder);

            bool hasMul = File.Exists(artIdxPath) && File.Exists(artMulPath);
            bool hasUop = File.Exists(uopPath);

            if (!hasMul && !hasUop)
            {
                System.Diagnostics.Debug.WriteLine("StaticArtWriter: No art.mul or artLegacyMUL.uop found");
                return 0;
            }

            int savedCount = 0;

            // ── MUL path ────────────────────────────────────────────
            if (hasMul)
            {
                savedCount = RebuildMulFiles(artIdxPath, artMulPath, encodedItems);
            }

            // ── UOP path ────────────────────────────────────────────
            if (hasUop)
            {
                var encodedItemsInt = encodedItems.ToDictionary(kvp => (int)kvp.Key, kvp => kvp.Value);
                int uopUpdated = UopArtWriter.UpdateUopArt(uopPath, encodedItemsInt);
                System.Diagnostics.Debug.WriteLine($"StaticArtWriter: Updated {uopUpdated} entries in UOP file");
                if (hasMul && uopUpdated < encodedItems.Count)
                    System.Diagnostics.Debug.WriteLine($"StaticArtWriter: WARNING — {encodedItems.Count - uopUpdated} item(s) saved to MUL but skipped in UOP (beyond UOP range)");

                // If MUL wasn't available, report UOP count as the saved count
                if (!hasMul)
                    savedCount = uopUpdated;
            }

            return savedCount;
        }

        /// <summary>
        /// Rebuild art.mul and artidx.mul with replacement entries.
        /// Automatically extends the index if new items exceed the existing range.
        /// </summary>
        private static int RebuildMulFiles(string artIdxPath, string artMulPath, Dictionary<ushort, byte[]> encodedItems)
        {
            int savedCount = 0;

            try
            {
                var indexEntries = ReadAllIndexEntries(artIdxPath);

                // Determine the max index needed — extend if any item exceeds current range
                int maxNeeded = indexEntries.Count;
                foreach (var kvp in encodedItems)
                {
                    int requiredIndex = kvp.Key + STATIC_OFFSET + 1;
                    if (requiredIndex > maxNeeded)
                        maxNeeded = requiredIndex;
                }

                // Pad index entries to cover new items
                while (indexEntries.Count < maxNeeded)
                {
                    indexEntries.Add(new IndexEntry { Offset = -1, Length = -1, Extra = -1 });
                }

                string tempMulPath = artMulPath + ".tmp";
                string tempIdxPath = artIdxPath + ".tmp";

                // Backup current files to .bak before starting (best-effort).
                try { BackupArtFiles(Path.GetDirectoryName(artMulPath)); } catch { }

                using (var originalMul = File.OpenRead(artMulPath))
                using (var newMul = File.Create(tempMulPath))
                using (var newIdx = File.Create(tempIdxPath))
                using (var idxWriter = new BinaryWriter(newIdx))
                {
                    for (int i = 0; i < indexEntries.Count; i++)
                    {
                        var entry = indexEntries[i];

                        int itemId = i - STATIC_OFFSET;
                        bool isReplacement = itemId >= 0 && itemId <= 0xFFFF &&
                                           encodedItems.ContainsKey((ushort)itemId);

                        if (isReplacement)
                        {
                            byte[] newData = encodedItems[(ushort)itemId];
                            long newOffset = newMul.Position;

                            newMul.Write(newData, 0, newData.Length);

                            idxWriter.Write((int)newOffset);
                            idxWriter.Write((int)newData.Length);
                            idxWriter.Write((int)0);

                            savedCount++;
                            System.Diagnostics.Debug.WriteLine($"StaticArtWriter: Wrote item 0x{itemId:X4} at offset {newOffset}, size {newData.Length}");
                        }
                        else if (entry.Offset >= 0 && entry.Length > 0)
                        {
                            long newOffset = newMul.Position;

                            byte[] existingData = new byte[entry.Length];
                            originalMul.Seek(entry.Offset, SeekOrigin.Begin);
                            originalMul.Read(existingData, 0, entry.Length);
                            newMul.Write(existingData, 0, existingData.Length);

                            idxWriter.Write((int)newOffset);
                            idxWriter.Write((int)entry.Length);
                            idxWriter.Write((int)entry.Extra);
                        }
                        else
                        {
                            idxWriter.Write((int)-1);
                            idxWriter.Write((int)-1);
                            idxWriter.Write((int)-1);
                        }
                    }
                }

                // Ensure tmp files are flushed to disk before swapping.
                try
                {
                    File.Replace(tempMulPath, artMulPath, artMulPath + ".bak");
                    File.Replace(tempIdxPath, artIdxPath, artIdxPath + ".bak");
                }
                catch
                {
                    // Attempt to restore .bak files on failure.
                    try
                    {
                        if (File.Exists(artMulPath + ".bak"))
                            File.Copy(artMulPath + ".bak", artMulPath, true);
                        if (File.Exists(artIdxPath + ".bak"))
                            File.Copy(artIdxPath + ".bak", artIdxPath, true);
                    }
                    catch { }
                    try { if (File.Exists(tempMulPath)) File.Delete(tempMulPath); } catch { }
                    try { if (File.Exists(tempIdxPath)) File.Delete(tempIdxPath); } catch { }
                    savedCount = 0;
                    throw;
                }

                System.Diagnostics.Debug.WriteLine($"StaticArtWriter: Rebuilt MUL files, saved {savedCount} items");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"StaticArtWriter: Error rebuilding MUL files: {ex.Message}");
            }

            return savedCount;
        }

        private struct IndexEntry
        {
            public int Offset;
            public int Length;
            public int Extra;
        }

        private static List<IndexEntry> ReadAllIndexEntries(string idxPath)
        {
            var entries = new List<IndexEntry>();
            using (var idxStream = File.OpenRead(idxPath))
            using (var idxReader = new BinaryReader(idxStream))
            {
                long count = idxStream.Length / 12;
                for (int i = 0; i < count; i++)
                {
                    entries.Add(new IndexEntry
                    {
                        Offset = idxReader.ReadInt32(),
                        Length = idxReader.ReadInt32(),
                        Extra = idxReader.ReadInt32()
                    });
                }
            }
            return entries;
        }

        /// <summary>
        /// Encodes a Bitmap into the UO static art format (RLE compressed).
        /// </summary>
        private static byte[] EncodeStaticArt(Bitmap bmp)
        {
            if (bmp == null) return null;

            int width = bmp.Width;
            int height = bmp.Height;

            if (width <= 0 || height <= 0 || width > 1024 || height > 1024)
                return null;

            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                // Header (4 bytes)
                bw.Write((uint)0);

                // Width and height (2 bytes each)
                bw.Write((ushort)width);
                bw.Write((ushort)height);

                // We'll need to write the lookup table first, then pixel data
                // Save position for lookup table
                long lookupPos = ms.Position;

                // Reserve space for lookup table (height * 2 bytes for 16-bit offsets)
                for (int i = 0; i < height; i++)
                {
                    bw.Write((ushort)0);
                }

                // Now encode pixel data row by row
                var pixelData = new List<ushort>();
                var lookupTable = new int[height];

                for (int y = 0; y < height; y++)
                {
                    lookupTable[y] = pixelData.Count;

                    int x = 0;
                    while (x < width)
                    {
                        // Find next non-transparent pixel
                        while (x < width)
                        {
                            Color pixel = bmp.GetPixel(x, y);
                            if (pixel.A > 0) break;
                            x++;
                        }

                        if (x >= width) break;

                        int runStart = x;

                        // Find run of non-transparent pixels
                        while (x < width)
                        {
                            Color pixel = bmp.GetPixel(x, y);
                            if (pixel.A == 0) break;
                            x++;
                        }

                        int runLength = x - runStart;
                        int xOffset = runStart - (pixelData.Count > 0 ? 0 : 0); // Simplified

                        // Write xOffset and runLength
                        pixelData.Add((ushort)runStart);
                        pixelData.Add((ushort)runLength);

                        // Write pixel colors
                        for (int i = runStart; i < runStart + runLength; i++)
                        {
                            Color pixel = bmp.GetPixel(i, y);
                            ushort color16 = (ushort)(((pixel.R >> 3) << 10) | ((pixel.G >> 3) << 5) | (pixel.B >> 3));
                            pixelData.Add((ushort)(color16 | 0x8000));
                        }
                    }

                    // End of row marker
                    pixelData.Add(0);
                    pixelData.Add(0);
                }

                // Write lookup table
                long pixelDataStart = ms.Position;
                ms.Seek(lookupPos, SeekOrigin.Begin);
                for (int i = 0; i < height; i++)
                {
                    bw.Write((ushort)(lookupTable[i] * 2));
                }
                ms.Seek(pixelDataStart, SeekOrigin.Begin);

                // Write pixel data
                foreach (var val in pixelData)
                {
                    bw.Write(val);
                }

                return ms.ToArray();
            }
        }

        /// <summary>
        /// Find next available empty slots in the art files.
        /// </summary>
        public static List<int> FindNextEmptySlots(string mulFolder, int count)
        {
            string artIdxPath = Path.Combine(mulFolder, "artidx.mul");
            if (!File.Exists(artIdxPath))
                return new List<int>();

            var emptySlots = new List<int>();

            using (var idxStream = File.OpenRead(artIdxPath))
            using (var idxReader = new BinaryReader(idxStream))
            {
                long maxIndex = idxStream.Length / 12;

                for (int itemId = 0; itemId <= 0xFFFF && emptySlots.Count < count; itemId++)
                {
                    int index = itemId + STATIC_OFFSET;
                    if (index >= maxIndex) break;

                    idxStream.Seek((long)index * 12, SeekOrigin.Begin);
                    int offset = idxReader.ReadInt32();
                    int length = idxReader.ReadInt32();

                    if (offset < 0 || length <= 0)
                    {
                        emptySlots.Add(itemId);
                    }
                }
            }

            return emptySlots;
        }

        /// <summary>
        /// Save a single static art item to art.mul/artidx.mul and/or UOP.
        /// </summary>
        public static bool SaveStaticArt(string mulFolder, ushort itemId, Bitmap bmp)
        {
            var items = new Dictionary<int, Bitmap> { { itemId, bmp } };
            return SaveMultipleStaticArts(mulFolder, items) > 0;
        }

        /// <summary>
        /// Backup art.mul, artidx.mul, artLegacyMUL.uop and tiledata.mul before modifying them.
        /// </summary>
        public static bool BackupArtFiles(string mulFolder)
        {
            string artMulPath = Path.Combine(mulFolder, "art.mul");
            string artIdxPath = Path.Combine(mulFolder, "artidx.mul");
            string uopPath = Path.Combine(mulFolder, "artLegacyMUL.uop");
            string tiledataPath = Path.Combine(mulFolder, "tiledata.mul");

            if (!File.Exists(artMulPath) && !File.Exists(artIdxPath) && !File.Exists(uopPath) && !File.Exists(tiledataPath))
                return false;

            try
            {
                CopyToBak(artMulPath);
                CopyToBak(artIdxPath);
                CopyToBak(uopPath);
                CopyToBak(tiledataPath);

                System.Diagnostics.Debug.WriteLine("StaticArtWriter: Backed up art files (.bak)");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"StaticArtWriter: Backup failed: {ex.Message}");
                return false;
            }
        }

        private static void CopyToBak(string path)
        {
            if (!File.Exists(path)) return;
            string bak = path + ".bak";
            try { if (File.Exists(bak)) File.Delete(bak); } catch { }
            File.Copy(path, bak);
        }
    }
}