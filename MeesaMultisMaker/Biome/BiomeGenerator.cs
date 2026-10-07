using System;
using System.Collections.Generic;
using System.Linq;

namespace MeesaMultisMaker.Biome
{
    /// <summary>
    /// All statics at a single (x,y) tile position, kept as one atomic unit.
    /// In UO multi-part objects (trees, fountains, walls) have their parts
    /// stacked at the same tile coordinate — this guarantees they are never
    /// split apart during generation.
    /// </summary>
    public class StaticGroup
    {
        public List<StaticGroupMember> Members { get; set; } = new List<StaticGroupMember>();
        /// <summary>Land tile type this group was found on</summary>
        public ushort LandTileId { get; set; }
        /// <summary>How many times this exact group appeared in the sample</summary>
        public int Occurrences { get; set; } = 1;

        public string GetSignature()
        {
            var sorted = Members
                .OrderBy(m => m.ItemId)
                .ThenBy(m => m.RelativeZ)
                .ThenBy(m => m.Hue);
            return string.Join("|", sorted.Select(m => $"{m.ItemId:X4},{m.RelativeZ},{m.Hue}"));
        }
    }

    /// <summary>
    /// One static within a same-tile group
    /// </summary>
    public class StaticGroupMember
    {
        public ushort ItemId { get; set; }
        /// <summary>Z offset relative to the land tile Z at this position</summary>
        public sbyte RelativeZ { get; set; }
        public ushort Hue { get; set; }
    }

    /// <summary>
    /// Records that a specific group signature was observed adjacent to
    /// another group signature at a given offset in the sample.
    /// Used to propagate multi-tile structures during generation.
    /// </summary>
    public class NeighborLink
    {
        public StaticGroup Neighbor { get; set; }
        public int DeltaX { get; set; }
        public int DeltaY { get; set; }
        public int Count { get; set; }
    }

    /// <summary>
    /// A multi-tile template captures a complete multi-tile structure
    /// (e.g., a tree with trunk and canopy spanning multiple tiles).
    /// All parts are placed atomically during generation.
    /// </summary>
    public class MultiTileTemplate
    {
        public List<MultiTilePart> Parts { get; set; } = new List<MultiTilePart>();
        public int Occurrences { get; set; } = 1;

        public string GetSignature()
        {
            var sorted = Parts
                .OrderBy(p => p.DeltaX)
                .ThenBy(p => p.DeltaY);
            return string.Join("||", sorted.Select(p => $"{p.DeltaX},{p.DeltaY}:{p.Group.GetSignature()}"));
        }
    }

    /// <summary>
    /// One part of a multi-tile template at a specific offset from the anchor.
    /// </summary>
    public class MultiTilePart
    {
        public int DeltaX { get; set; }
        public int DeltaY { get; set; }
        public StaticGroup Group { get; set; }
        /// <summary>Original land Z at this part minus land Z at the anchor</summary>
        public sbyte LandZOffset { get; set; }
    }

    /// <summary>
    /// WFC-inspired biome pattern generator.
    /// Learns tile frequencies, adjacency constraints, and static placement
    /// patterns from a sampled biome brush, then generates new terrain
    /// matching those learned distributions.
    ///
    /// Statics are grouped per-tile so every part of a multi-part object
    /// (tree trunk + canopy + branches at the same coordinate) is always
    /// placed together.  Neighbor propagation then handles structures that
    /// span multiple tile positions.
    /// </summary>
    public class BiomeGenerator
    {
        private readonly BiomeBrush _brush;
        private readonly Random _random;

        // Learned tile frequency (probability of each tile type)
        private Dictionary<ushort, float> _tileProbabilities;
        private Dictionary<ushort, int> _tileFrequency;

        // Adjacency rules: "tileA_dx_dy_tileB" -> observation count
        private Dictionary<string, int> _adjacencyLookup;

        // Same-tile static groups and placement density
        private List<StaticGroup> _staticGroups;
        private float _staticDensity; // fraction of tiles that have statics

        // Neighbor links keyed by group signature
        private Dictionary<string, List<NeighborLink>> _neighborLinks;

        // Signature -> StaticGroup lookup for multi-tile structure co-occurrence
        private Dictionary<string, StaticGroup> _groupBySig;
        // Co-occurrence threshold: links at or above this ratio are mandatory
        // parts of the same multi-tile object and are always placed together
        private const float MandatoryCooccurrenceThreshold = 0.5f;

        // Z analysis
        private Dictionary<ushort, List<sbyte>> _tileZValues;
        private float _avgZ;

        // Multi-tile templates (complete structures placed atomically)
        private List<MultiTileTemplate> _templates;
        private float _templateDensity;

        // 8-connected directions for neighbor analysis
        private static readonly (int dx, int dy)[] AllDirections =
        {
            (-1, -1), (0, -1), (1, -1),
            (-1,  0),          (1,  0),
            (-1,  1), (0,  1), (1,  1)
        };

        // Cardinal directions for land tile adjacency
        private static readonly (int dx, int dy)[] Directions =
        {
            (-1, 0), (1, 0), (0, -1), (0, 1)
        };

        public BiomeGenerator(BiomeBrush brush, int? seed = null)
        {
            _brush = brush;
            _random = seed.HasValue ? new Random(seed.Value) : new Random();
            AnalyzeBrush();
        }

        /// <summary>
        /// Analyze the biome brush to extract tile frequencies,
        /// adjacency constraints, same-tile static groups, and
        /// neighbor co-occurrence links.
        /// </summary>
        private void AnalyzeBrush()
        {
            // --- Tile frequency ---
            _tileFrequency = new Dictionary<ushort, int>();
            _tileZValues = new Dictionary<ushort, List<sbyte>>();

            foreach (var tile in _brush.LandTiles)
            {
                if (!_tileFrequency.ContainsKey(tile.TileId))
                {
                    _tileFrequency[tile.TileId] = 0;
                    _tileZValues[tile.TileId] = new List<sbyte>();
                }
                _tileFrequency[tile.TileId]++;
                _tileZValues[tile.TileId].Add(tile.Z);
            }

            int totalTiles = _brush.LandTiles.Count;
            _tileProbabilities = new Dictionary<ushort, float>();
            foreach (var kvp in _tileFrequency)
                _tileProbabilities[kvp.Key] = (float)kvp.Value / Math.Max(1, totalTiles);

            _avgZ = totalTiles > 0 ? (float)_brush.LandTiles.Average(t => t.Z) : 0f;

            // --- Land tile adjacency analysis ---
            _adjacencyLookup = new Dictionary<string, int>();
            var tileLookup = new Dictionary<(int, int), BiomeLandSample>();
            foreach (var tile in _brush.LandTiles)
                tileLookup[(tile.RelativeX, tile.RelativeY)] = tile;

            foreach (var tile in _brush.LandTiles)
            {
                foreach (var (dx, dy) in Directions)
                {
                    var nk = (tile.RelativeX + dx, tile.RelativeY + dy);
                    if (tileLookup.TryGetValue(nk, out var neighbor))
                    {
                        string key = $"{tile.TileId}_{dx}_{dy}_{neighbor.TileId}";
                        if (_adjacencyLookup.ContainsKey(key))
                            _adjacencyLookup[key]++;
                        else
                            _adjacencyLookup[key] = 1;
                    }
                }
            }

            // --- Same-tile static group extraction ---
            // Build spatial index: (relX, relY) -> list of statics at that tile
            var staticsByPos = new Dictionary<(int, int), List<BiomeStaticSample>>();
            foreach (var st in _brush.Statics)
            {
                var sk = (st.RelativeX, st.RelativeY);
                if (!staticsByPos.ContainsKey(sk))
                    staticsByPos[sk] = new List<BiomeStaticSample>();
                staticsByPos[sk].Add(st);
            }

            // For each tile that has statics, build a group containing
            // ALL statics at that exact position.
            var groupSigs = new Dictionary<string, StaticGroup>();
            // Also store the per-position signature for neighbor analysis
            var positionSigs = new Dictionary<(int, int), string>();

            foreach (var kvp in staticsByPos)
            {
                var pos = kvp.Key;
                var statics = kvp.Value;

                ushort landId = 0;
                sbyte landZ = 0;
                if (tileLookup.TryGetValue(pos, out var land))
                {
                    landId = land.TileId;
                    landZ = land.Z;
                }

                var group = new StaticGroup { LandTileId = landId };
                foreach (var st in statics)
                {
                    group.Members.Add(new StaticGroupMember
                    {
                        ItemId = st.ItemId,
                        RelativeZ = (sbyte)(st.Z - landZ),
                        Hue = st.Hue
                    });
                }

                string sig = group.GetSignature();
                positionSigs[pos] = sig;

                if (groupSigs.ContainsKey(sig))
                    groupSigs[sig].Occurrences++;
                else
                    groupSigs[sig] = group;
            }

            _staticGroups = groupSigs.Values.ToList();
            _groupBySig = new Dictionary<string, StaticGroup>();
            foreach (var g in _staticGroups)
                _groupBySig[g.GetSignature()] = g;
            int tilesWithStatics = staticsByPos.Count;
            _staticDensity = totalTiles > 0 ? (float)tilesWithStatics / totalTiles : 0f;

            // --- Neighbor co-occurrence links ---
            // For each tile with statics, check all 8 neighbors.
            // If a neighbor also has statics, record the link so the
            // generator can propagate multi-tile structures.
            _neighborLinks = new Dictionary<string, List<NeighborLink>>();

            foreach (var kvp in staticsByPos)
            {
                var pos = kvp.Key;
                if (!positionSigs.TryGetValue(pos, out string centerSig))
                    continue;

                foreach (var (dx, dy) in AllDirections)
                {
                    var nPos = (pos.Item1 + dx, pos.Item2 + dy);
                    if (!positionSigs.TryGetValue(nPos, out string nbrSig))
                        continue;

                    // Get the neighbor group
                    if (!groupSigs.TryGetValue(nbrSig, out var nbrGroup))
                        continue;

                    if (!_neighborLinks.ContainsKey(centerSig))
                        _neighborLinks[centerSig] = new List<NeighborLink>();

                    // Check if we already have this exact link
                    var existing = _neighborLinks[centerSig]
                        .FirstOrDefault(l => l.Neighbor == nbrGroup && l.DeltaX == dx && l.DeltaY == dy);
                    if (existing != null)
                        existing.Count++;
                    else
                        _neighborLinks[centerSig].Add(new NeighborLink
                        {
                            Neighbor = nbrGroup,
                            DeltaX = dx,
                            DeltaY = dy,
                            Count = 1
                        });
                }
            }

            // --- Multi-tile template extraction ---
            // Connected components of static positions linked by mandatory
            // co-occurrence become atomic templates so multi-part structures
            // (trees, fountains, etc.) are always placed as complete units.
            var templatePositions = new HashSet<(int, int)>(staticsByPos.Keys);
            var templateVisited = new HashSet<(int, int)>();
            var rawTemplates = new List<MultiTileTemplate>();
            var templateSigMap = new Dictionary<string, MultiTileTemplate>();
            int templateAnchorCount = 0;

            foreach (var startPos in templatePositions)
            {
                if (templateVisited.Contains(startPos))
                    continue;

                var component = new List<(int x, int y)>();
                var bfsQ = new Queue<(int, int)>();
                bfsQ.Enqueue(startPos);
                templateVisited.Add(startPos);

                while (bfsQ.Count > 0)
                {
                    var cpos = bfsQ.Dequeue();
                    component.Add(cpos);

                    if (!positionSigs.TryGetValue(cpos, out string cSig))
                        continue;
                    int cOcc = groupSigs.ContainsKey(cSig) ? groupSigs[cSig].Occurrences : 1;

                    foreach (var (ddx, ddy) in AllDirections)
                    {
                        var np = (cpos.Item1 + ddx, cpos.Item2 + ddy);
                        if (!templatePositions.Contains(np) || templateVisited.Contains(np))
                            continue;
                        if (!positionSigs.TryGetValue(np, out string nSig))
                            continue;

                        bool mandatory = false;

                        if (_neighborLinks.TryGetValue(cSig, out var fwdLinks))
                        {
                            foreach (var fl in fwdLinks)
                            {
                                if (fl.DeltaX == ddx && fl.DeltaY == ddy &&
                                    fl.Neighbor.GetSignature() == nSig)
                                {
                                    if ((float)fl.Count / Math.Max(1, cOcc) >= MandatoryCooccurrenceThreshold)
                                        mandatory = true;
                                    break;
                                }
                            }
                        }

                        if (!mandatory && _neighborLinks.TryGetValue(nSig, out var revLinks))
                        {
                            int nOcc = groupSigs.ContainsKey(nSig) ? groupSigs[nSig].Occurrences : 1;
                            foreach (var rl in revLinks)
                            {
                                if (rl.DeltaX == -ddx && rl.DeltaY == -ddy &&
                                    rl.Neighbor.GetSignature() == cSig)
                                {
                                    if ((float)rl.Count / Math.Max(1, nOcc) >= MandatoryCooccurrenceThreshold)
                                        mandatory = true;
                                    break;
                                }
                            }
                        }

                        if (mandatory)
                        {
                            templateVisited.Add(np);
                            bfsQ.Enqueue(np);
                        }
                    }
                }

                // Subdivide large components into compact multi-tile chunks
                // using capped BFS so tree-sized structures stay intact
                if (component.Count > 25)
                {
                    var remaining = new HashSet<(int, int)>(component.Select(p => (p.x, p.y)));

                    while (remaining.Count > 0)
                    {
                        var seed = remaining.First();
                        var subComponent = new List<(int x, int y)>();
                        var subQ = new Queue<(int, int)>();
                        subQ.Enqueue(seed);
                        remaining.Remove(seed);

                        while (subQ.Count > 0 && subComponent.Count < 12)
                        {
                            var sp = subQ.Dequeue();
                            subComponent.Add(sp);

                            foreach (var (ddx, ddy) in AllDirections)
                            {
                                var snp = (sp.Item1 + ddx, sp.Item2 + ddy);
                                if (remaining.Remove(snp))
                                    subQ.Enqueue(snp);
                            }
                        }

                        templateAnchorCount++;
                        int sax = subComponent.Min(p => p.x);
                        int say = subComponent.Where(p => p.x == sax).Min(p => p.y);
                        sbyte subAnchorZ = 0;
                        if (tileLookup.TryGetValue((sax, say), out var subAnchorLand))
                            subAnchorZ = subAnchorLand.Z;
                        var sTmpl = new MultiTileTemplate { Occurrences = 1 };
                        foreach (var (scx, scy) in subComponent)
                        {
                            if (!positionSigs.TryGetValue((scx, scy), out string sPSig)) continue;
                            if (!groupSigs.TryGetValue(sPSig, out var sPGrp)) continue;
                            sbyte subPartZ = 0;
                            if (tileLookup.TryGetValue((scx, scy), out var subPartLand))
                                subPartZ = subPartLand.Z;
                            sTmpl.Parts.Add(new MultiTilePart
                            {
                                DeltaX = scx - sax,
                                DeltaY = scy - say,
                                Group = sPGrp,
                                LandZOffset = (sbyte)(subPartZ - subAnchorZ)
                            });
                        }

                        if (sTmpl.Parts.Count > 0)
                        {
                            string sSig = sTmpl.GetSignature();
                            if (templateSigMap.ContainsKey(sSig))
                                templateSigMap[sSig].Occurrences++;
                            else
                            {
                                templateSigMap[sSig] = sTmpl;
                                rawTemplates.Add(sTmpl);
                            }
                        }
                    }
                    continue;
                }

                templateAnchorCount++;
                int ax = component.Min(p => p.x);
                int ay = component.Where(p => p.x == ax).Min(p => p.y);
                sbyte tmplAnchorZ = 0;
                if (tileLookup.TryGetValue((ax, ay), out var tmplAnchorLand))
                    tmplAnchorZ = tmplAnchorLand.Z;
                var tmpl = new MultiTileTemplate { Occurrences = 1 };
                foreach (var (cx, cy) in component)
                {
                    if (!positionSigs.TryGetValue((cx, cy), out string pSig)) continue;
                    if (!groupSigs.TryGetValue(pSig, out var pGrp)) continue;
                    sbyte partOrigZ = 0;
                    if (tileLookup.TryGetValue((cx, cy), out var partOrigLand))
                        partOrigZ = partOrigLand.Z;
                    tmpl.Parts.Add(new MultiTilePart
                    {
                        DeltaX = cx - ax,
                        DeltaY = cy - ay,
                        Group = pGrp,
                        LandZOffset = (sbyte)(partOrigZ - tmplAnchorZ)
                    });
                }

                if (tmpl.Parts.Count == 0)
                    continue;

                string tmplSig = tmpl.GetSignature();
                if (templateSigMap.ContainsKey(tmplSig))
                    templateSigMap[tmplSig].Occurrences++;
                else
                {
                    templateSigMap[tmplSig] = tmpl;
                    rawTemplates.Add(tmpl);
                }
            }

            _templates = rawTemplates;
            _templateDensity = totalTiles > 0 ? (float)templateAnchorCount / totalTiles : 0f;

            System.Diagnostics.Debug.WriteLine(
                $"BiomeGenerator: {_tileFrequency.Count} unique tiles, " +
                $"{_adjacencyLookup.Count} adjacency pairs, " +
                $"{_staticGroups.Count} unique static groups, " +
                $"density={_staticDensity:F2}, " +
                $"{_templates.Count} multi-tile templates, " +
                $"templateDensity={_templateDensity:F2}");
        }

        /// <summary>
        /// Generate terrain for a rectangular area.
        /// Uses direct spatial tiling from the sampled brush so that
        /// gradients (e.g. land → coast → water) are faithfully preserved.
        /// When the target area is larger than the brush the pattern repeats.
        /// </summary>
        public BiomeGenerationResult Generate(int width, int height, sbyte baseZ = 0, float randomness = 0.15f)
        {
            var result = new BiomeGenerationResult();

            if (_brush.LandTiles.Count == 0)
                return result;

            // Build spatial lookups from the brush sample
            var landLookup = new Dictionary<(int, int), BiomeLandSample>();
            foreach (var t in _brush.LandTiles)
                landLookup[(t.RelativeX, t.RelativeY)] = t;

            var staticLookup = new Dictionary<(int, int), List<BiomeStaticSample>>();
            foreach (var s in _brush.Statics)
            {
                var key = (s.RelativeX, s.RelativeY);
                if (!staticLookup.ContainsKey(key))
                    staticLookup[key] = new List<BiomeStaticSample>();
                staticLookup[key].Add(s);
            }

            int bw = Math.Max(1, _brush.Width);
            int bh = Math.Max(1, _brush.Height);

            // Z offset: shift sample Z values so the average aligns with the target base Z
            float zOffset = baseZ - _avgZ;

            // --- Phase 1: Tile land from the sample, preserving spatial structure ---
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int sx = x % bw;
                    int sy = y % bh;

                    if (landLookup.TryGetValue((sx, sy), out var sample))
                    {
                        sbyte z = (sbyte)Math.Max(-128, Math.Min(127,
                            (int)Math.Round(sample.Z + zOffset)));

                        result.LandTiles.Add(new BiomeLandSample
                        {
                            RelativeX = x,
                            RelativeY = y,
                            TileId = sample.TileId,
                            Z = z
                        });
                    }
                }
            }

            // --- Phase 2: Tile statics from the sample ---
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int sx = x % bw;
                    int sy = y % bh;

                    if (staticLookup.TryGetValue((sx, sy), out var statics))
                    {
                        foreach (var s in statics)
                        {
                            sbyte z = (sbyte)Math.Max(-128, Math.Min(127,
                                (int)Math.Round(s.Z + zOffset)));

                            result.Statics.Add(new BiomeStaticSample
                            {
                                RelativeX = x,
                                RelativeY = y,
                                ItemId = s.ItemId,
                                Z = z,
                                Hue = s.Hue
                            });
                        }
                    }
                }
            }

            return result;
        }

        private List<(int x, int y)> BuildBFSOrder(int width, int height)
        {
            var ordered = new List<(int, int)>();
            var visited = new HashSet<(int, int)>();

            var start = (width / 2, height / 2);
            var queue = new Queue<(int, int)>();
            queue.Enqueue(start);
            visited.Add(start);

            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                ordered.Add(cell);

                var neighbors = new List<(int, int)>();
                foreach (var (dx, dy) in Directions)
                {
                    int nx = cell.Item1 + dx;
                    int ny = cell.Item2 + dy;
                    if (nx >= 0 && nx < width && ny >= 0 && ny < height && visited.Add((nx, ny)))
                        neighbors.Add((nx, ny));
                }
                Shuffle(neighbors);
                foreach (var n in neighbors)
                    queue.Enqueue(n);
            }

            return ordered;
        }

        private ushort CollapseTile(int x, int y,
            Dictionary<(int, int), ushort> grid, float randomness)
        {
            var candidates = _tileFrequency.Keys.ToList();
            var scores = new float[candidates.Count];

            for (int i = 0; i < candidates.Count; i++)
            {
                ushort tileId = candidates[i];
                float score = _tileProbabilities[tileId];

                float adjBonus = 0f;
                int nbrCount = 0;

                foreach (var (dx, dy) in Directions)
                {
                    if (grid.TryGetValue((x + dx, y + dy), out ushort neighborTile))
                    {
                        nbrCount++;
                        string fwd = $"{tileId}_{dx}_{dy}_{neighborTile}";
                        string rev = $"{neighborTile}_{-dx}_{-dy}_{tileId}";

                        int obs = 0;
                        if (_adjacencyLookup.TryGetValue(fwd, out int c1)) obs += c1;
                        if (_adjacencyLookup.TryGetValue(rev, out int c2)) obs += c2;

                        adjBonus += obs > 0 ? obs * 2.0f : -0.5f;
                    }
                }

                if (nbrCount > 0)
                    score += adjBonus / nbrCount;

                score += (float)_random.NextDouble() * randomness;
                scores[i] = Math.Max(0.001f, score);
            }

            float total = 0f;
            for (int i = 0; i < scores.Length; i++) total += scores[i];

            float roll = (float)_random.NextDouble() * total;
            float cum = 0f;
            for (int i = 0; i < scores.Length; i++)
            {
                cum += scores[i];
                if (roll <= cum)
                    return candidates[i];
            }

            return candidates[_random.Next(candidates.Count)];
        }

        private sbyte ChooseZValue(ushort tileId, int x, int y,
            Dictionary<(int, int), sbyte> zGrid, sbyte baseZ)
        {
            float targetZ = baseZ;

            if (_tileZValues.TryGetValue(tileId, out var zValues) && zValues.Count > 0)
            {
                sbyte sampleZ = zValues[_random.Next(zValues.Count)];
                targetZ = baseZ + (sampleZ - _avgZ);
            }

            float nbrSum = 0;
            int nbrCnt = 0;
            foreach (var (dx, dy) in Directions)
            {
                if (zGrid.TryGetValue((x + dx, y + dy), out sbyte nz))
                {
                    nbrSum += nz;
                    nbrCnt++;
                }
            }

            if (nbrCnt > 0)
            {
                float nbrAvg = nbrSum / nbrCnt;
                targetZ = targetZ * 0.4f + nbrAvg * 0.6f;
            }

            return (sbyte)Math.Max(-128, Math.Min(127, (int)Math.Round(targetZ)));
        }

        /// <summary>
        /// Place multi-tile templates atomically on generated terrain.
        /// Each template is a connected group of static positions that were
        /// identified during analysis as belonging to the same structure.
        /// All parts of a template are placed together or not at all,
        /// ensuring multi-part objects (trees, etc.) are always complete.
        /// </summary>
        private void PlaceStaticGroups(List<BiomeLandSample> generatedLand,
            List<BiomeStaticSample> outStatics, int width, int height)
        {
            if (_templates == null || _templates.Count == 0 || _templateDensity <= 0)
                return;

            var landLookup = new Dictionary<(int, int), BiomeLandSample>();
            foreach (var lt in generatedLand)
                landLookup[(lt.RelativeX, lt.RelativeY)] = lt;

            var occupied = new HashSet<(int, int)>();
            var positions = new List<BiomeLandSample>(generatedLand);
            Shuffle(positions);

            foreach (var landTile in positions)
            {
                var pos = (landTile.RelativeX, landTile.RelativeY);
                if (occupied.Contains(pos))
                    continue;

                if (_random.NextDouble() > _templateDensity)
                    continue;

                var template = PickTemplate(landTile.TileId);
                if (template == null) continue;

                // Check that ALL positions required by the template are free and in bounds
                bool canPlace = true;
                foreach (var part in template.Parts)
                {
                    int px = pos.Item1 + part.DeltaX;
                    int py = pos.Item2 + part.DeltaY;
                    if (px < 0 || px >= width || py < 0 || py >= height)
                    {
                        canPlace = false;
                        break;
                    }
                    if (occupied.Contains((px, py)))
                    {
                        canPlace = false;
                        break;
                    }
                }

                if (!canPlace) continue;

                // Place all parts atomically — use anchor Z for consistent alignment
                sbyte anchorZ = landTile.Z;
                foreach (var part in template.Parts)
                {
                    var partPos = (pos.Item1 + part.DeltaX, pos.Item2 + part.DeltaY);
                    if (landLookup.TryGetValue(partPos, out var partLand))
                    {
                        sbyte baseZ = (sbyte)Math.Max(-128, Math.Min(127, anchorZ + part.LandZOffset));
                        EmitGroupWithBaseZ(part.Group, partLand, baseZ, outStatics);
                        occupied.Add(partPos);
                    }
                }
            }
        }

        /// <summary>
        /// Pick a multi-tile template, preferring those observed on the
        /// given land tile type.  Weighted random by occurrence count.
        /// </summary>
        private MultiTileTemplate PickTemplate(ushort landTileId)
        {
            var matching = new List<MultiTileTemplate>();
            foreach (var t in _templates)
            {
                foreach (var p in t.Parts)
                {
                    if (p.Group.LandTileId == landTileId)
                    {
                        matching.Add(t);
                        break;
                    }
                }
            }

            var pool = matching.Count > 0 ? matching : _templates;

            int totalWeight = 0;
            foreach (var t in pool) totalWeight += t.Occurrences;
            if (totalWeight == 0) return null;

            int roll = _random.Next(totalWeight);
            int cum = 0;
            foreach (var t in pool)
            {
                cum += t.Occurrences;
                if (roll < cum)
                    return t;
            }

            return pool[pool.Count - 1];
        }

        /// <summary>
        /// Emit all statics in a group at a single tile position.
        /// Z for each member = land tile Z + member's relative Z offset.
        /// </summary>
        private void EmitGroup(StaticGroup group, BiomeLandSample landTile,
            List<BiomeStaticSample> outStatics)
        {
            foreach (var member in group.Members)
            {
                outStatics.Add(new BiomeStaticSample
                {
                    RelativeX = landTile.RelativeX,
                    RelativeY = landTile.RelativeY,
                    ItemId = member.ItemId,
                    Z = (sbyte)Math.Max(-128, Math.Min(127, landTile.Z + member.RelativeZ)),
                    Hue = member.Hue
                });
            }
        }

        /// <summary>
        /// Emit all statics in a group using a specific base Z for consistent
        /// alignment across multi-tile templates (e.g., tree trunk + canopy).
        /// </summary>
        private void EmitGroupWithBaseZ(StaticGroup group, BiomeLandSample landTile,
            sbyte baseZ, List<BiomeStaticSample> outStatics)
        {
            foreach (var member in group.Members)
            {
                outStatics.Add(new BiomeStaticSample
                {
                    RelativeX = landTile.RelativeX,
                    RelativeY = landTile.RelativeY,
                    ItemId = member.ItemId,
                    Z = (sbyte)Math.Max(-128, Math.Min(127, baseZ + member.RelativeZ)),
                    Hue = member.Hue
                });
            }
        }

        private void Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _random.Next(i + 1);
                var tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }
    }

    /// <summary>
    /// Holds the output of a biome generation pass
    /// </summary>
    public class BiomeGenerationResult
    {
        public List<BiomeLandSample> LandTiles { get; set; } = new List<BiomeLandSample>();
        public List<BiomeStaticSample> Statics { get; set; } = new List<BiomeStaticSample>();
    }
}
