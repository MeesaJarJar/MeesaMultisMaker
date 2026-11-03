using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MeesaMultisMaker
{
    /// <summary>
    /// "Split Multi by Z-Bands + Category" (main canvas): dialog hosting,
    /// live tint preview state, and the Explode displacement.
    /// </summary>
    public partial class Form1
    {
        // Live part-color tint per object. Null/empty = preview off. Read ONLY
        // by DesignPictureBox_Paint (Form1.Canvas.cs) to draw a translucent
        // overlay rect per object; PlacedObject art is never touched.
        private Dictionary<PlacedObject, Color> zBandPreviewColors;

        /// <summary>
        /// Toolbar "Split Z-Bands" entry. Empty canvas is refused with a
        /// status hint (dialog would have nothing to histogram).
        /// </summary>
        private void OpenZBandSplitDialog()
        {
            if (placedObjects.Count == 0)
            {
                outputTextBox.AppendText("Split Z-Bands: canvas is empty - place objects first.\r\n");
                MessageBox.Show("Canvas is empty. Place objects first.",
                    "Split Z-Bands", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                using (var dlg = new ZBandSplitDialog(
                    placedObjects,
                    ClassifyForZBand,
                    map => { zBandPreviewColors = map; designPictureBox.Invalidate(); },
                    members => SelectZBandPart(members)))
                {
                    var result = dlg.ShowDialog(this);
                    if (result == DialogResult.OK && dlg.ResultParts != null && dlg.ResultParts.Count > 0)
                        ExplodeZBands(dlg.ResultParts, dlg.ResultCuts);
                }
            }
            catch (Exception ex)
            {
                outputTextBox.AppendText("Split Z-Bands failed: " + ex.Message + "\r\n");
            }
            finally
            {
                zBandPreviewColors = null;
                try { designPictureBox.Invalidate(); } catch { }
            }
        }

        /// <summary>
        /// Bridge from ZBandSplitter's pure classifier to live TileData: parses
        /// the GraphicId hex (unparseable "drop_N"/PNG ids -&gt; null, forcing the
        /// Z-heuristic fallback) and serves ItemTileData only when
        /// tiledata.mul actually loaded.
        /// </summary>
        private ZPartCategory ClassifyForZBand(PlacedObject o, int bandBaseZ, int topBandBaseZ, bool isTopBand)
        {
            ushort? tileId = null;
            try
            {
                int gid;
                if (o != null && TryParseGraphicId(o.GraphicId, out gid) && gid >= 0 && gid <= 0xFFFF)
                    tileId = (ushort)gid;
            }
            catch { tileId = null; }
            int z = (o != null) ? o.Z : 0;
            return ZBandSplitter.ClassifyCategory(
                tileId, z, bandBaseZ, topBandBaseZ, isTopBand,
                id => (tileDataReader != null && tileDataReader.IsLoaded) ? tileDataReader.GetItemTile(id) : null);
        }

        /// <summary>Select a part's members on the canvas (dialog "Select on canvas").</summary>
        private void SelectZBandPart(List<PlacedObject> members)
        {
            try
            {
                selectedObjects.Clear();
                selectedObject = null;
                isSkewMode = false;
                isRotateMode = false;
                if (members != null)
                {
                    foreach (var o in members)
                        if (o != null && placedObjects.Contains(o) && !lockedObjects.Contains(o))
                            selectedObjects.Add(o);
                    selectedObject = selectedObjects.FirstOrDefault();
                }
                SyncListBoxSelections();
                designPictureBox.Invalidate();
            }
            catch { }
        }

        /// <summary>
        /// Explode: ONE PushUndo (the same full-snapshot path as drag-move,
        /// which deep-clones every PlacedObject including transforms, so
        /// undo restores pre-explode positions exactly), then displace each
        /// part's objects to non-overlapping canvas regions in a row grid
        /// with footprint-derived spacing, keeping relative layout inside
        /// each part.
        ///
        /// Move math reuses the drag path verbatim (GridX/GridY clamp-free
        /// assignment + IsoPosition = GridToIso); only GridX/GridY change, so
        /// skew/rotation/scale/flip/pixel-offsets are preserved and Z-ties
        /// (same-X/Y/Z stacks) stay internally identical. Hidden and locked
        /// objects move with their part (undo restores all). Everything stays
        /// live objects: no art mutation, no file writes. The canvas grid
        /// grows to fit; grid growth is NOT part of the undo snapshot (same
        /// as every existing ResizeCanvas caller), so undo restores objects
        /// while the enlarged grid stays.
        /// </summary>
        private void ExplodeZBands(List<ZPart> parts, List<int> cuts)
        {
            if (parts == null || parts.Count == 0) return;
            PushUndo();
            try
            {
                int minX = placedObjects.Min(o => o.GridX);
                int minY = placedObjects.Min(o => o.GridY);
                int maxX = placedObjects.Max(o => o.GridX);
                int maxY = placedObjects.Max(o => o.GridY);
                int footW = Math.Max(1, maxX - minX + 1);
                int footH = Math.Max(1, maxY - minY + 1);

                int columns = Math.Min(ZBandSplitter.ExplodeColumns, Math.Max(1, parts.Count));
                var moves = ZBandSplitter.ComputeExplodeMoves(parts, minX, minY, footW, footH, columns);
                foreach (var kv in moves)
                {
                    var o = kv.Key;
                    o.GridX = kv.Value.X;
                    o.GridY = kv.Value.Y;
                    o.IsoPosition = GridToIso(kv.Value.X, kv.Value.Y);
                }

                // Grow the grid to contain the laid-out parts (no extra
                // PushUndo: the one above already covers object state).
                int needW = placedObjects.Max(o => o.GridX) + 1;
                int needH = placedObjects.Max(o => o.GridY) + 1;
                int newW = Math.Max(gridWidth, needW);
                int newH = Math.Max(gridHeight, needH);
                if (newW != gridWidth || newH != gridHeight)
                {
                    gridWidth = newW;
                    gridHeight = newH;
                    try
                    {
                        widthNumericUpDown.Value = Math.Min(widthNumericUpDown.Maximum,
                            Math.Max(widthNumericUpDown.Minimum, newW));
                        heightNumericUpDown.Value = Math.Min(heightNumericUpDown.Maximum,
                            Math.Max(heightNumericUpDown.Minimum, newH));
                    }
                    catch { }
                    ResizeCanvas(newW, newH, pushUndo: false);
                }

                int lockedCount = parts.Sum(p => p.Members.Count(m => m != null && lockedObjects.Contains(m)));
                outputTextBox.AppendText(string.Format(
                    "Split Z-Bands exploded: {0} part(s) in {1} column(s), footprint {2}x{3} (cuts: {4}){5}\r\n",
                    parts.Count, columns, footW, footH,
                    (cuts != null && cuts.Count > 0) ? string.Join(",", cuts.Select(c => "Z<=" + c)) : "none (single band)",
                    lockedCount > 0 ? string.Format(", incl. {0} locked (moved with part)", lockedCount) : string.Empty));
                foreach (var p in parts)
                    outputTextBox.AppendText(string.Format("  - {0}: {1} object(s)\r\n", p.Name, p.Members.Count));
                outputTextBox.AppendText("Export Parts note: parts are spatial groups, not ID groups - Export Parts still emits one PNG per unique GraphicId; use the manifest gridX/gridY rows (now separated per part) to map placements back to these part names.\r\n");
            }
            catch (Exception ex)
            {
                outputTextBox.AppendText("Split Z-Bands explode failed: " + ex.Message + "\r\n");
            }
            finally
            {
                zBandPreviewColors = null;
                RebuildLockLists();
                designPictureBox.Invalidate();
            }
        }
    }
}
