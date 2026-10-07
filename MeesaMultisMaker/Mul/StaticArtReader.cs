using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace MeesaMultisMaker.Mul
{
    /// <summary>
    /// Reads static item art from art.mul and artidx.mul
    /// Static items start at index 0x4000 in the art files (land tiles are 0x0000-0x3FFF)
    /// 
    /// Static item format in art.mul:
    /// - uint32 header (usually 0x00000000)
    /// - ushort width
    /// - ushort height
    /// - ushort[] lookupTable (height entries - offset to each row)
    /// - ushort[] pixelData (run-length encoded rows)
    /// 
    /// Run-length encoding per row:
    /// - ushort xOffset (pixels from left edge)
    /// - ushort runLength (number of pixels)
    /// - ushort[] colors (runLength pixels in RGB555 format)
    /// - Repeat until runLength is 0
    /// </summary>
    public static class StaticArtReader
    {
        private const int STATIC_OFFSET = 0x4000; // Static items start at index 0x4000

        // Cache of opened art archives per folder to avoid reopening files repeatedly
        private static readonly Dictionary<string, ArtArchive> archives = new Dictionary<string, ArtArchive>(StringComparer.OrdinalIgnoreCase);
        private static readonly object archivesLock = new object();

        /// <summary>
        /// Load a static item image from art.mul
        /// This optimized implementation caches the index and keeps the art.mul stream open,
        /// and uses LockBits+Marshal.Copy for much faster pixel writes.
        /// </summary>
        /// <param name="mulFolder">Path to folder containing art.mul and artidx.mul</param>
        /// <param name="itemId">Item ID (will be offset by 0x4000 internally)</param>
        /// <returns>Bitmap of the item, or null if not found</returns>
        public static Bitmap LoadStaticArt(string mulFolder, ushort itemId)
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
                System.Diagnostics.Debug.WriteLine($"StaticArtReader: ? EXCEPTION loading item 0x{itemId:X4}: {ex.GetType().Name}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"  Stack: {ex.StackTrace}");
                return null;
            }
        }

        /// <summary>
        /// Get all valid static item IDs from the art index (fast - doesn't load images).
        /// Returns IDs where art data exists (offset >= 0 and length > 0).
        /// </summary>
        public static List<ushort> GetValidStaticItemIds(string mulFolder)
        {
            var validIds = new List<ushort>();
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
        /// Land tiles are stored at index 0x0000-0x3FFF.
        /// Returns IDs where art data exists (offset >= 0 and length > 0).
        /// </summary>
        public static List<ushort> GetValidLandTileIds(string mulFolder)
        {
            var validIds = new List<ushort>();
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

        /// <summary>
        /// Try to get the dimensions of a static item without loading the full image
        /// Uses the cached archive when available
        /// </summary>
        public static bool TryGetDimensions(string mulFolder, ushort itemId, out int width, out int height)
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

        private static ArtArchive GetOrCreateArchive(string mulFolder)
        {
            lock (archivesLock)
            {
                if (archives.TryGetValue(mulFolder, out var existing))
                    return existing;

                string artIdxPath = Path.Combine(mulFolder, "artidx.mul");
                string artMulPath = Path.Combine(mulFolder, "art.mul");

                if (!File.Exists(artIdxPath) || !File.Exists(artMulPath))
                {
                    System.Diagnostics.Debug.WriteLine($"StaticArtReader: art.mul or artidx.mul not found in {mulFolder}");
                    return null;
                }

                try
                {
                    var archive = new ArtArchive(artIdxPath, artMulPath);
                    archives[mulFolder] = archive;
                    return archive;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"StaticArtReader: Failed to create archive: {ex.Message}");
                    return null;
                }
            }
        }

        /// <summary>
        /// Converts UO's RGB555 format to a 32-bit ARGB packed int (0xAARRGGBB)
        /// </summary>
        private static int ConvertRgb555ToIntArgb(ushort color16)
        {
            if (color16 == 0)
                return 0; // transparent -> all zero

            int r = ((color16 >> 10) & 0x1F) * 255 / 31;
            int g = ((color16 >> 5) & 0x1F) * 255 / 31;
            int b = (color16 & 0x1F) * 255 / 31;

            // Format expected for Marshal.Copy into Format32bppArgb is BGRA order in memory (little-endian),
            // but since we'll build byte[] as B,G,R,A we return ARGB int for convenience not used directly here.
            return (255 << 24) | (r << 16) | (g << 8) | b;
        }

        /// <summary>
        /// Represents an opened art.mul + artidx.mul pair with cached index entries
        /// </summary>
        private class ArtArchive : IDisposable
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

            public ArtArchive(string idxPath, string mulPath)
            {
                // Read entire index into memory
                using (var idxStream = File.OpenRead(idxPath))
                using (var idxReader = new BinaryReader(idxStream))
                {
                    long count = idxStream.Length / 12;
                    if (count <= 0)
                        throw new InvalidDataException("artidx.mul contains no entries");

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

            public Bitmap ReadItem(ushort itemId)
            {
                int index = itemId + STATIC_OFFSET;
                if (index < 0 || index >= indexEntries.Length)
                {
                    System.Diagnostics.Debug.WriteLine($"StaticArtReader: Index {index} out of bounds for item 0x{itemId:X4}");
                    return null;
                }

                var entry = indexEntries[index];
                if (entry.Offset < 0 || entry.Length <= 0 || entry.Offset >= mulStream.Length)
                {
                    System.Diagnostics.Debug.WriteLine($"StaticArtReader: Invalid entry for item 0x{itemId:X4} (offset={entry.Offset}, length={entry.Length})");
                    return null;
                }

                lock (streamLock)
                {
                    mulStream.Seek(entry.Offset, SeekOrigin.Begin);
                    using (var mulReader = new BinaryReader(mulStream, System.Text.Encoding.Default, leaveOpen: true))
                    {
                        uint header = mulReader.ReadUInt32();
                        ushort width = mulReader.ReadUInt16();
                        ushort height = mulReader.ReadUInt16();

                        if (width == 0 || height == 0 || width > 1024 || height > 1024)
                        {
                            System.Diagnostics.Debug.WriteLine($"StaticArtReader: Invalid dimensions for item 0x{itemId:X4}: {width}x{height}");
                            return null;
                        }

                        // Read lookup table (one entry per row, relative to start of pixel data)
                        ushort[] lookupTable = new ushort[height];
                        for (int i = 0; i < height; i++)
                        {
                            lookupTable[i] = mulReader.ReadUInt16();
                        }

                        long pixelDataStart = mulStream.Position;

                        // Prepare byte buffer for entire image (B,G,R,A per pixel)
                        int pixelBytesLength = width * height * 4;
                        var pixelBytes = new byte[pixelBytesLength];

                        // Decode each row using RLE into the byte buffer
                        for (int y = 0; y < height; y++)
                        {
                            long rowPos = pixelDataStart + lookupTable[y] * 2L;
                            if (rowPos < 0 || rowPos >= mulStream.Length)
                                continue;

                            mulStream.Seek(rowPos, SeekOrigin.Begin);

                            int x = 0;

                            while (x < width)
                            {
                                if (mulStream.Position + 4 > mulStream.Length)
                                    break;

                                ushort xOffset = mulReader.ReadUInt16();
                                ushort runLength = mulReader.ReadUInt16();

                                if (runLength == 0)
                                    break;

                                x += xOffset;
                                if (x >= width)
                                    break;

                                for (int i = 0; i < runLength && x < width; i++)
                                {
                                    if (mulStream.Position + 2 > mulStream.Length)
                                        break;

                                    ushort color16 = mulReader.ReadUInt16();

                                    if (color16 == 0)
                                    {
                                        // transparent - leave pixels as zero
                                        x++;
                                        continue;
                                    }

                                    int r = ((color16 >> 10) & 0x1F) * 255 / 31;
                                    int g = ((color16 >> 5) & 0x1F) * 255 / 31;
                                    int b = (color16 & 0x1F) * 255 / 31;

                                    int pixelIndex = (y * width + x) * 4;
                                    // BGRA order for memory
                                    pixelBytes[pixelIndex + 0] = (byte)b;
                                    pixelBytes[pixelIndex + 1] = (byte)g;
                                    pixelBytes[pixelIndex + 2] = (byte)r;
                                    pixelBytes[pixelIndex + 3] = 255;

                                    x++;
                                }
                            }
                        }

                        // Create bitmap and copy bytes into it
                        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                        var rect = new Rectangle(0, 0, width, height);
                        var data = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

                        try
                        {
                            // Copy the entire buffer into the bitmap memory
                            Marshal.Copy(pixelBytes, 0, data.Scan0, pixelBytesLength);
                        }
                        finally
                        {
                            bmp.UnlockBits(data);
                        }

                        return bmp;
                    }
                }
            }

            public bool TryGetDimensions(ushort itemId, out int width, out int height)
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

            /// <summary>
            /// Get all valid land tile IDs from the art index.
            /// Land tiles are stored at index 0x0000-0x3FFF.
            /// </summary>
            public List<ushort> GetValidLandTileIds()
            {
                var validIds = new List<ushort>();
                int limit = Math.Min(indexEntries.Length, STATIC_OFFSET);
                for (int i = 0; i < limit; i++)
                {
                    var entry = indexEntries[i];
                    if (entry.Offset >= 0 && entry.Length > 0 && entry.Offset < mulStream.Length)
                    {
                        validIds.Add((ushort)i);
                    }
                }
                return validIds;
            }

            /// <summary>
            /// Get all valid static item IDs from the art index.
            /// Static items start at index 0x4000. Returns IDs where art data exists.
            /// </summary>
            public List<ushort> GetValidStaticItemIds()
            {
                var validIds = new List<ushort>();
                for (int i = STATIC_OFFSET; i < indexEntries.Length; i++)
                {
                    var entry = indexEntries[i];
                    if (entry.Offset >= 0 && entry.Length > 0 && entry.Offset < mulStream.Length)
                    {
                        validIds.Add((ushort)(i - STATIC_OFFSET));
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
