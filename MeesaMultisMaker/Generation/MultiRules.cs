using System;
using System.Collections.Generic;
using System.Linq;

namespace MeesaMultisMaker.Generation
{
    /// <summary>
    /// Ultima Online multi/house structural constants and rules.
    ///
    /// Layer structure:
    ///   Foundation   Z = 0
    ///   Steps        Z = 7  (typical +7 from foundation)
    ///   Floor 1      Z = 7  (walls/doors/floor tiles)
    ///   Floor 2      Z = 27 (minimum 20 above floor 1)
    ///   Floor 3      Z = 47 (minimum 20 above floor 2)
    ///   Roof         top floor ceiling
    ///
    /// Rendering:
    ///   Each Z unit = 4 pixels vertical offset.
    ///   Grid tile = 44x44 pixels.
    ///   Draw order: back-to-front, left-to-right, bottom-Z to top-Z.
    /// </summary>
    public static class MultiRules
    {
        /// <summary>Pixels per Z unit in isometric rendering.</summary>
        public const int PixelsPerZ = 4;

        /// <summary>Tile grid width in pixels.</summary>
        public const int TileWidth = 44;

        /// <summary>Tile grid height in pixels.</summary>
        public const int TileHeight = 44;

        /// <summary>Foundation layer Z.</summary>
        public const int FoundationZ = 0;

        /// <summary>
        /// Typical step height above the foundation.
        /// Steps transition from Z=0 ground to Z=7 first floor.
        /// </summary>
        public const int StepZ = 7;

        /// <summary>
        /// Minimum Z separation between adjacent floors.
        /// Required for doors to fit, character heads not to clip, and
        /// pathfinding to work correctly.
        /// </summary>
        public const int MinFloorSeparation = 20;

        /// <summary>First floor Z (after steps).</summary>
        public const int Floor1Z = StepZ;

        /// <summary>Second floor Z.</summary>
        public const int Floor2Z = Floor1Z + MinFloorSeparation;

        /// <summary>Third floor Z.</summary>
        public const int Floor3Z = Floor2Z + MinFloorSeparation;

        /// <summary>
        /// Maximum number of roof overhang tiles beyond the wall perimeter.
        /// </summary>
        public const int MaxRoofOverhang = 1;

        /// <summary>
        /// Determines which floor level a given Z value belongs to.
        /// Returns 0 for foundation, 1 for first floor, 2 for second, etc.
        /// </summary>
        public static int GetFloorLevel(int z)
        {
            if (z < Floor1Z)
                return 0; // foundation
            return 1 + (z - Floor1Z) / MinFloorSeparation;
        }

        /// <summary>
        /// Returns the base Z for a given floor level.
        /// Floor 0 = 0, Floor 1 = 7, Floor 2 = 27, Floor 3 = 47, etc.
        /// </summary>
        public static int GetFloorBaseZ(int floorLevel)
        {
            if (floorLevel <= 0) return FoundationZ;
            return Floor1Z + (floorLevel - 1) * MinFloorSeparation;
        }

        /// <summary>
        /// Snaps a Z value to the nearest valid floor base Z.
        /// </summary>
        public static int SnapToFloorZ(int z)
        {
            int floor = GetFloorLevel(z);
            return GetFloorBaseZ(floor);
        }

        /// <summary>
        /// Detects discrete floor levels present in a set of components,
        /// using clustering with <see cref="MinFloorSeparation"/> as the gap threshold.
        /// </summary>
        public static List<int> DetectFloorLevels(IEnumerable<int> zValues)
        {
            var sorted = new SortedSet<int>(zValues);
            if (sorted.Count == 0)
                return new List<int> { FoundationZ };

            var floors = new List<int>();
            int lastFloorZ = int.MinValue;

            foreach (int z in sorted)
            {
                if (lastFloorZ == int.MinValue || z - lastFloorZ >= MinFloorSeparation)
                {
                    floors.Add(z);
                    lastFloorZ = z;
                }
            }

            if (floors.Count == 0)
                floors.Add(FoundationZ);

            return floors;
        }

        /// <summary>
        /// Assigns a Z value to the nearest detected floor level.
        /// </summary>
        public static int AssignToFloor(int z, List<int> floorLevels)
        {
            if (floorLevels == null || floorLevels.Count == 0)
                return FoundationZ;

            int bestFloor = floorLevels[0];
            int bestDist = Math.Abs(z - bestFloor);

            for (int i = 1; i < floorLevels.Count; i++)
            {
                int dist = Math.Abs(z - floorLevels[i]);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestFloor = floorLevels[i];
                }
            }

            return bestFloor;
        }

        /// <summary>
        /// Classify a tile's structural role using TileData flags with
        /// floor-level awareness. Falls back to Z-based heuristics when
        /// TileData is unavailable.
        ///
        /// Flag priority:
        ///   1. Explicit flags: Door, Roof, StairBack/StairRight, Wall
        ///   2. Surface (walkable) without Wall → Floor
        ///   3. Impassable + Height > 0 → Wall  (many cabin/custom walls
        ///      lack the explicit Wall flag but have Impassable)
        ///   4. Impassable + Height 0 on topmost floor → Roof  (cap tiles)
        ///   5. Bridge flag → Floor  (walkable under/over)
        ///   6. Z-heuristic fallback with floor awareness
        /// </summary>
        public static TileRole ClassifyRole(ushort tileId, int z, int flags, List<int> floorLevels)
        {
            var tileData = TileDataLookup.GetItemData(tileId);
            if (tileData != null)
            {
                var f = tileData.Flags;

                // 1. Explicit structural flags (most reliable)
                if (f.HasFlag(Mul.TileFlag.Door))
                    return TileRole.Door;
                if (f.HasFlag(Mul.TileFlag.Roof))
                    return TileRole.Roof;
                if (f.HasFlag(Mul.TileFlag.StairBack) || f.HasFlag(Mul.TileFlag.StairRight))
                    return TileRole.Stair;
                if (f.HasFlag(Mul.TileFlag.Wall))
                    return TileRole.Wall;

                // 2. Walkable surface → Floor
                if (f.HasFlag(Mul.TileFlag.Surface) && !f.HasFlag(Mul.TileFlag.Impassable))
                    return TileRole.Floor;

                // 3. Bridge (walkable platform) → Floor
                if (f.HasFlag(Mul.TileFlag.Bridge))
                    return TileRole.Floor;

                // 4. Impassable with height → Wall (log walls, custom walls, etc.)
                //    Many custom building pieces (log cabins, sandstone, fieldstone)
                //    use Impassable without the explicit Wall flag.
                if (f.HasFlag(Mul.TileFlag.Impassable) && tileData.Height > 0)
                    return TileRole.Wall;

                // 5. Impassable + zero height on the topmost floor → Roof cap
                if (f.HasFlag(Mul.TileFlag.Impassable) && tileData.Height == 0
                    && floorLevels != null && floorLevels.Count > 0)
                {
                    int topFloorZ = floorLevels[floorLevels.Count - 1];
                    if (z >= topFloorZ)
                        return TileRole.Roof;
                }

                // 6. Surface + Impassable (e.g. some foundation tiles)
                if (f.HasFlag(Mul.TileFlag.Surface))
                    return TileRole.Floor;

                // TileData loaded but no structural flags — fall through to Z heuristic
            }

            // Z-based heuristic with floor awareness
            if (floorLevels != null && floorLevels.Count > 0)
            {
                int floorZ = AssignToFloor(z, floorLevels);
                int relativeZ = z - floorZ;

                // Topmost floor: anything above the floor base is likely roof
                int topFloorZ = floorLevels[floorLevels.Count - 1];
                if (floorZ == topFloorZ && relativeZ > 0)
                    return TileRole.Roof;

                // On the floor base → floor tile
                if (relativeZ <= 0)
                    return TileRole.Floor;

                // Within wall height of this floor (below next floor)
                if (relativeZ < MinFloorSeparation)
                    return TileRole.Wall;

                return TileRole.Roof;
            }

            // Simple fallback
            if (z <= FoundationZ)
                return TileRole.Floor;
            if (z <= StepZ)
                return TileRole.Stair;

            return TileRole.Wall;
        }
    }
}
