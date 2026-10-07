using System;
using System.Collections.Generic;

namespace MeesaMultisMaker.Biome
{
    /// <summary>
    /// Represents a sampled land tile within a biome
    /// </summary>
    public class BiomeLandSample
    {
        public int RelativeX { get; set; }
        public int RelativeY { get; set; }
        public ushort TileId { get; set; }
        public sbyte Z { get; set; }
    }

    /// <summary>
    /// Represents a sampled static item within a biome
    /// </summary>
    public class BiomeStaticSample
    {
        public int RelativeX { get; set; }
        public int RelativeY { get; set; }
        public ushort ItemId { get; set; }
        public sbyte Z { get; set; }
        public ushort Hue { get; set; }
    }

    /// <summary>
    /// A biome brush captures a sampled area of the map including
    /// both land tiles and statics for use in generating similar terrain.
    /// </summary>
    public class BiomeBrush
    {
        public string Name { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public DateTime CreatedDate { get; set; }
        public string SourceFacet { get; set; }
        public int SourceX { get; set; }
        public int SourceY { get; set; }

        public List<BiomeLandSample> LandTiles { get; set; } = new List<BiomeLandSample>();
        public List<BiomeStaticSample> Statics { get; set; } = new List<BiomeStaticSample>();

        public override string ToString()
        {
            return $"{Name} ({Width}x{Height}) - {LandTiles.Count} tiles, {Statics.Count} statics";
        }
    }
}
