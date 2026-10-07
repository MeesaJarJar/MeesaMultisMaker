using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace MeesaMultisMaker.Mul
{
    /// <summary>
    /// Reads GUMP art from gumpart.mul and gumpidx.mul (or falls back to UOP format)
    /// 
    /// GUMP format in gumpart.mul:
    /// - Data is stored in a simple run-length encoded format
    /// - Each row consists of chunks: [colorValue:ushort][runLength:ushort]
    /// - colorValue is RGB555 format (or 0 for transparent)
    /// - The index file stores the width and height in the "extra" field
    /// </summary>
    public static class GumpArtReader
    {
        // Cache of opened gump archives per folder
        private static readonly Dictionary<string, GumpArchive> archives = new Dictionary<string, GumpArchive>(StringComparer.OrdinalIgnoreCase);
        private static readonly object archivesLock = new object();

        // Track which folders use UOP format
        private static readonly HashSet<string> uopFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Load a GUMP image from gumpart.mul or gumpartLegacyMUL.uop
        /// </summary>
        /// <param name="mulFolder">Path to folder containing gump files</param>
        /// <param name="gumpId">GUMP ID to load</param>
        /// <returns>Bitmap of the GUMP, or null if not found</returns>
        public static Bitmap LoadGumpArt(string mulFolder, int gumpId)
        {
            if (string.IsNullOrEmpty(mulFolder))
                return null;

            try
            {
                // Check if this folder uses UOP format
                lock (archivesLock)
                {
                    if (uopFolders.Contains(mulFolder))
                    {
                        return UopGumpReader.LoadGumpArt(mulFolder, gumpId);
                    }
                }

                var archive = GetOrCreateArchive(mulFolder);
                if (archive == null)
                {
                    // MUL files not found, try UOP
                    if (UopGumpReader.UopFileExists(mulFolder))
                    {
                        lock (archivesLock)
                        {
                            uopFolders.Add(mulFolder);
                        }
                        return UopGumpReader.LoadGumpArt(mulFolder, gumpId);
                    }
                    return null;
                }

                // Try MUL archive first
                var result = archive.ReadGump(gumpId);
                
                // If MUL failed and UOP exists, mark folder as UOP and try that
                if (result == null && UopGumpReader.UopFileExists(mulFolder))
                {
                    lock (archivesLock)
                    {
                        uopFolders.Add(mulFolder);
                    }
                    return UopGumpReader.LoadGumpArt(mulFolder, gumpId);
                }
                
                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GumpArtReader: EXCEPTION loading gump 0x{gumpId:X4}: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get list of all valid GUMP IDs in the archive
        /// </summary>
        public static List<int> GetValidGumpIds(string mulFolder)
        {
            var result = new List<int>();

            try
            {
                System.Diagnostics.Debug.WriteLine($"GumpArtReader.GetValidGumpIds: Checking folder {mulFolder}");
                
                // Check if this folder uses UOP format
                lock (archivesLock)
                {
                    if (uopFolders.Contains(mulFolder))
                    {
                        System.Diagnostics.Debug.WriteLine($"GumpArtReader: Folder already marked as UOP, using UopGumpReader");
                        return UopGumpReader.GetValidGumpIds(mulFolder);
                    }
                }

                var archive = GetOrCreateArchive(mulFolder);
                if (archive == null)
                {
                    System.Diagnostics.Debug.WriteLine($"GumpArtReader: No MUL archive found, checking for UOP...");
                    
                    // MUL files not found, try UOP
                    if (UopGumpReader.UopFileExists(mulFolder))
                    {
                        lock (archivesLock)
                        {
                            uopFolders.Add(mulFolder);
                        }
                        System.Diagnostics.Debug.WriteLine($"GumpArtReader: Using UOP format for {mulFolder}");
                        return UopGumpReader.GetValidGumpIds(mulFolder);
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"GumpArtReader: No UOP file found either");
                    return result;
                }

                System.Diagnostics.Debug.WriteLine($"GumpArtReader: Using MUL archive");
                result = archive.GetValidGumpIds();
                
                // If MUL returned no results and UOP exists, try UOP
                if (result.Count == 0 && UopGumpReader.UopFileExists(mulFolder))
                {
                    System.Diagnostics.Debug.WriteLine($"GumpArtReader: MUL returned 0 gumps, trying UOP...");
                    lock (archivesLock)
                    {
                        uopFolders.Add(mulFolder);
                    }
                    return UopGumpReader.GetValidGumpIds(mulFolder);
                }
                
                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GumpArtReader: Exception: {ex.Message}");
                return result;
            }
        }

        /// <summary>
        /// Try to get the dimensions of a GUMP without loading the full image
        /// </summary>
        public static bool TryGetDimensions(string mulFolder, int gumpId, out int width, out int height)
        {
            width = 0;
            height = 0;

            try
            {
                // For UOP, try the cached Extra field
                lock (archivesLock)
                {
                    if (uopFolders.Contains(mulFolder))
                    {
                        return UopGumpReader.TryGetDimensions(mulFolder, gumpId, out width, out height);
                    }
                }

                var archive = GetOrCreateArchive(mulFolder);
                if (archive == null)
                    return false;

                return archive.TryGetDimensions(gumpId, out width, out height);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Clear the archive cache (useful when files change)
        /// </summary>
        public static void ClearCache()
        {
            lock (archivesLock)
            {
                archives.Clear();
                uopFolders.Clear();
            }
            UopGumpReader.ClearCache();
        }

        /// <summary>
        /// Mark a folder as using UOP format for gumps
        /// </summary>
        public static void MarkAsUopFolder(string mulFolder)
        {
            lock (archivesLock)
            {
                if (!uopFolders.Contains(mulFolder))
                {
                    uopFolders.Add(mulFolder);
                    System.Diagnostics.Debug.WriteLine($"GumpArtReader: Marked {mulFolder} as UOP folder");
                }
            }
        }

        private static GumpArchive GetOrCreateArchive(string mulFolder)
        {
            lock (archivesLock)
            {
                if (archives.TryGetValue(mulFolder, out var existing))
                    return existing;

                string gumpIdxPath = Path.Combine(mulFolder, "gumpidx.mul");
                string gumpMulPath = Path.Combine(mulFolder, "gumpart.mul");

                if (!File.Exists(gumpIdxPath) || !File.Exists(gumpMulPath))
                {
                    System.Diagnostics.Debug.WriteLine($"GumpArtReader: gumpart.mul or gumpidx.mul not found in {mulFolder}");
                    return null;
                }

                try
                {
                    var archive = new GumpArchive(gumpIdxPath, gumpMulPath);
                    archives[mulFolder] = archive;
                    return archive;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"GumpArtReader: Failed to create archive: {ex.Message}");
                    return null;
                }
            }
        }

        /// <summary>
        /// Represents an opened gumpart.mul + gumpidx.mul pair with cached index entries
        /// </summary>
        private class GumpArchive
        {
            private struct IndexEntry
            {
                public int Offset;
                public int Length;
                public int Extra; // Contains width (high word) and height (low word)
            }

            private readonly IndexEntry[] indexEntries;
            private readonly FileStream mulStream;
            private readonly object streamLock = new object();

            public GumpArchive(string idxPath, string mulPath)
            {
                // Read entire index into memory
                using (var idxStream = File.OpenRead(idxPath))
                using (var idxReader = new BinaryReader(idxStream))
                {
                    long count = idxStream.Length / 12;
                    if (count <= 0)
                        throw new InvalidDataException("gumpidx.mul contains no entries");

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

                // Open mul stream once for reuse
                mulStream = new FileStream(mulPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            }

            public List<int> GetValidGumpIds()
            {
                var result = new List<int>();
                for (int i = 0; i < indexEntries.Length; i++)
                {
                    var entry = indexEntries[i];
                    // In UO MUL files, offset of -1 (0xFFFFFFFF) means invalid/empty entry
                    // Also check that length is positive and extra (dimensions) is non-zero
                    if (entry.Offset != -1 && entry.Offset >= 0 && entry.Length > 0 && entry.Extra != 0)
                    {
                        // Validate dimensions from Extra field
                        int width = (entry.Extra >> 16) & 0xFFFF;
                        int height = entry.Extra & 0xFFFF;

                        if (width > 0 && height > 0 && width <= 4096 && height <= 4096)
                        {
                            result.Add(i);
                        }
                    }
                }
                return result;
            }

            public bool TryGetDimensions(int gumpId, out int width, out int height)
            {
                width = 0;
                height = 0;

                if (gumpId < 0 || gumpId >= indexEntries.Length)
                    return false;

                var entry = indexEntries[gumpId];
                if (entry.Offset < 0 || entry.Length <= 0)
                    return false;

                // Extract width and height from the Extra field
                // Width is in the high word, Height is in the low word
                width = (entry.Extra >> 16) & 0xFFFF;
                height = entry.Extra & 0xFFFF;

                return width > 0 && height > 0 && width <= 4096 && height <= 4096;
            }

            public Bitmap ReadGump(int gumpId)
            {
                if (gumpId < 0 || gumpId >= indexEntries.Length)
                {
                    System.Diagnostics.Debug.WriteLine($"GumpArtReader: Index {gumpId} out of bounds");
                    return null;
                }

                var entry = indexEntries[gumpId];
                if (entry.Offset < 0 || entry.Length <= 0 || entry.Offset >= mulStream.Length)
                {
                    System.Diagnostics.Debug.WriteLine($"GumpArtReader: Invalid entry for gump 0x{gumpId:X4} (offset={entry.Offset}, length={entry.Length})");
                    return null;
                }

                // Extract width and height from Extra field
                int width = (entry.Extra >> 16) & 0xFFFF;
                int height = entry.Extra & 0xFFFF;

                if (width <= 0 || height <= 0 || width > 4096 || height > 4096)
                {
                    System.Diagnostics.Debug.WriteLine($"GumpArtReader: Invalid dimensions for gump 0x{gumpId:X4}: {width}x{height}");
                    return null;
                }

                lock (streamLock)
                {
                    mulStream.Seek(entry.Offset, SeekOrigin.Begin);
                    using (var mulReader = new BinaryReader(mulStream, System.Text.Encoding.Default, leaveOpen: true))
                    {
                        // Read the lookup table (one entry per row - offsets are relative to the pixel data start)
                        int[] lookupTable = new int[height];
                        for (int i = 0; i < height; i++)
                        {
                            lookupTable[i] = mulReader.ReadInt32();
                        }

                        long dataStart = entry.Offset + (height * 4); // position after lookup table

                        // Prepare byte buffer for the entire image (BGRA format)
                        int pixelBytesLength = width * height * 4;
                        var pixelBytes = new byte[pixelBytesLength];

                        // Decode each row
                        for (int y = 0; y < height; y++)
                        {
                            long rowStart = entry.Offset + (lookupTable[y] * 4L); // offsets are DWORDs from start of entry
                            if (rowStart < entry.Offset || rowStart >= entry.Offset + entry.Length)
                                continue;

                            mulStream.Seek(rowStart, SeekOrigin.Begin);

                            int x = 0;
                            while (x < width)
                            {
                                if (mulStream.Position >= entry.Offset + entry.Length)
                                    break;

                                ushort color16 = mulReader.ReadUInt16();
                                ushort runLength = mulReader.ReadUInt16();

                                if (runLength == 0)
                                    break;

                                // Convert RGB555 to BGRA
                                int r, g, b;
                                if (color16 == 0)
                                {
                                    // Transparent pixel
                                    for (int i = 0; i < runLength && x < width; i++, x++)
                                    {
                                        int pixelIndex = (y * width + x) * 4;
                                        pixelBytes[pixelIndex + 0] = 0; // B
                                        pixelBytes[pixelIndex + 1] = 0; // G
                                        pixelBytes[pixelIndex + 2] = 0; // R
                                        pixelBytes[pixelIndex + 3] = 0; // A
                                    }
                                }
                                else
                                {
                                    // Color 16 is ARGB1555: A (bit 15), R (bits 14-10), G (bits 9-5), B (bits 4-0)
                                    r = ((color16 >> 10) & 0x1F) * 255 / 31;
                                    g = ((color16 >> 5) & 0x1F) * 255 / 31;
                                    b = (color16 & 0x1F) * 255 / 31;

                                    for (int i = 0; i < runLength && x < width; i++, x++)
                                    {
                                        int pixelIndex = (y * width + x) * 4;
                                        pixelBytes[pixelIndex + 0] = (byte)b;
                                        pixelBytes[pixelIndex + 1] = (byte)g;
                                        pixelBytes[pixelIndex + 2] = (byte)r;
                                        pixelBytes[pixelIndex + 3] = 255;
                                    }
                                }
                            }
                        }

                        // Create bitmap and copy bytes
                        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                        var rect = new Rectangle(0, 0, width, height);
                        var data = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

                        try
                        {
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
        }
    }
}
