using MeesaMultisMaker.ThreeD;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace MeesaMultisMaker
{
    /// <summary>
    /// UO tile slicing: hard-cut the cloud into per-tile columns (full
    /// height), list them on the right for individual select/isolate, and
    /// send every tile to the multi canvas at its GridX/GridY in one go.
    ///
    /// Mapping: baked footprint (current Rot/Scale applied) maps exactly to
    /// TilesX x TilesZ. Piece (TX,TZ) -> canvas (BaseX+TX, BaseY+TZ, BaseZ).
    /// Export recenters each piece on its tile center and does NOT crop, so
    /// in-tile moves stay visible as pixel offsets on the canvas.
    /// </summary>
    public partial class ThreeDEditorForm
    {
        public class TileSprite
        {
            public Bitmap Image;
            public string Name;
            public int GridX;
            public int GridY;
            public int Z;
        }

        /// <summary>
        /// Fired for multi-tile send. Bitmap ownership transfers to the
        /// subscriber (do not dispose here). Form1 places each at GridX/GridY/Z.
        /// </summary>
        public event Action<List<TileSprite>> TilesReady;

        private Panel _tileDock;
        private NumericUpDown _tileBaseZNum;
        private NumericUpDown _nineWNum, _nineHNum;
        private Button _nineSendBtn;
        private Label _nineStateLabel;
        private CheckBox _nineHollowChk;
        private Button _sliceBtn, _sliceClearBtn;
        private Button _tileSendAllBtn, _tileExportAllBtn;
        private ListBox _tileList;
        private CheckBox _tileIsolateChk;
        private Label _sliceStatusLabel;

        private TileSliceResult _slice;
        private int _activeTileIdx = -1;
        private PlySplatModel _isolateCache;
        private int _isolateFor = -1;

        private void BuildTileDock()
        {
            try
            {
                if (_split == null || _split.Panel2 == null) return;
                _tileDock = new Panel
                {
                    Dock = DockStyle.Right,
                    Width = 210,
                    Padding = new Padding(6),
                    BackColor = HolographicTheme.PanelBackground
                };
                _split.Panel2.Controls.Add(_tileDock);
                _tileDock.BringToFront();

                int y = 8;
                var title = MakeLabel("TILE SLICES (UO grid)", 6, y, 190, 18);
                title.Font = new Font(title.Font.FontFamily, title.Font.Size, FontStyle.Bold);
                _tileDock.Controls.Add(title);
                y += 22;

                _tileDock.Controls.Add(MakeLabel("Grid: floor (follows viewer)", 6, y + 4, 184, 20));
                y += 26;

                _tileDock.Controls.Add(MakeLabel("Base Z (height):", 6, y + 4, 92, 20));
                _tileBaseZNum = new NumericUpDown
                {
                    Location = new Point(100, y), Width = 52, Height = 20,
                    Minimum = -128, Maximum = 128, Value = 0
                };
                _tileDock.Controls.Add(_tileBaseZNum);
                y += 26;

                _sliceBtn = new Button { Location = new Point(6, y), Width = 96, Height = 26, Text = "Slice" };
                _sliceBtn.Click += SliceBtn_Click;
                _tileDock.Controls.Add(_sliceBtn);
                _sliceClearBtn = new Button { Location = new Point(106, y), Width = 84, Height = 26, Text = "Clear" };
                _sliceClearBtn.Click += delegate { ClearSlices("Slices cleared."); };
                _tileDock.Controls.Add(_sliceClearBtn);
                y += 32;
                try
                {
                    var tip = new ToolTip { ShowAlways = true };
                    tip.SetToolTip(_sliceBtn, "Hard-cut the cloud on the floor grid (full height). One slice per floor cell holding gaussians; empty cells skipped.");
                    tip.SetToolTip(_tileBaseZNum, "Height level all tiles land on. The canvas centers the batch on its grid automatically.");
                }
                catch { }
                y += 2;

                _tileList = new ListBox
                {
                    Location = new Point(6, y), Width = 184, Height = 300
                };
                _tileList.SelectedIndexChanged += TileList_SelectedIndexChanged;
                _tileDock.Controls.Add(_tileList);
                y += 306;

                _tileIsolateChk = new CheckBox { Location = new Point(6, y), Width = 184, Height = 20, Text = "Isolate selected tile" };
                _tileIsolateChk.CheckedChanged += delegate
                {
                    _isolateCache = null;
                    _isolateFor = -1;
                    QueueRender();
                };
                _tileDock.Controls.Add(_tileIsolateChk);
                y += 24;

                _tileSendAllBtn = new Button { Location = new Point(6, y), Width = 184, Height = 28, Text = "Send all tiles to canvas" };
                _tileSendAllBtn.Click += TileSendAllBtn_Click;
                _tileDock.Controls.Add(_tileSendAllBtn);
                y += 32;

                _tileExportAllBtn = new Button { Location = new Point(6, y), Width = 184, Height = 26, Text = "Export all tiles (PNGs)" };
                _tileExportAllBtn.Click += TileExportAllBtn_Click;
                _tileDock.Controls.Add(_tileExportAllBtn);
                y += 30;

                var nineTitle = MakeLabel("9-SLICE EXPAND (3x3 frame)", 6, y, 184, 18);
                nineTitle.Font = new Font(nineTitle.Font.FontFamily, nineTitle.Font.Size, FontStyle.Bold);
                _tileDock.Controls.Add(nineTitle);
                y += 22;

                _tileDock.Controls.Add(MakeLabel("Size W  H:", 6, y + 4, 62, 20));
                _nineWNum = new NumericUpDown
                {
                    Location = new Point(70, y), Width = 52, Height = 20,
                    Minimum = 1, Maximum = 32, Value = 6
                };
                _tileDock.Controls.Add(_nineWNum);
                _nineHNum = new NumericUpDown
                {
                    Location = new Point(126, y), Width = 52, Height = 20,
                    Minimum = 1, Maximum = 32, Value = 6
                };
                _tileDock.Controls.Add(_nineHNum);
                y += 26;

                _nineHollowChk = new CheckBox { Location = new Point(6, y), Width = 184, Height = 20, Text = "Hollow (skip center)" };
                _tileDock.Controls.Add(_nineHollowChk);
                y += 24;

                _nineSendBtn = new Button { Location = new Point(6, y), Width = 184, Height = 26, Text = "Send expanded to canvas", Enabled = false };
                _nineSendBtn.Click += NineSliceSendBtn_Click;
                _tileDock.Controls.Add(_nineSendBtn);
                y += 30;

                _nineStateLabel = MakeLabel("", 6, y, 184, 30);
                _tileDock.Controls.Add(_nineStateLabel);
                y += 34;
                try
                {
                    var nineTip = new ToolTip { ShowAlways = true };
                    nineTip.SetToolTip(_nineSendBtn, "Repeat the 3x3 frame's edges and center to fill W x H tiles: corners stay, edges repeat on one axis, center fills the inside. Needs the slices to span exactly 3x3 floor cells.");
                }
                catch { }

                _sliceStatusLabel = MakeLabel("No slices.", 6, y, 184, 36);
                _tileDock.Controls.Add(_sliceStatusLabel);
                y += 40;

                var hint = MakeLabel("Click a tile to select its splats. Move / Rotate / Color ops then apply to that tile only.", 6, y, 184, 52);
                _tileDock.Controls.Add(hint);

                HolographicTheme.ApplyToButton(_sliceBtn, ButtonStyle.Accent);
                HolographicTheme.ApplyToButton(_tileSendAllBtn, ButtonStyle.Accent);
                RefreshTileList();
                Log("Tile slice panel ready (right side + top-bar 'Slice tiles' button).");
            }
            catch (Exception ex)
            {
                try { Log("Tile dock failed: " + ex.Message); }
                catch { }
            }
        }

        private void CurrentObjectTransform(out double[] mpos, out double[] robj,
            out float scX, out float scY, out float scZ,
            out float rx, out float ry, out float rz)
        {
            RotVals(out rx, out ry, out rz);
            ScaleVals(out scX, out scY, out scZ);
            double[] r = SplatRenderer.ObjectMatrix(rx, ry, rz);
            robj = r;
            mpos = SplatRenderer.ScaledMatrix(r, scX, scY, scZ);
        }

        private void SliceTopBtn_Click(object sender, EventArgs e)
        {
            try
            {
                if (_tileDock != null)
                {
                    _tileDock.Visible = true;
                    _tileDock.BringToFront();
                }
                Log("TILE SLICES panel is the far-right column (Tiles X/Z, Slice, tile list, Send all tiles to canvas).");
            }
            catch { }
        }

        private void SliceBtn_Click(object sender, EventArgs e)
        {
            if (_model == null)
            {
                MessageBox.Show(this, "No model loaded.", "3D Editor");
                return;
            }
            try
            {
                if (!DoSliceCore())
                    return;
                QueueRender();
            }
            catch (Exception ex)
            {
                Log("Slice failed: " + ex.Message);
            }
        }

        /// <summary>Shared slice body. Returns false when nothing found.</summary>
        private bool DoSliceCore()
        {
            // Tile counts come from the floor grid itself: one slice
            // per floor cell that holds gaussians.
            int tx = SplatRenderer.FloorGridCells;
            int tz = SplatRenderer.FloorGridCells;
            double[] mpos, robj;
            float scX, scY, scZ, rx, ry, rz;
            CurrentObjectTransform(out mpos, out robj, out scX, out scY, out scZ, out rx, out ry, out rz);
            var res = SplatTileSlicer.Slice(_model, tx, tz, mpos,
                double.NegativeInfinity, double.PositiveInfinity,
                rx, ry, rz, scX, scY, scZ, _yaw);
            if (res.Pieces.Count == 0)
            {
                Log("Slice: nothing in range (model empty?).");
                return false;
            }
            _slice = res;
            _activeTileIdx = -1;
            _isolateCache = null;
            _isolateFor = -1;
            RefreshTileList();
            _sliceStatusLabel.Text = string.Format("{0} tiles ({1}x{2}). Click to select.", res.Pieces.Count, tx, tz);
            Log(string.Format("Sliced into {0} tiles on the floor grid ({1}x{2}, extent +-{3:0.00}, facing {4:0} deg, {5} empty skipped).",
                res.Pieces.Count, tx, tz, res.FootMaxX, NormYaw(_yaw), tx * tz - res.Pieces.Count));
            return true;
        }

        /// <summary>
        /// Send/export entry guard: if the view moved since slicing (orbit
        /// or dials), the old bins no longer follow the floor lines you see
        /// and the canvas mosaic would come out scrambled. Re-slice at the
        /// current facing first so what you see is what is sent.
        /// </summary>
        private bool EnsureFreshSlice()
        {
            if (_slice == null || _model == null) return false;
            string why;
            if (!SliceStale(out why)) return true;
            Log("View changed since slice (" + why + ") - re-slicing at facing " +
                NormYaw(_yaw).ToString("0") + " deg so the send matches the viewport...");
            return DoSliceCore();
        }

        private Timer _sliceRefreshTimer;

        /// <summary>
        /// Debounced auto-refresh: Rot/Scale dials fire continuously while
        /// dragged, so wait until they settle before re-slicing.
        /// </summary>
        private void ScheduleSliceRefresh()
        {
            try
            {
                if (_slice == null || _model == null) return;
                if (_sliceRefreshTimer == null)
                {
                    _sliceRefreshTimer = new Timer { Interval = 350 };
                    _sliceRefreshTimer.Tick += delegate
                    {
                        try
                        {
                            _sliceRefreshTimer.Stop();
                            AutoRefreshSlices();
                        }
                        catch { }
                    };
                }
                _sliceRefreshTimer.Stop();
                _sliceRefreshTimer.Start();
            }
            catch { }
        }

        /// <summary>
        /// Re-cut bins when the VIEW changed (orbit/dials) so the list and
        /// highlight always follow the floor lines on screen. Point edits
        /// (move/pan) never come here: assignment follows the points, and
        /// re-binning would erase the edit. The active tile follows its
        /// (TX,TZ); the selection mask is left untouched.
        /// </summary>
        private void AutoRefreshSlices()
        {
            try
            {
                if (_slice == null || _model == null) return;
                if (IsDisposed || !IsHandleCreated) return;
                string why;
                if (!SliceStale(out why)) return;
                int atx = -1, atz = -1;
                if (_activeTileIdx >= 0 && _activeTileIdx < _slice.Pieces.Count)
                {
                    atx = _slice.Pieces[_activeTileIdx].TX;
                    atz = _slice.Pieces[_activeTileIdx].TZ;
                }
                if (!DoSliceCore()) return;
                _activeTileIdx = -1;
                for (int i = 0; i < _slice.Pieces.Count; i++)
                {
                    if (_slice.Pieces[i].TX == atx && _slice.Pieces[i].TZ == atz)
                    {
                        _activeTileIdx = i;
                        break;
                    }
                }
                _suppressTileSelect = true;
                try
                {
                    if (_tileList != null) _tileList.SelectedIndex = _activeTileIdx;
                }
                catch { }
                finally { _suppressTileSelect = false; }
                QueueRender();
            }
            catch { }
        }

        private void RefreshTileList()
        {
            try
            {
                if (_tileList == null) return;
                _tileList.BeginUpdate();
                _tileList.Items.Clear();
                if (_slice != null)
                {
                    foreach (var p in _slice.Pieces)
                        _tileList.Items.Add(p.Label);
                }
                _tileList.EndUpdate();
                if (_sliceStatusLabel != null && _slice == null)
                    _sliceStatusLabel.Text = "No slices.";
                UpdateNineSliceState();
            }
            catch { }
        }

        /// <summary>
        /// 9-slice eligibility: the non-empty slices must span exactly 3x3
        /// floor cells (empties inside allowed). The frame's min corner is
        /// the role origin; roles are relative positions 0..2 per axis.
        /// </summary>
        private bool NineSliceFrame(out int minTX, out int minTZ, out string why)
        {
            minTX = minTZ = 0;
            why = string.Empty;
            if (_slice == null || _slice.Pieces.Count == 0)
            {
                why = "Slice first.";
                return false;
            }
            int x0 = int.MaxValue, x1 = int.MinValue, z0 = int.MaxValue, z1 = int.MinValue;
            foreach (var p in _slice.Pieces)
            {
                if (p.TX < x0) x0 = p.TX;
                if (p.TX > x1) x1 = p.TX;
                if (p.TZ < z0) z0 = p.TZ;
                if (p.TZ > z1) z1 = p.TZ;
            }
            minTX = x0; minTZ = z0;
            int w = x1 - x0 + 1, h = z1 - z0 + 1;
            if (w != 3 || h != 3)
            {
                why = string.Format("Needs a 3x3 frame (now {0}x{1}).", w, h);
                return false;
            }
            return true;
        }

        private void UpdateNineSliceState()
        {
            try
            {
                if (_nineSendBtn == null) return;
                int fx, fz;
                string why;
                bool ok = NineSliceFrame(out fx, out fz, out why);
                _nineSendBtn.Enabled = ok;
                if (_nineStateLabel != null)
                    _nineStateLabel.Text = ok ? "3x3 frame ready." : why;
            }
            catch { }
        }

        private static int NineSliceRole(int dst, int target)
        {
            if (target <= 1) return 1; // degenerate: middle band
            if (dst <= 0) return 0;
            if (dst >= target - 1) return 2;
            return 1;
        }

        private void NineSliceSendBtn_Click(object sender, EventArgs e)
        {
            if (_model == null)
            {
                MessageBox.Show(this, "No model loaded.", "3D Editor");
                return;
            }
            if (_slice == null || _slice.Pieces.Count == 0)
            {
                MessageBox.Show(this, "Slice first.", "3D Editor");
                return;
            }
            try
            {
                if (!EnsureFreshSlice()) return;
                int fx, fz;
                string why;
                if (!NineSliceFrame(out fx, out fz, out why))
                {
                    MessageBox.Show(this, "9-slice expand needs the slices to span exactly 3x3 floor cells.\r\n" + why, "3D Editor");
                    return;
                }
                int wt = _nineWNum != null ? (int)_nineWNum.Value : 6;
                int ht = _nineHNum != null ? (int)_nineHNum.Value : 6;
                if (wt < 1) wt = 1;
                if (ht < 1) ht = 1;
                if (wt > 32) wt = 32;
                if (ht > 32) ht = 32;
                // Role lookup by relative frame coords.
                var byRole = new Dictionary<long, TilePiece>();
                foreach (var p in _slice.Pieces)
                    byRole[((long)(p.TX - fx) << 32) | (uint)(p.TZ - fz)] = p;
                int size = ExportSize();
                float yaw = _yaw;
                int baseZ = _tileBaseZNum != null ? (int)_tileBaseZNum.Value : 0;
                // Origin-relative coords in the same frame as normal sends
                // (model origin bin - slice grid center), so the canvas
                // centers the expanded batch the same way.
                int orgTX = _slice.TilesX / 2, orgTZ = _slice.TilesZ / 2;
                try
                {
                    double ext = _slice.FootMaxX;
                    if (ext > 0 && _slice.TileSizeX > 1e-9 && _slice.TileSizeZ > 1e-9)
                    {
                        int ox = (int)Math.Floor(ext / _slice.TileSizeX);
                        int oz = (int)Math.Floor(ext / _slice.TileSizeZ);
                        if (ox >= 0 && ox < _slice.TilesX) orgTX = ox;
                        if (oz >= 0 && oz < _slice.TilesZ) orgTZ = oz;
                    }
                }
                catch { }
                string baseName = Path.GetFileNameWithoutExtension(
                    !string.IsNullOrEmpty(_plyPath) ? _plyPath : "model");
                var roleBmp = new Dictionary<long, Bitmap>();
                var sprites = new List<TileSprite>();
                int holes = 0;
                Cursor.Current = Cursors.WaitCursor;
                try
                {
                    for (int dy = 0; dy < ht; dy++)
                    {
                        for (int dx = 0; dx < wt; dx++)
                        {
                            int roleX = NineSliceRole(dx, wt);
                            int roleZ = NineSliceRole(dy, ht);
                            if (_nineHollowChk != null && _nineHollowChk.Checked &&
                                roleX == 1 && roleZ == 1)
                            {
                                holes++;
                                continue;
                            }
                            long key = ((long)roleX << 32) | (uint)roleZ;
                            TilePiece piece;
                            if (!byRole.TryGetValue(key, out piece))
                            {
                                holes++;
                                continue;
                            }
                            long rkey = ((long)piece.TX << 32) | (uint)piece.TZ;
                            Bitmap src;
                            if (!roleBmp.TryGetValue(rkey, out src))
                            {
                                src = RenderPieceExport(piece, yaw, size);
                                if (src == null)
                                {
                                    holes++;
                                    continue;
                                }
                                roleBmp[rkey] = src;
                            }
                            Bitmap copy;
                            try { copy = new Bitmap(src); }
                            catch { holes++; continue; }
                            // Target cell (dx,dy) stamped with its role's art,
                            // placed in the slice grid frame so the normal
                            // origin convention (and canvas centering) holds.
                            sprites.Add(new TileSprite
                            {
                                Image = copy, // each stamp owns its bitmap
                                Name = string.Format("3d_{0}_9s{1}_{2}_{3}",
                                    baseName, dx, dy,
                                    SplatRenderer.DirectionNames[_direction]),
                                GridX = (fx + dx) - orgTX,
                                GridY = (fz + dy) - orgTZ,
                                Z = baseZ
                            });
                        }
                    }
                }
                finally
                {
                    Cursor.Current = Cursors.Default;
                    foreach (var kv in roleBmp)
                    {
                        try { kv.Value.Dispose(); }
                        catch { }
                    }
                }
                if (sprites.Count == 0)
                {
                    MessageBox.Show(this, "All target cells were empty roles.", "3D Editor");
                    return;
                }
                if (TilesReady != null)
                    TilesReady(sprites);
                else if (SpriteReady != null)
                {
                    foreach (var s in sprites)
                        SpriteReady(s.Image, s.Name);
                }
                Log(string.Format("Sent 9-slice {0}x{1} ({2} sprites{3}, facing {4}).",
                    wt, ht, sprites.Count,
                    holes > 0 ? ", " + holes + " cells skipped (empty/hollow)" : string.Empty,
                    SplatRenderer.DirectionNames[_direction]));
            }
            catch (Exception ex)
            {
                Cursor.Current = Cursors.Default;
                MessageBox.Show(this, "9-slice send failed:\r\n" + ex.Message, "3D Editor");
            }
        }

        private void ClearSlices(string msg)
        {
            try
            {
                _slice = null;
                _activeTileIdx = -1;
                _isolateCache = null;
                _isolateFor = -1;
                RefreshTileList();
                if (!string.IsNullOrEmpty(msg)) Log(msg);
                QueueRender();
            }
            catch { }
        }

        private bool _suppressTileSelect;

        /// <summary>
        /// Re-glue bins to the floor after anything moved points or counts
        /// (delete, clone, move, pan, thin, undo, cage...): every splat is
        /// re-binned into the same stored grid. The active tile follows its
        /// (TX,TZ); the selection mask is left alone (callers remap it).
        /// Logs only when the non-empty count actually changed.
        /// </summary>
        private void RefreshSlicesKeepActive(string why)
        {
            if (_slice == null || _model == null) return;
            try
            {
                int before = _slice.Pieces.Count;
                int atx = -1, atz = -1;
                if (_activeTileIdx >= 0 && _activeTileIdx < _slice.Pieces.Count)
                {
                    atx = _slice.Pieces[_activeTileIdx].TX;
                    atz = _slice.Pieces[_activeTileIdx].TZ;
                }
                double[] mpos, robj;
                float scX, scY, scZ, rx, ry, rz;
                CurrentObjectTransform(out mpos, out robj, out scX, out scY, out scZ, out rx, out ry, out rz);
                SplatTileSlicer.Reclassify(_model, _slice, mpos, rx, ry, rz, scX, scY, scZ);
                _isolateCache = null;
                _isolateFor = -1;
                _activeTileIdx = -1;
                for (int i = 0; i < _slice.Pieces.Count; i++)
                {
                    if (_slice.Pieces[i].TX == atx && _slice.Pieces[i].TZ == atz)
                    {
                        _activeTileIdx = i;
                        break;
                    }
                }
                _suppressTileSelect = true;
                try
                {
                    RefreshTileList();
                    if (_tileList != null)
                    {
                        try { _tileList.SelectedIndex = _activeTileIdx; }
                        catch { }
                    }
                    if (_sliceStatusLabel != null)
                        _sliceStatusLabel.Text = string.Format("{0} tiles ({1}x{2}). Click to select.",
                            _slice.Pieces.Count, _slice.TilesX, _slice.TilesZ);
                }
                finally { _suppressTileSelect = false; }
                if (_slice.Pieces.Count != before)
                    Log(string.Format("Tiles updated after {0}: {1} -> {2} non-empty.", why, before, _slice.Pieces.Count));
            }
            catch (Exception ex) { Log("Tile refresh failed: " + ex.Message); }
        }

        private void TileList_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                if (_suppressTileSelect) return;
                if (_slice == null || _model == null) return;
                int idx = _tileList != null ? _tileList.SelectedIndex : -1;
                if (idx < 0 || idx >= _slice.Pieces.Count)
                {
                    _activeTileIdx = -1;
                    _isolateCache = null;
                    _isolateFor = -1;
                    QueueRender();
                    return;
                }
                _activeTileIdx = idx;
                var piece = _slice.Pieces[idx];
                // Guard against stale indices after edits.
                if (_sel == null || _sel.Length != _model.Count)
                    _sel = new bool[_model.Count];
                else
                    Array.Clear(_sel, 0, _sel.Length);
                foreach (int i in piece.Indices)
                {
                    if (i >= 0 && i < _sel.Length) _sel[i] = true;
                }
                _isolateCache = null;
                _isolateFor = -1;
                SelectionChanged();
                Log(string.Format("Selected {0} ({1} splats). Edit ops now apply to this tile only.",
                    piece.Label, piece.Count));
            }
            catch (Exception ex) { Log("Tile select failed: " + ex.Message); }
        }

        /// <summary>
        /// Model for viewport rendering: isolated tile (baked, in place) or
        /// the full model. Returns true when the caller owns the bitmap
        /// source and must NOT dispose the shared _model.
        /// </summary>
        private PlySplatModel ViewportModel()
        {
            try
            {
                if (_slice != null && _tileIsolateChk != null && _tileIsolateChk.Checked &&
                    _activeTileIdx >= 0 && _activeTileIdx < _slice.Pieces.Count && _model != null)
                {
                    if (_isolateCache == null || _isolateFor != _activeTileIdx)
                    {
                        double[] mpos, robj;
                        float scX, scY, scZ, rx, ry, rz;
                        CurrentObjectTransform(out mpos, out robj, out scX, out scY, out scZ, out rx, out ry, out rz);
                        _isolateCache = SplatTileSlicer.BakePiece(_model, _slice.Pieces[_activeTileIdx],
                            mpos, robj, scX, scY, scZ, false);
                        _isolateFor = _activeTileIdx;
                    }
                    return _isolateCache;
                }
            }
            catch { }
            return _model;
        }

        private Bitmap RenderPieceExport(TilePiece piece, float yawDeg, int size)
        {
            double[] mpos, robj;
            float scX, scY, scZ, rx, ry, rz;
            CurrentObjectTransform(out mpos, out robj, out scX, out scY, out scZ, out rx, out ry, out rz);
            var baked = SplatTileSlicer.BakePiece(_model, piece, mpos, robj, scX, scY, scZ, true);
            if (baked.Count == 0) return null;
            float pitch = pitchNumeric != null ? (float)pitchNumeric.Value : 26.57f;
            // Calibrated scale: one floor-grid cell renders exactly one
            // canvas tile wide (44px diamond). The viewport zoom is for
            // looking only and must not leak into export sizes.
            float zoom = zoomTrackBar != null ? zoomTrackBar.Value / 100f : 1f;
            try
            {
                double cell = _slice != null ? _slice.TileSizeX : 0;
                if (cell > 1e-9 && size > 0)
                {
                    double wantFit = 22.0 / cell;
                    double z = wantFit / (size * 0.42);
                    if (z < 0.02) z = 0.02;
                    if (z > 8) z = 8;
                    zoom = (float)z;
                }
            }
            catch { }
            float dots = DotScale();
            // Identity transform: already baked. UO projection always for canvas.
            Bitmap layer = null;
            try { layer = GlSplatRenderer.RenderLayer(baked, yawDeg, pitch, size, size, zoom, dots, 0, true, 0f, 0f, 0f, 1f, 1f, 1f); }
            catch { layer = null; }
            Bitmap bmp = SplatRenderer.Render(baked, yawDeg, pitch, size, size, zoom, dots,
                true, _bgColor, 0, true, Color.Empty, true, 0f, layer != null, 0f, 0f, 0f, 1f, 1f, 1f);
            if (layer != null)
            {
                try
                {
                    using (var g = Graphics.FromImage(bmp))
                        g.DrawImage(layer, 0, 0, size, size);
                }
                finally { layer.Dispose(); }
            }
            // Tighten to content, but keep the tile-center pixel at the
            // image center: the canvas centers each sprite on its tile, so
            // a plain bbox crop would recenter shifted content and break
            // placement (and in-tile moves). SymmetricAboutCenter keeps
            // offsets exact while dropping the empty margins.
            // Vertical stays tight to content: canvas placement is
            // bottom-anchored, so height never affects alignment.
            bool crop = false;
            try { crop = cropCheckBox != null && cropCheckBox.Checked && transparentCheckBox != null && transparentCheckBox.Checked; }
            catch { }
            if (crop)
            {
                Bitmap tight = CropTileCentered(bmp, 4);
                if (tight != null && tight != bmp)
                {
                    bmp.Dispose();
                    bmp = tight;
                }
            }
            return bmp;
        }

        /// <summary>
        /// Crop transparent margins around content, expanding the X range
        /// symmetrically about the image center (the tile center after a
        /// recentered render) so canvas centering stays pixel-exact.
        /// </summary>
        private static Bitmap CropTileCentered(Bitmap src, int pad)
        {
            if (src == null) return null;
            var rect = new Rectangle(0, 0, src.Width, src.Height);
            var data = src.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            int minX = src.Width, maxX = -1, minY = src.Height, maxY = -1;
            try
            {
                int bytes = Math.Abs(data.Stride) * src.Height;
                var buf = new byte[bytes];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buf, 0, bytes);
                for (int y = 0; y < src.Height; y++)
                {
                    int row = y * data.Stride;
                    for (int x = 0; x < src.Width; x++)
                    {
                        if (buf[row + x * 4 + 3] > 8)
                        {
                            if (x < minX) minX = x;
                            if (x > maxX) maxX = x;
                            if (y < minY) minY = y;
                            if (y > maxY) maxY = y;
                        }
                    }
                }
            }
            finally { src.UnlockBits(data); }
            if (maxX < minX) return new Bitmap(src);
            int cx = src.Width / 2;
            int halfW = Math.Max(cx - minX, maxX - cx) + pad;
            int x0 = Math.Max(0, cx - halfW);
            int x1 = Math.Min(src.Width - 1, cx + halfW);
            int y0 = Math.Max(0, minY - pad);
            int y1 = Math.Min(src.Height - 1, maxY + pad);
            if (x1 <= x0 || y1 <= y0) return new Bitmap(src);
            var out_ = new Bitmap(x1 - x0 + 1, y1 - y0 + 1,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(out_))
                g.DrawImage(src, new Rectangle(0, 0, out_.Width, out_.Height),
                    new Rectangle(x0, y0, out_.Width, out_.Height), GraphicsUnit.Pixel);
            return out_;
        }

        private void TileSendAllBtn_Click(object sender, EventArgs e)
        {
            if (_model == null)
            {
                MessageBox.Show(this, "No model loaded.", "3D Editor");
                return;
            }
            if (_slice == null || _slice.Pieces.Count == 0)
            {
                MessageBox.Show(this, "Slice first (Tiles X/Z, then Slice).", "3D Editor");
                return;
            }
            try
            {
                if (!EnsureFreshSlice()) return;
                int size = ExportSize();
                float yaw = _yaw;
                // Origin-relative grid coords; the canvas centers the batch
                // on its own grid, so no absolute base is needed here.
                int baseX = 0;
                int baseY = 0;
                int baseZ = _tileBaseZNum != null ? (int)_tileBaseZNum.Value : 0;
                // Origin-relative: the bin holding the model origin lands on
                // the base tile, so the mosaic keeps the editor's origin.
                int orgTX = _slice.TilesX / 2, orgTZ = _slice.TilesZ / 2;
                try
                {
                    double ext = _slice.FootMaxX;
                    if (ext > 0 && _slice.TileSizeX > 1e-9 && _slice.TileSizeZ > 1e-9)
                    {
                        int ox = (int)Math.Floor(ext / _slice.TileSizeX);
                        int oz = (int)Math.Floor(ext / _slice.TileSizeZ);
                        if (ox < 0) ox = 0; else if (ox >= _slice.TilesX) ox = _slice.TilesX - 1;
                        if (oz < 0) oz = 0; else if (oz >= _slice.TilesZ) oz = _slice.TilesZ - 1;
                        orgTX = ox; orgTZ = oz;
                    }
                }
                catch { }
                string baseName = Path.GetFileNameWithoutExtension(
                    !string.IsNullOrEmpty(_plyPath) ? _plyPath : "model");
                var sprites = new List<TileSprite>();
                Cursor.Current = Cursors.WaitCursor;
                try
                {
                    foreach (var piece in _slice.Pieces)
                    {
                        Bitmap bmp = RenderPieceExport(piece, yaw, size);
                        if (bmp == null) continue;
                        sprites.Add(new TileSprite
                        {
                            Image = bmp, // ownership to canvas
                            Name = string.Format("3d_{0}_t{1}_{2}_{3}",
                                baseName, piece.TX, piece.TZ,
                                SplatRenderer.DirectionNames[_direction]),
                            GridX = baseX + (piece.TX - orgTX),
                            GridY = baseY + (piece.TZ - orgTZ),
                            Z = baseZ
                        });
                    }
                }
                finally { Cursor.Current = Cursors.Default; }
                if (sprites.Count == 0)
                {
                    MessageBox.Show(this, "All tiles rendered empty.", "3D Editor");
                    return;
                }
                if (TilesReady != null)
                    TilesReady(sprites);
                else if (SpriteReady != null)
                {
                    // Back-compat: single-event subscribers get each tile.
                    foreach (var s in sprites)
                        SpriteReady(s.Image, s.Name);
                }
                Log(string.Format("Sent {0} tiles to canvas, origin on ({1},{2}) Z{3} (facing {4}).",
                    sprites.Count, baseX, baseY, baseZ,
                    SplatRenderer.DirectionNames[_direction]));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Send tiles failed:\r\n" + ex.Message, "3D Editor");
            }
        }

        private void TileExportAllBtn_Click(object sender, EventArgs e)
        {
            if (_model == null)
            {
                MessageBox.Show(this, "No model loaded.", "3D Editor");
                return;
            }
            if (_slice == null || _slice.Pieces.Count == 0)
            {
                MessageBox.Show(this, "Slice first (Tiles X/Z, then Slice).", "3D Editor");
                return;
            }
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Folder for per-tile sprites (current view)";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    if (!EnsureFreshSlice()) return;
                    int size = ExportSize();
                    float yaw = _yaw;
                    string baseName = Path.GetFileNameWithoutExtension(
                        !string.IsNullOrEmpty(_plyPath) ? _plyPath : "model");
                    var saved = new List<string>();
                    Cursor.Current = Cursors.WaitCursor;
                    try
                    {
                        foreach (var piece in _slice.Pieces)
                        {
                            using (var bmp = RenderPieceExport(piece, yaw, size))
                            {
                                if (bmp == null) continue;
                                string path = Path.Combine(dlg.SelectedPath, string.Format("{0}_t{1}_{2}_{3}.png",
                                    baseName, piece.TX, piece.TZ,
                                    SplatRenderer.DirectionNames[_direction]));
                                bmp.Save(path, ImageFormat.Png);
                                saved.Add(path);
                            }
                        }
                    }
                    finally { Cursor.Current = Cursors.Default; }
                    Log("Exported " + saved.Count + " tile sprites to " + dlg.SelectedPath + ".");
                    SetStatus("Exported " + saved.Count + " tiles.", Color.Green);
                }
                catch (Exception ex)
                {
                    Cursor.Current = Cursors.Default;
                    MessageBox.Show(this, "Export failed:\r\n" + ex.Message, "3D Editor");
                }
            }
        }

        /// <summary>
        /// True when the floor view moved since Slice: orbiting changes the
        /// yaw the bins are cut in, and the Rot/Scale dials change the baked
        /// positions the bins are cut from. Either one shifts bins off the
        /// floor lines on screen.
        /// </summary>
        private bool SliceStale(out string why)
        {
            why = string.Empty;
            try
            {
                if (_slice == null) return false;
                float rx, ry, rz;
                RotVals(out rx, out ry, out rz);
                float sx, sy, sz;
                ScaleVals(out sx, out sy, out sz);
                const float eps = 1e-6f;
                if (Math.Abs(rx - _slice.SliceRX) > eps ||
                    Math.Abs(ry - _slice.SliceRY) > eps ||
                    Math.Abs(rz - _slice.SliceRZ) > eps ||
                    Math.Abs(sx - _slice.SliceSX) > eps ||
                    Math.Abs(sy - _slice.SliceSY) > eps ||
                    Math.Abs(sz - _slice.SliceSZ) > eps)
                {
                    why = "Rot/Scale changed since Slice";
                    return true;
                }
                float dy = Math.Abs(NormYaw(_yaw) - NormYaw(_slice.SliceYaw));
                if (dy > 180f) dy = 360f - dy;
                if (dy > 0.5f)
                {
                    why = "facing changed since Slice";
                    return true;
                }
            }
            catch { }
            return false;
        }

        private bool SliceStale()
        {
            string why;
            return SliceStale(out why);
        }

        private void DrawTileGridOverlay(Graphics g, int w, int h)
        {
            try
            {
                if (_slice == null || g == null) return;
                if (_model == null) return;
                float zoom = zoomTrackBar != null ? zoomTrackBar.Value / 100f : 1f;
                double fit = Math.Min(w, h) * 0.42 * zoom;
                double cx = w * 0.5, cy = h * 0.52;
                float pitch = pitchNumeric != null ? (float)pitchNumeric.Value : 26.57f;
                bool uo = UoMode();
                // Model-frame footprint corners through the CURRENT object
                // transform: identical math to the splat path, so the grid
                // sits exactly under the rendered model. Stale slices (dials
                // moved after slicing) draw orange as a re-slice hint.
                double[] mposNow, robjNow;
                float scX, scY, scZ, rx, ry, rz;
                CurrentObjectTransform(out mposNow, out robjNow, out scX, out scY, out scZ, out rx, out ry, out rz);
                bool stale = SliceStale();
                double x0 = _slice.ModMinX, x1 = _slice.ModMaxX;
                double z0 = _slice.ModMinZ, z1 = _slice.ModMaxZ;
                double gyM = _slice.ModMinY;
                Color gridCol = stale ? Color.FromArgb(230, 255, 140, 0) : Color.FromArgb(220, 255, 220, 0);
                using (var pen = new Pen(gridCol, 1.5f))
                {
                    for (int ix = 0; ix <= _slice.TilesX; ix++)
                    {
                        double xm = x0 + (x1 - x0) * ix / Math.Max(1, _slice.TilesX);
                        double ax, ay, az, bx, by, bz;
                        SplatRenderer.TransformPoint(mposNow, xm, gyM, z0, out ax, out ay, out az);
                        SplatRenderer.TransformPoint(mposNow, xm, gyM, z1, out bx, out by, out bz);
                        float sax, say, sbx, sby;
                        SplatRenderer.ProjectPoint((float)ax, (float)ay, (float)az, _yaw, pitch, uo, fit, cx, cy, out sax, out say);
                        SplatRenderer.ProjectPoint((float)bx, (float)by, (float)bz, _yaw, pitch, uo, fit, cx, cy, out sbx, out sby);
                        g.DrawLine(pen, sax, say, sbx, sby);
                    }
                    for (int iz = 0; iz <= _slice.TilesZ; iz++)
                    {
                        double zm = z0 + (z1 - z0) * iz / Math.Max(1, _slice.TilesZ);
                        double ax, ay, az, bx, by, bz;
                        SplatRenderer.TransformPoint(mposNow, x0, gyM, zm, out ax, out ay, out az);
                        SplatRenderer.TransformPoint(mposNow, x1, gyM, zm, out bx, out by, out bz);
                        float sax, say, sbx, sby;
                        SplatRenderer.ProjectPoint((float)ax, (float)ay, (float)az, _yaw, pitch, uo, fit, cx, cy, out sax, out say);
                        SplatRenderer.ProjectPoint((float)bx, (float)by, (float)bz, _yaw, pitch, uo, fit, cx, cy, out sbx, out sby);
                        g.DrawLine(pen, sax, say, sbx, sby);
                    }
                }
            }
            catch { }
        }
    }
}
