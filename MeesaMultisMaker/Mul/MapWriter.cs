using System;
using System.IO;

namespace MeesaMultisMaker.Mul
{
    /// <summary>
    /// Writes map data to map{N}.mul files.
    /// Format mirrors MapReader: column-major 8×8 blocks,
    /// each block = 4-byte header + 64 tiles × 3 bytes (ushort tileId + sbyte z).
    /// </summary>
    public static class MapWriter
    {
        private const int BlockSize = 196; // 4-byte header + 64 * 3

        /// <summary>
        /// Write the full in-memory map to a .mul file.
        /// Any land-tile modifications already applied to <paramref name="map"/>.Tiles
        /// are included automatically.
        /// </summary>
        public static void Save(string outputPath, MapData map)
        {
            if (map == null)
                throw new ArgumentNullException(nameof(map));
            if (map.Tiles == null)
                throw new ArgumentException("MapData.Tiles is null");

            int blocksX = map.Width / 8;
            int blocksY = map.Height / 8;

            string tempPath = outputPath + ".tmp";

            try
            {
                using (var fs = File.Create(tempPath))
                using (var writer = new BinaryWriter(fs))
                {
                    // Column-major order — matches MapReader.LoadFromMul
                    for (int bx = 0; bx < blocksX; bx++)
                    {
                        for (int by = 0; by < blocksY; by++)
                        {
                            // 4-byte block header (usually 0)
                            writer.Write((uint)0);

                            // 64 tiles (8×8) within this block
                            for (int ty = 0; ty < 8; ty++)
                            {
                                for (int tx = 0; tx < 8; tx++)
                                {
                                    int x = bx * 8 + tx;
                                    int y = by * 8 + ty;

                                    if (x < map.Width && y < map.Height && map.Tiles[x, y] != null)
                                    {
                                        writer.Write(map.Tiles[x, y].TileId);
                                        writer.Write(map.Tiles[x, y].Z);
                                    }
                                    else
                                    {
                                        writer.Write((ushort)0);
                                        writer.Write((sbyte)0);
                                    }
                                }
                            }
                        }
                    }
                }

                // Atomic replace
                if (File.Exists(outputPath))
                    File.Delete(outputPath);
                File.Move(tempPath, outputPath);

                System.Diagnostics.Debug.WriteLine(
                    $"MapWriter: Saved {blocksX * blocksY} blocks ({map.Width}x{map.Height}) to {outputPath}");
            }
            catch
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
                throw;
            }
        }
    }
}
