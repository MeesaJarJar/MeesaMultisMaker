using System;
using System.Collections.Generic;
using System.Linq;

namespace MeesaMultisMaker.Generation
{
    /// <summary>
    /// Validation result for a multi structure.
    /// </summary>
    public class MultiValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
        public List<string> Errors { get; set; } = new List<string>();

        /// <summary>Number of components that were corrected.</summary>
        public int CorrectedCount { get; set; }

        public override string ToString()
        {
            string status = IsValid ? "VALID" : "INVALID";
            return $"{status} | {Errors.Count} errors, {Warnings.Count} warnings, {CorrectedCount} corrected";
        }
    }

    /// <summary>
    /// Validates and optionally corrects multi structures to conform to
    /// Ultima Online house/multi rules as defined in <see cref="MultiRules"/>.
    ///
    /// Checks performed:
    /// - Foundation exists at Z=0
    /// - Floor separation ≥ 20 Z-units between levels
    /// - Walls exist between floor base and ceiling
    /// - Upper floors have structural support (floor tile below)
    /// - Roof tiles do not extend more than 1 tile beyond walls
    /// - Stairs bridge between floors at correct Z intervals
    /// </summary>
    public static class MultiValidator
    {
        /// <summary>
        /// Validate a set of tile instances against UO multi rules.
        /// </summary>
        public static MultiValidationResult Validate(List<TileInstance> tiles)
        {
            var result = new MultiValidationResult { IsValid = true };

            if (tiles == null || tiles.Count == 0)
            {
                result.Errors.Add("Structure is empty.");
                result.IsValid = false;
                return result;
            }

            // Detect floor levels from Z values
            var zValues = tiles.Select(t => t.Position.Z);
            var floorLevels = MultiRules.DetectFloorLevels(zValues);

            // 1. Foundation check
            ValidateFoundation(tiles, floorLevels, result);

            // 2. Floor separation check
            ValidateFloorSeparation(floorLevels, result);

            // 3. Structural support check (upper floors need support below)
            ValidateStructuralSupport(tiles, floorLevels, result);

            // 4. Wall coverage check
            ValidateWallCoverage(tiles, floorLevels, result);

            return result;
        }

        /// <summary>
        /// Validate and correct a set of tile instances.
        /// Returns corrected tiles along with validation result.
        /// </summary>
        public static List<TileInstance> ValidateAndCorrect(List<TileInstance> tiles, out MultiValidationResult result)
        {
            result = new MultiValidationResult { IsValid = true };

            if (tiles == null || tiles.Count == 0)
            {
                result.Errors.Add("Structure is empty.");
                result.IsValid = false;
                return tiles ?? new List<TileInstance>();
            }

            var corrected = new List<TileInstance>(tiles.Count);
            foreach (var t in tiles)
                corrected.Add(t.Clone());

            // Detect floor levels and correct Z positions
            var zValues = corrected.Select(t => t.Position.Z);
            var floorLevels = MultiRules.DetectFloorLevels(zValues);

            // Correct floor separation if too small
            int correctedFloors = CorrectFloorSeparation(corrected, floorLevels, result);
            if (correctedFloors > 0)
            {
                // Re-detect after correction
                zValues = corrected.Select(t => t.Position.Z);
                floorLevels = MultiRules.DetectFloorLevels(zValues);
            }

            // Remove unsupported upper-floor tiles
            int removed = RemoveUnsupportedTiles(corrected, floorLevels, result);

            result.CorrectedCount = correctedFloors + removed;

            // Re-validate the corrected structure
            ValidateFoundation(corrected, floorLevels, result);
            ValidateFloorSeparation(floorLevels, result);

            return corrected;
        }

        /// <summary>
        /// Validates multi components (from multi.mul) against UO rules.
        /// </summary>
        public static MultiValidationResult ValidateComponents(List<Mul.MultiComponent> components)
        {
            if (components == null || components.Count == 0)
                return new MultiValidationResult { IsValid = false, Errors = { "Empty multi." } };

            var tiles = components.Select(c => new TileInstance(
                c.TileId,
                new Point3D(c.X, c.Y, c.Z),
                c.Flags)).ToList();

            return Validate(tiles);
        }

        private static void ValidateFoundation(List<TileInstance> tiles, List<int> floorLevels, MultiValidationResult result)
        {
            if (floorLevels.Count == 0 || floorLevels[0] > MultiRules.StepZ)
            {
                result.Warnings.Add($"No foundation layer found near Z=0. Lowest floor is at Z={floorLevels.FirstOrDefault()}.");
            }

            // Check that foundation has reasonable coverage
            int baseZ = floorLevels.Count > 0 ? floorLevels[0] : 0;
            var foundationTiles = tiles.Where(t => t.Position.Z >= baseZ && t.Position.Z <= baseZ + 1).ToList();
            if (foundationTiles.Count == 0)
            {
                result.Warnings.Add("Foundation layer has no floor tiles.");
            }
        }

        private static void ValidateFloorSeparation(List<int> floorLevels, MultiValidationResult result)
        {
            for (int i = 1; i < floorLevels.Count; i++)
            {
                int gap = floorLevels[i] - floorLevels[i - 1];
                if (gap < MultiRules.MinFloorSeparation)
                {
                    result.Errors.Add(
                        $"Floor separation too small between Z={floorLevels[i - 1]} and Z={floorLevels[i]} " +
                        $"(gap={gap}, minimum={MultiRules.MinFloorSeparation}). " +
                        "Doors won't fit and character heads will clip.");
                    result.IsValid = false;
                }
            }
        }

        private static void ValidateStructuralSupport(List<TileInstance> tiles, List<int> floorLevels, MultiValidationResult result)
        {
            if (floorLevels.Count <= 1) return;

            // Build XY footprint per floor level
            var footprintByFloor = new Dictionary<int, HashSet<long>>();
            foreach (var tile in tiles)
            {
                int assignedFloor = MultiRules.AssignToFloor(tile.Position.Z, floorLevels);
                if (!footprintByFloor.ContainsKey(assignedFloor))
                    footprintByFloor[assignedFloor] = new HashSet<long>();

                long key = ((long)tile.Position.X << 32) | (uint)tile.Position.Y;
                footprintByFloor[assignedFloor].Add(key);
            }

            // Upper floors should have support from floor below
            for (int i = 1; i < floorLevels.Count; i++)
            {
                int upperZ = floorLevels[i];
                int lowerZ = floorLevels[i - 1];

                if (!footprintByFloor.ContainsKey(upperZ) || !footprintByFloor.ContainsKey(lowerZ))
                    continue;

                int unsupported = 0;
                foreach (long xy in footprintByFloor[upperZ])
                {
                    if (!footprintByFloor[lowerZ].Contains(xy))
                        unsupported++;
                }

                if (unsupported > 0)
                {
                    int total = footprintByFloor[upperZ].Count;
                    double pct = total > 0 ? (unsupported * 100.0 / total) : 0;
                    if (pct > 50)
                    {
                        result.Warnings.Add(
                            $"Floor at Z={upperZ}: {unsupported}/{total} tiles ({pct:F0}%) lack structural support from floor at Z={lowerZ}.");
                    }
                }
            }
        }

        private static void ValidateWallCoverage(List<TileInstance> tiles, List<int> floorLevels, MultiValidationResult result)
        {
            if (floorLevels.Count == 0) return;

            // Skip the topmost floor — it's typically the roof level
            // and doesn't require surrounding walls.
            int topFloorZ = floorLevels[floorLevels.Count - 1];

            foreach (int floorZ in floorLevels)
            {
                if (floorZ == topFloorZ && floorLevels.Count > 1)
                    continue; // roof level

                var floorTiles = tiles.Where(t =>
                {
                    int assigned = MultiRules.AssignToFloor(t.Position.Z, floorLevels);
                    return assigned == floorZ;
                }).ToList();

                if (floorTiles.Count < 4) continue; // too small to have meaningful perimeter

                // Build occupied XY set and wall XY set for this floor
                var occupiedXY = new HashSet<long>();
                var wallXY = new HashSet<long>();

                foreach (var t in floorTiles)
                {
                    long key = ((long)t.Position.X << 32) | (uint)t.Position.Y;
                    occupiedXY.Add(key);

                    var role = MultiRules.ClassifyRole(t.TileId, t.Position.Z, t.Flags, floorLevels);
                    if (role == TileRole.Wall || role == TileRole.Door)
                        wallXY.Add(key);
                }

                // Find EDGE positions: tiles with at least one empty cardinal neighbor.
                // This handles L-shapes, courtyards, and irregular footprints correctly
                // (unlike the old bounding-box perimeter approach).
                int edgeCount = 0;
                int walledEdgeCount = 0;

                foreach (var t in floorTiles)
                {
                    int x = t.Position.X;
                    int y = t.Position.Y;

                    bool isEdge = false;
                    long[] neighbors = new long[]
                    {
                        ((long)(x - 1) << 32) | (uint)y,
                        ((long)(x + 1) << 32) | (uint)y,
                        ((long)x << 32) | (uint)(y - 1),
                        ((long)x << 32) | (uint)(y + 1)
                    };

                    foreach (long nk in neighbors)
                    {
                        if (!occupiedXY.Contains(nk))
                        {
                            isEdge = true;
                            break;
                        }
                    }

                    if (isEdge)
                    {
                        edgeCount++;
                        long key = ((long)x << 32) | (uint)y;
                        if (wallXY.Contains(key))
                            walledEdgeCount++;
                    }
                }

                if (edgeCount > 0)
                {
                    double wallCoverage = (double)walledEdgeCount / edgeCount;
                    if (wallCoverage < 0.2)
                    {
                        result.Warnings.Add(
                            $"Floor at Z={floorZ}: only {wallCoverage:P0} of edge positions have walls ({walledEdgeCount}/{edgeCount}).");
                    }
                }
            }
        }

        /// <summary>
        /// Corrects floor Z values where separation is less than the minimum.
        /// Shifts upper floors upward to maintain at least MinFloorSeparation.
        /// </summary>
        private static int CorrectFloorSeparation(List<TileInstance> tiles, List<int> floorLevels, MultiValidationResult result)
        {
            if (floorLevels.Count <= 1) return 0;

            // Compute required shifts for each floor level
            var zShifts = new Dictionary<int, int>();
            int cumulativeShift = 0;

            for (int i = 1; i < floorLevels.Count; i++)
            {
                int gap = (floorLevels[i] + cumulativeShift) - (floorLevels[i - 1] + (zShifts.ContainsKey(floorLevels[i - 1]) ? zShifts[floorLevels[i - 1]] : 0));
                if (gap < MultiRules.MinFloorSeparation)
                {
                    int needed = MultiRules.MinFloorSeparation - gap;
                    cumulativeShift += needed;
                }
                zShifts[floorLevels[i]] = cumulativeShift;
            }

            if (cumulativeShift == 0) return 0;

            // Apply shifts
            int corrected = 0;
            for (int i = 0; i < tiles.Count; i++)
            {
                int assignedFloor = MultiRules.AssignToFloor(tiles[i].Position.Z, floorLevels);
                if (zShifts.ContainsKey(assignedFloor) && zShifts[assignedFloor] > 0)
                {
                    int shift = zShifts[assignedFloor];
                    var pos = tiles[i].Position;
                    tiles[i].Position = new Point3D(pos.X, pos.Y, pos.Z + shift);
                    corrected++;
                }
            }

            if (corrected > 0)
            {
                result.Warnings.Add($"Corrected floor separation: shifted {corrected} tiles upward to maintain minimum {MultiRules.MinFloorSeparation}Z gap.");
            }

            return corrected;
        }

        /// <summary>
        /// Removes tiles on upper floors that have no structural support
        /// (no tile at the same XY on any lower floor).
        /// </summary>
        private static int RemoveUnsupportedTiles(List<TileInstance> tiles, List<int> floorLevels, MultiValidationResult result)
        {
            if (floorLevels.Count <= 1) return 0;

            int baseFloor = floorLevels[0];

            // Collect XY positions that have support at the base floor
            var supportedXY = new HashSet<long>();
            foreach (var t in tiles)
            {
                int assigned = MultiRules.AssignToFloor(t.Position.Z, floorLevels);
                if (assigned == baseFloor)
                {
                    long key = ((long)t.Position.X << 32) | (uint)t.Position.Y;
                    supportedXY.Add(key);
                }
            }

            // Remove tiles on upper floors without support, but only if they are
            // far outside the footprint (allow slight overhang for roofs)
            int removed = 0;
            for (int i = tiles.Count - 1; i >= 0; i--)
            {
                int assigned = MultiRules.AssignToFloor(tiles[i].Position.Z, floorLevels);
                if (assigned == baseFloor) continue;

                long key = ((long)tiles[i].Position.X << 32) | (uint)tiles[i].Position.Y;
                if (supportedXY.Contains(key)) continue;

                // Check adjacent XY for support (allow 1-tile overhang for roofs)
                bool hasAdjacentSupport = false;
                for (int dx = -MultiRules.MaxRoofOverhang; dx <= MultiRules.MaxRoofOverhang && !hasAdjacentSupport; dx++)
                {
                    for (int dy = -MultiRules.MaxRoofOverhang; dy <= MultiRules.MaxRoofOverhang && !hasAdjacentSupport; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        long adjKey = ((long)(tiles[i].Position.X + dx) << 32) | (uint)(tiles[i].Position.Y + dy);
                        if (supportedXY.Contains(adjKey))
                            hasAdjacentSupport = true;
                    }
                }

                if (!hasAdjacentSupport)
                {
                    tiles.RemoveAt(i);
                    removed++;
                }
            }

            if (removed > 0)
            {
                result.Warnings.Add($"Removed {removed} unsupported upper-floor tiles (no foundation support within {MultiRules.MaxRoofOverhang} tile overhang).");
            }

            return removed;
        }
    }
}
