using MeesaMultisMaker.Mul;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace MeesaMultisMaker
{
    public partial class MulViewerForm
    {
        // Position roles for structure-aware WFC
        private enum PosRole { Interior, EdgeN, EdgeS, EdgeW, EdgeE, CornerNW, CornerNE, CornerSW, CornerSE }

        private void GenButton_Click(object sender, EventArgs e)
        {
            if (multiList.SelectedItems.Count == 0)
            {
                MessageBox.Show(this, "Select one or more multis on the left list.", "Generate",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                // ── Read all UI controls ─────────────────────────────────
                int outW = (int)genWidth.Value;
                int outH = (int)genHeight.Value;
                double similarity = Math.Max(0, Math.Min(1, genSimilarity.Value / 100.0));
                int epochs = (int)genEpochs.Value;
                double temperature = (double)genLr.Value;
                int kernel = (int)genKernel.Value;
                bool requireAllClasses = chkRequireAllClasses.Checked;
                bool doWfc = useWfc.Checked;

                LogMessage("════════════════════════════════════════════");
                LogMessage($"Settings: {outW}x{outH} | Similarity={similarity:P0} | " +
                           $"Epochs={epochs} | Temp={temperature:F3} | Kernel={kernel} | " +
                           $"WFC={doWfc} | RequireAll={requireAllClasses}");

                // ══════════════════════════════════════════════════════════
                //  PHASE 1: Collect training data
                // ══════════════════════════════════════════════════════════
                LogMessage("── Phase 1: Collecting training data ──");

                var ds = new Dataset();
                foreach (var item in multiList.SelectedItems)
                {
                    var entryItem = item as ListBoxEntryItem;
                    if (entryItem != null)
                    {
                        ds.grids.Add(BuildGridFromEntry(entryItem.Entry));
                        var cols = BuildColumnsFromEntry(entryItem.Entry);
                        ds.columns.Add(cols);
                    }
                }

                // Count total training cells
                int totalCells = 0;
                foreach (var grid in ds.columns)
                    totalCells += grid.GetLength(0) * grid.GetLength(1);

                LogMessage($"  Sources: {ds.columns.Count} grid(s), {totalCells} total cells");

                // ══════════════════════════════════════════════════════════
                //  PHASE 2: Pattern extraction with quantization
                // ══════════════════════════════════════════════════════════
                LogMessage("── Phase 2: Extracting patterns ──");

                int zQuantize = ComputeZQuantization(similarity);
                var registry = new ColumnRegistry();
                var roleMap = new Dictionary<int, HashSet<PosRole>>();
                var groupMembers = new Dictionary<int, List<List<TileZ>>>();

                // Also track all unique tile IDs for RequireAllClasses
                var allTrainingTileIds = new HashSet<int>();

                foreach (var grid in ds.columns)
                {
                    int gw = grid.GetLength(0), gh = grid.GetLength(1);
                    for (int x = 0; x < gw; x++)
                    {
                        for (int y = 0; y < gh; y++)
                        {
                            var rawCol = grid[x, y];
                            foreach (var tz in rawCol)
                                allTrainingTileIds.Add(tz.TileId);

                            var quantized = QuantizeColumn(rawCol, zQuantize);
                            int id = registry.GetOrCreateId(quantized);

                            if (!groupMembers.ContainsKey(id))
                                groupMembers[id] = new List<List<TileZ>>();
                            groupMembers[id].Add(new List<TileZ>(rawCol));

                            var role = ClassifyPosition(x, y, gw, gh);
                            if (!roleMap.ContainsKey(id))
                                roleMap[id] = new HashSet<PosRole>();
                            roleMap[id].Add(role);
                        }
                    }
                }

                if (registry.Count == 0) throw new Exception("Not enough data to train");

                LogMessage($"  Z-quantize={zQuantize} | {registry.Count} unique patterns | " +
                           $"{allTrainingTileIds.Count} unique tile IDs");

                // Show pattern distribution
                int edgePatterns = 0, interiorPatterns = 0;
                foreach (var kv in roleMap)
                {
                    if (kv.Value.Contains(PosRole.Interior)) interiorPatterns++;
                    if (kv.Value.Any(r => r != PosRole.Interior)) edgePatterns++;
                }
                LogMessage($"  Edge patterns: {edgePatterns} | Interior patterns: {interiorPatterns}");

                // ══════════════════════════════════════════════════════════
                //  PHASE 3: Build + expand adjacency (Kernel controls this)
                // ══════════════════════════════════════════════════════════
                LogMessage("── Phase 3: Adjacency analysis ──");

                var adj = BuildAdjacencyFromGrids(ds.columns, registry, zQuantize);

                int directRightCount = 0, directBelowCount = 0;
                foreach (var kv in adj.rightOf) directRightCount += kv.Value.Count;
                foreach (var kv in adj.belowOf) directBelowCount += kv.Value.Count;
                LogMessage($"  Direct adjacency: {directRightCount} right + {directBelowCount} below = " +
                           $"{directRightCount + directBelowCount} total rules");

                // Expand adjacency based on pattern similarity (Kernel controls threshold)
                if (kernel >= 2)
                {
                    int expanded = ExpandAdjacencyBySimilarity(adj.rightOf, adj.belowOf, registry, kernel);
                    LogMessage($"  Kernel={kernel}: expanded by {expanded} rules via tile-similarity");

                    int newRight = 0, newBelow = 0;
                    foreach (var kv in adj.rightOf) newRight += kv.Value.Count;
                    foreach (var kv in adj.belowOf) newBelow += kv.Value.Count;
                    LogMessage($"  Total adjacency: {newRight} right + {newBelow} below = {newRight + newBelow}");
                }

                double avgAdj = registry.Count > 0
                    ? (double)(directRightCount + directBelowCount) / registry.Count : 0;
                LogMessage($"  Avg adjacency per pattern: {avgAdj:F1}");

                // ══════════════════════════════════════════════════════════
                //  PHASE 4: Generate (Epochs = attempts, LR = temperature)
                // ══════════════════════════════════════════════════════════
                LogMessage($"── Phase 4: Generating ({epochs} attempts) ──");

                // Pre-compute average parts-per-cell from training data (used for scoring)
                double avgTrainingParts = 1.0;
                if (totalCells > 0)
                {
                    int totalTrainParts = 0;
                    foreach (var grid in ds.columns)
                    {
                        int gw = grid.GetLength(0), gh = grid.GetLength(1);
                        for (int gx = 0; gx < gw; gx++)
                            for (int gy = 0; gy < gh; gy++)
                                totalTrainParts += grid[gx, gy].Count;
                    }
                    avgTrainingParts = totalTrainParts / (double)totalCells;
                }

                int[,] bestGrid = null;
                double bestScore = -1;
                int bestParts = 0;
                int attempts = Math.Min(epochs, 500); // cap for performance

                // Single RNG for seeding each attempt — avoids the problem of
                // new Random() in a tight loop producing identical seeds.
                var epochRng = new Random();

                for (int epoch = 1; epoch <= attempts; epoch++)
                {
                    int[,] outGrid;

                    if (doWfc)
                    {
                        var wfc = BuildWfc(registry, ds.columns, zQuantize, roleMap, epochRng.Next());
                        outGrid = wfc.Run(outW, outH, temperature);
                    }
                    else
                    {
                        var freqGen = new FrequencyGenerator(registry, adj.rightOf, adj.belowOf, roleMap);
                        outGrid = freqGen.Generate(outW, outH, similarity, temperature, new Random(epochRng.Next()));
                    }

                    // Score this attempt — reward structural coherence, penalize noise
                    int parts = 0;
                    int violations = 0;
                    int zMismatches = 0;
                    var outputTileIds = new HashSet<int>();

                    for (int x = 0; x < outW; x++)
                    {
                        for (int y = 0; y < outH; y++)
                        {
                            int colId = outGrid[x, y];
                            if (colId < 0) continue;
                            var col = registry.GetById(colId);
                            parts += col.Items.Count;
                            foreach (var tz in col.Items)
                                outputTileIds.Add(tz.TileId);

                            // Check adjacency validity
                            if (x + 1 < outW && outGrid[x + 1, y] >= 0)
                            {
                                int rightId = outGrid[x + 1, y];
                                if (!adj.rightOf.ContainsKey(colId) || !adj.rightOf[colId].Contains(rightId))
                                    violations++;

                                // Z-coherence: check that shared Z-levels are consistent.
                                // A column with Z={0} next to Z={0,27} is fine (subset).
                                // A column with Z={0,20} next to Z={0,27} has a conflict (20 vs 27).
                                var rightCol = registry.GetById(rightId);
                                if (HasConflictingZLevels(col.Items, rightCol.Items))
                                    zMismatches++;
                            }
                            if (y + 1 < outH && outGrid[x, y + 1] >= 0)
                            {
                                int belowId = outGrid[x, y + 1];
                                if (!adj.belowOf.ContainsKey(colId) || !adj.belowOf[colId].Contains(belowId))
                                    violations++;

                                var belowCol = registry.GetById(belowId);
                                if (HasConflictingZLevels(col.Items, belowCol.Items))
                                    zMismatches++;
                            }
                        }
                    }

                    // Compute expected parts and deviation
                    double expectedParts = avgTrainingParts * outW * outH;
                    double partDeviation = Math.Abs(parts - expectedParts) / Math.Max(1, expectedParts);

                    // RequireAllClasses check
                    bool hasAllClasses = true;
                    if (requireAllClasses)
                    {
                        foreach (int tid in allTrainingTileIds)
                        {
                            if (!outputTileIds.Contains(tid))
                            {
                                hasAllClasses = false;
                                break;
                            }
                        }
                    }

                    // Score: coherence-focused instead of "more parts = better"
                    double coverage = allTrainingTileIds.Count > 0
                        ? (double)outputTileIds.Count / allTrainingTileIds.Count : 1.0;
                    double score = coverage * 200.0
                                 - violations * 50.0
                                 - zMismatches * 30.0
                                 - partDeviation * 100.0;

                    if (requireAllClasses && !hasAllClasses)
                        score -= 10000; // heavy penalty

                    // Log every 10th attempt or first/last
                    if (epoch <= 3 || epoch % 10 == 0 || epoch == attempts)
                    {
                        string classNote = (requireAllClasses && !hasAllClasses) ? " [MISSING TILES]" : "";
                        LogMessage($"  Attempt {epoch}/{attempts}: parts={parts} (expected ~{expectedParts:F0}), " +
                                   $"adj-violations={violations}, z-mismatches={zMismatches}, " +
                                   $"tiles={outputTileIds.Count}/{allTrainingTileIds.Count}, " +
                                   $"score={score:F0}{classNote}");
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestGrid = outGrid;
                        bestParts = parts;
                    }

                    // Early exit if high-quality result (few violations, coherent Z, all tiles)
                    if (violations == 0 && zMismatches <= 2 && hasAllClasses && parts > 0)
                    {
                        LogMessage($"  ✓ High-quality result at attempt {epoch}!");
                        break;
                    }
                }

                if (bestGrid == null)
                    throw new Exception("All generation attempts failed");

                LogMessage($"  Best: score={bestScore:F0}, parts={bestParts}");

                // ══════════════════════════════════════════════════════════
                //  PHASE 5: Emit output and validate against UO multi rules
                // ══════════════════════════════════════════════════════════
                var rng = new Random();
                var sb = new StringBuilder();
                generatedComponents = new List<MultiComponent>();
                for (int x = 0; x < bestGrid.GetLength(0); x++)
                {
                    for (int y = 0; y < bestGrid.GetLength(1); y++)
                    {
                        int colId = bestGrid[x, y];
                        if (colId < 0) continue;

                        List<TileZ> concrete;
                        if (groupMembers.ContainsKey(colId) && groupMembers[colId].Count > 0)
                            concrete = groupMembers[colId][rng.Next(groupMembers[colId].Count)];
                        else
                            concrete = registry.GetById(colId).Items;

                        foreach (var tz in concrete)
                        {
                            generatedComponents.Add(new MultiComponent
                            {
                                TileId = (ushort)tz.TileId,
                                X = (short)x,
                                Y = (short)y,
                                Z = tz.Z,
                                Flags = 0
                            });
                        }
                    }
                }

                // Validate and correct against UO multi rules
                var validation = Generation.MultiValidator.ValidateComponents(generatedComponents);
                if (!validation.IsValid || validation.Warnings.Count > 0)
                {
                    LogMessage("── Multi Validation ──");
                    foreach (var err in validation.Errors)
                        LogMessage($"  ✗ {err}");
                    foreach (var warn in validation.Warnings)
                        LogMessage($"  ⚠ {warn}");
                }

                foreach (var comp in generatedComponents)
                    sb.AppendLine($"0x{comp.TileId:X4}\t{comp.X}\t{comp.Y}\t{comp.Z}\t0");

                exportBox.Text = sb.ToString();
                showingGenerated = true;
                previewBox.Invalidate();
                SafeCopyToClipboard(exportBox.Text, LogMessage);
                LogMessage($"Done: {generatedComponents.Count} components emitted");
                LogMessage("════════════════════════════════════════════");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Generate Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  Column quantization (floor-boundary-aware)
        // ════════════════════════════════════════════════════════════════

        private int ComputeZQuantization(double similarity)
        {
            if (similarity >= 0.95) return 1;
            if (similarity >= 0.8) return 3;
            if (similarity >= 0.6) return 5;
            if (similarity >= 0.4) return 7;
            if (similarity >= 0.2) return 10;
            return 20;
        }

        /// <summary>
        /// Quantize a column's Z values while preserving UO floor boundaries.
        /// Tiles are snapped to the nearest floor level before normal
        /// quantization so that distinct floors are never merged together.
        /// </summary>
        private ColumnPattern QuantizeColumn(List<TileZ> rawCol, int zQuant)
        {
            if (zQuant <= 1)
                return new ColumnPattern { Items = new List<TileZ>(rawCol) };

            // Detect floor levels in this column
            var floorLevels = Generation.MultiRules.DetectFloorLevels(rawCol.Select(t => (int)t.Z));

            var quantized = new List<TileZ>();
            foreach (var tz in rawCol)
            {
                short qz;
                if (floorLevels.Count > 1)
                {
                    // Snap to the assigned floor first, then quantize within that floor
                    int floorZ = Generation.MultiRules.AssignToFloor(tz.Z, floorLevels);
                    int relZ = tz.Z - floorZ;
                    int quantRelZ = (relZ / zQuant) * zQuant;
                    qz = (short)(floorZ + quantRelZ);
                }
                else
                {
                    qz = (short)((tz.Z / zQuant) * zQuant);
                }
                quantized.Add(new TileZ { TileId = tz.TileId, Z = qz });
            }
            var seen = new HashSet<long>();
            var deduped = new List<TileZ>();
            foreach (var tz in quantized.OrderBy(t => t.Z).ThenBy(t => t.TileId))
            {
                long key = ((long)tz.TileId << 16) | (ushort)tz.Z;
                if (seen.Add(key))
                    deduped.Add(tz);
            }
            return new ColumnPattern { Items = deduped };
        }

        // ════════════════════════════════════════════════════════════════
        //  Adjacency expansion — Kernel controls how aggressively we
        //  add new adjacency rules based on pattern similarity.
        //  Higher kernel = more permissive = more variety.
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Expand adjacency by transitivity through similar patterns.
        /// If A→B is allowed, and C is similar to B, then A→C is also allowed.
        /// Kernel controls similarity threshold:
        ///   kernel=2: patterns must share ≥50% of tile IDs AND same Z-levels
        ///   kernel=3: ≥33%
        ///   kernel=4: ≥25%
        ///   kernel=5: ≥20%
        /// Returns number of new rules added.
        /// </summary>
        private int ExpandAdjacencyBySimilarity(
            Dictionary<int, HashSet<int>> rightOf,
            Dictionary<int, HashSet<int>> belowOf,
            ColumnRegistry registry, int kernel)
        {
            double threshold = 1.0 / kernel; // kernel=2 → 0.5, kernel=5 → 0.2

            // Build tile-ID sets AND Z-level sets for all patterns
            var tileIdSets = new Dictionary<int, HashSet<int>>();
            var zLevelSets = new Dictionary<int, HashSet<int>>();
            for (int i = 0; i < registry.Count; i++)
            {
                var tileSet = new HashSet<int>();
                var zSet = new HashSet<int>();
                foreach (var tz in registry.GetById(i).Items)
                {
                    tileSet.Add(tz.TileId);
                    zSet.Add(tz.Z);
                }
                tileIdSets[i] = tileSet;
                zLevelSets[i] = zSet;
            }

            // Pre-compute similarity between all pairs
            // Require BOTH tile similarity AND same Z-level structure
            var similar = new Dictionary<int, List<int>>();
            for (int i = 0; i < registry.Count; i++)
                similar[i] = new List<int>();

            for (int i = 0; i < registry.Count; i++)
            {
                for (int j = i + 1; j < registry.Count; j++)
                {
                    // Z-levels must be compatible — one must be a subset of the other
                    // or they must share the same set.  This prevents mixing columns
                    // with conflicting floor heights (e.g. Z={0,20} with Z={0,27})
                    // while still allowing subset relationships (Z={0} with Z={0,27}).
                    if (!zLevelSets[i].IsSubsetOf(zLevelSets[j]) && !zLevelSets[j].IsSubsetOf(zLevelSets[i]))
                        continue;

                    double sim = JaccardSimilarity(tileIdSets[i], tileIdSets[j]);
                    if (sim >= threshold)
                    {
                        similar[i].Add(j);
                        similar[j].Add(i);
                    }
                }
            }

            int added = 0;

            // Expand rightOf: if A→B and C~B, then A→C
            var rightKeys = new List<int>(rightOf.Keys);
            foreach (int a in rightKeys)
            {
                var origNeighbors = new List<int>(rightOf[a]);
                foreach (int b in origNeighbors)
                {
                    if (!similar.ContainsKey(b)) continue;
                    foreach (int c in similar[b])
                    {
                        if (rightOf[a].Add(c))
                            added++;
                    }
                }
            }

            // Expand belowOf similarly
            var belowKeys = new List<int>(belowOf.Keys);
            foreach (int a in belowKeys)
            {
                var origNeighbors = new List<int>(belowOf[a]);
                foreach (int b in origNeighbors)
                {
                    if (!similar.ContainsKey(b)) continue;
                    foreach (int c in similar[b])
                    {
                        if (belowOf[a].Add(c))
                            added++;
                    }
                }
            }

            return added;
        }

        private double JaccardSimilarity(HashSet<int> a, HashSet<int> b)
        {
            if (a.Count == 0 && b.Count == 0) return 1.0;
            if (a.Count == 0 || b.Count == 0) return 0.0;

            int intersection = 0;
            foreach (int item in a)
            {
                if (b.Contains(item))
                    intersection++;
            }
            int union = a.Count + b.Count - intersection;
            return union > 0 ? (double)intersection / union : 0;
        }

        /// <summary>
        /// Check if two adjacent columns have conflicting Z-level structures.
        /// Subset relationships are ALLOWED (column A has Z={0}, B has Z={0,27}).
        /// Only flags a conflict when one column has a Z-level that the other
        /// doesn't share AND that Z-level doesn't correspond to a valid floor
        /// in the other column's Z-structure (e.g. Z={0,20} vs Z={0,27}).
        /// Empty columns never conflict.
        /// </summary>
        private static bool HasConflictingZLevels(List<TileZ> a, List<TileZ> b)
        {
            if (a.Count == 0 || b.Count == 0)
                return false;

            var aZs = new HashSet<int>();
            var bZs = new HashSet<int>();
            foreach (var t in a) aZs.Add(t.Z);
            foreach (var t in b) bZs.Add(t.Z);

            // If one is a subset of the other, that's fine (shorter column)
            if (aZs.IsSubsetOf(bZs) || bZs.IsSubsetOf(aZs))
                return false;

            // Both have Z-levels the other doesn't — check if the exclusive
            // Z-levels are close enough to be conflicting floor heights
            foreach (int z in aZs)
            {
                if (bZs.Contains(z)) continue;
                // Check if B has a Z-level within MinFloorSeparation that is different
                foreach (int bz in bZs)
                {
                    if (aZs.Contains(bz)) continue;
                    int gap = Math.Abs(z - bz);
                    if (gap > 0 && gap < Generation.MultiRules.MinFloorSeparation)
                        return true; // conflicting floor heights
                }
            }

            return false;
        }

        // ════════════════════════════════════════════════════════════════
        //  Position classification
        // ════════════════════════════════════════════════════════════════

        private PosRole ClassifyPosition(int x, int y, int w, int h)
        {
            bool top = (y == 0), bottom = (y == h - 1);
            bool left = (x == 0), right = (x == w - 1);

            if (top && left) return PosRole.CornerNW;
            if (top && right) return PosRole.CornerNE;
            if (bottom && left) return PosRole.CornerSW;
            if (bottom && right) return PosRole.CornerSE;
            if (top) return PosRole.EdgeN;
            if (bottom) return PosRole.EdgeS;
            if (left) return PosRole.EdgeW;
            if (right) return PosRole.EdgeE;
            return PosRole.Interior;
        }

        // ════════════════════════════════════════════════════════════════
        //  Data helpers
        // ════════════════════════════════════════════════════════════════

        private int[,] BuildGridFromEntry(MultiEntry entry)
        {
            var comps = entry.Components;
            if (!comps.Any()) return new int[1, 1];
            int minX = comps.Min(c => c.X), minY = comps.Min(c => c.Y);
            int maxX = comps.Max(c => c.X), maxY = comps.Max(c => c.Y);
            int w = maxX - minX + 1, h = maxY - minY + 1;
            int[,] grid = new int[w, h];
            for (int x = 0; x < w; x++) for (int y = 0; y < h; y++) grid[x, y] = -1;
            foreach (var c in comps) grid[c.X - minX, c.Y - minY] = c.TileId;
            return grid;
        }

        private List<TileZ>[,] BuildColumnsFromEntry(MultiEntry entry)
        {
            var comps = entry.Components;
            if (!comps.Any()) return new List<TileZ>[1, 1] { { new List<TileZ>() } };
            int minX = comps.Min(c => c.X), minY = comps.Min(c => c.Y);
            int maxX = comps.Max(c => c.X), maxY = comps.Max(c => c.Y);
            int w = maxX - minX + 1, h = maxY - minY + 1;
            var cols = new List<TileZ>[w, h];
            for (int x = 0; x < w; x++) for (int y = 0; y < h; y++) cols[x, y] = new List<TileZ>();
            foreach (var c in comps) cols[c.X - minX, c.Y - minY].Add(new TileZ { TileId = c.TileId, Z = c.Z });
            for (int x = 0; x < w; x++) for (int y = 0; y < h; y++) cols[x, y] = cols[x, y].OrderBy(t => t.Z).ThenBy(t => t.TileId).ToList();
            return cols;
        }

        private List<Point> BuildContextOffsets(int kernel)
        {
            var offsets = new List<Point>();
            int r = Math.Max(1, kernel / 2);
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                    if (dx != 0 || dy != 0)
                        offsets.Add(new Point(dx, dy));
            return offsets;
        }

        private List<Sample> BuildColumnSamplesK(List<List<TileZ>[,]> columns, ColumnRegistry registry, List<Point> offsets)
        {
            var samples = new List<Sample>();
            foreach (var grid in columns)
            {
                int w = grid.GetLength(0), h = grid.GetLength(1);
                for (int x = 0; x < w; x++)
                    for (int y = 0; y < h; y++)
                    {
                        int id = registry.GetOrCreateId(new ColumnPattern { Items = new List<TileZ>(grid[x, y]) });
                        var ctx = new int[offsets.Count];
                        for (int oi = 0; oi < offsets.Count; oi++)
                        {
                            int nx = x + offsets[oi].X;
                            int ny = y + offsets[oi].Y;
                            if (nx >= 0 && nx < w && ny >= 0 && ny < h)
                                ctx[oi] = registry.GetOrCreateId(new ColumnPattern { Items = new List<TileZ>(grid[nx, ny]) });
                            else
                                ctx[oi] = -1;
                        }
                        samples.Add(new Sample { Ctx = ctx, Y = id });
                    }
            }
            return samples;
        }

        // ════════════════════════════════════════════════════════════════
        //  Adjacency extraction
        // ════════════════════════════════════════════════════════════════

        private (Dictionary<int, HashSet<int>> rightOf, Dictionary<int, HashSet<int>> belowOf) BuildAdjacencyFromGrids(
            List<List<TileZ>[,]> columns, ColumnRegistry registry, int zQuantize)
        {
            var rightOf = new Dictionary<int, HashSet<int>>();
            var belowOf = new Dictionary<int, HashSet<int>>();

            foreach (var grid in columns)
            {
                int w = grid.GetLength(0), h = grid.GetLength(1);
                for (int x = 0; x < w; x++)
                {
                    for (int y = 0; y < h; y++)
                    {
                        int centerId = registry.GetOrCreateId(QuantizeColumn(grid[x, y], zQuantize));

                        if (x + 1 < w)
                        {
                            int rightId = registry.GetOrCreateId(QuantizeColumn(grid[x + 1, y], zQuantize));
                            if (!rightOf.ContainsKey(centerId))
                                rightOf[centerId] = new HashSet<int>();
                            rightOf[centerId].Add(rightId);
                        }

                        if (y + 1 < h)
                        {
                            int belowId = registry.GetOrCreateId(QuantizeColumn(grid[x, y + 1], zQuantize));
                            if (!belowOf.ContainsKey(centerId))
                                belowOf[centerId] = new HashSet<int>();
                            belowOf[centerId].Add(belowId);
                        }
                    }
                }
            }

            return (rightOf, belowOf);
        }

        // ════════════════════════════════════════════════════════════════
        //  WFC builder
        // ════════════════════════════════════════════════════════════════

        private WfcModel BuildWfc(ColumnRegistry registry, List<List<TileZ>[,]> columns,
            int zQuantize, Dictionary<int, HashSet<PosRole>> roleMap, int seed)
        {
            var patterns = new List<WfcPattern>();
            var weightCounts = new Dictionary<int, int>();

            foreach (var grid in columns)
            {
                int w = grid.GetLength(0), h = grid.GetLength(1);
                for (int x = 0; x < w; x++)
                    for (int y = 0; y < h; y++)
                    {
                        int id = registry.GetOrCreateId(QuantizeColumn(grid[x, y], zQuantize));
                        if (!weightCounts.ContainsKey(id))
                            weightCounts[id] = 0;
                        weightCounts[id]++;
                    }
            }

            for (int i = 0; i < registry.Count; i++)
            {
                double weight = weightCounts.ContainsKey(i) ? weightCounts[i] : 0.1;
                patterns.Add(new WfcPattern
                {
                    Id = i,
                    Pattern = registry.GetById(i),
                    Weight = weight,
                    Roles = roleMap.ContainsKey(i) ? roleMap[i] : new HashSet<PosRole> { PosRole.Interior }
                });
            }

            foreach (var grid in columns)
            {
                int w = grid.GetLength(0), h = grid.GetLength(1);
                for (int x = 0; x < w; x++)
                {
                    for (int y = 0; y < h; y++)
                    {
                        int id = registry.GetOrCreateId(QuantizeColumn(grid[x, y], zQuantize));
                        var pat = patterns[id];

                        if (x + 1 < w)
                        {
                            int rId = registry.GetOrCreateId(QuantizeColumn(grid[x + 1, y], zQuantize));
                            pat.RightOk.Add(rId);
                            patterns[rId].LeftOk.Add(id);
                        }
                        if (x > 0)
                        {
                            int lId = registry.GetOrCreateId(QuantizeColumn(grid[x - 1, y], zQuantize));
                            pat.LeftOk.Add(lId);
                            patterns[lId].RightOk.Add(id);
                        }
                        if (y + 1 < h)
                        {
                            int dId = registry.GetOrCreateId(QuantizeColumn(grid[x, y + 1], zQuantize));
                            pat.DownOk.Add(dId);
                            patterns[dId].UpOk.Add(id);
                        }
                        if (y > 0)
                        {
                            int uId = registry.GetOrCreateId(QuantizeColumn(grid[x, y - 1], zQuantize));
                            pat.UpOk.Add(uId);
                            patterns[uId].DownOk.Add(id);
                        }
                    }
                }
            }

            return new WfcModel(patterns, seed);
        }

        // ════════════════════════════════════════════════════════════════
        //  WFC Model — constraint propagation + position roles + temperature
        // ════════════════════════════════════════════════════════════════

        private class WfcPattern
        {
            public int Id;
            public ColumnPattern Pattern;
            public HashSet<int> RightOk = new HashSet<int>();
            public HashSet<int> LeftOk = new HashSet<int>();
            public HashSet<int> UpOk = new HashSet<int>();
            public HashSet<int> DownOk = new HashSet<int>();
            public double Weight;
            public HashSet<PosRole> Roles = new HashSet<PosRole>();
        }

        private class WfcModel
        {
            private readonly List<WfcPattern> _patterns;
            private readonly Dictionary<int, WfcPattern> _byId;
            private readonly Random _rng;
            private readonly List<int> _nonEmptyIds;
            private readonly int _emptyId;

            public WfcModel(List<WfcPattern> patterns, int seed)
            {
                _patterns = patterns;
                _byId = patterns.ToDictionary(p => p.Id, p => p);
                _rng = new Random(seed);
                _nonEmptyIds = patterns.Where(p => p.Pattern.Items.Count > 0).Select(p => p.Id).ToList();
                _emptyId = patterns.Where(p => p.Pattern.Items.Count == 0).Select(p => p.Id).DefaultIfEmpty(-1).First();
            }

            /// <summary>
            /// Run WFC generation.
            /// Temperature controls randomness of pattern selection:
            ///   low (0.01) = always pick highest-weight pattern (deterministic)
            ///   mid (0.1)  = moderate variety
            ///   high (1.0) = nearly uniform random (maximum variety)
            /// </summary>
            public int[,] Run(int W, int H, double temperature = 0.1)
            {
                var allIds = _patterns.Where(p => p.Pattern.Items.Count > 0).Select(p => p.Id).ToArray();
                if (allIds.Length == 0) allIds = _patterns.Select(p => p.Id).ToArray();

                // Pre-filter by position role
                var possible = new HashSet<int>[W, H];
                for (int x = 0; x < W; x++)
                {
                    for (int y = 0; y < H; y++)
                    {
                        var outputRole = ClassifyOutputPosition(x, y, W, H);
                        possible[x, y] = new HashSet<int>();
                        foreach (int id in allIds)
                        {
                            if (IsRoleCompatible(outputRole, _byId[id].Roles))
                                possible[x, y].Add(id);
                        }
                        if (possible[x, y].Count == 0)
                            possible[x, y] = new HashSet<int>(allIds);
                    }
                }

                var collapsed = new int[W, H];
                for (int x = 0; x < W; x++)
                    for (int y = 0; y < H; y++)
                        collapsed[x, y] = -1;

                int totalCells = W * H;
                for (int iter = 0; iter < totalCells; iter++)
                {
                    int bestX = -1, bestY = -1;
                    double bestEntropy = double.MaxValue;

                    for (int x = 0; x < W; x++)
                    {
                        for (int y = 0; y < H; y++)
                        {
                            if (collapsed[x, y] >= 0) continue;
                            int count = possible[x, y].Count;
                            if (count == 0) continue;
                            double entropy = count + _rng.NextDouble() * 0.1;
                            if (entropy < bestEntropy)
                            {
                                bestEntropy = entropy;
                                bestX = x;
                                bestY = y;
                            }
                        }
                    }

                    if (bestX == -1) break;

                    int pick = PickWeightedFrom(possible[bestX, bestY], temperature);
                    collapsed[bestX, bestY] = pick;
                    possible[bestX, bestY].Clear();
                    possible[bestX, bestY].Add(pick);

                    Propagate(possible, collapsed, W, H, bestX, bestY);
                }

                var outGrid = new int[W, H];
                for (int x = 0; x < W; x++)
                {
                    for (int y = 0; y < H; y++)
                    {
                        if (collapsed[x, y] >= 0)
                            outGrid[x, y] = collapsed[x, y];
                        else if (possible[x, y].Count > 0)
                            outGrid[x, y] = PickWeightedFrom(possible[x, y], temperature);
                        else
                        {
                            // No valid pattern fits — leave cell empty rather than
                            // injecting random noise that breaks structural coherence.
                            outGrid[x, y] = _emptyId >= 0 ? _emptyId : -1;
                        }
                    }
                }

                return outGrid;
            }

            private PosRole ClassifyOutputPosition(int x, int y, int w, int h)
            {
                bool top = (y == 0), bottom = (y == h - 1);
                bool left = (x == 0), right = (x == w - 1);
                if (top && left) return PosRole.CornerNW;
                if (top && right) return PosRole.CornerNE;
                if (bottom && left) return PosRole.CornerSW;
                if (bottom && right) return PosRole.CornerSE;
                if (top) return PosRole.EdgeN;
                if (bottom) return PosRole.EdgeS;
                if (left) return PosRole.EdgeW;
                if (right) return PosRole.EdgeE;
                return PosRole.Interior;
            }

            private bool IsRoleCompatible(PosRole outputRole, HashSet<PosRole> patternRoles)
            {
                // Exact role match
                if (patternRoles.Contains(outputRole))
                    return true;

                // Strict fallback: corners can use adjacent edges,
                // but never swap edge/interior or mix opposite edges.
                switch (outputRole)
                {
                    case PosRole.CornerNW:
                        return patternRoles.Contains(PosRole.EdgeN) || patternRoles.Contains(PosRole.EdgeW);
                    case PosRole.CornerNE:
                        return patternRoles.Contains(PosRole.EdgeN) || patternRoles.Contains(PosRole.EdgeE);
                    case PosRole.CornerSW:
                        return patternRoles.Contains(PosRole.EdgeS) || patternRoles.Contains(PosRole.EdgeW);
                    case PosRole.CornerSE:
                        return patternRoles.Contains(PosRole.EdgeS) || patternRoles.Contains(PosRole.EdgeE);
                    case PosRole.EdgeN:
                    case PosRole.EdgeS:
                    case PosRole.EdgeE:
                    case PosRole.EdgeW:
                        // Edges only match their exact role (already checked above)
                        return false;
                    case PosRole.Interior:
                        return false;
                    default:
                        return false;
                }
            }

            private void Propagate(HashSet<int>[,] possible, int[,] collapsed, int W, int H, int startX, int startY)
            {
                var queue = new Queue<Point>();
                queue.Enqueue(new Point(startX, startY));
                var inQueue = new bool[W, H];
                inQueue[startX, startY] = true;

                while (queue.Count > 0)
                {
                    var p = queue.Dequeue();
                    int cx = p.X, cy = p.Y;
                    inQueue[cx, cy] = false;
                    var centerPossible = possible[cx, cy];

                    int[] dxs = { 1, -1, 0, 0 };
                    int[] dys = { 0, 0, 1, -1 };

                    for (int d = 0; d < 4; d++)
                    {
                        int nx = cx + dxs[d];
                        int ny = cy + dys[d];
                        if (nx < 0 || nx >= W || ny < 0 || ny >= H) continue;
                        if (collapsed[nx, ny] >= 0) continue;

                        var allowed = new HashSet<int>();
                        foreach (int pid in centerPossible)
                        {
                            if (!_byId.ContainsKey(pid)) continue;
                            var pat = _byId[pid];
                            HashSet<int> okSet;
                            if (d == 0) okSet = pat.RightOk;
                            else if (d == 1) okSet = pat.LeftOk;
                            else if (d == 2) okSet = pat.DownOk;
                            else okSet = pat.UpOk;

                            foreach (int ok in okSet)
                                allowed.Add(ok);
                        }

                        int beforeCount = possible[nx, ny].Count;

                        if (allowed.Count > 0)
                            possible[nx, ny].IntersectWith(allowed);

                        if (possible[nx, ny].Count < beforeCount && !inQueue[nx, ny])
                        {
                            inQueue[nx, ny] = true;
                            queue.Enqueue(new Point(nx, ny));
                        }
                    }
                }
            }

            /// <summary>
            /// Pick a pattern ID from the set, weighted by pattern weight and temperature.
            /// Low temperature → peaked distribution (favors high-weight patterns).
            /// High temperature → flat distribution (more random).
            /// </summary>
            private int PickWeightedFrom(HashSet<int> ids, double temperature)
            {
                if (ids.Count == 0) return _nonEmptyIds.Count > 0 ? _nonEmptyIds[_rng.Next(_nonEmptyIds.Count)] : 0;
                if (ids.Count == 1)
                {
                    foreach (int id in ids) return id;
                }

                // Apply temperature: weight^(1/temp)
                // Low temp → sharpen weights (deterministic)
                // High temp → flatten weights (uniform random)
                double invTemp = temperature > 0.001 ? 1.0 / temperature : 1000.0;
                invTemp = Math.Min(invTemp, 100.0); // clamp to prevent overflow

                double sum = 0;
                var weighted = new List<KeyValuePair<int, double>>();
                foreach (int id in ids)
                {
                    double w = _byId.ContainsKey(id) ? _byId[id].Weight : 0.1;
                    double adjusted = Math.Pow(w, invTemp);
                    if (double.IsInfinity(adjusted) || double.IsNaN(adjusted))
                        adjusted = w;
                    weighted.Add(new KeyValuePair<int, double>(id, adjusted));
                    sum += adjusted;
                }

                if (sum <= 0)
                {
                    int skip = _rng.Next(ids.Count);
                    foreach (int id in ids) { if (skip-- == 0) return id; }
                }

                double r = _rng.NextDouble() * sum;
                foreach (var kv in weighted)
                {
                    r -= kv.Value;
                    if (r <= 0) return kv.Key;
                }
                return weighted[weighted.Count - 1].Key;
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  Frequency-based generator (non-WFC) — also uses temperature
        // ════════════════════════════════════════════════════════════════

        private class FrequencyGenerator
        {
            private readonly ColumnRegistry _registry;
            private readonly Dictionary<int, HashSet<int>> _rightOf;
            private readonly Dictionary<int, HashSet<int>> _belowOf;
            private readonly Dictionary<int, HashSet<PosRole>> _roleMap;

            public FrequencyGenerator(ColumnRegistry registry,
                Dictionary<int, HashSet<int>> rightOf,
                Dictionary<int, HashSet<int>> belowOf,
                Dictionary<int, HashSet<PosRole>> roleMap)
            {
                _registry = registry;
                _rightOf = rightOf;
                _belowOf = belowOf;
                _roleMap = roleMap;
            }

            public int[,] Generate(int w, int h, double similarity, double temperature, Random rng)
            {
                var grid = new int[w, h];
                for (int x = 0; x < w; x++)
                    for (int y = 0; y < h; y++)
                        grid[x, y] = -1;

                var allIds = new List<int>();
                for (int i = 0; i < _registry.Count; i++)
                {
                    if (_registry.GetById(i).Items.Count > 0)
                        allIds.Add(i);
                }
                if (allIds.Count == 0) return grid;

                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        var outputRole = ClassifyPos(x, y, w, h);
                        var candidates = new HashSet<int>();
                        foreach (int id in allIds)
                        {
                            if (_roleMap.ContainsKey(id) && IsRoleOk(outputRole, _roleMap[id]))
                                candidates.Add(id);
                        }
                        if (candidates.Count == 0)
                            candidates = new HashSet<int>(allIds);

                        if (x > 0 && grid[x - 1, y] >= 0)
                        {
                            int leftId = grid[x - 1, y];
                            if (_rightOf.ContainsKey(leftId) && _rightOf[leftId].Count > 0)
                            {
                                var constrained = new HashSet<int>(candidates);
                                constrained.IntersectWith(_rightOf[leftId]);
                                if (constrained.Count > 0)
                                    candidates = constrained;
                            }
                        }

                        if (y > 0 && grid[x, y - 1] >= 0)
                        {
                            int aboveId = grid[x, y - 1];
                            if (_belowOf.ContainsKey(aboveId) && _belowOf[aboveId].Count > 0)
                            {
                                var constrained = new HashSet<int>(candidates);
                                constrained.IntersectWith(_belowOf[aboveId]);
                                if (constrained.Count > 0)
                                    candidates = constrained;
                            }
                        }

                        if (candidates.Count == 0)
                        {
                            // No pattern satisfies both role and adjacency — leave empty
                            grid[x, y] = -1;
                            continue;
                        }

                        // Temperature-based selection
                        if (temperature > 0.5 || rng.NextDouble() < temperature)
                        {
                            var list = candidates.ToList();
                            grid[x, y] = list[rng.Next(list.Count)];
                        }
                        else
                        {
                            grid[x, y] = candidates.First();
                        }
                    }
                }

                return grid;
            }

            private PosRole ClassifyPos(int x, int y, int w, int h)
            {
                bool top = (y == 0), bottom = (y == h - 1);
                bool left = (x == 0), right = (x == w - 1);
                if (top && left) return PosRole.CornerNW;
                if (top && right) return PosRole.CornerNE;
                if (bottom && left) return PosRole.CornerSW;
                if (bottom && right) return PosRole.CornerSE;
                if (top) return PosRole.EdgeN;
                if (bottom) return PosRole.EdgeS;
                if (left) return PosRole.EdgeW;
                if (right) return PosRole.EdgeE;
                return PosRole.Interior;
            }

            private bool IsRoleOk(PosRole outputRole, HashSet<PosRole> patRoles)
            {
                if (patRoles.Contains(outputRole)) return true;
                switch (outputRole)
                {
                    case PosRole.CornerNW:
                        return patRoles.Contains(PosRole.EdgeN) || patRoles.Contains(PosRole.EdgeW);
                    case PosRole.CornerNE:
                        return patRoles.Contains(PosRole.EdgeN) || patRoles.Contains(PosRole.EdgeE);
                    case PosRole.CornerSW:
                        return patRoles.Contains(PosRole.EdgeS) || patRoles.Contains(PosRole.EdgeW);
                    case PosRole.CornerSE:
                        return patRoles.Contains(PosRole.EdgeS) || patRoles.Contains(PosRole.EdgeE);
                    default:
                        return false;
                }
            }
        }
    }
}
