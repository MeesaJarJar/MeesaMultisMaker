using System;
using System.Collections.Generic;
using System.IO;

namespace MeesaMultisMaker.Mul
{
    /// <summary>
    /// Represents a single static item on the map
    /// </summary>
    public class StaticTile
    {
        /// <summary>
        /// Item ID (graphic ID for static items, 0x4000+ in art.mul)
        /// </summary>
        public ushort ItemId { get; set; }

        /// <summary>
        /// X position within the 8x8 block (0-7)
        /// </summary>
        public byte X { get; set; }

        /// <summary>
        /// Y position within the 8x8 block (0-7)
        /// </summary>
        public byte Y { get; set; }

        /// <summary>
        /// Z (altitude) of the static item
        /// </summary>
        public sbyte Z { get; set; }

        /// <summary>
        /// Hue/color index for the item
        /// </summary>
        public ushort Hue { get; set; }

        /// <summary>
        /// World X coordinate (computed from block + offset)
        /// </summary>
        public int WorldX { get; set; }

        /// <summary>
        /// World Y coordinate (computed from block + offset)
        /// </summary>
        public int WorldY { get; set; }
    }

    /// <summary>
    /// Contains all statics data for a map
    /// </summary>
    public class StaticsData
    {
        /// <summary>
        /// Dictionary keyed by (worldX, worldY) containing list of statics at that position
        /// </summary>
        public Dictionary<(int x, int y), List<StaticTile>> StaticsByPosition { get; set; }
            = new Dictionary<(int x, int y), List<StaticTile>>();

        /// <summary>
        /// Get all statics at a specific world position
        /// </summary>
        public List<StaticTile> GetStaticsAt(int worldX, int worldY)
        {
            if (StaticsByPosition.TryGetValue((worldX, worldY), out var statics))
                return statics;
            return new List<StaticTile>();
        }

        /// <summary>
        /// Get all statics within a rectangular area
        /// </summary>
        public List<StaticTile> GetStaticsInArea(int startX, int startY, int endX, int endY)
        {
            var result = new List<StaticTile>();
            for (int x = startX; x <= endX; x++)
            {
                for (int y = startY; y <= endY; y++)
                {
                    if (StaticsByPosition.TryGetValue((x, y), out var statics))
                    {
                        result.AddRange(statics);
                    }
                }
            }
            return result;
        }
    }

    /// <summary>
    /// Reads static items from statics{mapIndex}.mul and staidx{mapIndex}.mul
    /// 
    /// File format:
    /// - staidx{n}.mul: Index file with 12-byte entries per 8x8 block
    ///   - int32 offset: Offset into statics.mul (-1 if no statics)
    ///   - int32 length: Length of data in bytes
    ///   - int32 extra: Usually 0
    ///   
    /// - statics{n}.mul: Data file with static items
    ///   - Each item is 7 bytes:
    ///     - ushort itemId (2 bytes)
    ///     - byte x (1 byte) - offset within 8x8 block
    ///     - byte y (1 byte) - offset within 8x8 block  
    ///     - sbyte z (1 byte) - altitude
    ///     - ushort hue (2 bytes) - color/hue index
    /// </summary>
    public static class StaticsReader
    {
        private const int INDEX_ENTRY_SIZE = 12;
        private const int STATIC_ENTRY_SIZE = 7;

        /// <summary>
        /// Load all statics for a specific map
        /// </summary>
        public static StaticsData Load(string folder, int mapIndex)
        {
            string idxPath = Path.Combine(folder, $"staidx{mapIndex}.mul");
            string mulPath = Path.Combine(folder, $"statics{mapIndex}.mul");

            if (!File.Exists(idxPath))
            {
                System.Diagnostics.Debug.WriteLine($"StaticsReader: staidx{mapIndex}.mul not found at {idxPath}");
                return new StaticsData();
            }

            if (!File.Exists(mulPath))
            {
                System.Diagnostics.Debug.WriteLine($"StaticsReader: statics{mapIndex}.mul not found at {mulPath}");
                return new StaticsData();
            }

            // Get map dimensions - auto-detect from staidx file size
            int blocksX, blocksY;
            GetBlockDimensions(mapIndex, idxPath, out blocksX, out blocksY);
            if (blocksX == 0 || blocksY == 0)
            {
                System.Diagnostics.Debug.WriteLine($"StaticsReader: Could not determine dimensions for map index {mapIndex}");
                return new StaticsData();
            }

            var result = new StaticsData();

            try
            {
                using (var idxStream = File.OpenRead(idxPath))
                using (var mulStream = File.OpenRead(mulPath))
                using (var idxReader = new BinaryReader(idxStream))
                using (var mulReader = new BinaryReader(mulStream))
                {
                    int totalBlocks = blocksX * blocksY;
                    System.Diagnostics.Debug.WriteLine($"StaticsReader: Loading statics for map{mapIndex}, {blocksX}x{blocksY} blocks ({totalBlocks} total)");

                    // Read index entries using column-major order (same as map data)
                    for (int bx = 0; bx < blocksX; bx++)
                    {
                        for (int by = 0; by < blocksY; by++)
                        {
                            // Column-major index: (bx * blocksY + by)
                            long indexOffset = ((long)bx * blocksY + by) * INDEX_ENTRY_SIZE;

                            if (indexOffset + INDEX_ENTRY_SIZE > idxStream.Length)
                                continue;

                            idxStream.Seek(indexOffset, SeekOrigin.Begin);

                            int dataOffset = idxReader.ReadInt32();
                            int dataLength = idxReader.ReadInt32();
                            int extra = idxReader.ReadInt32();

                            // Skip invalid entries
                            if (dataOffset < 0 || dataLength <= 0 || dataOffset >= mulStream.Length)
                                continue;

                            // Read statics for this block
                            mulStream.Seek(dataOffset, SeekOrigin.Begin);

                            int numStatics = dataLength / STATIC_ENTRY_SIZE;

                            for (int i = 0; i < numStatics; i++)
                            {
                                if (mulStream.Position + STATIC_ENTRY_SIZE > mulStream.Length)
                                    break;

                                ushort itemId = mulReader.ReadUInt16();
                                byte x = mulReader.ReadByte();
                                byte y = mulReader.ReadByte();
                                sbyte z = mulReader.ReadSByte();
                                ushort hue = mulReader.ReadUInt16();

                                // Calculate world coordinates
                                int worldX = bx * 8 + x;
                                int worldY = by * 8 + y;

                                var staticTile = new StaticTile
                                {
                                    ItemId = itemId,
                                    X = x,
                                    Y = y,
                                    Z = z,
                                    Hue = hue,
                                    WorldX = worldX,
                                    WorldY = worldY
                                };

                                // Add to dictionary
                                var key = (worldX, worldY);
                                if (!result.StaticsByPosition.ContainsKey(key))
                                {
                                    result.StaticsByPosition[key] = new List<StaticTile>();
                                }
                                result.StaticsByPosition[key].Add(staticTile);
                            }
                        }
                    }

                    System.Diagnostics.Debug.WriteLine($"StaticsReader: Loaded {result.StaticsByPosition.Count} positions with statics");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"StaticsReader: Error loading statics: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Load statics for a specific rectangular area only (for performance)
        /// </summary>
        public static StaticsData LoadArea(string folder, int mapIndex, int startX, int startY, int endX, int endY)
        {
            string idxPath = Path.Combine(folder, $"staidx{mapIndex}.mul");
            string mulPath = Path.Combine(folder, $"statics{mapIndex}.mul");

            if (!File.Exists(idxPath) || !File.Exists(mulPath))
            {
                return new StaticsData();
            }

            // Get map dimensions - auto-detect from staidx file size
            int blocksX, blocksY;
            GetBlockDimensions(mapIndex, idxPath, out blocksX, out blocksY);
            if (blocksX == 0 || blocksY == 0)
                return new StaticsData();

            var result = new StaticsData();

            // Calculate block range
            int startBlockX = startX / 8;
            int startBlockY = startY / 8;
            int endBlockX = endX / 8;
            int endBlockY = endY / 8;

            try
            {
                using (var idxStream = File.OpenRead(idxPath))
                using (var mulStream = File.OpenRead(mulPath))
                using (var idxReader = new BinaryReader(idxStream))
                using (var mulReader = new BinaryReader(mulStream))
                {
                    for (int bx = startBlockX; bx <= endBlockX; bx++)
                    {
                        for (int by = startBlockY; by <= endBlockY; by++)
                        {
                            // Column-major index
                            long indexOffset = ((long)bx * blocksY + by) * INDEX_ENTRY_SIZE;

                            if (indexOffset + INDEX_ENTRY_SIZE > idxStream.Length)
                                continue;

                            idxStream.Seek(indexOffset, SeekOrigin.Begin);

                            int dataOffset = idxReader.ReadInt32();
                            int dataLength = idxReader.ReadInt32();
                            int extra = idxReader.ReadInt32();

                            if (dataOffset < 0 || dataLength <= 0 || dataOffset >= mulStream.Length)
                                continue;

                            mulStream.Seek(dataOffset, SeekOrigin.Begin);

                            int numStatics = dataLength / STATIC_ENTRY_SIZE;

                            for (int i = 0; i < numStatics; i++)
                            {
                                if (mulStream.Position + STATIC_ENTRY_SIZE > mulStream.Length)
                                    break;

                                ushort itemId = mulReader.ReadUInt16();
                                byte x = mulReader.ReadByte();
                                byte y = mulReader.ReadByte();
                                sbyte z = mulReader.ReadSByte();
                                ushort hue = mulReader.ReadUInt16();

                                int worldX = bx * 8 + x;
                                int worldY = by * 8 + y;

                                // Filter to requested area
                                if (worldX < startX || worldX > endX || worldY < startY || worldY > endY)
                                    continue;

                                var staticTile = new StaticTile
                                {
                                    ItemId = itemId,
                                    X = x,
                                    Y = y,
                                    Z = z,
                                    Hue = hue,
                                    WorldX = worldX,
                                    WorldY = worldY
                                };

                                var key = (worldX, worldY);
                                if (!result.StaticsByPosition.ContainsKey(key))
                                {
                                    result.StaticsByPosition[key] = new List<StaticTile>();
                                }
                                result.StaticsByPosition[key].Add(staticTile);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"StaticsReader: Error loading area statics: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Determine block dimensions from the staidx file size and the known map index.
        /// The staidx file has 12 bytes per 8x8 block, so total blocks = fileSize / 12.
        /// We match that against known (blocksX * blocksY) combinations.
        /// </summary>
        internal static void GetBlockDimensions(int mapIndex, string idxPath, out int blocksX, out int blocksY)
        {
            blocksX = 0;
            blocksY = 0;

            try
            {
                long idxFileLength = new FileInfo(idxPath).Length;
                int totalBlocks = (int)(idxFileLength / INDEX_ENTRY_SIZE);

                // Known UO map sizes: (width/8, height/8, totalBlocks)
                var knownSizes = new[]
                {
                    (896, 512),  // 7168x4096 (Felucca/Trammel post-ML)
                    (768, 512),  // 6144x4096 (Felucca/Trammel classic)
                    (288, 200),  // 2304x1600 (Ilshenar)
                    (320, 256),  // 2560x2048 (Malas)
                    (181, 181),  // 1448x1448 (Tokuno)
                    (160, 512),  // 1280x4096 (Ter Mur)
                };

                // Try to match total blocks to a known size
                foreach (var (bx, by) in knownSizes)
                {
                    if (bx * by == totalBlocks)
                    {
                        blocksX = bx;
                        blocksY = by;
                        return;
                    }
                }

                // Fallback: use default dimensions by map index
                switch (mapIndex)
                {
                    case 0:
                    case 1:
                        blocksX = 896; blocksY = 512; break;
                    case 2:
                        blocksX = 288; blocksY = 200; break;
                    case 3:
                        blocksX = 320; blocksY = 256; break;
                    case 4:
                        blocksX = 181; blocksY = 181; break;
                    case 5:
                        blocksX = 160; blocksY = 512; break;
                    default:
                        // Custom map: find the best factorisation of totalBlocks.
                        if (totalBlocks > 0)
                        {
                            int bestBx = totalBlocks, bestBy = 1;
                            double bestRatio = double.MaxValue;
                            const double targetRatio = 1.75;
                            int sqrt = (int)Math.Sqrt(totalBlocks);
                            for (int f = 1; f <= sqrt; f++)
                            {
                                if (totalBlocks % f != 0) continue;
                                int other = totalBlocks / f;
                                double diff = Math.Abs((double)other / f - targetRatio);
                                if (diff < bestRatio) { bestRatio = diff; bestBx = other; bestBy = f; }
                                double diffT = Math.Abs((double)f / other - targetRatio);
                                if (diffT < bestRatio) { bestRatio = diffT; bestBx = f; bestBy = other; }
                            }
                            blocksX = bestBx;
                            blocksY = bestBy;
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"StaticsReader: Error detecting dimensions: {ex.Message}");
            }
        }

        /// <summary>
        /// Scan the entire statics file and count how many times each ItemId is placed.
        /// Returns a dictionary mapping ItemId to total placement count.
        /// </summary>
        public static Dictionary<ushort, int> CountStaticFrequencies(string folder, int mapIndex)
        {
            var counts = new Dictionary<ushort, int>();

            string idxPath = Path.Combine(folder, $"staidx{mapIndex}.mul");
            string mulPath = Path.Combine(folder, $"statics{mapIndex}.mul");

            if (!File.Exists(idxPath) || !File.Exists(mulPath))
                return counts;

            int blocksX, blocksY;
            GetBlockDimensions(mapIndex, idxPath, out blocksX, out blocksY);
            if (blocksX == 0 || blocksY == 0)
                return counts;

            try
            {
                using (var idxStream = File.OpenRead(idxPath))
                using (var mulStream = File.OpenRead(mulPath))
                using (var idxReader = new BinaryReader(idxStream))
                using (var mulReader = new BinaryReader(mulStream))
                {
                    int totalBlocks = blocksX * blocksY;
                    for (int b = 0; b < totalBlocks; b++)
                    {
                        long indexOffset = (long)b * INDEX_ENTRY_SIZE;
                        if (indexOffset + INDEX_ENTRY_SIZE > idxStream.Length)
                            continue;

                        idxStream.Seek(indexOffset, SeekOrigin.Begin);

                        int dataOffset = idxReader.ReadInt32();
                        int dataLength = idxReader.ReadInt32();
                        int extra = idxReader.ReadInt32();

                        if (dataOffset < 0 || dataLength <= 0 || dataOffset >= mulStream.Length)
                            continue;

                        mulStream.Seek(dataOffset, SeekOrigin.Begin);
                        int numStatics = dataLength / STATIC_ENTRY_SIZE;

                        for (int i = 0; i < numStatics; i++)
                        {
                            if (mulStream.Position + STATIC_ENTRY_SIZE > mulStream.Length)
                                break;

                            ushort itemId = mulReader.ReadUInt16();
                            mulStream.Seek(5, SeekOrigin.Current); // skip x, y, z, hue

                            if (counts.ContainsKey(itemId))
                                counts[itemId]++;
                            else
                                counts[itemId] = 1;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"StaticsReader: Error counting frequencies: {ex.Message}");
            }

            return counts;
        }
    }
}
