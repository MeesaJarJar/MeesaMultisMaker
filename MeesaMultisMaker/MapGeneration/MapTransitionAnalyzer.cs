using System;
using System.Collections.Generic;
using System.Linq;
using MeesaMultisMaker.Mul;

namespace MeesaMultisMaker.MapGeneration
{
    /// <summary>
    /// Learns 3x3 tile placement grammar from a source UO map using the existing
    /// <see cref="MapReader.Pattern3x3"/> infrastructure, then samples tile IDs
    /// during world generation that produce seamless, authentic-looking results.
    ///
    /// Approach: non-parametric texture synthesis (Efros-Leung style).
    ///   1. Scan source map with MapReader.CountLandTilePatterns3x3
    ///   2. Prune bottom 10% of rare patterns (noise / one-off artefacts)
    ///   3. Build lookup: 5 known neighbors (TL, TM, TR, ML, BL) → list of (center MM, count)
    ///   4. At generation time (raster order): look up already-placed neighbors,
    ///      pick the most common center tile, with multi-level backoff
    /// </summary>
    public class MapTransitionAnalyzer
    {
        // ── Reverse lookup: tile ID → biome ──────────────────────────
        private readonly Dictionary<ushort, UOBiomeType> _tileToBiome = new Dictionary<ushort, UOBiomeType>();

        // ── 4-neighbor lookup (TL, TM, TR, ML) → [(MM, count)] ─────
        // Primary table. All 4 neighbors are available in raster order
        // (TL, TM, TR from row above; ML from current row left).
        private readonly Dictionary<NeighborKey4, List<(ushort centerTile, int count)>> _table4
            = new Dictionary<NeighborKey4, List<(ushort, int)>>();

        // ── 3-neighbor backoff (TM, ML, TR) → [(MM, count)] ────────
        private readonly Dictionary<NeighborKey3, List<(ushort centerTile, int count)>> _table3
            = new Dictionary<NeighborKey3, List<(ushort, int)>>();

        // ── 2-neighbor backoff (TM, ML) → [(MM, count)] ─────────────
        private readonly Dictionary<NeighborKey2, List<(ushort centerTile, int count)>> _table2
            = new Dictionary<NeighborKey2, List<(ushort, int)>>();

        // ── Biome-based backoff table (center biome, 8-neighbor biome pattern) ──
        private readonly Dictionary<(UOBiomeType center, uint pattern), List<(ushort tileId, int count)>> _biomeTable
            = new Dictionary<(UOBiomeType, uint), List<(ushort, int)>>();

        // ── Stats ────────────────────────────────────────────────────
        public int TotalPatternsLearned { get; private set; }
        public int PatternsAfterPruning { get; private set; }
        public int PrunedPatterns { get; private set; }
        public int TotalSamples { get; private set; }

        /// <summary>
        /// Build the transition grammar from a source map.
        /// Uses <see cref="MapReader.CountLandTilePatterns3x3"/> for the raw pattern scan.
        /// </summary>
        /// <param name="map">Source map data.</param>
        /// <param name="prunePercentile">Remove patterns below this percentile (0.0–1.0). Default 0.10 = bottom 10%.</param>
        public void LearnFromMap(MapData map, double prunePercentile = 0.10)
        {
            _table4.Clear();
            _table3.Clear();
            _table2.Clear();
            _biomeTable.Clear();
            BuildReverseLookup();

            var patternCounts = MapReader.CountLandTilePatterns3x3(map);
            TotalPatternsLearned = patternCounts.Count;
            TotalSamples = patternCounts.Values.Sum();

            var sortedByCount = patternCounts.OrderBy(kv => kv.Value).ToList();
            int pruneIndex = (int)(prunePercentile * sortedByCount.Count);
            int countThreshold = sortedByCount[Math.Max(0, Math.Min(sortedByCount.Count - 1, pruneIndex))].Value;

            foreach (var kvp in patternCounts)
            {
                if (kvp.Value < countThreshold) continue;

                var p = kvp.Key;
                int count = kvp.Value;

                // 4-neighbor key: TL, TM, TR, ML → predict MM
                // All 4 are available in raster order (TR is at x+1, y-1 which is already placed)
                var key4 = new NeighborKey4(p.TL, p.TM, p.TR, p.ML);
                if (!_table4.TryGetValue(key4, out var list4))
                {
                    list4 = new List<(ushort, int)>();
                    _table4[key4] = list4;
                }
                list4.Add((p.MM, count));

                // 3-neighbor key: TM, ML, TR → predict MM
                var key3 = new NeighborKey3(p.TM, p.ML, p.TR);
                if (!_table3.TryGetValue(key3, out var list3))
                {
                    list3 = new List<(ushort, int)>();
                    _table3[key3] = list3;
                }
                list3.Add((p.MM, count));

                // 2-neighbor key: TM, ML → predict MM
                var key2 = new NeighborKey2(p.TM, p.ML);
                if (!_table2.TryGetValue(key2, out var list2))
                {
                    list2 = new List<(ushort, int)>();
                    _table2[key2] = list2;
                }
                list2.Add((p.MM, count));
            }

            foreach (var list in _table4.Values) list.Sort((a, b) => b.count.CompareTo(a.count));
            foreach (var list in _table3.Values) list.Sort((a, b) => b.count.CompareTo(a.count));
            foreach (var list in _table2.Values) list.Sort((a, b) => b.count.CompareTo(a.count));

            BuildBiomeTable(map, prunePercentile);

            PatternsAfterPruning = _table4.Count;
            PrunedPatterns = TotalPatternsLearned - _table4.Count;
        }

        /// <summary>
        /// Sample a tile ID for position (x, y) given already-placed neighbors.
        /// Raster order assumed: TL, TM, TR, ML, BL are already placed.
        ///
        /// Backoff chain:
        ///   Level 1: exact 5-neighbor match (TL, TM, TR, ML, BL)
        ///   Level 2: 3-neighbor match (TM, ML, TR)
        ///   Level 3: 2-neighbor match (TM, ML)
        ///   Level 4: biome-based 8-neighbor match
        ///   Level 5: CoherentTile hash fallback
        /// </summary>
        public ushort SampleTile(LandTile[,] placedTiles, int x, int y, Random rng, bool preferMostCommon = true)
        {
            int w = placedTiles.GetLength(0);
            int h = placedTiles.GetLength(1);

            if (y == 0 || x == 0)
                return FallbackTile(x, y, placedTiles);

            ushort tl = GetTileId(placedTiles, x - 1, y - 1);
            ushort tm = GetTileId(placedTiles, x, y - 1);
            ushort tr = (x < w - 1) ? GetTileId(placedTiles, x + 1, y - 1) : (ushort)0;
            ushort ml = GetTileId(placedTiles, x - 1, y);

            // Level 1: 4-neighbor (TL, TM, TR, ML) — all placed in raster order
            var key4 = new NeighborKey4(tl, tm, tr, ml);
            if (_table4.TryGetValue(key4, out var entries4))
            {
                if (preferMostCommon)
                    return entries4[0].centerTile;
                return WeightedSample(entries4, rng);
            }

            // Level 2: 3-neighbor (TM, ML, TR)
            var key3 = new NeighborKey3(tm, ml, tr);
            if (_table3.TryGetValue(key3, out var entries3))
            {
                if (preferMostCommon)
                    return entries3[0].centerTile;
                return WeightedSample(entries3, rng);
            }

            // Level 3: 2-neighbor (TM, ML)
            var key2 = new NeighborKey2(tm, ml);
            if (_table2.TryGetValue(key2, out var entries2))
            {
                if (preferMostCommon)
                    return entries2[0].centerTile;
                return WeightedSample(entries2, rng);
            }

            // Level 4: biome-based fallback
            UOBiomeType biome = ClassifyTile(tm);
            uint biomePattern = EncodeBiomeNeighbors(placedTiles, x, y, w, h);
            var biomeKey = (biome, biomePattern);
            if (_biomeTable.TryGetValue(biomeKey, out var biomeEntries))
            {
                if (preferMostCommon)
                    return biomeEntries[0].tileId;
                return WeightedSample(biomeEntries, rng);
            }

            // Level 5: CoherentTile fallback
            return UOBiomes.CoherentTile(biome, x, y);
        }

        /// <summary>
        /// Sample using a pre-classified biome map (for world generation pipeline).
        /// Uses biome-based table primarily, with CoherentTile fallback.
        /// </summary>
        public ushort SampleTileByBiome(UOBiomeType centerBiome, UOBiomeType[,] biomeMap, int x, int y, Random rng, bool preferMostCommon = true)
        {
            int w = biomeMap.GetLength(0);
            int h = biomeMap.GetLength(1);

            if (x > 0 && x < w - 1 && y > 0 && y < h - 1)
            {
                uint pattern = EncodeBiomeNeighborsFromMap(biomeMap, x, y);
                var key = (centerBiome, pattern);
                if (_biomeTable.TryGetValue(key, out var entries))
                {
                    if (preferMostCommon)
                        return entries[0].tileId;
                    return WeightedSample(entries, rng);
                }
            }

            return UOBiomes.CoherentTile(centerBiome, x, y);
        }

        /// <summary>
        /// Get the biome for a tile ID using the learned reverse lookup.
        /// </summary>
        public UOBiomeType ClassifyTile(ushort tileId)
        {
            if (_tileToBiome.TryGetValue(tileId, out var biome))
                return biome;
            return HeuristicClassify(tileId);
        }

        /// <summary>
        /// Get a summary of learned pattern statistics.
        /// </summary>
        public string GetStats()
        {
            return $"Patterns learned: {TotalPatternsLearned}\n" +
                   $"After pruning (bottom 10%): {PatternsAfterPruning}\n" +
                   $"Pruned: {PrunedPatterns}\n" +
                   $"Total samples: {TotalSamples}\n" +
                   $"4-neighbor entries: {_table4.Count}\n" +
                   $"3-neighbor entries: {_table3.Count}\n" +
                   $"2-neighbor entries: {_table2.Count}\n" +
                   $"Biome entries: {_biomeTable.Count}";
        }

        // ================================================================
        //  Private helpers
        // ================================================================

        private void BuildReverseLookup()
        {
            _tileToBiome.Clear();
            foreach (UOBiomeType biome in Enum.GetValues(typeof(UOBiomeType)))
            {
                foreach (ushort tileId in UOBiomes.GetTileIds(biome))
                {
                    if (!_tileToBiome.ContainsKey(tileId))
                        _tileToBiome[tileId] = biome;
                }
            }
        }

        private void BuildBiomeTable(MapData map, double prunePercentile)
        {
            int w = map.Width;
            int h = map.Height;

            // Classify biomes for the whole map
            var biomeMap = new UOBiomeType[w, h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    biomeMap[x, y] = ClassifyTile(map.Tiles[x, y]?.TileId ?? 0);

            // Count biome-pattern frequencies
            var biomeCounts = new Dictionary<(int center, uint pattern), Dictionary<ushort, int>>();

            for (int y = 1; y < h - 1; y++)
            {
                for (int x = 1; x < w - 1; x++)
                {
                    UOBiomeType center = biomeMap[x, y];
                    ushort tileId = map.Tiles[x, y]?.TileId ?? 0;
                    uint pattern = EncodeBiomeNeighborsFromMap(biomeMap, x, y);

                    int centerInt = (int)center;
                    var key = (centerInt, pattern);
                    if (!biomeCounts.TryGetValue(key, out var tileCounts))
                    {
                        tileCounts = new Dictionary<ushort, int>();
                        biomeCounts[key] = tileCounts;
                    }
                    if (!tileCounts.TryGetValue(tileId, out int c))
                        tileCounts[tileId] = 1;
                    else
                        tileCounts[tileId] = c + 1;
                }
            }

            // Prune
            var allCounts = biomeCounts.Values.Select(d => d.Values.Sum()).OrderBy(c => c).ToList();
            int thresholdIndex = (int)(prunePercentile * allCounts.Count);
            int threshold = allCounts[Math.Max(0, Math.Min(allCounts.Count - 1, thresholdIndex))];

            foreach (var kvp in biomeCounts)
            {
                if (kvp.Value.Values.Sum() < threshold) continue;

                var center = (UOBiomeType)kvp.Key.center;
                var sorted = kvp.Value
                    .OrderByDescending(kv => kv.Value)
                    .Select(kv => (kv.Key, kv.Value))
                    .ToList();

                _biomeTable[(center, kvp.Key.pattern)] = sorted;
            }
        }

        private static ushort GetTileId(LandTile[,] tiles, int x, int y)
        {
            if (x < 0 || y < 0 || x >= tiles.GetLength(0) || y >= tiles.GetLength(1))
                return 0;
            return tiles[x, y]?.TileId ?? 0;
        }

        private ushort FallbackTile(int x, int y, LandTile[,] placedTiles)
        {
            // Edge tiles: use coherent hash
            UOBiomeType biome = y > 0 ? ClassifyTile(GetTileId(placedTiles, x, y - 1)) : UOBiomeType.Grass;
            return UOBiomes.CoherentTile(biome, x, y);
        }

        private uint EncodeBiomeNeighbors(LandTile[,] tiles, int x, int y, int w, int h)
        {
            // Quick biome lookup for neighbors
            UOBiomeType GetBiome(int nx, int ny)
            {
                if (nx < 0 || nx >= w || ny < 0 || ny >= h) return UOBiomeType.DeepWater;
                return ClassifyTile(GetTileId(tiles, nx, ny));
            }

            uint packed = 0;
            packed |= ((uint)(int)GetBiome(x, y - 1) & 0xF);
            packed |= ((uint)(int)GetBiome(x + 1, y - 1) & 0xF) << 4;
            packed |= ((uint)(int)GetBiome(x + 1, y) & 0xF) << 8;
            packed |= ((uint)(int)GetBiome(x + 1, y + 1) & 0xF) << 12;
            packed |= ((uint)(int)GetBiome(x, y + 1) & 0xF) << 16;
            packed |= ((uint)(int)GetBiome(x - 1, y + 1) & 0xF) << 20;
            packed |= ((uint)(int)GetBiome(x - 1, y) & 0xF) << 24;
            packed |= ((uint)(int)GetBiome(x - 1, y - 1) & 0xF) << 28;
            return packed;
        }

        private static uint EncodeBiomeNeighborsFromMap(UOBiomeType[,] biomes, int x, int y)
        {
            uint packed = 0;
            packed |= ((uint)(int)biomes[x, y - 1] & 0xF);
            packed |= ((uint)(int)biomes[x + 1, y - 1] & 0xF) << 4;
            packed |= ((uint)(int)biomes[x + 1, y] & 0xF) << 8;
            packed |= ((uint)(int)biomes[x + 1, y + 1] & 0xF) << 12;
            packed |= ((uint)(int)biomes[x, y + 1] & 0xF) << 16;
            packed |= ((uint)(int)biomes[x - 1, y + 1] & 0xF) << 20;
            packed |= ((uint)(int)biomes[x - 1, y] & 0xF) << 24;
            packed |= ((uint)(int)biomes[x - 1, y - 1] & 0xF) << 28;
            return packed;
        }

        private ushort WeightedSample(List<(ushort centerTile, int count)> entries, Random rng)
        {
            int total = entries.Sum(e => e.count);
            int roll = rng.Next(total);
            int cumulative = 0;
            foreach (var entry in entries)
            {
                cumulative += entry.count;
                if (roll < cumulative)
                    return entry.centerTile;
            }
            return entries[entries.Count - 1].centerTile;
        }

        private static UOBiomeType HeuristicClassify(ushort tileId)
        {
            if (tileId >= 0x00A8 && tileId <= 0x00AB) return UOBiomeType.DeepWater;
            if (tileId == 0x0136 || tileId == 0x0137) return UOBiomeType.ShallowWater;
            if (tileId >= 0x0016 && tileId <= 0x001B) return UOBiomeType.Beach;
            if (tileId >= 0x0003 && tileId <= 0x0009) return UOBiomeType.Grass;
            if (tileId >= 0x009C && tileId <= 0x00A3) return UOBiomeType.Forest;
            if (tileId >= 0x0230 && tileId <= 0x0233) return UOBiomeType.Jungle;
            if (tileId >= 0x0578 && tileId <= 0x057B) return UOBiomeType.Swamp;
            if (tileId >= 0x002C && tileId <= 0x0031) return UOBiomeType.Desert;
            if (tileId >= 0x006A && tileId <= 0x006F) return UOBiomeType.Rock;
            if (tileId >= 0x00AC && tileId <= 0x00B2) return UOBiomeType.Mountain;
            if (tileId >= 0x010C && tileId <= 0x0111) return UOBiomeType.Snow;
            if (tileId >= 0x01F4 && tileId <= 0x01F8) return UOBiomeType.Lava;
            if (tileId >= 0x000A && tileId <= 0x000D) return UOBiomeType.Farmland;
            return UOBiomeType.Grass;
        }

        // ================================================================
        //  Neighbor key structs (value types for dictionary keys)
        // ================================================================

        private struct NeighborKey4 : IEquatable<NeighborKey4>
        {
            public ushort TL, TM, TR, ML;
            public NeighborKey4(ushort tl, ushort tm, ushort tr, ushort ml)
            {
                TL = tl; TM = tm; TR = tr; ML = ml;
            }
            public bool Equals(NeighborKey4 other) => TL == other.TL && TM == other.TM && TR == other.TR && ML == other.ML;
            public override bool Equals(object obj) => obj is NeighborKey4 k && Equals(k);
            public override int GetHashCode()
            {
                unchecked
                {
                    int h = 17;
                    h = h * 31 + TL; h = h * 31 + TM; h = h * 31 + TR;
                    h = h * 31 + ML;
                    return h;
                }
            }
        }

        private struct NeighborKey3 : IEquatable<NeighborKey3>
        {
            public ushort A, B, C;
            public NeighborKey3(ushort a, ushort b, ushort c) { A = a; B = b; C = c; }
            public bool Equals(NeighborKey3 other) => A == other.A && B == other.B && C == other.C;
            public override bool Equals(object obj) => obj is NeighborKey3 k && Equals(k);
            public override int GetHashCode()
            {
                unchecked { int h = 17; h = h * 31 + A; h = h * 31 + B; h = h * 31 + C; return h; }
            }
        }

        private struct NeighborKey2 : IEquatable<NeighborKey2>
        {
            public ushort A, B;
            public NeighborKey2(ushort a, ushort b) { A = a; B = b; }
            public bool Equals(NeighborKey2 other) => A == other.A && B == other.B;
            public override bool Equals(object obj) => obj is NeighborKey2 k && Equals(k);
            public override int GetHashCode()
            {
                unchecked { int h = 17; h = h * 31 + A; h = h * 31 + B; return h; }
            }
        }
    }
}
