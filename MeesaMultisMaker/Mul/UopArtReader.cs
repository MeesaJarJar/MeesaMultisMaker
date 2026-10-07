using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace MeesaMultisMaker.Mul
{
    /// <summary>
    /// Reads static item art from artLegacyMUL.uop format (UOForever style)
    /// UOP files are self-contained with their own index - artidx.mul is NOT used
    /// </summary>
    public static class UopArtReader
    {
        private const string ART_LEGACY_UOP_FILENAME = "artLegacyMUL.uop";
        private const int STATIC_OFFSET = 0x4000;
        private const int LAND_TILE_SIZE = 44;
        private const string ART_HASH_FORMAT = "build/artlegacymul/{0:D8}.tga";

        // Cache the UOP index to avoid re-parsing
        private static Dictionary<int, UopEntry> _indexCache = null;
        private static string _cachedUopPath = null;

        private class UopEntry
        {
            public long Offset;
            public int CompressedSize;
            public int DecompressedSize;
            public short CompressionMethod;
        }

        /// <summary>
        /// Get all valid land tile IDs from the UOP index (fast - doesn't load images).
        /// Land tiles are stored at index 0x0000-0x3FFF.
        /// </summary>
        public static List<ushort> GetValidLandTileIds(string mulFolder)
        {
            var validIds = new List<ushort>();
            string uopPath = Path.Combine(mulFolder, ART_LEGACY_UOP_FILENAME);

            if (!File.Exists(uopPath))
                return validIds;

            // Build/refresh cache if needed
            if (_indexCache == null || _cachedUopPath != uopPath)
            {
                _indexCache = BuildUopIndex(uopPath);
                _cachedUopPath = uopPath;
            }

            // Extract valid land tile IDs from the index
            foreach (var kvp in _indexCache)
            {
                int index = kvp.Key;
                if (index < STATIC_OFFSET)
                {
                    validIds.Add((ushort)index);
                }
            }

            validIds.Sort();
            System.Diagnostics.Debug.WriteLine($"UopArtReader: Found {validIds.Count} valid land tile IDs");
            return validIds;
        }

        /// <summary>
        /// Get all valid static item IDs from the UOP index (fast - doesn't load images)
        /// </summary>
        public static List<ushort> GetValidStaticItemIds(string mulFolder)
        {
            var validIds = new List<ushort>();
            string uopPath = Path.Combine(mulFolder, ART_LEGACY_UOP_FILENAME);

            if (!File.Exists(uopPath))
                return validIds;

            // Build/refresh cache if needed
            if (_indexCache == null || _cachedUopPath != uopPath)
            {
                _indexCache = BuildUopIndex(uopPath);
                _cachedUopPath = uopPath;
            }

            // Extract valid static item IDs from the index
            // Static items are stored at index 0x4000 and above
            foreach (var kvp in _indexCache)
            {
                int index = kvp.Key;
                if (index >= STATIC_OFFSET)
                {
                    ushort itemId = (ushort)(index - STATIC_OFFSET);
                    validIds.Add(itemId);
                }
            }

            validIds.Sort();
            System.Diagnostics.Debug.WriteLine($"UopArtReader: Found {validIds.Count} valid static item IDs");
            return validIds;
        }

        /// <summary>
        /// Load a land tile texture from artLegacyMUL.uop (indices 0x0000-0x3FFF)
        /// </summary>
        public static Bitmap LoadLandTile(string mulFolder, ushort tileId)
        {
            // Land tiles are in indices 0x0000 to 0x3FFF
            if (tileId >= STATIC_OFFSET)
            {
                return null;
            }

            string uopPath = Path.Combine(mulFolder, ART_LEGACY_UOP_FILENAME);

            if (!File.Exists(uopPath))
            {
                return null;
            }

            // Build/refresh cache if needed
            if (_indexCache == null || _cachedUopPath != uopPath)
            {
                _indexCache = BuildUopIndex(uopPath);
                _cachedUopPath = uopPath;
            }

            // Land tiles use index directly (0 to 0x3FFF)
            int index = tileId;

            if (!_indexCache.TryGetValue(index, out var entry))
            {
                return null;
            }

            try
            {
                using (var stream = File.OpenRead(uopPath))
                using (var reader = new BinaryReader(stream))
                {
                    stream.Seek(entry.Offset, SeekOrigin.Begin);

                    byte[] data;
                    if (entry.CompressionMethod != 0 && entry.CompressedSize != entry.DecompressedSize)
                    {
                        // Data is compressed - decompress it
                        byte[] compressedData = reader.ReadBytes(entry.CompressedSize);
                        data = Decompress(compressedData, entry.DecompressedSize);
                    }
                    else
                    {
                        // Data is not compressed
                        data = reader.ReadBytes(entry.DecompressedSize > 0 ? entry.DecompressedSize : entry.CompressedSize);
                    }

                    if (data == null || data.Length < 4)
                        return null;

                    // Parse the land tile data
                    using (var dataStream = new MemoryStream(data))
                    using (var dataReader = new BinaryReader(dataStream))
                    {
                        // Land tiles are stored as 44x44 diamond pattern with run-length encoding
                        Bitmap bmp = new Bitmap(LAND_TILE_SIZE, LAND_TILE_SIZE, PixelFormat.Format32bppArgb);

                        // Read diamond pattern
                        for (int y = 0; y < LAND_TILE_SIZE; y++)
                        {
                            // Calculate how many pixels in this row
                            int pixelsInRow;
                            if (y < 22)
                                pixelsInRow = 2 + (y * 2); // Growing: 2, 4, 6, ..., 44
                            else
                                pixelsInRow = 2 + ((43 - y) * 2); // Shrinking: 42, 40, ..., 2

                            // Calculate starting X position (center the row)
                            int startX = (LAND_TILE_SIZE - pixelsInRow) / 2;

                            // Read pixels for this row
                            for (int x = 0; x < pixelsInRow; x++)
                            {
                                if (dataStream.Position + 2 > dataStream.Length)
                                    break;

                                ushort color16 = dataReader.ReadUInt16();

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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UopArtReader: Error loading land tile 0x{tileId:X4}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Load a static item image from artLegacyMUL.uop
        /// </summary>
        public static Bitmap LoadStaticArt(string mulFolder, ushort itemId)
        {
            string uopPath = Path.Combine(mulFolder, ART_LEGACY_UOP_FILENAME);

            if (!File.Exists(uopPath))
            {
                return null;
            }

            // Build/refresh cache if needed
            if (_indexCache == null || _cachedUopPath != uopPath)
            {
                _indexCache = BuildUopIndex(uopPath);
                _cachedUopPath = uopPath;
            }

            // Calculate index (static items start at 0x4000)
            int index = itemId + STATIC_OFFSET;

            if (!_indexCache.TryGetValue(index, out var entry))
            {
                return null;
            }

            try
            {
                using (var stream = File.OpenRead(uopPath))
                using (var reader = new BinaryReader(stream))
                {
                    stream.Seek(entry.Offset, SeekOrigin.Begin);

                    byte[] data;
                    if (entry.CompressionMethod != 0 && entry.CompressedSize != entry.DecompressedSize)
                    {
                        // Data is compressed - decompress it
                        byte[] compressedData = reader.ReadBytes(entry.CompressedSize);
                        data = Decompress(compressedData, entry.DecompressedSize);
                    }
                    else
                    {
                        // Data is not compressed
                        data = reader.ReadBytes(entry.DecompressedSize > 0 ? entry.DecompressedSize : entry.CompressedSize);
                    }

                    if (data == null || data.Length < 8)
                        return null;

                    // Parse the art data from the byte array
                    using (var dataStream = new MemoryStream(data))
                    using (var dataReader = new BinaryReader(dataStream))
                    {
                        // Static art format: 4-byte header (usually 0), then width/height
                        uint header = dataReader.ReadUInt32();
                        ushort width = dataReader.ReadUInt16();
                        ushort height = dataReader.ReadUInt16();

                        if (width == 0 || height == 0 || width > 1024 || height > 1024)
                        {
                            return null;
                        }

                        // Read lookup table
                        ushort[] lookupTable = new ushort[height];
                        for (int i = 0; i < height; i++)
                            lookupTable[i] = dataReader.ReadUInt16();

                        long pixelDataStart = dataStream.Position;

                        // Use fast pixel writing with LockBits
                        int pixelBytesLength = width * height * 4;
                        var pixelBytes = new byte[pixelBytesLength];

                        // Decode RLE pixel data
                        for (int y = 0; y < height; y++)
                        {
                            dataStream.Seek(pixelDataStart + lookupTable[y] * 2, SeekOrigin.Begin);
                            int x = 0;

                            while (x < width)
                            {
                                if (dataStream.Position + 4 > dataStream.Length)
                                    break;

                                ushort xOffset = dataReader.ReadUInt16();
                                ushort runLength = dataReader.ReadUInt16();

                                if (runLength == 0)
                                    break;

                                x += xOffset;
                                if (x >= width)
                                    break;

                                for (int i = 0; i < runLength && x < width; i++)
                                {
                                    if (dataStream.Position + 2 > dataStream.Length)
                                        break;

                                    ushort color16 = dataReader.ReadUInt16();

                                    if (color16 != 0)
                                    {
                                        int r = ((color16 >> 10) & 0x1F) * 255 / 31;
                                        int g = ((color16 >> 5) & 0x1F) * 255 / 31;
                                        int b = (color16 & 0x1F) * 255 / 31;

                                        int pixelIndex = (y * width + x) * 4;
                                        pixelBytes[pixelIndex + 0] = (byte)b;
                                        pixelBytes[pixelIndex + 1] = (byte)g;
                                        pixelBytes[pixelIndex + 2] = (byte)r;
                                        pixelBytes[pixelIndex + 3] = 255;
                                    }
                                    x++;
                                }
                            }
                        }

                        // Create bitmap and copy bytes
                        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                        var rect = new Rectangle(0, 0, width, height);
                        var bmpData = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                        try
                        {
                            Marshal.Copy(pixelBytes, 0, bmpData.Scan0, pixelBytesLength);
                        }
                        finally
                        {
                            bmp.UnlockBits(bmpData);
                        }

                        return bmp;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UopArtReader: Error loading 0x{itemId:X4}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Build index of all entries in the UOP file using hash lookup
        /// </summary>
        private static Dictionary<int, UopEntry> BuildUopIndex(string uopPath)
        {
            var index = new Dictionary<int, UopEntry>();

            // Pre-compute hashes for all possible art indices
            var hashToIndex = new Dictionary<ulong, int>();
            for (int i = 0; i < 0x20000; i++) // Cover land tiles + static items
            {
                string path = string.Format(ART_HASH_FORMAT, i);
                ulong hash = HashLittle2(path);
                hashToIndex[hash] = i;
            }

            System.Diagnostics.Debug.WriteLine($"UopArtReader: Building UOP index from {Path.GetFileName(uopPath)}...");

            using (var stream = File.OpenRead(uopPath))
            using (var reader = new BinaryReader(stream))
            {
                // Read UOP header
                uint signature = reader.ReadUInt32(); // "MYP\0"
                int version = reader.ReadInt32();
                uint timestamp = reader.ReadUInt32();
                long startAddress = reader.ReadInt64();
                uint blockSize = reader.ReadUInt32();
                int fileCount = reader.ReadInt32();

                System.Diagnostics.Debug.WriteLine($"UopArtReader: version={version}, fileCount={fileCount}");

                if (signature != 0x0050594D) // "MYP\0"
                {
                    System.Diagnostics.Debug.WriteLine($"UopArtReader: Invalid UOP signature");
                    return index;
                }

                // Read all blocks
                long nextBlock = startAddress;
                int blocksRead = 0;

                while (nextBlock != 0 && blocksRead < 10000)
                {
                    stream.Seek(nextBlock, SeekOrigin.Begin);

                    int filesInBlock = reader.ReadInt32();
                    long nextBlockAddress = reader.ReadInt64();

                    if (filesInBlock < 0 || filesInBlock > 1000)
                        break;

                    for (int i = 0; i < filesInBlock; i++)
                    {
                        long offset = reader.ReadInt64();
                        int headerSize = reader.ReadInt32();
                        int compressedSize = reader.ReadInt32();
                        int decompressedSize = reader.ReadInt32();
                        ulong hash = reader.ReadUInt64();
                        uint adler = reader.ReadUInt32();
                        short compressionMethod = reader.ReadInt16();

                        if (offset == 0 || compressedSize == 0)
                            continue;

                        // Look up the art index from the hash
                        if (hashToIndex.TryGetValue(hash, out int artIndex))
                        {
                            index[artIndex] = new UopEntry
                            {
                                Offset = offset + headerSize,
                                CompressedSize = compressedSize,
                                DecompressedSize = decompressedSize,
                                CompressionMethod = compressionMethod
                            };
                        }
                    }

                    nextBlock = nextBlockAddress;
                    blocksRead++;

                    if (nextBlock < 0 || nextBlock >= stream.Length)
                        break;
                }

                System.Diagnostics.Debug.WriteLine($"UopArtReader: Indexed {index.Count} entries from {blocksRead} blocks");
            }

            return index;
        }

        /// <summary>
        /// Decompress zlib data
        /// </summary>
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
            catch
            {
                return data; // Return original if decompression fails
            }
        }

        /// <summary>
        /// Jenkins hash function (HashLittle2) used by UO for file lookups
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

                a -= c; a ^= (c << 4) | (c >> 28); c += b;
                b -= a; b ^= (a << 6) | (a >> 26); a += c;
                c -= b; c ^= (b << 8) | (b >> 24); b += a;
                a -= c; a ^= (c << 16) | (c >> 16); c += b;
                b -= a; b ^= (a << 19) | (a >> 13); a += c;
                c -= b; c ^= (b << 4) | (b >> 28); b += a;

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

                c ^= b; c -= (b << 14) | (b >> 18);
                a ^= c; a -= (c << 11) | (c >> 21);
                b ^= a; b -= (a << 25) | (a >> 7);
                c ^= b; c -= (b << 16) | (b >> 16);
                a ^= c; a -= (c << 4) | (c >> 28);
                b ^= a; b -= (a << 14) | (a >> 18);
                c ^= b; c -= (b << 24) | (b >> 8);
            }

            return ((ulong)b << 32) | c;
        }

        public static bool UopFileExists(string mulFolder)
        {
            return File.Exists(Path.Combine(mulFolder, ART_LEGACY_UOP_FILENAME));
        }

        public static void ClearCache()
        {
            _indexCache = null;
            _cachedUopPath = null;
        }
    }
}
