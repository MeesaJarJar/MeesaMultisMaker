using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace MeesaMultisMaker.Mul
{
    /// <summary>
    /// Reads static item art from Tecmo Expanded Art format (art.mul and artidx.mul)
    /// Supports up to 0x40000 (262,144) total entries with 32-bit lookup tables for tall art
    /// Land tiles: 0x0000-0x3FFF (16384, unchanged)
    /// Static items: 0x4000-0x3FFFF (245,760 items, using int ItemId)
    /// </summary>
    public static class TecmoArtReader
    {
        private const int STATIC_OFFSET = 0x4000; // Static items start at index 0x4000
        private const int MAX_ART_ENTRIES = 0x40000; // 262144 entries

        // Cache of opened art archives per folder
        private static readonly Dictionary<string, TecmoArtArchive> archives = new Dictionary<string, TecmoArtArchive>(StringComparer.OrdinalIgnoreCase);
        private static readonly object archivesLock = new object();

        /// <summary>
        /// Load a static item image from Tecmo expanded art.mul
        /// </summary>
        /// <param name="mulFolder">Path to folder containing art.mul and artidx.mul</param>
        /// <param name="itemId">Item ID (int, can exceed 65535)</param>
        /// <returns>Bitmap of the item, or null if not found</returns>
        public static Bitmap LoadStaticArt(string mulFolder, int itemId)
        {
            if (string.IsNullOrEmpty(mulFolder))
                return null;

            try
            {
                var archive = GetOrCreateArchive(mulFolder);
                if (archive == null)
                    return null;

                return archive.ReadItem(itemId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TecmoArtReader: EXCEPTION loading item 0x{itemId:X}: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Load a land tile texture from Tecmo expanded art.mul
        /// </summary>
        public static Bitmap LoadLandTile(string mulFolder, int tileId)
        {
            if (string.IsNullOrEmpty(mulFolder))
                return null;

            // Land tiles must be < 0x4000
            if (tileId >= STATIC_OFFSET)
            {
                System.Diagnostics.Debug.WriteLine($"TecmoArtReader: TileID {tileId:X} is too high for land tiles (max is 0x3FFF)");
                return null;
            }

            try
            {
                var archive = GetOrCreateArchive(mulFolder);
                if (archive == null)
                    return null;

                return archive.ReadLandTile(tileId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TecmoArtReader: EXCEPTION loading land tile 0x{tileId:X}: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get all valid static item IDs from the art index (fast - doesn't load images).
        /// Returns IDs where art data exists (offset >= 0 and length > 0).
        /// </summary>
        public static List<int> GetValidStaticItemIds(string mulFolder)
        {
            var validIds = new List<int>();
            if (string.IsNullOrEmpty(mulFolder))
                return validIds;

            try
            {
                var archive = GetOrCreateArchive(mulFolder);
                if (archive == null)
                    return validIds;

                return archive.GetValidStaticItemIds();
            }
            catch
            {
                return validIds;
            }
        }

        /// <summary>
        /// Get all valid land tile IDs from the art index (fast - doesn't load images).
        /// </summary>
        public static List<int> GetValidLandTileIds(string mulFolder)
        {
            var validIds = new List<int>();
            if (string.IsNullOrEmpty(mulFolder))
                return validIds;

            try
            {
                var archive = GetOrCreateArchive(mulFolder);
                if (archive == null)
                    return validIds;

                return archive.GetValidLandTileIds();
            }
            catch
            {
                return validIds;
            }
        }

        /// <summary>
        /// Try to get the dimensions of a static item without loading the full image
        /// </summary>
        public static bool TryGetDimensions(string mulFolder, int itemId, out int width, out int height)
        {
            width = 0;
            height = 0;

            try
            {
                var archive = GetOrCreateArchive(mulFolder);
                if (archive == null)
                    return false;

                return archive.TryGetDimensions(itemId, out width, out height);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Detects if a folder contains Tecmo Expanded Art format files
        /// </summary>
        public static bool IsTecmoFormat(string mulFolder)
        {
            string artIdxPath = Path.Combine(mulFolder, "artidx.mul");
            string artMulPath = Path.Combine(mulFolder, "art.mul");

            if (!File.Exists(artIdxPath) || !File.Exists(artMulPath))
                return false;

            try
            {
                using (var idxStream = File.OpenRead(artIdxPath))
                {
                    long count = idxStream.Length / 12;
                    // Tecmo format has 0x40000 (262144) entries
                    return count >= MAX_ART_ENTRIES;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Releases any cached open art.mul streams so writers can replace files.
        /// </summary>
        public static void ClearCache()
        {
            lock (archivesLock)
            {
                foreach (var archive in archives.Values)
                {
                    archive.Dispose();
                }
                archives.Clear();
            }
        }

        private static TecmoArtArchive GetOrCreateArchive(string mulFolder)
        {
            lock (archivesLock)
            {
                if (archives.TryGetValue(mulFolder, out var existing))
                    return existing;

                string artIdxPath = Path.Combine(mulFolder, "artidx.mul");
                string artMulPath = Path.Combine(mulFolder, "art.mul");

                if (!File.Exists(artIdxPath) || !File.Exists(artMulPath))
                {
                    System.Diagnostics.Debug.WriteLine($"TecmoArtReader: art.mul or artidx.mul not found in {mulFolder}");
                    return null;
                }

                try
                {
                    var archive = new TecmoArtArchive(artIdxPath, artMulPath);
                    archives[mulFolder] = archive;
                    return archive;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"TecmoArtReader: Failed to create archive: {ex.Message}");
                    return null;
                }
            }
        }

        /// <summary>
        /// Represents an opened Tecmo art.mul + artidx.mul pair with cached index entries
        /// </summary>
        private class TecmoArtArchive : IDisposable
        {
            private struct IndexEntry
            {
                public int Offset;
                public int Length;
                public int Extra;
            }

            private readonly IndexEntry[] indexEntries;
            private readonly FileStream mulStream;
            private readonly object streamLock = new object();

            public TecmoArtArchive(string idxPath, string mulPath)
            {
                // Read entire index into memory
                using (var idxStream = File.OpenRead(idxPath))
                using (var idxReader = new BinaryReader(idxStream))
                {
                    long count = idxStream.Length / 12;
                    if (count <= 0)
                        throw new InvalidDataException("artidx.mul contains no entries");

                    if (count > MAX_ART_ENTRIES)
                        count = MAX_ART_ENTRIES;

                    indexEntries = new IndexEntry[count];

                    idxStream.Seek(0, SeekOrigin.Begin);
                    for (long i = 0; i < count; i++)
                    {
                        int offset = idxReader.ReadInt32();
                        int length = idxReader.ReadInt32();
                        int extra = idxReader.ReadInt32();

                        indexEntries[i].Offset = offset;
                        indexEntries[i].Length = length;
                        indexEntries[i].Extra = extra;
                    }
                }

                // Open mul stream once for reuse. Allow sharing read access.
                mulStream = new FileStream(mulPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            }

            public Bitmap ReadItem(int itemId)
            {
                int index = itemId + STATIC_OFFSET;
                if (index < 0 || index >= indexEntries.Length)
                {
                    System.Diagnostics.Debug.WriteLine($"TecmoArtReader: Index {index} out of bounds for item 0x{itemId:X}");
                    return null;
                }

                var entry = indexEntries[index];
                if (entry.Offset < 0 || entry.Length <= 0 || entry.Offset >= mulStream.Length)
                {
                    System.Diagnostics.Debug.WriteLine($"TecmoArtReader: Invalid entry for item 0x{itemId:X} (offset={entry.Offset}, length={entry.Length})");
                    return null;
                }

                lock (streamLock)
                {
                    byte[] artData = new byte[entry.Length];
                    mulStream.Seek(entry.Offset, SeekOrigin.Begin);
                    mulStream.Read(artData, 0, entry.Length);

                    uint header = BitConverter.ToUInt32(artData, 0);
                    ushort width = BitConverter.ToUInt16(artData, 4);
                    ushort height = BitConverter.ToUInt16(artData, 6);

                    if (width == 0 || height == 0 || width > 1024 || height > 1024)
                    {
                        System.Diagnostics.Debug.WriteLine($"TecmoArtReader: Invalid dimensions for item 0x{itemId:X}: {width}x{height}");
                        return null;
                    }

                    // Try 16-bit lookup first, fallback to 32-bit (matches reference Art.cs)
                    Bitmap bmp = DecodeStaticLookup16(artData, width, height);
                    if (bmp != null)
                        return bmp;

                    bmp = DecodeStaticLookup32(artData, width, height);
                    return bmp;
                }
            }

            private static unsafe Bitmap DecodeStaticLookup16(byte[] data, int width, int height)
            {
                int rowDataStartWords = (8 + (height * 2)) / 2;
                int totalWords = data.Length / 2;

                if (rowDataStartWords >= totalWords)
                    return null;

                fixed (byte* pData = data)
                {
                    ushort* binData16 = (ushort*)pData;
                    ushort* lookup16 = (ushort*)(pData + 8);

                    if (lookup16[0] != 0)
                        return null;

                    Bitmap bmp = new Bitmap(width, height, PixelFormat.Format16bppArgb1555);
                    BitmapData bd = bmp.LockBits(
                        new Rectangle(0, 0, width, height),
                        ImageLockMode.WriteOnly,
                        PixelFormat.Format16bppArgb1555);

                    try
                    {
                        ushort* line = (ushort*)bd.Scan0;
                        int delta = bd.Stride >> 1;

                        for (int y = 0; y < height; ++y, line += delta)
                        {
                            int count = rowDataStartWords + lookup16[y];

                            if (count < rowDataStartWords || count >= totalWords)
                            {
                                bmp.UnlockBits(bd);
                                bmp.Dispose();
                                return null;
                            }

                            ushort* cur = line;
                            int x = 0;

                            while (true)
                            {
                                if (count + 1 >= totalWords)
                                {
                                    bmp.UnlockBits(bd);
                                    bmp.Dispose();
                                    return null;
                                }

                                int xOffset = binData16[count++];
                                int xRun = binData16[count++];

                                if (xOffset == 0 && xRun == 0)
                                    break;

                                x += xOffset;

                                if (x < 0 || x + xRun > width)
                                {
                                    bmp.UnlockBits(bd);
                                    bmp.Dispose();
                                    return null;
                                }

                                if (count + xRun > totalWords)
                                {
                                    bmp.UnlockBits(bd);
                                    bmp.Dispose();
                                    return null;
                                }

                                ushort* dest = line + x;

                                for (int i = 0; i < xRun; ++i)
                                {
                                    dest[i] = (ushort)(binData16[count++] ^ 0x8000);
                                }

                                x += xRun;
                            }
                        }
                    }
                    catch
                    {
                        bmp.UnlockBits(bd);
                        bmp.Dispose();
                        return null;
                    }

                    bmp.UnlockBits(bd);
                    return bmp;
                }
            }

            private static unsafe Bitmap DecodeStaticLookup32(byte[] data, int width, int height)
            {
                int rowDataStartWords = (8 + (height * 4)) / 2;
                int totalWords = data.Length / 2;

                if (rowDataStartWords >= totalWords)
                    return null;

                fixed (byte* pData = data)
                {
                    ushort* binData16 = (ushort*)pData;
                    uint* lookup32 = (uint*)(pData + 8);

                    if (lookup32[0] != 0)
                        return null;

                    Bitmap bmp = new Bitmap(width, height, PixelFormat.Format16bppArgb1555);
                    BitmapData bd = bmp.LockBits(
                        new Rectangle(0, 0, width, height),
                        ImageLockMode.WriteOnly,
                        PixelFormat.Format16bppArgb1555);

                    try
                    {
                        ushort* line = (ushort*)bd.Scan0;
                        int delta = bd.Stride >> 1;

                        for (int y = 0; y < height; ++y, line += delta)
                        {
                            ulong count64 = (ulong)rowDataStartWords + lookup32[y];

                            if (count64 >= (ulong)totalWords)
                            {
                                bmp.UnlockBits(bd);
                                bmp.Dispose();
                                return null;
                            }

                            int count = (int)count64;
                            int x = 0;

                            while (true)
                            {
                                if (count + 1 >= totalWords)
                                {
                                    bmp.UnlockBits(bd);
                                    bmp.Dispose();
                                    return null;
                                }

                                int xOffset = binData16[count++];
                                int xRun = binData16[count++];

                                if (xOffset == 0 && xRun == 0)
                                    break;

                                x += xOffset;

                                if (x < 0 || x + xRun > width)
                                {
                                    bmp.UnlockBits(bd);
                                    bmp.Dispose();
                                    return null;
                                }

                                if (count + xRun > totalWords)
                                {
                                    bmp.UnlockBits(bd);
                                    bmp.Dispose();
                                    return null;
                                }

                                ushort* dest = line + x;

                                for (int i = 0; i < xRun; ++i)
                                {
                                    dest[i] = (ushort)(binData16[count++] ^ 0x8000);
                                }

                                x += xRun;
                            }
                        }
                    }
                    catch
                    {
                        bmp.UnlockBits(bd);
                        bmp.Dispose();
                        return null;
                    }

                    bmp.UnlockBits(bd);
                    return bmp;
                }
            }

            private static Bitmap DecodeStaticFromBytes(byte[] pixelBytes, int width, int height)
            {
                var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                var rect = new Rectangle(0, 0, width, height);
                var data = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

                try
                {
                    Marshal.Copy(pixelBytes, 0, data.Scan0, pixelBytes.Length);
                }
                finally
                {
                    bmp.UnlockBits(data);
                }

                return bmp;
            }

            public Bitmap ReadLandTile(int tileId)
            {
                if (tileId < 0 || tileId >= STATIC_OFFSET || tileId >= indexEntries.Length)
                {
                    System.Diagnostics.Debug.WriteLine($"TecmoArtReader: TileID {tileId:X} out of bounds for land tile");
                    return null;
                }

                var entry = indexEntries[tileId];
                if (entry.Offset < 0 || entry.Length <= 0 || entry.Offset >= mulStream.Length)
                {
                    System.Diagnostics.Debug.WriteLine($"TecmoArtReader: Invalid entry for land tile 0x{tileId:X}");
                    return null;
                }

                lock (streamLock)
                {
                    mulStream.Seek(entry.Offset, SeekOrigin.Begin);
                    using (var mulReader = new BinaryReader(mulStream, System.Text.Encoding.Default, leaveOpen: true))
                    {
                        uint header = mulReader.ReadUInt32();

                        const int LAND_TILE_SIZE = 44;
                        var bmp = new Bitmap(LAND_TILE_SIZE, LAND_TILE_SIZE, PixelFormat.Format32bppArgb);

                        // Read diamond pattern (same as standard format)
                        for (int y = 0; y < LAND_TILE_SIZE; y++)
                        {
                            int pixelsInRow;
                            if (y < 22)
                                pixelsInRow = 2 + (y * 2); // Growing: 2, 4, 6, ..., 44
                            else
                                pixelsInRow = 2 + ((43 - y) * 2); // Shrinking: 42, 40, ..., 2

                            int startX = (LAND_TILE_SIZE - pixelsInRow) / 2;

                            for (int x = 0; x < pixelsInRow; x++)
                            {
                                if (mulStream.Position + 2 > mulStream.Length)
                                    break;

                                ushort color16 = mulReader.ReadUInt16();

                                if (color16 != 0)
                                {
                                    int r = ((color16 >> 10) & 0x1F) * 255 / 31;
                                    int g = ((color16 >> 5) & 0x1F) * 255 / 31;
                                    int b = (color16 & 0x1F) * 255 / 31;
                                    bmp.SetPixel(startX + x, y, Color.FromArgb(255, r, g, b));
                                }
                            }
                        }

                        return bmp;
                    }
                }
            }

            public bool TryGetDimensions(int itemId, out int width, out int height)
            {
                width = 0;
                height = 0;

                int index = itemId + STATIC_OFFSET;
                if (index < 0 || index >= indexEntries.Length)
                    return false;

                var entry = indexEntries[index];
                if (entry.Offset < 0 || entry.Length <= 0 || entry.Offset >= mulStream.Length)
                    return false;

                lock (streamLock)
                {
                    mulStream.Seek(entry.Offset, SeekOrigin.Begin);
                    using (var mulReader = new BinaryReader(mulStream, System.Text.Encoding.Default, leaveOpen: true))
                    {
                        mulReader.ReadUInt32(); // header
                        width = mulReader.ReadUInt16();
                        height = mulReader.ReadUInt16();
                        return width > 0 && height > 0 && width <= 1024 && height <= 1024;
                    }
                }
            }

            public List<int> GetValidLandTileIds()
            {
                var validIds = new List<int>();
                int limit = Math.Min(indexEntries.Length, STATIC_OFFSET);
                for (int i = 0; i < limit; i++)
                {
                    var entry = indexEntries[i];
                    if (entry.Offset >= 0 && entry.Length > 0 && entry.Offset < mulStream.Length)
                    {
                        validIds.Add(i);
                    }
                }
                return validIds;
            }

            public List<int> GetValidStaticItemIds()
            {
                var validIds = new List<int>();
                for (int i = STATIC_OFFSET; i < indexEntries.Length; i++)
                {
                    var entry = indexEntries[i];
                    if (entry.Offset >= 0 && entry.Length > 0 && entry.Offset < mulStream.Length)
                    {
                        validIds.Add(i - STATIC_OFFSET);
                    }
                }
                return validIds;
            }

            public void Dispose()
            {
                mulStream?.Dispose();
            }
        }
    }
}