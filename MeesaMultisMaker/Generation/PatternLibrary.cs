using System.Collections.Generic;
using System.Linq;

namespace MeesaMultisMaker.Generation
{
    /// <summary>
    /// Represents a learned spatial pattern between tiles
    /// </summary>
    public class TilePattern
    {
        public ushort CenterTileId { get; set; }
        public Point3D Offset { get; set; }
        public ushort NeighborTileId { get; set; }
        public int Occurrences { get; set; }
        public float Probability { get; set; }
        public bool IsAllowed { get; set; }

        public TilePattern(ushort centerTileId, Point3D offset, ushort neighborTileId)
        {
            CenterTileId = centerTileId;
            Offset = offset;
            NeighborTileId = neighborTileId;
            Occurrences = 1;
            Probability = 1.0f;
            IsAllowed = true;
        }

        public override string ToString()
        {
            return $"0x{CenterTileId:X4} + {Offset} -> 0x{NeighborTileId:X4} (p={Probability:F2})";
        }

        public string GetAdjacencyKey()
        {
            return $"{CenterTileId}_{Offset.X}_{Offset.Y}_{Offset.Z}_{NeighborTileId}";
        }
    }

    /// <summary>
    /// Collection of patterns learned from training data.
    /// Tracks which Z levels each tile appears at and groups tiles by Z level.
    /// </summary>
    public class PatternLibrary
    {
        public HashSet<ushort> AllowedTiles { get; private set; }
        public List<TilePattern> Patterns { get; private set; }
        public Dictionary<ushort, List<TilePattern>> PatternsByCenter { get; private set; }
        public HashSet<string> AllowedAdjacencies { get; private set; }

        // Z-level tracking: which tiles appear at which Z levels
        private readonly Dictionary<ushort, HashSet<int>> _tileZLevels;
        private readonly HashSet<int> _observedZLevels;

        public PatternLibrary()
        {
            AllowedTiles = new HashSet<ushort>();
            Patterns = new List<TilePattern>();
            PatternsByCenter = new Dictionary<ushort, List<TilePattern>>();
            AllowedAdjacencies = new HashSet<string>();
            _tileZLevels = new Dictionary<ushort, HashSet<int>>();
            _observedZLevels = new HashSet<int>();
        }

        public void AddPattern(TilePattern pattern)
        {
            Patterns.Add(pattern);
            AllowedTiles.Add(pattern.CenterTileId);
            AllowedTiles.Add(pattern.NeighborTileId);

            if (!PatternsByCenter.ContainsKey(pattern.CenterTileId))
                PatternsByCenter[pattern.CenterTileId] = new List<TilePattern>();

            PatternsByCenter[pattern.CenterTileId].Add(pattern);
            AllowedAdjacencies.Add(pattern.GetAdjacencyKey());
        }

        /// <summary>
        /// Record that a tile was observed at a specific Z level in training data.
        /// </summary>
        public void RecordTileAtZ(ushort tileId, int z)
        {
            if (!_tileZLevels.ContainsKey(tileId))
                _tileZLevels[tileId] = new HashSet<int>();
            _tileZLevels[tileId].Add(z);
            _observedZLevels.Add(z);
        }

        /// <summary>
        /// Get all Z levels that were observed in training data.
        /// </summary>
        public List<int> GetObservedZLevels()
        {
            var sorted = _observedZLevels.ToList();
            sorted.Sort();
            return sorted;
        }

        /// <summary>
        /// Get tiles grouped by the Z levels they appeared at.
        /// </summary>
        public Dictionary<int, List<ushort>> GetTilesByZLevel()
        {
            var result = new Dictionary<int, List<ushort>>();
            foreach (var kvp in _tileZLevels)
            {
                foreach (int z in kvp.Value)
                {
                    if (!result.ContainsKey(z))
                        result[z] = new List<ushort>();
                    if (!result[z].Contains(kvp.Key))
                        result[z].Add(kvp.Key);
                }
            }
            return result;
        }

        public void NormalizeProbabilities()
        {
            foreach (var centerTile in PatternsByCenter.Keys.ToList())
            {
                var patterns = PatternsByCenter[centerTile];
                int totalOccurrences = patterns.Sum(p => p.Occurrences);

                if (totalOccurrences > 0)
                {
                    foreach (var pattern in patterns)
                    {
                        pattern.Probability = (float)pattern.Occurrences / totalOccurrences;
                    }
                }
            }
        }

        public List<TilePattern> GetPatternsFor(ushort tileId)
        {
            if (PatternsByCenter.ContainsKey(tileId))
                return PatternsByCenter[tileId];
            return new List<TilePattern>();
        }

        public bool IsAdjacencyAllowed(ushort centerTile, Point3D offset, ushort neighborTile)
        {
            string key = $"{centerTile}_{offset.X}_{offset.Y}_{offset.Z}_{neighborTile}";
            return AllowedAdjacencies.Contains(key);
        }

        public List<ushort> GetValidNeighbors(ushort centerTile, Point3D offset)
        {
            var validNeighbors = new List<ushort>();
            var patterns = GetPatternsFor(centerTile);

            foreach (var pattern in patterns)
            {
                if (pattern.Offset == offset && pattern.IsAllowed)
                {
                    validNeighbors.Add(pattern.NeighborTileId);
                }
            }

            return validNeighbors;
        }

        public override string ToString()
        {
            return $"PatternLibrary: {AllowedTiles.Count} unique tiles, {Patterns.Count} patterns, " +
                   $"{AllowedAdjacencies.Count} adjacencies, {_observedZLevels.Count} Z-levels";
        }
    }
}
