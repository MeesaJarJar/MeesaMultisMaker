using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using MeesaMultisMaker.Mul;

namespace MeesaMultisMaker.MapGeneration
{
    /// <summary>
    /// Imports a grayscale image and converts it into a MapData heightmap.
    /// Black = lowest Z, white = highest Z.  Pixels below a configurable
    /// threshold become water tiles; everything else becomes land.
    /// </summary>
    public static class HeightmapImporter
    {
        /// <summary>
        /// Import a grayscale image file as a heightmap.
        /// The image is scaled to fit the target map dimensions.
        /// </summary>
        /// <param name="imagePath">Path to the grayscale image (PNG, BMP, JPG, etc.)</param>
        /// <param name="mapWidth">Target map width in tiles (will be rounded to multiple of 8)</param>
        /// <param name="mapHeight">Target map height in tiles (will be rounded to multiple of 8)</param>
        /// <param name="zMin">Z value for pure black pixels</param>
        /// <param name="zMax">Z value for pure white pixels</param>
        /// <param name="waterThreshold">Brightness (0-255) at or below which tiles become water (set to 0 to disable)</param>
        /// <param name="waterZ">Z value for water tiles</param>
        /// <param name="autoBiomes">When true, automatically assign biome tile IDs based on elevation, moisture and temperature</param>
        public static MapData Import(
            string imagePath,
            int mapWidth, int mapHeight,
            sbyte zMin = -15, sbyte zMax = 80,
            int waterThreshold = 64,
            sbyte waterZ = -5,
            bool autoBiomes = false)
        {
            mapWidth = (mapWidth / 8) * 8;
            mapHeight = (mapHeight / 8) * 8;
            if (mapWidth < 8) mapWidth = 8;
            if (mapHeight < 8) mapHeight = 8;

            // Load and scale the image
            using (var srcImg = new Bitmap(imagePath))
            using (var scaled = new Bitmap(mapWidth, mapHeight, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(scaled))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
                    g.DrawImage(srcImg, 0, 0, mapWidth, mapHeight);
                }

                return FromBitmap(scaled, zMin, zMax, waterThreshold, waterZ, autoBiomes);
            }
        }

        /// <summary>
        /// Convert an already-loaded bitmap to a MapData heightmap.
        /// </summary>
        public static MapData FromBitmap(
            Bitmap bmp,
            sbyte zMin = -15, sbyte zMax = 80,
            int waterThreshold = 64,
            sbyte waterZ = -5,
            bool autoBiomes = false)
        {
            int w = bmp.Width;
            int h = bmp.Height;

            // Read all pixels via LockBits for performance
            var rect = new Rectangle(0, 0, w, h);
            var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            byte[] pixels = new byte[data.Stride * h];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            int stride = data.Stride;
            bmp.UnlockBits(data);

            var map = new MapData
            {
                Width = w,
                Height = h,
                Tiles = new LandTile[w, h]
            };

            var rng = new Random(42);

            // Pre-generate moisture and temperature maps when auto-biomes is enabled
            double[,] moisture = null;
            double[,] temperature = null;
            if (autoBiomes)
            {
                moisture = new double[w, h];
                temperature = new double[w, h];

                var moistNoise = new PerlinNoise(rng.Next());
                var tempNoise = new PerlinNoise(rng.Next());
                double moistScale = 0.004;
                double tempScale = 0.003;

                for (int y = 0; y < h; y++)
                {
                    double latBase = 1.0 - Math.Abs(y - h / 2.0) / (h / 2.0);
                    for (int x = 0; x < w; x++)
                    {
                        moisture[x, y] = (moistNoise.FBM(x * moistScale, y * moistScale, 4) + 1.0) * 0.5;
                        double tNoise = tempNoise.FBM(x * tempScale, y * tempScale, 3) * 0.3;
                        temperature[x, y] = Math.Max(0, Math.Min(1, latBase + tNoise));
                    }
                }
            }

            ushort waterTile = 0x00A8;
            ushort grassTile = 0x0003;

            for (int y = 0; y < h; y++)
            {
                int rowOffset = y * stride;
                for (int x = 0; x < w; x++)
                {
                    int px = rowOffset + x * 4;
                    // Convert to greyscale luminance
                    int brightness = (pixels[px + 2] * 299 + pixels[px + 1] * 587 + pixels[px + 0] * 114) / 1000;

                    if (brightness <= waterThreshold && waterThreshold > 0)
                    {
                        ushort wTile = waterTile;
                        if (autoBiomes)
                        {
                            // Classify deep vs shallow water based on how dark the pixel is
                            double depth = 1.0 - (double)brightness / Math.Max(1, waterThreshold);
                            var waterBiome = depth > 0.5 ? UOBiomeType.DeepWater : UOBiomeType.ShallowWater;
                            wTile = UOBiomes.RandomTile(waterBiome, rng);
                        }
                        map.Tiles[x, y] = new LandTile { TileId = wTile, Z = waterZ };
                    }
                    else
                    {
                        // Map brightness to Z range
                        double t = (brightness - waterThreshold) / (double)(255 - waterThreshold);
                        sbyte z = (sbyte)Math.Max(zMin, Math.Min(zMax, zMin + t * (zMax - zMin)));

                        ushort tileId = grassTile;
                        if (autoBiomes)
                        {
                            // Elevation normalized 0..1 for biome classification
                            double elevation = t;

                            // Check for beach: tiles just above water threshold
                            if (elevation < 0.03)
                            {
                                tileId = UOBiomes.RandomTile(UOBiomeType.Beach, rng);
                            }
                            else
                            {
                                var biome = UOBiomes.Classify(elevation, moisture[x, y], temperature[x, y]);
                                tileId = UOBiomes.RandomTile(biome, rng);
                            }
                        }

                        map.Tiles[x, y] = new LandTile { TileId = tileId, Z = z };
                    }
                }
            }

            return map;
        }

        /// <summary>
        /// Apply a heightmap image onto an existing map, overwriting Z values
        /// and tile IDs.  Useful for refining a generated map.
        /// </summary>
        public static void ApplyToExistingMap(
            MapData map, Bitmap bmp,
            sbyte zMin = -15, sbyte zMax = 80,
            int waterThreshold = 64,
            sbyte waterZ = -5)
        {
            int w = Math.Min(map.Width, bmp.Width);
            int h = Math.Min(map.Height, bmp.Height);

            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            byte[] pixels = new byte[data.Stride * bmp.Height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            int stride = data.Stride;
            bmp.UnlockBits(data);

            ushort waterTile = 0x00A8;

            for (int y = 0; y < h; y++)
            {
                int rowOffset = y * stride;
                for (int x = 0; x < w; x++)
                {
                    int px = rowOffset + x * 4;
                    int brightness = (pixels[px + 2] * 299 + pixels[px + 1] * 587 + pixels[px + 0] * 114) / 1000;

                    var tile = map.Tiles[x, y];
                    if (tile == null)
                    {
                        tile = new LandTile();
                        map.Tiles[x, y] = tile;
                    }

                    if (brightness <= waterThreshold && waterThreshold > 0)
                    {
                        tile.TileId = waterTile;
                        tile.Z = waterZ;
                    }
                    else
                    {
                        double t = (brightness - waterThreshold) / (double)(255 - waterThreshold);
                        tile.Z = (sbyte)Math.Max(zMin, Math.Min(zMax, zMin + t * (zMax - zMin)));
                        // Keep existing tile ID if it's already a land tile
                    }
                }
            }
        }
    }
}
