using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace MeesaMultisMaker.Mul
{
    public class LandTile
    {
        public ushort TileId { get; set; }
        public sbyte Z { get; set; }
    }

    public class MapData
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public LandTile[,] Tiles { get; set; }
    }

    public static class MapReader
    {
        private const int BlockSize = 196; // 4 byte header + 64 tiles * 3 bytes each

        public static MapData Load(string folder, int mapIndex)
        {
            var mapPath = Path.Combine(folder, $"map{mapIndex}.mul");
            var uopPath = Path.Combine(folder, $"map{mapIndex}LegacyMUL.uop");

            bool hasMul = File.Exists(mapPath);
            bool hasUop = File.Exists(uopPath);

            if (!hasMul && !hasUop)
                throw new FileNotFoundException($"map{mapIndex}.mul and map{mapIndex}LegacyMUL.uop not found in {folder}");

            int width, height;
            GetMapDimensions(mapIndex, out width, out height);

            if (hasMul)
            {
                return LoadFromMul(mapPath, width, height);
            }
            else
            {
                return LoadFromUop(uopPath, mapIndex, width, height);
            }
        }

        private static void GetMapDimensions(int mapIndex, out int width, out int height)
        {
            // Default dimensions for modern UO clients (post-Mondain's Legacy).
            // The MUL path auto-detects from file size via TryFitDimensions
            // if these are too large.
            switch (mapIndex)
            {
                case 0: // Felucca
                case 1: // Trammel
                    width = 7168;
                    height = 4096;
                    break;
                case 2: // Ilshenar
                    width = 2304;
                    height = 1600;
                    break;
                case 3: // Malas
                    width = 2560;
                    height = 2048;
                    break;
                case 4: // Tokuno
                    width = 1448;
                    height = 1448;
                    break;
                case 5: // Ter Mur
                    width = 1280;
                    height = 4096;
                    break;
                default:
                    // Custom maps: use a reasonable default that will be
                    // auto-corrected by TryFitDimensions from the actual file size.
                    width = 7168;
                    height = 4096;
                    break;
            }
        }

        private static MapData LoadFromMul(string mapPath, int width, int height)
        {
            var tiles = new LandTile[width, height];
            int blocksX = width / 8;
            int blocksY = height / 8;

            using (var fs = File.OpenRead(mapPath))
            using (var reader = new BinaryReader(fs))
            {
                long fileLength = fs.Length;

                // Detect actual dimensions from file size if needed
                long expectedSize = (long)blocksX * blocksY * BlockSize;
                if (fileLength < expectedSize)
                {
                    // Try standard dimensions that fit the file
                    TryFitDimensions(fileLength, ref width, ref height);
                    tiles = new LandTile[width, height];
                    blocksX = width / 8;
                    blocksY = height / 8;
                }

                // IMPORTANT: UO uses COLUMN-MAJOR order for blocks
                for (int bx = 0; bx < blocksX; bx++)
                {
                    for (int by = 0; by < blocksY; by++)
                    {
                        long blockOffset = ((long)bx * blocksY + by) * BlockSize;

                        if (blockOffset + BlockSize > fileLength)
                            continue;

                        fs.Seek(blockOffset, SeekOrigin.Begin);

                        // Skip 4-byte block header
                        reader.ReadUInt32();

                        // Read 64 tiles (8x8) within this block
                        for (int ty = 0; ty < 8; ty++)
                        {
                            for (int tx = 0; tx < 8; tx++)
                            {
                                int x = bx * 8 + tx;
                                int y = by * 8 + ty;

                                if (x < width && y < height)
                                {
                                    if (fs.Position + 3 > fileLength)
                                        return new MapData { Width = width, Height = height, Tiles = tiles };

                                    ushort tileId = reader.ReadUInt16();
                                    sbyte z = reader.ReadSByte();

                                    tiles[x, y] = new LandTile { TileId = tileId, Z = z };
                                }
                                else
                                {
                                    if (fs.Position + 3 > fileLength)
                                        return new MapData { Width = width, Height = height, Tiles = tiles };

                                    reader.BaseStream.Seek(3, SeekOrigin.Current);
                                }
                            }
                        }
                    }
                }
            }

            return new MapData { Width = width, Height = height, Tiles = tiles };
        }

        private static MapData LoadFromUop(string uopPath, int mapIndex, int width, int height)
        {
            System.Diagnostics.Debug.WriteLine($"MapReader: Loading map{mapIndex} from UOP: {uopPath}");

            var tiles = new LandTile[width, height];
            int blocksX = width / 8;
            int blocksY = height / 8;
            int totalBlocks = blocksX * blocksY;

            // Build hash format for this map: "build/map{n}legacymul/{index:D8}.dat"
            string hashFormat = $"build/map{mapIndex}legacymul/{{0:D8}}.dat";

            // Pre-compute hashes for UOP entry indices.
            // Each UOP entry is a chunk of many map blocks, so the entry count
            // is much smaller than the total block count. 0x300 covers all maps.
            var hashToIndex = new Dictionary<ulong, int>();
            for (int i = 0; i < 0x300; i++)
            {
                string path = string.Format(hashFormat, i);
                ulong hash = UopHashEngine.HashLittle2(path);
                hashToIndex[hash] = i;
            }

            // Parse UOP file and extract entries
            var uopEntries = new Dictionary<int, UopBlockEntry>();

            using (var stream = File.OpenRead(uopPath))
            using (var reader = new BinaryReader(stream))
            {
                // Read UOP header
                uint signature = reader.ReadUInt32(); // "MYP\0"
                if (signature != 0x0050594D)
                {
                    System.Diagnostics.Debug.WriteLine("MapReader: Invalid UOP signature");
                    throw new InvalidDataException("Invalid UOP file signature");
                }

                int version = reader.ReadInt32();
                uint timestamp = reader.ReadUInt32();
                long startAddress = reader.ReadInt64();
                uint blockSizeCapacity = reader.ReadUInt32();
                int fileCount = reader.ReadInt32();

                System.Diagnostics.Debug.WriteLine($"MapReader: UOP version={version}, fileCount={fileCount}");

                // Read all blocks
                long nextBlock = startAddress;
                int blocksRead = 0;

                while (nextBlock != 0 && blocksRead < 10000)
                {
                    stream.Seek(nextBlock, SeekOrigin.Begin);

                    int filesInBlock = reader.ReadInt32();
                    long nextBlockAddress = reader.ReadInt64();

                    if (filesInBlock < 0 || filesInBlock > 10000)
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

                        if (hashToIndex.TryGetValue(hash, out int entryIndex))
                        {
                            uopEntries[entryIndex] = new UopBlockEntry
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

                System.Diagnostics.Debug.WriteLine($"MapReader: Found {uopEntries.Count} UOP entries from {blocksRead} blocks");

                // Determine the chunk size (number of map blocks per UOP entry).
                // All entries except possibly the last one have the same size.
                // Use the largest DecompressedSize to get the standard chunk size.
                int chunkSize = 0;
                foreach (var kvp in uopEntries)
                {
                    int blocks = kvp.Value.DecompressedSize / BlockSize;
                    if (blocks > chunkSize)
                        chunkSize = blocks;
                }

                if (chunkSize == 0)
                    chunkSize = 4096; // safe default

                System.Diagnostics.Debug.WriteLine($"MapReader: Chunk size = {chunkSize} blocks per UOP entry");

                // Verify/auto-detect dimensions from total decompressed data
                long totalDecompressed = 0;
                foreach (var kvp in uopEntries)
                {
                    totalDecompressed += kvp.Value.DecompressedSize;
                }
                int detectedTotalBlocks = (int)(totalDecompressed / BlockSize);
                if (detectedTotalBlocks > 0 && detectedTotalBlocks != totalBlocks)
                {
                    System.Diagnostics.Debug.WriteLine($"MapReader: Detected {detectedTotalBlocks} blocks from UOP data vs expected {totalBlocks}, adjusting dimensions");
                    TryFitDimensionsFromBlocks(detectedTotalBlocks, ref width, ref height);
                    blocksX = width / 8;
                    blocksY = height / 8;
                    totalBlocks = blocksX * blocksY;
                    tiles = new LandTile[width, height];
                }

                // Now read each UOP entry and extract map block data
                // Entry N contains map blocks [N*chunkSize .. N*chunkSize + actualCount)
                foreach (var kvp in uopEntries)
                {
                    int entryIndex = kvp.Key;
                    var entry = kvp.Value;

                    stream.Seek(entry.Offset, SeekOrigin.Begin);

                    byte[] data;
                    if (entry.CompressionMethod != 0 && entry.CompressedSize != entry.DecompressedSize)
                    {
                        byte[] compressedData = reader.ReadBytes(entry.CompressedSize);
                        data = Decompress(compressedData, entry.DecompressedSize);
                    }
                    else
                    {
                        int size = entry.DecompressedSize > 0 ? entry.DecompressedSize : entry.CompressedSize;
                        data = reader.ReadBytes(size);
                    }

                    if (data == null || data.Length < BlockSize)
                        continue;

                    int numBlocksInEntry = data.Length / BlockSize;
                    int baseBlockIndex = entryIndex * chunkSize;

                    using (var dataStream = new MemoryStream(data))
                    using (var dataReader = new BinaryReader(dataStream))
                    {
                        for (int b = 0; b < numBlocksInEntry; b++)
                        {
                            int blockNumber = baseBlockIndex + b;
                            if (blockNumber >= totalBlocks)
                                break;

                            // Convert linear block index to column-major (bx, by)
                            int bx = blockNumber / blocksY;
                            int by = blockNumber % blocksY;

                            if (bx >= blocksX)
                                continue;

                            long blockDataOffset = (long)b * BlockSize;
                            if (blockDataOffset + BlockSize > data.Length)
                                break;

                            dataStream.Seek(blockDataOffset, SeekOrigin.Begin);

                            // Skip 4-byte block header
                            dataReader.ReadUInt32();

                            // Read 64 tiles (8x8) within this block
                            for (int ty = 0; ty < 8; ty++)
                            {
                                for (int tx = 0; tx < 8; tx++)
                                {
                                    int x = bx * 8 + tx;
                                    int y = by * 8 + ty;

                                    if (dataStream.Position + 3 > data.Length)
                                        break;

                                    ushort tileId = dataReader.ReadUInt16();
                                    sbyte z = dataReader.ReadSByte();

                                    if (x < width && y < height)
                                    {
                                        tiles[x, y] = new LandTile { TileId = tileId, Z = z };
                                    }
                                }
                            }
                        }
                    }
                }
            }

            System.Diagnostics.Debug.WriteLine($"MapReader: Finished loading UOP map {mapIndex}: {width}x{height}");
            return new MapData { Width = width, Height = height, Tiles = tiles };
        }

        private static void TryFitDimensions(long fileLength, ref int width, ref int height)
        {
            // Known UO map sizes (width, height) - ordered largest first
            var knownSizes = new[]
            {
                (7168, 4096), (6144, 4096), (2560, 2048),
                (2304, 1600), (1448, 1448), (1280, 4096)
            };

            foreach (var (w, h) in knownSizes)
            {
                long expected = (long)(w / 8) * (h / 8) * BlockSize;
                if (fileLength >= expected)
                {
                    width = w;
                    height = h;
                    return;
                }
            }

            // Custom map: derive dimensions from file size.
            // totalBlocks = fileLength / BlockSize = blocksX * blocksY
            // Try square-ish maps first, then common aspect ratios.
            int totalBlocks = (int)(fileLength / BlockSize);
            if (totalBlocks > 0)
            {
                FitDimensionsFromBlockCount(totalBlocks, ref width, ref height);
            }
        }

        private static void TryFitDimensionsFromBlocks(int totalBlocks, ref int width, ref int height)
        {
            var knownSizes = new[]
            {
                (7168, 4096), (6144, 4096), (2560, 2048),
                (2304, 1600), (1448, 1448), (1280, 4096)
            };

            foreach (var (w, h) in knownSizes)
            {
                int expected = (w / 8) * (h / 8);
                if (totalBlocks >= expected)
                {
                    width = w;
                    height = h;
                    return;
                }
            }

            // Custom map: derive dimensions from block count.
            if (totalBlocks > 0)
            {
                FitDimensionsFromBlockCount(totalBlocks, ref width, ref height);
            }
        }

        /// <summary>
        /// For custom maps with unknown dimensions, find block-aligned (multiple
        /// of 8) width and height whose product of block counts equals totalBlocks.
        /// Prefers landscape aspect ratios close to 2:1.
        /// </summary>
        private static void FitDimensionsFromBlockCount(int totalBlocks, ref int width, ref int height)
        {
            // Find the factorisation of totalBlocks = blocksX * blocksY
            // that gives the most landscape-like aspect ratio.
            int bestBx = totalBlocks;
            int bestBy = 1;
            double bestRatio = double.MaxValue;
            const double targetRatio = 1.75; // typical UO maps are ~7168/4096 ≈ 1.75

            int sqrt = (int)Math.Sqrt(totalBlocks);
            for (int by = 1; by <= sqrt; by++)
            {
                if (totalBlocks % by != 0) continue;
                int bx = totalBlocks / by;
                double ratio = (double)bx / by;
                double diff = Math.Abs(ratio - targetRatio);
                if (diff < bestRatio)
                {
                    bestRatio = diff;
                    bestBx = bx;
                    bestBy = by;
                }
                // Also try the transposed version
                double diffT = Math.Abs((double)by / bx - targetRatio);
                if (diffT < bestRatio)
                {
                    bestRatio = diffT;
                    bestBx = by;
                    bestBy = bx;
                }
            }

            width = bestBx * 8;
            height = bestBy * 8;
        }

        /// <summary>
        /// Scan an already-loaded map and count how many times each land tile ID appears.
        /// Returns a dictionary mapping TileId to total placement count.
        /// </summary>
        public static Dictionary<ushort, int> CountLandTileFrequencies(MapData map)
        {
            var counts = new Dictionary<ushort, int>();

            if (map == null || map.Tiles == null)
                return counts;

            int width = map.Width;
            int height = map.Height;

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    var tile = map.Tiles[x, y];
                    if (tile != null)
                    {
                        ushort tileId = tile.TileId;
                        if (counts.ContainsKey(tileId))
                            counts[tileId]++;
                        else
                            counts[tileId] = 1;
                    }
                }
            }

            return counts;
        }

        /// <summary>
        /// Represents a 3x3 land tile pattern (9 tiles, row-major order from top-left).
        /// </summary>
        public struct Pattern3x3 : IEquatable<Pattern3x3>
        {
            public ushort TL, TM, TR; // top row
            public ushort ML, MM, MR; // middle row
            public ushort BL, BM, BR; // bottom row

            public ushort[] ToArray() => new[] { TL, TM, TR, ML, MM, MR, BL, BM, BR };

            public override string ToString() =>
                $"{TL:X4} {TM:X4} {TR:X4} | {ML:X4} {MM:X4} {MR:X4} | {BL:X4} {BM:X4} {BR:X4}";

            public bool Equals(Pattern3x3 other) =>
                TL == other.TL && TM == other.TM && TR == other.TR &&
                ML == other.ML && MM == other.MM && MR == other.MR &&
                BL == other.BL && BM == other.BM && BR == other.BR;

            public override bool Equals(object obj) => obj is Pattern3x3 p && Equals(p);

            public override int GetHashCode()
            {
                // Combine all 9 tile IDs into a hash
                unchecked
                {
                    int hash = 17;
                    hash = hash * 31 + TL.GetHashCode();
                    hash = hash * 31 + TM.GetHashCode();
                    hash = hash * 31 + TR.GetHashCode();
                    hash = hash * 31 + ML.GetHashCode();
                    hash = hash * 31 + MM.GetHashCode();
                    hash = hash * 31 + MR.GetHashCode();
                    hash = hash * 31 + BL.GetHashCode();
                    hash = hash * 31 + BM.GetHashCode();
                    hash = hash * 31 + BR.GetHashCode();
                    return hash;
                }
            }
        }

        /// <summary>
        /// Scan an already-loaded map with a 3x3 sliding window and count how many times
        /// each unique land tile pattern appears. Skips edges where the kernel doesn't fit.
        /// Returns a dictionary mapping Pattern3x3 to its occurrence count.
        /// </summary>
        public static Dictionary<Pattern3x3, int> CountLandTilePatterns3x3(MapData map)
        {
            var counts = new Dictionary<Pattern3x3, int>();

            if (map == null || map.Tiles == null || map.Width < 3 || map.Height < 3)
                return counts;

            int width = map.Width;
            int height = map.Height;

            // Walk a 3x3 window: top-left corner (x,y) ranges so that x+2 < width and y+2 < height
            for (int y = 0; y <= height - 3; y++)
            {
                for (int x = 0; x <= width - 3; x++)
                {
                    var p = new Pattern3x3
                    {
                        TL = map.Tiles[x, y]?.TileId ?? 0,
                        TM = map.Tiles[x + 1, y]?.TileId ?? 0,
                        TR = map.Tiles[x + 2, y]?.TileId ?? 0,
                        ML = map.Tiles[x, y + 1]?.TileId ?? 0,
                        MM = map.Tiles[x + 1, y + 1]?.TileId ?? 0,
                        MR = map.Tiles[x + 2, y + 1]?.TileId ?? 0,
                        BL = map.Tiles[x, y + 2]?.TileId ?? 0,
                        BM = map.Tiles[x + 1, y + 2]?.TileId ?? 0,
                        BR = map.Tiles[x + 2, y + 2]?.TileId ?? 0
                    };

                    if (counts.ContainsKey(p))
                        counts[p]++;
                    else
                        counts[p] = 1;
                }
            }

            return counts;
        }

        /// <summary>
        /// Represents a 2x2 land tile pattern (4 tiles, row-major order from top-left).
        /// </summary>
        public struct Pattern2x2 : IEquatable<Pattern2x2>
        {
            public ushort TL, TR; // top row
            public ushort BL, BR; // bottom row

            public ushort[] ToArray() => new[] { TL, TR, BL, BR };

            public override string ToString() =>
                $"{TL:X4} {TR:X4} | {BL:X4} {BR:X4}";

            public bool Equals(Pattern2x2 other) =>
                TL == other.TL && TR == other.TR &&
                BL == other.BL && BR == other.BR;

            public override bool Equals(object obj) => obj is Pattern2x2 p && Equals(p);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 31 + TL.GetHashCode();
                    hash = hash * 31 + TR.GetHashCode();
                    hash = hash * 31 + BL.GetHashCode();
                    hash = hash * 31 + BR.GetHashCode();
                    return hash;
                }
            }
        }

        /// <summary>
        /// Scan an already-loaded map with a 2x2 sliding window and count how many times
        /// each unique land tile pattern appears. Skips edges where the kernel doesn't fit.
        /// Returns a dictionary mapping Pattern2x2 to its occurrence count.
        /// </summary>
        public static Dictionary<Pattern2x2, int> CountLandTilePatterns2x2(MapData map)
        {
            var counts = new Dictionary<Pattern2x2, int>();

            if (map == null || map.Tiles == null || map.Width < 2 || map.Height < 2)
                return counts;

            int width = map.Width;
            int height = map.Height;

            for (int y = 0; y <= height - 2; y++)
            {
                for (int x = 0; x <= width - 2; x++)
                {
                    var p = new Pattern2x2
                    {
                        TL = map.Tiles[x, y]?.TileId ?? 0,
                        TR = map.Tiles[x + 1, y]?.TileId ?? 0,
                        BL = map.Tiles[x, y + 1]?.TileId ?? 0,
                        BR = map.Tiles[x + 1, y + 1]?.TileId ?? 0
                    };

                    if (counts.ContainsKey(p))
                        counts[p]++;
                    else
                        counts[p] = 1;
                }
            }

            return counts;
        }

        private static byte[] Decompress(byte[] data, int decompressedSize)
        {
            try
            {
                // Skip first 2 bytes (zlib header)
                using (var input = new MemoryStream(data, 2, data.Length - 2))
                using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
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

        private class UopBlockEntry
        {
            public long Offset;
            public int CompressedSize;
            public int DecompressedSize;
            public short CompressionMethod;
        }
    }
}