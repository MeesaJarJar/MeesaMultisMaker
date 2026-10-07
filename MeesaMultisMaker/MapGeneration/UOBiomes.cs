using System;

namespace MeesaMultisMaker.MapGeneration
{
    /// <summary>
    /// Ultima Online biome types used for world generation.
    /// </summary>
    public enum UOBiomeType
    {
        DeepWater,
        ShallowWater,
        Beach,
        Grass,
        Forest,
        Jungle,
        Swamp,
        Desert,
        Rock,
        Mountain,
        Snow,
        Lava,
        Farmland
    }

    /// <summary>
    /// Defines the tile IDs and characteristics for each UO biome.
    /// Each biome has multiple tile variants for visual variety.
    /// </summary>
    public static class UOBiomes
    {
        /// <summary>
        /// Land tile ID sets for each biome — multiple variants are randomly chosen.
        /// </summary>
        public static ushort[] GetTileIds(UOBiomeType biome)
        {
            switch (biome)
            {
                case UOBiomeType.DeepWater:
                    return new ushort[] { 0x00A8, 0x00A9, 0x00AA, 0x00AB };
                case UOBiomeType.ShallowWater:
                    return new ushort[] { 0x0136, 0x0137 };
                case UOBiomeType.Beach:
                    return new ushort[] { 0x0016, 0x0017, 0x0018, 0x0019, 0x001A, 0x001B };
                case UOBiomeType.Grass:
                    return new ushort[] { 0x0003, 0x0004, 0x0005, 0x0006, 0x0007, 0x0008, 0x0009 };
                case UOBiomeType.Forest:
                    return new ushort[] { 0x009C, 0x009D, 0x009E, 0x009F, 0x00A0, 0x00A1, 0x00A2, 0x00A3 };
                case UOBiomeType.Jungle:
                    return new ushort[] { 0x0230, 0x0231, 0x0232, 0x0233 };
                case UOBiomeType.Swamp:
                    return new ushort[] { 0x0578, 0x0579, 0x057A, 0x057B };
                case UOBiomeType.Desert:
                    return new ushort[] { 0x002C, 0x002D, 0x002E, 0x002F, 0x0030, 0x0031 };
                case UOBiomeType.Rock:
                    return new ushort[] { 0x006A, 0x006B, 0x006C, 0x006D, 0x006E, 0x006F };
                case UOBiomeType.Mountain:
                    return new ushort[] { 0x00AC, 0x00AD, 0x00AE, 0x00AF, 0x00B0, 0x00B1, 0x00B2 };
                case UOBiomeType.Snow:
                    return new ushort[] { 0x010C, 0x010D, 0x010E, 0x010F, 0x0110, 0x0111 };
                case UOBiomeType.Lava:
                    return new ushort[] { 0x01F4, 0x01F5, 0x01F6, 0x01F7, 0x01F8 };
                case UOBiomeType.Farmland:
                    return new ushort[] { 0x000A, 0x000B, 0x000C, 0x000D };
                default:
                    return new ushort[] { 0x0003 };
            }
        }

        /// <summary>
        /// Pick a random tile from the biome's tile set.
        /// </summary>
        public static ushort RandomTile(UOBiomeType biome, Random rng)
        {
            var tiles = GetTileIds(biome);
            return tiles[rng.Next(tiles.Length)];
        }

        /// <summary>
        /// Pick a tile variant using a noise-seeded cell grid so nearby tiles
        /// share the same variant within small patches, avoiding both
        /// checkerboard noise and diagonal stripe artefacts.
        /// </summary>
        public static ushort CoherentTile(UOBiomeType biome, int x, int y, int cellSize = 3)
        {
            var tiles = GetTileIds(biome);
            // Quantise to a cell grid so a small patch shares one variant
            int cx = (x >= 0 ? x : x - cellSize + 1) / cellSize;
            int cy = (y >= 0 ? y : y - cellSize + 1) / cellSize;
            // Squirrel-noise style hash – low correlation across axes
            int n = cx + 189 * cy;
            n = (n << 13) ^ n;
            n = n * (n * n * 15731 + 789221) + 1376312589;
            n &= 0x7FFFFFFF;
            return tiles[n % tiles.Length];
        }

        /// <summary>
        /// Classify a biome based on elevation (0-1), moisture (0-1), and temperature (0-1).
        /// This mimics how real-world climate zones distribute across terrain.
        /// </summary>
        public static UOBiomeType Classify(double elevation, double moisture, double temperature)
        {
            // Underwater
            if (elevation < 0.0)
                return elevation < -0.15 ? UOBiomeType.DeepWater : UOBiomeType.ShallowWater;

            // Shoreline
            if (elevation < 0.03)
                return UOBiomeType.Beach;

            // Very high = snow or mountain
            if (elevation > 0.7)
            {
                if (temperature < 0.3)
                    return UOBiomeType.Snow;
                return UOBiomeType.Mountain;
            }

            // High = rock/mountain
            if (elevation > 0.5)
            {
                if (temperature > 0.8)
                    return UOBiomeType.Lava;
                if (temperature < 0.3)
                    return UOBiomeType.Snow;
                return UOBiomeType.Rock;
            }

            // Mid-elevation: biome depends on moisture and temperature
            if (temperature > 0.7)
            {
                if (moisture > 0.6)
                    return UOBiomeType.Jungle;
                if (moisture < 0.25)
                    return UOBiomeType.Desert;
                return UOBiomeType.Grass;
            }

            if (temperature < 0.3)
            {
                if (elevation > 0.3)
                    return UOBiomeType.Snow;
                return UOBiomeType.Grass;
            }

            // Temperate
            if (moisture > 0.65)
                return UOBiomeType.Swamp;
            if (moisture > 0.45)
                return UOBiomeType.Forest;
            if (moisture > 0.25)
                return UOBiomeType.Grass;
            if (moisture > 0.1)
                return UOBiomeType.Farmland;
            return UOBiomeType.Desert;
        }
    }
}
