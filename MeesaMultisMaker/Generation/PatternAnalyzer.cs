using System;
using System.Collections.Generic;
using System.Linq;

namespace MeesaMultisMaker.Generation
{
    /// <summary>
    /// Analyzes multi structures to extract spatial patterns.
    /// Building-aware: classifies tiles as floor, wall, roof, door, stair
    /// using TileData flags when available. Uses floor-level detection from
    /// <see cref="MultiRules"/> for multi-story awareness.
    /// </summary>
    public class PatternAnalyzer
    {
        /// <summary>
        /// Floor levels detected during the current analysis pass.
        /// Used by <see cref="ClassifyTileRole(ushort,int,int)"/> for
        /// floor-aware heuristic fallback.
        /// </summary>
        private List<int> _currentFloorLevels;

        private static readonly Point3D[] NeighborOffsets = new[]
        {
            // Horizontal neighbors (same Z)
            new Point3D(-1, 0, 0), new Point3D(1, 0, 0),
            new Point3D(0, -1, 0), new Point3D(0, 1, 0),
            new Point3D(-1, -1, 0), new Point3D(1, -1, 0),
            new Point3D(-1, 1, 0), new Point3D(1, 1, 0),

            // Vertical neighbors (Z-axis)
            new Point3D(0, 0, -1), new Point3D(0, 0, 1),

            // Extended patterns (2-tile distance for larger features)
            new Point3D(-2, 0, 0), new Point3D(2, 0, 0),
            new Point3D(0, -2, 0), new Point3D(0, 2, 0)
        };

        public PatternLibrary AnalyzeStructures(List<List<PlacedObject>> trainingStructures)
        {
            var library = new PatternLibrary();
            var patternCounts = new Dictionary<string, TilePattern>();

            foreach (var structure in trainingStructures)
            {
                AnalyzeSingleStructure(structure, patternCounts, library);
            }

            foreach (var pattern in patternCounts.Values)
            {
                library.AddPattern(pattern);
            }

            library.NormalizeProbabilities();
            return library;
        }

        /// <summary>
        /// Analyze structures from multi.mul components directly,
        /// bridging the MulViewer to the generation pipeline.
        /// </summary>
        public PatternLibrary AnalyzeMultiComponents(List<List<Mul.MultiComponent>> multiEntries)
        {
            var library = new PatternLibrary();
            var patternCounts = new Dictionary<string, TilePattern>();

            foreach (var entry in multiEntries)
            {
                AnalyzeMultiEntry(entry, patternCounts, library);
            }

            foreach (var pattern in patternCounts.Values)
            {
                library.AddPattern(pattern);
            }

            library.NormalizeProbabilities();
            return library;
        }

        private void AnalyzeSingleStructure(List<PlacedObject> structure,
            Dictionary<string, TilePattern> patternCounts, PatternLibrary library)
        {
            var tileMap = new Dictionary<Point3D, PlacedObject>();
            foreach (var obj in structure)
            {
                var pos = new Point3D(obj.GridX, obj.GridY, obj.Z);
                tileMap[pos] = obj;

                ushort tileId = ParseTileId(obj.GraphicId);
                library.AllowedTiles.Add(tileId);
                library.RecordTileAtZ(tileId, obj.Z);
            }

            foreach (var obj in structure)
            {
                var centerPos = new Point3D(obj.GridX, obj.GridY, obj.Z);
                ushort centerTile = ParseTileId(obj.GraphicId);

                foreach (var offset in NeighborOffsets)
                {
                    var neighborPos = centerPos + offset;

                    if (tileMap.ContainsKey(neighborPos))
                    {
                        ushort neighborTile = ParseTileId(tileMap[neighborPos].GraphicId);

                        string signature = $"{centerTile:X4}_{offset.X}_{offset.Y}_{offset.Z}_{neighborTile:X4}";

                        if (patternCounts.ContainsKey(signature))
                        {
                            patternCounts[signature].Occurrences++;
                        }
                        else
                        {
                            patternCounts[signature] = new TilePattern(centerTile, offset, neighborTile);
                        }
                    }
                }
            }
        }

        private void AnalyzeMultiEntry(List<Mul.MultiComponent> components,
            Dictionary<string, TilePattern> patternCounts, PatternLibrary library)
        {
            if (components == null || components.Count == 0) return;

            // Normalize positions
            int minX = components.Min(c => c.X);
            int minY = components.Min(c => c.Y);

            var tileMap = new Dictionary<Point3D, Mul.MultiComponent>();
            foreach (var comp in components)
            {
                var pos = new Point3D(comp.X - minX, comp.Y - minY, comp.Z);
                tileMap[pos] = comp;

                library.AllowedTiles.Add(comp.TileId);
                library.RecordTileAtZ(comp.TileId, comp.Z);
            }

            foreach (var comp in components)
            {
                var centerPos = new Point3D(comp.X - minX, comp.Y - minY, comp.Z);
                ushort centerTile = comp.TileId;

                foreach (var offset in NeighborOffsets)
                {
                    var neighborPos = centerPos + offset;

                    if (tileMap.ContainsKey(neighborPos))
                    {
                        ushort neighborTile = tileMap[neighborPos].TileId;

                        string signature = $"{centerTile:X4}_{offset.X}_{offset.Y}_{offset.Z}_{neighborTile:X4}";

                        if (patternCounts.ContainsKey(signature))
                        {
                            patternCounts[signature].Occurrences++;
                        }
                        else
                        {
                            patternCounts[signature] = new TilePattern(centerTile, offset, neighborTile);
                        }
                    }
                }
            }
        }

        private ushort ParseTileId(string graphicId)
        {
            if (string.IsNullOrEmpty(graphicId))
                return 0;

            string cleaned = graphicId.Replace("0x", "").Replace("0X", "").Trim();

            if (ushort.TryParse(cleaned, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out ushort result))
            {
                return result;
            }

            return 0;
        }

        /// <summary>
        /// Analyzes structural zones of a building multi using TileData flags.
        /// Classifies tiles as floor, wall, roof, door, stair based on flags.
        /// Falls back to floor-level-aware Z heuristics when TileData is not available.
        /// </summary>
        public StructureZones AnalyzeZones(List<PlacedObject> structure)
        {
            if (structure == null || structure.Count == 0)
                return null;

            var zones = new StructureZones();

            int minX = structure.Min(o => o.GridX);
            int maxX = structure.Max(o => o.GridX);
            int minY = structure.Min(o => o.GridY);
            int maxY = structure.Max(o => o.GridY);

            zones.Width = maxX - minX + 1;
            zones.Height = maxY - minY + 1;
            zones.MinX = minX;
            zones.MinY = minY;

            // Detect floor levels for multi-story classification
            _currentFloorLevels = MultiRules.DetectFloorLevels(structure.Select(o => o.Z));
            zones.FloorLevels = _currentFloorLevels;

            foreach (var obj in structure)
            {
                ushort tileId = ParseTileId(obj.GraphicId);
                var pos = new Point3D(obj.GridX - minX, obj.GridY - minY, obj.Z);
                var tile = new TileInstance(tileId, pos, obj.Flags);

                // Classify tile by its role in the building
                var role = ClassifyTileRole(tileId, obj.Z, obj.Flags);

                switch (role)
                {
                    case TileRole.Floor:
                        zones.FloorTiles.Add(tile);
                        break;
                    case TileRole.Wall:
                        zones.WallTiles.Add(tile);
                        break;
                    case TileRole.Roof:
                        zones.RoofTiles.Add(tile);
                        break;
                    case TileRole.Door:
                        zones.DoorTiles.Add(tile);
                        break;
                    case TileRole.Stair:
                        zones.StairTiles.Add(tile);
                        break;
                    default:
                        zones.OtherTiles.Add(tile);
                        break;
                }
            }

            return zones;
        }

        /// <summary>
        /// Analyze zones from multi.mul components directly.
        /// </summary>
        public StructureZones AnalyzeZonesFromMulti(List<Mul.MultiComponent> components)
        {
            if (components == null || components.Count == 0)
                return null;

            var zones = new StructureZones();

            int minX = components.Min(c => c.X);
            int maxX = components.Max(c => c.X);
            int minY = components.Min(c => c.Y);
            int maxY = components.Max(c => c.Y);

            zones.Width = maxX - minX + 1;
            zones.Height = maxY - minY + 1;
            zones.MinX = minX;
            zones.MinY = minY;

            // Detect floor levels for multi-story classification
            _currentFloorLevels = MultiRules.DetectFloorLevels(components.Select(c => (int)c.Z));
            zones.FloorLevels = _currentFloorLevels;

            foreach (var comp in components)
            {
                var pos = new Point3D(comp.X - minX, comp.Y - minY, comp.Z);
                var tile = new TileInstance(comp.TileId, pos, comp.Flags);

                var role = ClassifyTileRole(comp.TileId, comp.Z, comp.Flags);

                switch (role)
                {
                    case TileRole.Floor:
                        zones.FloorTiles.Add(tile);
                        break;
                    case TileRole.Wall:
                        zones.WallTiles.Add(tile);
                        break;
                    case TileRole.Roof:
                        zones.RoofTiles.Add(tile);
                        break;
                    case TileRole.Door:
                        zones.DoorTiles.Add(tile);
                        break;
                    case TileRole.Stair:
                        zones.StairTiles.Add(tile);
                        break;
                    default:
                        zones.OtherTiles.Add(tile);
                        break;
                }
            }

            return zones;
        }

        /// <summary>
        /// Classify what role a tile plays in a building structure.
        /// Uses TileData flags (via a static TileDataReader if loaded)
        /// and falls back to floor-level-aware Z heuristics via
        /// <see cref="MultiRules.ClassifyRole"/>.
        /// </summary>
        private TileRole ClassifyTileRole(ushort tileId, int z, int flags)
        {
            return ClassifyTileRole(tileId, z, flags, _currentFloorLevels);
        }

        /// <summary>
        /// Classify with explicit floor levels for multi-story awareness.
        /// </summary>
        private TileRole ClassifyTileRole(ushort tileId, int z, int flags, List<int> floorLevels)
        {
            return MultiRules.ClassifyRole(tileId, z, flags, floorLevels);
        }
    }

    /// <summary>
    /// Structural role of a tile in a building.
    /// </summary>
    public enum TileRole
    {
        Floor,
        Wall,
        Roof,
        Door,
        Stair,
        Other
    }

    /// <summary>
    /// Represents structural zones in a building multi.
    /// Building-aware: classifies by structural role (floor, wall, roof, etc.)
    /// instead of arbitrary spatial zones.
    /// </summary>
    public class StructureZones
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public int MinX { get; set; }
        public int MinY { get; set; }

        /// <summary>
        /// Detected floor levels (Z values) using <see cref="MultiRules.DetectFloorLevels"/>.
        /// Populated during analysis for use by downstream generation.
        /// </summary>
        public List<int> FloorLevels { get; set; } = new List<int>();

        public List<TileInstance> FloorTiles { get; set; } = new List<TileInstance>();
        public List<TileInstance> WallTiles { get; set; } = new List<TileInstance>();
        public List<TileInstance> RoofTiles { get; set; } = new List<TileInstance>();
        public List<TileInstance> DoorTiles { get; set; } = new List<TileInstance>();
        public List<TileInstance> StairTiles { get; set; } = new List<TileInstance>();
        public List<TileInstance> OtherTiles { get; set; } = new List<TileInstance>();

        // Legacy compatibility
        public List<TileInstance> BowTiles { get { return FloorTiles; } }
        public List<TileInstance> MidshipTiles { get { return WallTiles; } }
        public List<TileInstance> SternTiles { get { return RoofTiles; } }
        public List<TileInstance> VerticalFeatures { get { return OtherTiles; } }

        public override string ToString()
        {
            string floorInfo = FloorLevels.Count > 0 ? $", Floors:{FloorLevels.Count}" : "";
            return $"Zones: {Width}x{Height} (Floor:{FloorTiles.Count}, Wall:{WallTiles.Count}, " +
                   $"Roof:{RoofTiles.Count}, Door:{DoorTiles.Count}, Stair:{StairTiles.Count}, Other:{OtherTiles.Count}{floorInfo})";
        }
    }

    /// <summary>
    /// Static accessor for TileData lookup. Set by the application when TileData is loaded.
    /// This avoids passing TileDataReader through the entire generation pipeline.
    /// </summary>
    public static class TileDataLookup
    {
        private static Mul.TileDataReader _reader;

        public static void SetReader(Mul.TileDataReader reader)
        {
            _reader = reader;
        }

        public static Mul.ItemTileData GetItemData(ushort itemId)
        {
            if (_reader == null || !_reader.IsLoaded)
                return null;
            return _reader.GetItemTile(itemId);
        }
    }
}
