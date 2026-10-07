using System.Drawing;

namespace MeesaMultisMaker
{
    public class PlacedObject
    {
        public Image Image { get; set; }
        public Image OriginalImage { get; set; } // Backup of image before AI modification
        public Image PreEditImage { get; set; } // Backup of image before manual editing
        public string GraphicId { get; set; }
        public int GridX { get; set; }
        public int GridY { get; set; }
        public int Z { get; set; }
        public int Flags { get; set; }
        public Point IsoPosition { get; set; }
        public int Layer { get; set; } //0..N-1 draw order
        public bool Hidden { get; set; } // when true, not rendered or hit-tested
        public bool IsEdited { get; set; } // Track if image has been manually edited

        // Transformation properties
        public float Scale { get; set; } = 1.0f; // Scale factor (1.0 = original size)
        public float Rotation { get; set; } = 0f; // Rotation in degrees (0, 90, 180, 270 or any angle)
        public int PixelOffsetX { get; set; } = 0; // Fine pixel offset X (for precise positioning)
        public int PixelOffsetY { get; set; } = 0; // Fine pixel offset Y (for precise positioning)
        public bool FlipHorizontal { get; set; } = false; // Horizontal flip
        public bool FlipVertical { get; set; } = false; // Vertical flip

        // Skew/Distort properties - corner offsets from default rectangle positions
        // These are offsets in pixels from where the corner would normally be
        // TopLeft = corner 0, TopRight = corner 1, BottomRight = corner 2, BottomLeft = corner 3
        public PointF SkewTopLeft { get; set; } = PointF.Empty;      // Offset for top-left corner
        public PointF SkewTopRight { get; set; } = PointF.Empty;     // Offset for top-right corner
        public PointF SkewBottomRight { get; set; } = PointF.Empty;  // Offset for bottom-right corner
        public PointF SkewBottomLeft { get; set; } = PointF.Empty;   // Offset for bottom-left corner

        /// <summary>
        /// Returns true if any skew/distort transformation is applied
        /// </summary>
        public bool HasSkew => SkewTopLeft != PointF.Empty || SkewTopRight != PointF.Empty ||
                               SkewBottomRight != PointF.Empty || SkewBottomLeft != PointF.Empty;

        /// <summary>
        /// Resets all skew offsets to zero
        /// </summary>
        public void ResetSkew()
        {
            SkewTopLeft = PointF.Empty;
            SkewTopRight = PointF.Empty;
            SkewBottomRight = PointF.Empty;
            SkewBottomLeft = PointF.Empty;
        }

        /// <summary>
        /// Deep-copy for undo/clipboard snapshots. Bitmaps are cloned so
        /// canvas, clipboard and undo entries never share Image instances.
        /// Falls back to the shared reference if cloning fails.
        /// </summary>
        public PlacedObject CloneForUndo()
        {
            return new PlacedObject
            {
                Image = CloneImageSafe(Image),
                OriginalImage = CloneImageSafe(OriginalImage),
                PreEditImage = CloneImageSafe(PreEditImage),
                GraphicId = GraphicId,
                GridX = GridX,
                GridY = GridY,
                Z = Z,
                Flags = Flags,
                IsoPosition = IsoPosition,
                Layer = Layer,
                Hidden = Hidden,
                IsEdited = IsEdited,
                Scale = Scale,
                Rotation = Rotation,
                FlipHorizontal = FlipHorizontal,
                FlipVertical = FlipVertical,
                PixelOffsetX = PixelOffsetX,
                PixelOffsetY = PixelOffsetY,
                SkewTopLeft = SkewTopLeft,
                SkewTopRight = SkewTopRight,
                SkewBottomRight = SkewBottomRight,
                SkewBottomLeft = SkewBottomLeft
            };
        }

        private static Image CloneImageSafe(Image src)
        {
            if (src == null) return null;
            try
            {
                // Touch Width/Height first: a disposed GDI+ image throws
                // ArgumentException ("Parameter is not valid") here. In that
                // case return null so callers skip the object instead of
                // crashing, and so we never return a shared/disposed reference.
                var w = src.Width;
                var h = src.Height;
                if (w <= 0 || h <= 0) return null;
                return new Bitmap(src);
            }
            catch { return null; }
        }

        public override string ToString()
        {
            string transformInfo = "";
            if (Scale != 1.0f) transformInfo += $" S:{Scale:F2}";
            if (Rotation != 0f) transformInfo += $" R:{Rotation:F0}°";
            if (FlipHorizontal || FlipVertical) transformInfo += $" F:{(FlipHorizontal ? "H" : "")}{(FlipVertical ? "V" : "")}";
            if (PixelOffsetX != 0 || PixelOffsetY != 0) transformInfo += $" O:{PixelOffsetX},{PixelOffsetY}";
            if (HasSkew) transformInfo += " Skew";

            return $"{GraphicId} @ ({GridX},{GridY}) Z:{Z} L:{Layer}{(Hidden ? " (Hidden)" : string.Empty)}{(IsEdited ? " (Edited)" : string.Empty)}{transformInfo}";
        }
    }
}
