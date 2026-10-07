using System.Collections.Generic;
using MeesaMultisMaker.Mul;

namespace MeesaMultisMaker
{
    /// <summary>
    /// Captures the before/after state of a single user action on the map
    /// so it can be undone and redone.  Stores only deltas (affected positions),
    /// not full map snapshots, for memory efficiency.
    /// </summary>
    public class MapAction
    {
        public string Description { get; set; }

        /// <summary>
        /// Land tile changes keyed by world position.
        /// </summary>
        public Dictionary<(int x, int y), LandTileChange> LandChanges { get; set; }
            = new Dictionary<(int x, int y), LandTileChange>();

        /// <summary>
        /// Static override changes keyed by world position.
        /// </summary>
        public Dictionary<(int x, int y), StaticOverrideChange> StaticChanges { get; set; }
            = new Dictionary<(int x, int y), StaticOverrideChange>();

        public bool HasChanges => LandChanges.Count > 0 || StaticChanges.Count > 0;

        /// <summary>
        /// Deep-clone a list of <see cref="StaticTile"/> for safe snapshotting.
        /// </summary>
        public static List<StaticTile> CloneStaticList(List<StaticTile> source)
        {
            if (source == null) return null;
            var clone = new List<StaticTile>(source.Count);
            foreach (var s in source)
            {
                clone.Add(new StaticTile
                {
                    ItemId = s.ItemId,
                    X = s.X,
                    Y = s.Y,
                    Z = s.Z,
                    Hue = s.Hue,
                    WorldX = s.WorldX,
                    WorldY = s.WorldY
                });
            }
            return clone;
        }
    }

    /// <summary>
    /// Before/after snapshot for a single land tile position.
    /// </summary>
    public struct LandTileChange
    {
        public ushort OldTileId;
        public sbyte OldZ;
        public ushort NewTileId;
        public sbyte NewZ;
    }

    /// <summary>
    /// Before/after snapshot for a single static-override position.
    /// </summary>
    public class StaticOverrideChange
    {
        /// <summary>
        /// True if this position had an entry in staticOverrides before the action.
        /// When false, undo removes the key entirely rather than restoring a list.
        /// </summary>
        public bool HadOverrideBefore;

        /// <summary>
        /// The override list before the action (null when HadOverrideBefore is false).
        /// </summary>
        public List<StaticTile> OldOverride;

        /// <summary>
        /// The override list after the action.
        /// </summary>
        public List<StaticTile> NewOverride;
    }
}
