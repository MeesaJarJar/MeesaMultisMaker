using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace MeesaMultisMaker
{
    /// <summary>
    /// Part categories for the "Split Multi by Z-Bands + Category" feature.
    /// Door and Stair tiles share one bucket ("Stairs/Doors") so a band x
    /// category matrix stays small; everything else maps 1:1 from TileRole.
    /// </summary>
    public enum ZPartCategory
    {
        Floor,
        Wall,
        StairsDoor,
        Roof,
        Other
    }

    /// <summary>
    /// One Z band: every object with MinZ &lt;= Z &lt;= MaxZ belongs to it.
    /// A cut value C is the TOP Z of the band below the gap, i.e. objects
    /// with Z &lt;= C stay in the lower band and Z &gt; C move to the upper
    /// band. Objects sitting exactly ON a cut Z therefore join the LOWER
    /// band (documented Z-tie rule).
    /// </summary>
    public sealed class ZBand
    {
        public int Index;
        public string Name;
        public int MinZ;
        public int MaxZ;
        public override string ToString()
        {
            return string.Format("{0}  (Z {1}..{2})", Name, MinZ, MaxZ);
        }
    }

    /// <summary>
    /// One exploded part = exactly one (band x category) cell. Empty cells
    /// are never materialized, so Parts only contains non-empty groups.
    /// Members are LIVE PlacedObject references (never cloned, never art).
    /// </summary>
    public sealed class ZPart
    {
        public int BandIndex;
        public string BandName;
        public ZPartCategory Category;
        public string Name;
        public Color Color;
        public List<PlacedObject> Members = new List<PlacedObject>();
        public override string ToString()
        {
            return string.Format("{0} ({1})", Name, Members.Count);
        }
    }

    /// <summary>
    /// Pure logic for "Split Multi by Z-Bands + Category". No WinForms, no
    /// file IO: the dialog owns the UI, Form1 owns canvas mutation. All
    /// methods are static so the math stays unit-testable in isolation.
    /// </summary>
    public static class ZBandSplitter
    {
        /// <summary>Minimum empty-Z gap that qualifies as a band cut.</summary>
        public const int MinGap = 4;
        /// <summary>Hard cap on band count (2..6 sensible for houses).</summary>
        public const int MaxBands = 6;
        /// <summary>Columns of the exploded grid before wrapping to a new row.</summary>
        public const int ExplodeColumns = 4;

        /// <summary>
        /// Fixed, color-blind-tolerant-ish palette for part tints. Index is
        /// assigned in part order (band bottom-up, then category order).
        /// </summary>
        public static readonly Color[] PartPalette = new Color[]
        {
            Color.FromArgb(0, 200, 255),    // cyan
            Color.FromArgb(255, 170, 0),    // amber
            Color.FromArgb(120, 255, 120),  // green
            Color.FromArgb(255, 110, 180),  // pink
            Color.FromArgb(170, 130, 255),  // violet
            Color.FromArgb(255, 255, 120),  // yellow
            Color.FromArgb(255, 120, 90),   // coral
            Color.FromArgb(120, 255, 220),  // mint
            Color.FromArgb(150, 200, 255),  // light blue
            Color.FromArgb(220, 255, 170),  // lime
            Color.FromArgb(255, 200, 230),  // rose
            Color.FromArgb(200, 200, 200),  // gray
        };

        /// <summary>Display suffix per category used in part names.</summary>
        public static string CategorySuffix(ZPartCategory cat)
        {
            switch (cat)
            {
                case ZPartCategory.Floor: return "Floor";
                case ZPartCategory.Wall: return "Walls";
                case ZPartCategory.StairsDoor: return "Stairs/Doors";
                case ZPartCategory.Roof: return "Roof";
                default: return "Other";
            }
        }

        /// <summary>
        /// Z histogram over canvas objects. Effective tile Z IS PlacedObject.Z
        /// (there is no separate GridZ field). Hidden objects ARE included so
        /// the histogram always matches what Explode will move.
        /// </summary>
        public static SortedDictionary<int, int> BuildHistogram(IEnumerable<PlacedObject> objects)
        {
            var hist = new SortedDictionary<int, int>();
            if (objects == null) return hist;
            foreach (var o in objects)
            {
                if (o == null) continue;
                int c;
                hist.TryGetValue(o.Z, out c);
                hist[o.Z] = c + 1;
            }
            return hist;
        }

        /// <summary>
        /// Auto-propose band cuts at the largest empty Z gaps. Gaps are
        /// measured between successive DISTINCT Z values; only gaps &gt;=
        /// minGap qualify. Proposes (#qualifying gaps) cuts capped so total
        /// bands &lt;= maxBands (keeps the N-1 largest gaps). Returns cut Zs
        /// sorted ascending. Empty = single band ("no gaps found").
        /// </summary>
        public static List<int> ProposeCuts(SortedSet<int> distinctZ, int minGap, int maxBands)
        {
            var cuts = new List<int>();
            if (distinctZ == null || distinctZ.Count < 2) return cuts;
            if (minGap < 1) minGap = 1;
            if (maxBands < 1) maxBands = 1;
            var zs = distinctZ.ToList();
            var gaps = new List<Tuple<int, int>>(); // (gapSize, lowerZ)
            for (int i = 1; i < zs.Count; i++)
            {
                int gap = zs[i] - zs[i - 1];
                if (gap >= minGap) gaps.Add(Tuple.Create(gap, zs[i - 1]));
            }
            // Largest gaps first; on ties prefer the HIGHER gap (roof splits
            // read better than foundation splits for houses).
            gaps.Sort((a, b) =>
            {
                int c = b.Item1.CompareTo(a.Item1);
                if (c != 0) return c;
                return b.Item2.CompareTo(a.Item2);
            });
            int want = Math.Min(gaps.Count, maxBands - 1);
            for (int i = 0; i < want; i++) cuts.Add(gaps[i].Item2);
            cuts.Sort();
            return cuts;
        }

        public static List<int> ProposeCuts(SortedSet<int> distinctZ)
        {
            return ProposeCuts(distinctZ, MinGap, MaxBands);
        }

        /// <summary>Band index for a Z given ascending cuts (cut = top of lower band).</summary>
        public static int BandIndexForZ(int z, List<int> cuts)
        {
            if (cuts == null) return 0;
            int band = 0;
            foreach (int c in cuts)
            {
                if (z > c) band++;
                else break;
            }
            return band;
        }

        /// <summary>
        /// Build band descriptors spanning [globalMinZ..globalMaxZ].
        /// Band i covers (cuts[i-1]..cuts[i]] with cuts[-1] = -inf.
        /// </summary>
        public static List<ZBand> BuildBands(List<int> cuts, int globalMinZ, int globalMaxZ)
        {
            var sorted = (cuts ?? new List<int>()).Distinct().OrderBy(c => c).ToList();
            var bands = new List<ZBand>();
            int lo = globalMinZ;
            for (int i = 0; i <= sorted.Count; i++)
            {
                int hi = (i < sorted.Count) ? sorted[i] : globalMaxZ;
                if (hi < lo) hi = lo; // degenerate cut outside data range
                bands.Add(new ZBand { Index = i, MinZ = lo, MaxZ = hi });
                lo = hi + 1;
            }
            NameBands(bands);
            return bands;
        }

        /// <summary>
        /// Bottom-to-top naming: bottom = "Foundation", top = "Roof", middle =
        /// "Story 1", "Story 2", ... Single band falls back to "Foundation"
        /// when it sits at step height (maxZ &lt;= 7) else "Band 1".
        /// </summary>
        public static void NameBands(List<ZBand> bands)
        {
            if (bands == null || bands.Count == 0) return;
            if (bands.Count == 1)
            {
                bands[0].Name = bands[0].MaxZ <= Generation.MultiRules.StepZ ? "Foundation" : "Band 1";
                return;
            }
            for (int i = 0; i < bands.Count; i++)
            {
                if (i == 0) bands[i].Name = "Foundation";
                else if (i == bands.Count - 1) bands[i].Name = "Roof";
                else bands[i].Name = "Story " + i;
            }
        }

        /// <summary>
        /// THE CATEGORY RULE (exact, as requested). Priority order, evaluated
        /// top to bottom, first match wins. Mirrors
        /// Generation.MultiRules.ClassifyRole priorities, except Door+Stair
        /// merge into StairsDoor:
        ///
        ///  TileData present (tileId parsed AND tiledata.mul loaded):
        ///   1. Door flag                    -&gt; StairsDoor
        ///   2. Roof flag                    -&gt; Roof
        ///   3. StairBack/StairRight flag    -&gt; StairsDoor
        ///   4. Wall flag                    -&gt; Wall
        ///   5. Bridge flag                  -&gt; Floor
        ///   6. Surface && !Impassable        -&gt; Floor
        ///   7. Impassable && Height &gt; 0      -&gt; Wall  (custom/log walls
        ///      often lack the Wall flag but are Impassable with height)
        ///   8. Impassable && Height == 0 && Z &gt;= topBandBaseZ -&gt; Roof (cap tiles)
        ///   9. Surface (with Impassable, e.g. foundations) -&gt; Floor
        ///  Fallback, no TileData (or no flags matched):
        ///   a. top band && Z &gt; topBandBaseZ -&gt; Roof
        ///   b. Z &lt;= bandBaseZ              -&gt; Floor
        ///   c. Z - bandBaseZ &lt; 20 (MultiRules.MinFloorSeparation) -&gt; Wall
        ///   d. else                          -&gt; Roof
        ///  Unparseable GraphicIds ("drop_N" placeholders, PNG imports) always
        ///  take the fallback path (tileId = null).
        /// </summary>
        public static ZPartCategory ClassifyCategory(
            ushort? tileId,
            int z,
            int bandBaseZ,
            int topBandBaseZ,
            bool isTopBand,
            Func<ushort, Mul.ItemTileData> getTile)
        {
            if (tileId.HasValue && getTile != null)
            {
                Mul.ItemTileData td = null;
                try { td = getTile(tileId.Value); }
                catch { td = null; }
                if (td != null)
                {
                    var f = td.Flags;
                    if (f.HasFlag(Mul.TileFlag.Door)) return ZPartCategory.StairsDoor;
                    if (f.HasFlag(Mul.TileFlag.Roof)) return ZPartCategory.Roof;
                    if (f.HasFlag(Mul.TileFlag.StairBack) || f.HasFlag(Mul.TileFlag.StairRight))
                        return ZPartCategory.StairsDoor;
                    if (f.HasFlag(Mul.TileFlag.Wall)) return ZPartCategory.Wall;
                    if (f.HasFlag(Mul.TileFlag.Bridge)) return ZPartCategory.Floor;
                    if (f.HasFlag(Mul.TileFlag.Surface) && !f.HasFlag(Mul.TileFlag.Impassable))
                        return ZPartCategory.Floor;
                    if (f.HasFlag(Mul.TileFlag.Impassable) && td.Height > 0)
                        return ZPartCategory.Wall;
                    if (f.HasFlag(Mul.TileFlag.Impassable) && td.Height == 0 && z >= topBandBaseZ)
                        return ZPartCategory.Roof;
                    if (f.HasFlag(Mul.TileFlag.Surface))
                        return ZPartCategory.Floor;
                }
            }
            // Z-heuristic fallback (band-aware).
            if (isTopBand && z > topBandBaseZ) return ZPartCategory.Roof;
            if (z <= bandBaseZ) return ZPartCategory.Floor;
            if (z - bandBaseZ < Generation.MultiRules.MinFloorSeparation) return ZPartCategory.Wall;
            return ZPartCategory.Roof;
        }

        private static readonly ZPartCategory[] CategoryOrder = new ZPartCategory[]
        {
            ZPartCategory.Floor,
            ZPartCategory.Wall,
            ZPartCategory.StairsDoor,
            ZPartCategory.Roof,
            ZPartCategory.Other,
        };

        /// <summary>
        /// Build parts = band x category, skipping empty cells. Part naming:
        /// bottom-band Floor -&gt; "Foundation", top-band Roof -&gt; "Roof",
        /// else "{Band} {suffix}" ("Story 2 Walls", "Story 1 Floor", ...).
        /// bandBases[i] is the reference Z of band i (its MinZ); used by the
        /// Z-heuristic fallback. classify maps (object, bandBaseZ,
        /// topBandBaseZ, isTopBand) to a category.
        /// </summary>
        public static List<ZPart> BuildParts(
            List<PlacedObject> objects,
            List<ZBand> bands,
            Func<PlacedObject, int, int, bool, ZPartCategory> classify)
        {
            var parts = new List<ZPart>();
            if (objects == null || bands == null || bands.Count == 0) return parts;
            int topBase = bands[bands.Count - 1].MinZ;
            // Real assignment: recompute cuts from band boundaries.
            var cuts = new List<int>();
            for (int i = 0; i < bands.Count - 1; i++) cuts.Add(bands[i].MaxZ);
            var members = new Dictionary<string, ZPart>();
            var order = new List<string>();
            foreach (var o in objects)
            {
                if (o == null) continue;
                int bi = BandIndexForZ(o.Z, cuts);
                if (bi < 0) bi = 0;
                if (bi >= bands.Count) bi = bands.Count - 1;
                var band = bands[bi];
                ZPartCategory cat = ZPartCategory.Other;
                try { cat = classify(o, band.MinZ, topBase, bi == bands.Count - 1); }
                catch { cat = ZPartCategory.Other; }
                string key = bi + "|" + cat;
                ZPart p;
                if (!members.TryGetValue(key, out p))
                {
                    p = new ZPart { BandIndex = bi, BandName = band.Name, Category = cat };
                    members[key] = p;
                    order.Add(key);
                }
                p.Members.Add(o);
            }
            // Order: band bottom-up, then category order.
            var sortedKeys = order
                .OrderBy(k => members[k].BandIndex)
                .ThenBy(k => Array.IndexOf(CategoryOrder, members[k].Category))
                .ToList();
            bool multiBand = bands.Count > 1;
            int colorIdx = 0;
            foreach (var key in sortedKeys)
            {
                var p = members[key];
                var band = bands[p.BandIndex];
                bool isBottom = p.BandIndex == 0;
                bool isTop = p.BandIndex == bands.Count - 1;
                if (p.Category == ZPartCategory.Floor && isBottom && band.Name == "Foundation")
                    p.Name = "Foundation";
                else if (p.Category == ZPartCategory.Roof && isTop && band.Name == "Roof" && multiBand)
                    p.Name = "Roof";
                else
                    p.Name = band.Name + " " + CategorySuffix(p.Category);
                p.Color = PartPalette[colorIdx % PartPalette.Length];
                colorIdx++;
                parts.Add(p);
            }
            return parts;
        }

        /// <summary>
        /// Exploded layout math (pure). Each part keeps its internal relative
        /// layout: newGrid = partOrigin + (oldGrid - globalMin). Parts are
        /// placed left-to-right in list order, wrapping every `columns` parts
        /// to a new row. Cell step and inter-part gap derive from the multi's
        /// own footprint: gap = max(2, min(footW, footH) / 2).
        ///
        /// Only GridX/GridY change; Z, Layer, PixelOffset, Scale, Rotation,
        /// Flip and Skew are untouched, so skew/rotation and Z-ties survive
        /// the move untouched.
        /// </summary>
        public static Dictionary<PlacedObject, Point> ComputeExplodeMoves(
            List<ZPart> parts,
            int globalMinX,
            int globalMinY,
            int footW,
            int footH,
            int columns)
        {
            var moves = new Dictionary<PlacedObject, Point>();
            if (parts == null || parts.Count == 0) return moves;
            if (columns < 1) columns = 1;
            if (footW < 1) footW = 1;
            if (footH < 1) footH = 1;
            int gap = Math.Max(2, Math.Min(footW, footH) / 2);
            for (int i = 0; i < parts.Count; i++)
            {
                int col = i % columns;
                int row = i / columns;
                int ox = globalMinX + col * (footW + gap);
                int oy = globalMinY + row * (footH + gap);
                foreach (var o in parts[i].Members)
                {
                    if (o == null) continue;
                    moves[o] = new Point(ox + (o.GridX - globalMinX), oy + (o.GridY - globalMinY));
                }
            }
            return moves;
        }
    }
}
