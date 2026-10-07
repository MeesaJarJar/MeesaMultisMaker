using MeesaMultisMaker.ThreeD;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MeesaMultisMaker
{
    /// <summary>
    /// Point-cloud editor extension: selection system (modes, generators),
    /// general undo, color/geometry/UO-hue operators, SAM/mask select and
    /// spintable export. Additive; the generation pipeline is untouched.
    /// </summary>
    public partial class ThreeDEditorForm
    {
        #region Selection state + general undo

        private bool[] _sel;
        private int _selCount;
        private int _editMode; // 0 orbit, 1 lasso-select, 2 brush, 3 wand
        private Button[] _modeBtns;
        private Label _selCountLabel;
        private CheckBox _subtractChk;

        private class EditUndo
        {
            public bool IsAdd;
            /// <summary>
            /// True = rows were removed (undo re-inserts Before at Idx).
            /// False = in-place edit (undo overwrites Points[Idx] with
            /// Before). Without this, undoing a move/color/pan INSERTS
            /// copies and visibly clones the model.
            /// </summary>
            public bool IsDelete;
            public int[] Idx;
            public SplatPoint[] Before;
            public bool IsCage; // one cage-corner drag; collapsed into a single step on Apply
        }
        private readonly List<EditUndo> _editUndo = new List<EditUndo>();

        private bool EnsureSel()
        {
            if (_model == null) return false;
            if (_sel == null || _sel.Length != _model.Count)
                _sel = new bool[_model.Count];
            return true;
        }

        private void ResetSelection()
        {
            _editUndo.Clear();
            if (_model != null) _sel = new bool[_model.Count];
            else _sel = null;
            _selCount = 0;
            _lastClickIdx = -1;
            SplatRenderer.HighlightMask = null;
            undoLassoButton.Enabled = false;
            RefreshSelCount();
        }

        private void RefreshSelCount()
        {
            _selCount = SplatSelect.Count(_sel);
            if (_selCountLabel != null)
            {
                if (InvokeRequired) BeginInvoke(new Action(RefreshSelCount));
                else _selCountLabel.Text = _selCount + " selected";
            }
        }

        private void RefreshModelLabel()
        {
            try
            {
                plyInfoLabel.Text = string.Format("{0} splats - {1}",
                    _model.Count, Path.GetFileName(_plyPath));
            }
            catch { }
        }

        /// <summary>Null = whole model, else current selection (may be empty).</summary>
        private bool[] UseSel()
        {
            return _selCount > 0 ? _sel : null;
        }

        private string Scope()
        {
            return _selCount > 0 ? "selection" : "model";
        }

        private void SelectionChanged()
        {
            RefreshSelCount();
            SplatRenderer.HighlightMask = _selCount > 0 ? _sel : null;
            QueueRender();
        }

        private SplatPoint[] Snapshot(List<int> idx)
        {
            var pts = _model.Points;
            var snap = new SplatPoint[idx.Count];
            for (int k = 0; k < idx.Count; k++) snap[k] = pts[idx[k]];
            return snap;
        }

        private void PushMutateUndo(List<int> idx)
        {
            _editUndo.Add(new EditUndo { IsAdd = false, IsDelete = false, Idx = idx.ToArray(), Before = Snapshot(idx) });
            TrimUndo();
            undoLassoButton.Enabled = true;
        }

        private void PushDeleteUndo(List<int> idx)
        {
            _editUndo.Add(new EditUndo { IsAdd = false, IsDelete = true, Idx = idx.ToArray(), Before = Snapshot(idx) });
            TrimUndo();
            undoLassoButton.Enabled = true;
        }

        private void PushAddUndo(List<int> added)
        {
            _editUndo.Add(new EditUndo { IsAdd = true, Idx = added.ToArray(), Before = new SplatPoint[0] });
            TrimUndo();
            undoLassoButton.Enabled = true;
        }

        private void TrimUndo()
        {
            while (_editUndo.Count > 10) _editUndo.RemoveAt(0);
        }

        private void MarkUndoAvailable()
        {
            undoLassoButton.Enabled = _editUndo.Count > 0;
        }

        private void UndoEdit()
        {
            try
            {
                if (_editUndo.Count == 0 || _model == null)
                {
                    Log("Nothing to undo.");
                    return;
                }
                var u = _editUndo[_editUndo.Count - 1];
                _editUndo.RemoveAt(_editUndo.Count - 1);
                var pts = _model.Points;
                if (u.IsAdd)
                {
                    var desc = new List<int>(u.Idx);
                    desc.Sort();
                    for (int k = desc.Count - 1; k >= 0; k--)
                        if (desc[k] >= 0 && desc[k] < pts.Count) pts.RemoveAt(desc[k]);
                    ReindexAfterRemoval(desc);
                    Log(string.Format("Undo: removed {0} added splats.", desc.Count));
                }
                else if (u.IsDelete)
                {
                    for (int k = 0; k < u.Idx.Length; k++)
                    {
                        int at = u.Idx[k];
                        if (at < 0) at = 0;
                        if (at > pts.Count) at = pts.Count;
                        pts.Insert(at, u.Before[k]);
                    }
                    // Selection may reference shifted rows; rebuild conservatively.
                    if (_sel != null && _sel.Length != pts.Count)
                    {
                        var ns = new bool[pts.Count];
                        int m = Math.Min(_sel.Length, ns.Length);
                        for (int k = 0; k < m; k++) ns[k] = _sel[k];
                        _sel = ns;
                    }
                    Log(string.Format("Undo: restored {0} splats.", u.Before.Length));
                }
                else
                {
                    int n = 0;
                    for (int k = 0; k < u.Idx.Length && k < u.Before.Length; k++)
                    {
                        int at = u.Idx[k];
                        if (at < 0 || at >= pts.Count) continue;
                        pts[at] = u.Before[k];
                        n++;
                    }
                    Log(string.Format("Undo: reverted {0} splats.", n));
                }
                MarkUndoAvailable();
                RefreshModelLabel();
                RefreshSlicesKeepActive("undo");
                SelectionChanged();
            }
            catch (Exception ex) { Log("Undo failed: " + ex.Message); }
        }

        /// <summary>Rebuild selection after rows were removed (ascending).</summary>
        private void ReindexAfterRemoval(List<int> removedAsc)
        {
            if (_sel == null || _model == null) return;
            var gone = new HashSet<int>(removedAsc);
            var ns = new List<bool>(_model.Count);
            for (int i = 0; i < _model.Count + removedAsc.Count; i++) { }
            // Walk old rows -> new rows.
            var old = _sel;
            var rebuilt = new List<bool>();
            int r = 0;
            for (int i = 0; i < old.Length; i++)
            {
                if (r < removedAsc.Count && removedAsc[r] == i) { r++; continue; }
                rebuilt.Add(old[i]);
            }
            while (rebuilt.Count < _model.Count) rebuilt.Add(false);
            while (rebuilt.Count > _model.Count) rebuilt.RemoveAt(rebuilt.Count - 1);
            _sel = rebuilt.ToArray();
            GC.KeepAlive(gone);
            GC.KeepAlive(ns);
        }

        private void AfterEdit(string msg)
        {
            RefreshModelLabel();
            // Bins follow moved points (silent unless the tile set changed).
            RefreshSlicesKeepActive("edit");
            SelectionChanged();
            if (!string.IsNullOrEmpty(msg)) Log(msg);
        }

        #endregion

        #region Edit panel UI

        private Button[] _modeBtnsArr;
        private NumericUpDown _brushRNum, _wandTolNum, _slabMinNum, _slabMaxNum;
        private ComboBox _slabAxisCombo, _wandTargetCombo, _relightCombo;
        private NumericUpDown _hueDegNum, _satNum, _lightNum, _tintStrNum, _posterNum, _aoStrNum;
        private NumericUpDown _retexStrNum;
        private CheckBox _retexFrontChk;
        private NumericUpDown _moveXNum, _moveYNum, _moveZNum, _explodeNum, _hueIdNum;
        private NumericUpDown _boxX0, _boxX1, _boxY0, _boxY1, _boxZ0, _boxZ1;
        private CheckBox _huePartialChk;
        private Label _hueStatusLabel, _samStatusLabel;
        private Color _tintColor = Color.OrangeRed;
        private int _lastClickIdx = -1;
        private Point _lastClickPt;

        private void BuildEditPanel()
        {
            Panel left = null;
            try
            {
                foreach (Control c in _split.Panel1.Controls)
                {
                    if (c is Panel) { left = (Panel)c; break; }
                }
            }
            catch { }
            if (left == null) return;

            int y = 8;
            foreach (Control c in left.Controls)
            {
                if (c.Bottom > y) y = c.Bottom;
            }
            y += 10;

            var title = MakeLabel("EDIT SPLATS (point cloud)", 10, y, 320, 18);
            title.Font = new Font(title.Font.FontFamily, title.Font.Size, FontStyle.Bold);
            left.Controls.Add(title);
            y += 22;

            // Modes.
            string[] modes = new string[] { "Orbit", "Lasso+", "Brush", "Wand" };
            _modeBtns = new Button[4];
            _modeBtnsArr = _modeBtns;
            for (int i = 0; i < 4; i++)
            {
                int m = i;
                var b = new Button { Location = new Point(10 + i * 80, y), Width = 74, Height = 26, Text = modes[i] };
                b.Click += delegate { SetEditMode(m); };
                left.Controls.Add(b);
                _modeBtns[i] = b;
            }
            var modeTip = new ToolTip { ShowAlways = true };
            try
            {
                modeTip.SetToolTip(_modeBtns[0], "Plain orbit (default). Ctrl+drag still lasso-deletes.");
                modeTip.SetToolTip(_modeBtns[1], "Drag a loop to ADD to selection. Alt+drag (or Subtract) removes.");
                modeTip.SetToolTip(_modeBtns[2], "Paint selection with a round brush. Front surface only.");
                modeTip.SetToolTip(_modeBtns[3], "Click a splat: flood-select similar ones (color or normal).");
            }
            catch { }
            y += 32;

            y = AddRow2(left, y, "Brush R:", 4, 200, 1, 40, 0, "Wand tol:", 0, 100, 1, 30, 0,
                out _brushRNum, out _wandTolNum);
            _wandTargetCombo = new ComboBox { Location = new Point(170, y), Width = 150, Height = 21, DropDownStyle = ComboBoxStyle.DropDownList };
            _wandTargetCombo.Items.AddRange(new object[] { "Wand: Color", "Wand: Normal" });
            _wandTargetCombo.SelectedIndex = 0;
            left.Controls.Add(_wandTargetCombo);
            left.Controls.Add(MakeLabel("Subtract:", 10, y + 26, 70, 20));
            _subtractChk = new CheckBox { Location = new Point(80, y + 24), Width = 240, Height = 20, Text = "subtract (or hold Alt)" };
            left.Controls.Add(_subtractChk);
            y += 50;

            // Select buttons + count.
            var bAll = MkBtn(left, "All", 10, y, 62);
            bAll.Click += delegate { if (EnsureSel()) { SplatSelect.All(_sel, true); SelectionChanged(); } };
            var bNone = MkBtn(left, "None", 76, y, 62);
            bNone.Click += delegate { if (EnsureSel()) { SplatSelect.All(_sel, false); SelectionChanged(); } };
            var bInv = MkBtn(left, "Invert", 142, y, 62);
            bInv.Click += delegate { if (EnsureSel()) { SplatSelect.Invert(_sel); SelectionChanged(); } };
            _selCountLabel = MakeLabel("0 selected", 210, y + 4, 110, 20);
            left.Controls.Add(_selCountLabel);
            y += 32;

            y = AddGroup(left, y, "Select by…");
            left.Controls.Add(MakeLabel("Slab:", 10, y, 40, 20));
            _slabAxisCombo = new ComboBox { Location = new Point(52, y), Width = 48, Height = 21, DropDownStyle = ComboBoxStyle.DropDownList };
            _slabAxisCombo.Items.AddRange(new object[] { "X", "Y", "Z" });
            _slabAxisCombo.SelectedIndex = 1;
            left.Controls.Add(_slabAxisCombo);
            _slabMinNum = MkNum(left, 104, y, -2, 2, 0.1M, -1, 2);
            _slabMaxNum = MkNum(left, 172, y, -2, 2, 0.1M, 1, 2);
            var bSlab = MkBtn(left, "Select", 240, y, 80);
            bSlab.Click += Slab_Click;
            y += 28;

            string[] ndirs = new string[] { "Up", "Down", "Front", "Back", "Left", "Right" };
            for (int i = 0; i < 6; i++)
            {
                int d = i;
                var b = MkBtn(left, ndirs[i], 10 + i * 52, y, 48);
                b.Click += delegate { Normal_Click(d); };
            }
            y += 28;
            var bConn = MkBtn(left, "Connected (from last click)", 10, y, 200);
            bConn.Click += Connected_Click;
            y += 30;

            left.Controls.Add(MakeLabel("Box X0 X1:", 10, y, 64, 20));
            _boxX0 = MkNum(left, 76, y, -2, 2, 0.1M, -1, 2);
            _boxX1 = MkNum(left, 144, y, -2, 2, 0.1M, 1, 2);
            var bBoxSel = MkBtn(left, "Select", 212, y, 52);
            bBoxSel.Click += delegate { Box_Select(false); };
            var bBoxDel = MkBtn(left, "DelOut", 268, y, 52);
            bBoxDel.Click += delegate { Box_Select(true); };
            y += 26;
            left.Controls.Add(MakeLabel("Box Y0 Y1:", 10, y, 64, 20));
            _boxY0 = MkNum(left, 76, y, -2, 2, 0.1M, -1, 2);
            _boxY1 = MkNum(left, 144, y, -2, 2, 0.1M, 1, 2);
            y += 26;
            left.Controls.Add(MakeLabel("Box Z0 Z1:", 10, y, 64, 20));
            _boxZ0 = MkNum(left, 76, y, -2, 2, 0.1M, -1, 2);
            _boxZ1 = MkNum(left, 144, y, -2, 2, 0.1M, 1, 2);
            y += 30;

            y = AddGroup(left, y, "Color (selection, else all)");
            y = AddRow2(left, y, "Hue deg:", -180, 180, 5, 30, 0, "Sat x:", 0, 3, 0.1M, 1, 1,
                out _hueDegNum, out _satNum);
            var bHue = MkBtn(left, "Hue", 10, y, 70);
            bHue.Click += delegate { ColorOp("hue"); };
            var bSat = MkBtn(left, "Sat", 86, y, 70);
            bSat.Click += delegate { ColorOp("sat"); };
            left.Controls.Add(MakeLabel("Light x:", 162, y + 4, 52, 20));
            _lightNum = MkNum(left, 216, y, 0, 3, 0.1M, 1, 2);
            var bLightGo = MkBtn(left, "Go", 282, y, 38);
            bLightGo.Click += delegate { ColorOp("light"); };
            y += 28;
            var bTint = MkBtn(left, "Tint…", 10, y, 70);
            bTint.Click += delegate
            {
                using (var dlg = new ColorDialog { Color = _tintColor, FullOpen = true })
                {
                    if (dlg.ShowDialog(this) == DialogResult.OK) { _tintColor = dlg.Color; ColorOp("tint"); }
                }
            };
            left.Controls.Add(MakeLabel("Tint str:", 86, y + 4, 56, 20));
            _tintStrNum = MkNum(left, 144, y, 0, 1, 0.1M, 0.6M, 2);
            var bInv2 = MkBtn(left, "Invert", 212, y, 52);
            bInv2.Click += delegate { ColorOp("invert"); };
            var bGray = MkBtn(left, "Gray", 268, y, 52);
            bGray.Click += delegate { ColorOp("gray"); };
            y += 28;
            left.Controls.Add(MakeLabel("Posterize:", 10, y + 4, 64, 20));
            _posterNum = MkNum(left, 76, y, 2, 16, 1, 6, 0);
            var bPost = MkBtn(left, "Apply", 144, y, 60);
            bPost.Click += delegate { ColorOp("poster"); };
            var bDef = MkBtn(left, "Defringe", 210, y, 60);
            bDef.Click += delegate { ColorOp("defringe"); };
            y += 28;
            left.Controls.Add(MakeLabel("AO str:", 10, y + 4, 52, 20));
            _aoStrNum = MkNum(left, 64, y, 0, 1, 0.1M, 0.5M, 2);
            var bAo = MkBtn(left, "AO bake", 132, y, 72);
            bAo.Click += delegate { ColorOp("ao"); };
            y += 30;

            var bRetex = MkBtn(left, "Re-texture: init img", 10, y, 140);
            bRetex.Click += delegate { RetextureFromInit(); };
            left.Controls.Add(MakeLabel("Blend:", 156, y + 4, 44, 20));
            _retexStrNum = MkNum(left, 202, y, 0, 1, 0.1M, 1, 2);
            _retexFrontChk = new CheckBox { Location = new Point(270, y + 2), Width = 52, Height = 20, Text = "Front", Checked = true };
            left.Controls.Add(_retexFrontChk);
            var retexTip = new ToolTip { ShowAlways = true };
            try
            {
                retexTip.SetToolTip(bRetex, "Repaint this model from the current init image (same file shown in the Init preview).\nGeometry (XYZ/scale/rot/opacity) is kept; only splat RGB changes.\nWYSIWYG: orbit to match the init viewpoint first (e.g. S front), then click.\nRespects selection (else whole model). One undo step. Black background pixels are skipped.");
                retexTip.SetToolTip(_retexStrNum, "Blend 0..1: 1 = full replace, 0.5 = half mix with old color.");
                retexTip.SetToolTip(_retexFrontChk, "Front: only repaint the visible surface (recommended).\nUncheck to repaint occluded/back splats too.");
            }
            catch { }
            y += 30;

            y = AddGroup(left, y, "Shape (selection, else all)");
            var bDel = MkBtn(left, "Delete", 10, y, 70);
            bDel.Click += delegate { ShapeOp("delete"); };
            left.Controls.Add(MakeLabel("Move dx dy dz:", 86, y + 4, 92, 20));
            _moveXNum = MkNum(left, 180, y, -2, 2, 0.05M, 0, 2);
            _moveYNum = MkNum(left, 248, y, -2, 2, 0.05M, 0, 2);
            y += 26;
            _moveZNum = MkNum(left, 180, y, -2, 2, 0.05M, 0, 2);
            var bMove = MkBtn(left, "Move", 248, y, 70);
            bMove.Click += delegate { ShapeOp("move"); };
            y += 28;
            var bClone = MkBtn(left, "Clone+Move", 10, y, 90);
            bClone.Click += delegate { ShapeOp("clone"); };
            var bMX = MkBtn(left, "MirX", 106, y, 52);
            bMX.Click += delegate { ShapeOp("mirx"); };
            var bMY = MkBtn(left, "MirY", 162, y, 52);
            bMY.Click += delegate { ShapeOp("miry"); };
            var bMZ = MkBtn(left, "MirZ", 218, y, 52);
            bMZ.Click += delegate { ShapeOp("mirz"); };
            y += 28;
            var bCage = MkBtn(left, "Cage", 10, y, 70);
            bCage.Click += delegate { CageToggle(); };
            var bCageGo = MkBtn(left, "Apply", 86, y, 60);
            bCageGo.Click += delegate { CageApply(); };
            var bCageX = MkBtn(left, "Cancel", 152, y, 60);
            bCageX.Click += delegate { CageCancel(); };
            var cageTip = new ToolTip { ShowAlways = true };
            try
            {
                cageTip.SetToolTip(bCage, "Show the bounding-box cage (quad view): drag cyan corners in Top / Front / Side to skew/scale.");
                cageTip.SetToolTip(bCageGo, "Bake the cage deform into the model (one undo step).");
                cageTip.SetToolTip(bCageX, "Throw the cage away and restore the model.");
            }
            catch { }
            y += 28;
            var bOut = MkBtn(left, "De-floater", 10, y, 90);
            bOut.Click += delegate { ShapeOp("outlier"); };
            var bThin = MkBtn(left, "Thin 1/2", 106, y, 70);
            bThin.Click += delegate { ShapeOp("thin"); };
            var bSm = MkBtn(left, "Smooth", 182, y, 70);
            bSm.Click += delegate { ShapeOp("smooth"); };
            y += 28;
            left.Controls.Add(MakeLabel("Explode:", 10, y + 4, 56, 20));
            _explodeNum = MkNum(left, 68, y, 0, 1, 0.01M, 0.05M, 3);
            var bEx = MkBtn(left, "Apply", 136, y, 60);
            bEx.Click += delegate { ShapeOp("explode"); };
            y += 30;

            y = AddGroup(left, y, "UO hue (hues.mul)");
            left.Controls.Add(MakeLabel("Hue id:", 10, y + 4, 52, 20));
            _hueIdNum = MkNum(left, 64, y, 1, 3000, 1, 1, 0);
            _huePartialChk = new CheckBox { Location = new Point(132, y + 2), Width = 70, Height = 20, Text = "Partial", Checked = true };
            left.Controls.Add(_huePartialChk);
            var bHueGo = MkBtn(left, "Apply", 208, y, 60);
            bHueGo.Click += delegate { ShapeOp("uohue"); };
            y += 26;
            _hueStatusLabel = MakeLabel("", 10, y, 310, 18);
            left.Controls.Add(_hueStatusLabel);
            y += 22;

            y = AddGroup(left, y, "AI / mask / light / spin");
            var bAi = MkBtn(left, "AI Select (SAM)", 10, y, 150);
            bAi.Click += delegate { var t = AiSelectAsync(); };
            var bMask = MkBtn(left, "Load mask…", 166, y, 100);
            bMask.Click += MaskImport_Click;
            y += 30;
            _samStatusLabel = MakeLabel("", 10, y, 310, 30);
            left.Controls.Add(_samStatusLabel);
            y += 34;
            _relightCombo = new ComboBox { Location = new Point(10, y), Width = 130, Height = 21, DropDownStyle = ComboBoxStyle.DropDownList };
            _relightCombo.Items.AddRange(new object[] { "Light: Top", "Light: Front", "Light: Left" });
            _relightCombo.SelectedIndex = 0;
            left.Controls.Add(_relightCombo);
            var bRel = MkBtn(left, "Relight", 146, y, 70);
            bRel.Click += delegate { ShapeOp("relight"); };
            var bSpin = MkBtn(left, "Spintable", 222, y, 90);
            bSpin.Click += Spintable_Click;
            y += 30;

            SetEditMode(0);
            RefreshSelCount();
        }

        private int AddGroup(Panel left, int y, string text)
        {
            var l = MakeLabel(text, 10, y, 310, 18);
            l.Font = new Font(l.Font.FontFamily, l.Font.Size, FontStyle.Bold);
            left.Controls.Add(l);
            return y + 22;
        }

        private int AddRow2(Panel left, int y,
            string t1, decimal mn1, decimal mx1, decimal inc1, decimal def1, int dec1,
            string t2, decimal mn2, decimal mx2, decimal inc2, decimal def2, int dec2,
            out NumericUpDown n1, out NumericUpDown n2)
        {
            left.Controls.Add(MakeLabel(t1, 10, y + 4, 62, 20));
            n1 = MkNum(left, 74, y, mn1, mx1, inc1, def1, dec1);
            left.Controls.Add(MakeLabel(t2, 150, y + 4, 62, 20));
            n2 = MkNum(left, 214, y, mn2, mx2, inc2, def2, dec2);
            return y + 28;
        }

        private NumericUpDown MkNum(Panel left, int x, int y, decimal mn, decimal mx, decimal inc, decimal val, int dec)
        {
            var n = new NumericUpDown
            {
                Location = new Point(x, y), Width = 62, Height = 20,
                Minimum = mn, Maximum = mx, Increment = inc, Value = val, DecimalPlaces = dec
            };
            left.Controls.Add(n);
            return n;
        }

        private Button MkBtn(Panel left, string text, int x, int y, int w)
        {
            var b = new Button { Location = new Point(x, y), Width = w, Height = 24, Text = text };
            left.Controls.Add(b);
            return b;
        }

        private void SetEditMode(int m)
        {
            _editMode = m;
            if (_modeBtns != null)
            {
                for (int i = 0; i < _modeBtns.Length; i++)
                {
                    if (i == m)
                    {
                        _modeBtns[i].BackColor = HolographicTheme.ButtonAccent;
                        _modeBtns[i].ForeColor = Color.White;
                        _modeBtns[i].FlatStyle = FlatStyle.Flat;
                    }
                    else
                    {
                        HolographicTheme.ApplyToButton(_modeBtns[i], ButtonStyle.Default);
                    }
                }
            }
        }

        private bool SubtractMode()
        {
            if (_subtractChk != null && _subtractChk.Checked) return true;
            try { return (Control.ModifierKeys & Keys.Alt) == Keys.Alt; }
            catch { return false; }
        }

        /// <summary>
        /// Mouse wheel over the viewport zooms (form-level handler so no
        /// focus tricks are needed; scrollable controls keep their wheel).
        /// </summary>
        private void ThreeDView_MouseWheel(object sender, MouseEventArgs e)
        {
            try
            {
                if (_model == null || viewport == null || zoomTrackBar == null) return;
                if (!viewport.Visible) return;
                Point p = viewport.PointToClient(Cursor.Position);
                if (!viewport.ClientRectangle.Contains(p)) return;
                int step = (Control.ModifierKeys & Keys.Shift) == Keys.Shift ? 5 : 20;
                int v = zoomTrackBar.Value + (e.Delta > 0 ? step : -step);
                v = Math.Max(zoomTrackBar.Minimum, Math.Min(zoomTrackBar.Maximum, v));
                if (v != zoomTrackBar.Value) zoomTrackBar.Value = v; // fires ViewParam_Changed
                var he = e as HandledMouseEventArgs;
                if (he != null) he.Handled = true;
            }
            catch { }
        }

        #endregion

        #region Edit gestures (called from the main mouse handlers)

        private bool _editSelStroke;
        private readonly List<Point> _selPts = new List<Point>();
        private bool _brushing;
        private Point _brushLast;
        private SplatSelect.DepthMap _brushDepth;
        private Point _wandDown;
        private bool _wandDownValid;

        private double[] EditRobj()
        {
            float rx, ry, rz;
            RotVals(out rx, out ry, out rz);
            return SplatRenderer.ObjectMatrix(rx, ry, rz);
        }

        private double[] EditRobjScaled()
        {
            float rx, ry, rz;
            RotVals(out rx, out ry, out rz);
            float sx, sy, sz;
            ScaleVals(out sx, out sy, out sz);
            return SplatRenderer.ScaledMatrix(SplatRenderer.ObjectMatrix(rx, ry, rz), sx, sy, sz);
        }

        private void ViewportCam(out int w, out int h, out double fit, out double cx, out double cy)
        {
            w = Math.Max(64, viewport.Width);
            h = Math.Max(64, viewport.Height);
            fit = Math.Min(w, h) * 0.42 * (zoomTrackBar.Value / 100f);
            cx = w * 0.5;
            cy = h * 0.52;
        }

        private bool HandleEditMouseDown(MouseEventArgs e)
        {
            if (_editMode == 0 || _model == null) return false;
            if (_editMode == 1)
            {
                _editSelStroke = true;
                _selPts.Clear();
                _selPts.Add(e.Location);
                viewport.Capture = true;
                viewport.Invalidate();
                return true;
            }
            if (_editMode == 2)
            {
                _brushing = true;
                _brushLast = e.Location;
                BeginBrushStroke();
                PaintBrushAt(e.Location, e.Location);
                return true;
            }
            if (_editMode == 3)
            {
                _wandDown = e.Location;
                _wandDownValid = true;
                viewport.Capture = true;
                return true;
            }
            return false;
        }

        private bool HandleEditMouseMove(MouseEventArgs e)
        {
            if (!e.Button.HasFlag(MouseButtons.Left)) { _brushing = false; }
            if (_editSelStroke)
            {
                if (_model == null) return true;
                Point last = _selPts[_selPts.Count - 1];
                int dx = e.X - last.X, dy = e.Y - last.Y;
                if (dx * dx + dy * dy >= 9)
                {
                    _selPts.Add(e.Location);
                    viewport.Invalidate();
                }
                return true;
            }
            if (_brushing)
            {
                if (_model == null) return true;
                PaintBrushAt(_brushLast, e.Location);
                _brushLast = e.Location;
                return true;
            }
            return false;
        }

        private bool HandleEditMouseUp(MouseEventArgs e)
        {
            if (_editSelStroke)
            {
                _editSelStroke = false;
                try { viewport.Capture = false; }
                catch { }
                try
                {
                    if (_selPts.Count >= 3) LassoSelect();
                }
                catch (Exception ex) { Log("Lasso select failed: " + ex.Message); }
                _selPts.Clear();
                viewport.Invalidate();
                return true;
            }
            if (_brushing)
            {
                _brushing = false;
                try { viewport.Capture = false; }
                catch { }
                _brushDepth = null;
                _strokeSX = _strokeSY = _strokeDP = null;
                viewport.Invalidate();
                return true;
            }
            if (_editMode == 3 && _wandDownValid)
            {
                _wandDownValid = false;
                try { viewport.Capture = false; }
                catch { }
                int dx = e.X - _wandDown.X, dy = e.Y - _wandDown.Y;
                if (dx * dx + dy * dy <= 36) WandPick(e.Location);
                viewport.Invalidate();
                return true;
            }
            return false;
        }

        private bool PaintEditOverlay(PaintEventArgs e)
        {
            bool drew = false;
            try
            {
                if (_editSelStroke && _selPts.Count >= 2)
                {
                    using (var pen = new Pen(Color.Lime, 2f))
                    {
                        e.Graphics.DrawLines(pen, _selPts.ToArray());
                        e.Graphics.DrawLine(pen, _selPts[_selPts.Count - 1], _selPts[0]);
                    }
                    drew = true;
                }
                if (_brushing && _brushRNum != null)
                {
                    float r = (float)_brushRNum.Value;
                    using (var pen = new Pen(Color.Cyan, 1.5f))
                    {
                        e.Graphics.DrawEllipse(pen, _brushLast.X - r, _brushLast.Y - r, r * 2, r * 2);
                    }
                    drew = true;
                }
            }
            catch { }
            return drew;
        }

        #endregion

        #region Selection generators

        private void LassoSelect()
        {
            if (!EnsureSel() || _selPts.Count < 3) return;
            bool uo = UoMode();
            float pitch = (float)pitchNumeric.Value;
            int w, h;
            double fit, cx, cy;
            ViewportCam(out w, out h, out fit, out cx, out cy);
            double[] robj = EditRobjScaled();
            int n = 0;
            var pts = _model.Points;
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                double qx, qy, qz;
                SplatRenderer.TransformPoint(robj, p.X, p.Y, p.Z, out qx, out qy, out qz);
                // ProjectPoint takes un-rotated model coords + yaw only, so
                // un-rotate the yaw here is wrong; instead replicate: use the
                // same math as the renderer via yaw-only projection of q.
                float sx, sy;
                ProjectYawOnly((float)qx, (float)qy, (float)qz, uo, pitch, fit, cx, cy, out sx, out sy);
                if (PointInLasso(sx, sy, _selPts))
                {
                    if (SubtractMode()) { if (_sel[i]) { _sel[i] = false; n--; } }
                    else if (!_sel[i]) { _sel[i] = true; n++; }
                }
            }
            Log("Lasso select: " + Math.Abs(n) + (SubtractMode() ? " removed." : " added."));
            SelectionChanged();
        }

        /// <summary>Yaw-only projection of already object-transformed coords.</summary>
        private void ProjectYawOnly(float x, float y, float z, bool uo, float pitchDeg,
            double fit, double cx, double cy, out float sx, out float sy)
        {
            double yaw = _yaw * Math.PI / 180.0;
            double cosY = Math.Cos(yaw), sinY = Math.Sin(yaw);
            double x1 = x * cosY + z * sinY;
            double z1 = -x * sinY + z * cosY;
            if (uo)
            {
                sx = (float)(cx + (x1 - z1) * fit);
                sy = (float)(cy + (x1 + z1) * fit - y * SplatRenderer.UoVerticalScale * fit);
            }
            else
            {
                double pr = pitchDeg * Math.PI / 180.0;
                double y2 = y * Math.Cos(pr) - z1 * Math.Sin(pr);
                sx = (float)(cx + x1 * fit);
                sy = (float)(cy - y2 * fit);
            }
        }

        private float[] _strokeSX, _strokeSY, _strokeDP;

        private void BeginBrushStroke()
        {
            _strokeSX = _strokeSY = _strokeDP = null;
            _brushDepth = null;
            if (_model == null) return;
            try
            {
                bool uo = UoMode();
                float pitch = (float)pitchNumeric.Value;
                int w, h;
                double fit, cx, cy;
                ViewportCam(out w, out h, out fit, out cx, out cy);
                int n = _model.Count;
                _strokeSX = new float[n];
                _strokeSY = new float[n];
                _strokeDP = new float[n];
                double[] robj = EditRobjScaled();
                SplatSelect.ProjectAll(_model, _yaw, pitch, uo, robj,
                    zoomTrackBar.Value / 100f, w, h, _strokeSX, _strokeSY, _strokeDP);
                // Depth map from the same pass (min depth per 4px cell).
                // NOTE: same scaled matrix as the projection above, so the
                // depth test compares like with like when scaled.
                _brushDepth = new SplatSelect.DepthMap(_model, _yaw, pitch, uo,
                    EditRobjScaled(), zoomTrackBar.Value / 100f, w, h, 4);
            }
            catch (Exception ex) { Log("Brush prep failed: " + ex.Message); }
        }

        private void PaintBrushAt(Point a, Point b)
        {
            if (!EnsureSel() || _strokeSX == null) return;
            float radius = _brushRNum != null ? (float)_brushRNum.Value : 40f;
            if (radius < 2) radius = 2;
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            int steps = Math.Max(1, (int)(dist / 4.0));
            int n = 0;
            float r2 = radius * radius;
            float tol = SplatOps.EstimateSpacing(_model) * 3f;
            int count = _model.Count;
            for (int s = 0; s <= steps; s++)
            {
                double px = a.X + dx * s / steps, py = a.Y + dy * s / steps;
                for (int i = 0; i < count; i++)
                {
                    float sx = _strokeSX[i], sy = _strokeSY[i];
                    double ddx = sx - px, ddy = sy - py;
                    if (ddx * ddx + ddy * ddy > r2) continue;
                    if (_brushDepth != null && !_brushDepth.IsFront(sx, sy, _strokeDP[i], tol)) continue;
                    if (SubtractMode()) { if (_sel[i]) { _sel[i] = false; n--; } }
                    else if (!_sel[i]) { _sel[i] = true; n++; }
                }
            }
            if (n != 0) SelectionChanged();
            else viewport.Invalidate();
        }

        private float RotX() { float a, b, c; RotVals(out a, out b, out c); return a; }
        private float RotY() { float a, b, c; RotVals(out a, out b, out c); return b; }
        private float RotZ() { float a, b, c; RotVals(out a, out b, out c); return c; }

        private int NearestSplat(Point loc, float maxDist)
        {
            if (_model == null) return -1;
            bool uo = UoMode();
            float pitch = (float)pitchNumeric.Value;
            int w, h;
            double fit, cx, cy;
            ViewportCam(out w, out h, out fit, out cx, out cy);
            double[] robj = EditRobjScaled();
            var pts = _model.Points;
            int best = -1;
            double best2 = maxDist * maxDist;
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                double qx, qy, qz;
                SplatRenderer.TransformPoint(robj, p.X, p.Y, p.Z, out qx, out qy, out qz);
                float sx, sy;
                ProjectYawOnly((float)qx, (float)qy, (float)qz, uo, pitch, fit, cx, cy, out sx, out sy);
                double dx = sx - loc.X, dy = sy - loc.Y;
                double d2 = dx * dx + dy * dy;
                if (d2 < best2) { best2 = d2; best = i; }
            }
            return best;
        }

        private void WandPick(Point loc)
        {
            if (!EnsureSel()) return;
            int seed = NearestSplat(loc, 14f);
            if (seed < 0)
            {
                Log("Wand: no splat near click.");
                return;
            }
            _lastClickIdx = seed;
            _lastClickPt = loc;
            float spacing = SplatOps.EstimateSpacing(_model);
            var hash = new SplatSelect.SpatialHash(_model.Points, Math.Max(spacing * 2f, 1e-4f));
            bool sub = SubtractMode();
            int n;
            if (_wandTargetCombo != null && _wandTargetCombo.SelectedIndex == 1)
            {
                float tolDeg = _wandTolNum != null ? (float)_wandTolNum.Value : 25f;
                double cosTol = Math.Cos(tolDeg * Math.PI / 180.0);
                double[] robj = SplatRenderer.ObjectMatrix(RotX(), RotY(), RotZ());
                var sp = _model.Points[seed];
                double snx = sp.Nx, sny = sp.Ny, snz = sp.Nz;
                {
                    double rx = robj[0] * snx + robj[1] * sny + robj[2] * snz;
                    double ry = robj[3] * snx + robj[4] * sny + robj[5] * snz;
                    double rz = robj[6] * snx + robj[7] * sny + robj[8] * snz;
                    snx = rx; sny = ry; snz = rz;
                }
                double fsnx = snx, fsny = sny, fsnz = snz;
                var pts = _model.Points;
                n = SplatSelect.Flood(_model, _sel, seed, hash, spacing * 2f, delegate (SplatPoint p, SplatPoint s)
                {
                    double nx = p.Nx, ny = p.Ny, nz = p.Nz;
                    double rx = robj[0] * nx + robj[1] * ny + robj[2] * nz;
                    double ry = robj[3] * nx + robj[4] * ny + robj[5] * nz;
                    double rz = robj[6] * nx + robj[7] * ny + robj[8] * nz;
                    return rx * fsnx + ry * fsny + rz * fsnz >= cosTol;
                }, sub);
                Log("Wand (normal): " + Math.Abs(n) + (sub ? " removed." : " added."));
            }
            else
            {
                double tol = _wandTolNum != null ? (double)_wandTolNum.Value : 30.0;
                var sp = _model.Points[seed];
                double fsr = sp.R, fsg = sp.G, fsb = sp.B;
                n = SplatSelect.Flood(_model, _sel, seed, hash, spacing * 2f, delegate (SplatPoint p, SplatPoint s)
                {
                    double dr = p.R - fsr, dg = p.G - fsg, db = p.B - fsb;
                    return Math.Sqrt(dr * dr + dg * dg + db * db) <= tol;
                }, sub);
                Log("Wand (color): " + Math.Abs(n) + (sub ? " removed." : " added."));
            }
            SelectionChanged();
        }

        private void Slab_Click(object sender, EventArgs e)
        {
            if (!EnsureSel() || _model == null) return;
            int axis = _slabAxisCombo != null ? _slabAxisCombo.SelectedIndex : 1;
            float lo = _slabMinNum != null ? (float)_slabMinNum.Value : -1f;
            float hi = _slabMaxNum != null ? (float)_slabMaxNum.Value : 1f;
            int n = SplatSelect.Slab(_model, _sel, axis, Math.Min(lo, hi), Math.Max(lo, hi), SubtractMode());
            Log("Slab: " + Math.Abs(n) + (SubtractMode() ? " removed." : " added."));
            SelectionChanged();
        }

        private void Normal_Click(int dir)
        {
            if (!EnsureSel() || _model == null) return;
            float dx = 0, dy = 0, dz = 0;
            if (dir == 0) dy = 1;
            else if (dir == 1) dy = -1;
            else if (dir == 2) dz = 1;
            else if (dir == 3) dz = -1;
            else if (dir == 4) dx = -1;
            else dx = 1;
            double[] robj = SplatRenderer.ObjectMatrix(RotX(), RotY(), RotZ());
            int n = SplatSelect.ByNormal(_model, _sel, dx, dy, dz, 0.7f, SubtractMode(), robj);
            Log("Normal select: " + Math.Abs(n) + (SubtractMode() ? " removed." : " added."));
            SelectionChanged();
        }

        private void Connected_Click(object sender, EventArgs e)
        {
            if (!EnsureSel() || _model == null) return;
            if (_lastClickIdx < 0 || _lastClickIdx >= _model.Count)
            {
                Log("Connected: click a splat with the Wand tool first (sets the seed).");
                return;
            }
            float spacing = SplatOps.EstimateSpacing(_model);
            var hash = new SplatSelect.SpatialHash(_model.Points, Math.Max(spacing * 2f, 1e-4f));
            int n = SplatSelect.Flood(_model, _sel, _lastClickIdx, hash, spacing * 2f,
                delegate (SplatPoint p, SplatPoint s) { return true; }, SubtractMode());
            Log("Connected: " + Math.Abs(n) + (SubtractMode() ? " removed." : " added."));
            SelectionChanged();
        }

        #endregion

        #region Operators

        private void ColorOp(string which)
        {
            if (_model == null) return;
            if (!EnsureSel()) return;
            var idx = SplatOps.Affected(_model, UseSel());
            if (idx.Count == 0) { Log("Nothing to recolor."); return; }
            PushMutateUndo(idx);
            int n = 0;
            string name = which;
            if (which == "hue") n = SplatOps.HueShift(_model, UseSel(), _hueDegNum != null ? (float)_hueDegNum.Value : 30f);
            else if (which == "sat") n = SplatOps.SatMul(_model, UseSel(), _satNum != null ? (float)_satNum.Value : 1f);
            else if (which == "light") n = SplatOps.LightMul(_model, UseSel(), _lightNum != null ? (float)_lightNum.Value : 1f);
            else if (which == "tint") n = SplatOps.Tint(_model, UseSel(), _tintColor, _tintStrNum != null ? (float)_tintStrNum.Value : 0.6f);
            else if (which == "invert") n = SplatOps.Invert(_model, UseSel());
            else if (which == "gray") n = SplatOps.Grayscale(_model, UseSel());
            else if (which == "poster") n = SplatOps.Posterize(_model, UseSel(), _posterNum != null ? (int)_posterNum.Value : 6);
            else if (which == "defringe") n = SplatOps.Defringe(_model, UseSel(), 40, 0.8f);
            else if (which == "ao") n = SplatOps.AoBake(_model, UseSel(), _aoStrNum != null ? (float)_aoStrNum.Value : 0.5f, 4f);
            AfterEdit(name + ": recolored " + n + " splats (" + Scope() + ").");
        }

        private Mul.HuesReader _hues;
        private int _hueCount;

        private Color[] UoHueTable(int hueId)
        {
            try
            {
                if (_hues == null)
                {
                    _hues = new Mul.HuesReader();
                    string folder = string.Empty;
                    try { folder = AppConfig.Instance.FindMulFolder(); } catch { }
                    if (string.IsNullOrEmpty(folder) || !_hues.Load(folder))
                    {
                        _hueStatusLabel.Text = "hues.mul not found.";
                        return null;
                    }
                    // Count usable tables.
                    _hueCount = 0;
                    for (int h = 1; h <= 3000; h++)
                        if (_hues.GetHueColors(h) != null) _hueCount++;
                    _hueStatusLabel.Text = _hueCount + " hues loaded.";
                }
                return _hues.GetHueColors(hueId);
            }
            catch (Exception ex)
            {
                _hueStatusLabel.Text = "hue error.";
                Log("UO hue failed: " + ex.Message);
                return null;
            }
        }

        private void ShapeOp(string which)
        {
            if (_model == null) return;
            if (!EnsureSel()) return;
            bool sub = SubtractMode();
            int n = 0;
            if (which == "delete")
            {
                var idx = SplatOps.Affected(_model, UseSel());
                if (idx.Count == 0) { Log("Nothing to delete."); return; }
                if (idx.Count == _model.Count)
                {
                    if (MessageBox.Show(this, "Delete ALL " + idx.Count + " splats?", "3D Editor",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                }
                PushDeleteUndo(idx);
                SplatOps.DeleteIndices(_model, idx);
                ReindexAfterRemoval(idx);
                AfterEdit("Deleted " + idx.Count + " splats (" + Scope() + ").");
                return;
            }
            if (which == "move" || which == "clone")
            {
                float dx = _moveXNum != null ? (float)_moveXNum.Value : 0f;
                float dy = _moveYNum != null ? (float)_moveYNum.Value : 0f;
                float dz = _moveZNum != null ? (float)_moveZNum.Value : 0f;
                if (which == "move")
                {
                    var idx = SplatOps.Affected(_model, UseSel());
                    if (idx.Count == 0) { Log("Nothing to move."); return; }
                    PushMutateUndo(idx);
                    n = SplatOps.Move(_model, UseSel(), dx, dy, dz);
                    AfterEdit("Moved " + n + " splats by (" + dx + "," + dy + "," + dz + ").");
                }
                else
                {
                    var idx = SplatOps.Affected(_model, UseSel());
                    if (idx.Count == 0) { Log("Nothing to clone."); return; }
                    var added = SplatOps.Clone(_model, UseSel(), dx, dy, dz);
                    PushAddUndo(added);
                    if (_sel != null)
                    {
                        var ns = new bool[_model.Count];
                        int m = Math.Min(_sel.Length, ns.Length);
                        for (int k = 0; k < m; k++) ns[k] = _sel[k];
                        _sel = ns;
                    }
                    AfterEdit("Cloned " + added.Count + " splats (offset " + dx + "," + dy + "," + dz + ").");
                }
                return;
            }
            if (which == "mirx" || which == "miry" || which == "mirz")
            {
                var idx = SplatOps.Affected(_model, UseSel());
                if (idx.Count == 0) { Log("Nothing to mirror."); return; }
                PushMutateUndo(idx);
                n = SplatOps.Mirror(_model, UseSel(), which == "mirx" ? 0 : (which == "miry" ? 1 : 2));
                AfterEdit("Mirrored " + n + " splats (" + which + ").");
                return;
            }
            if (which == "outlier")
            {
                var idx = SplatOps.Affected(_model, UseSel());
                if (idx.Count == 0) { Log("Nothing to clean."); return; }
                List<int> removed;
                SplatPoint[] gone;
                n = SplatOps.OutlierRemove(_model, UseSel(), 6, 3f, out removed, out gone);
                if (n == 0) { Log("De-floater: no strays found."); return; }
                _editUndo.Add(new EditUndo { IsAdd = false, IsDelete = true, Idx = removed.ToArray(), Before = gone });
                TrimUndo();
                MarkUndoAvailable();
                ReindexAfterRemoval(removed);
                AfterEdit("De-floater: removed " + n + " stray splats.");
                return;
            }
            if (which == "thin")
            {
                var idx = SplatOps.Affected(_model, UseSel());
                if (idx.Count == 0) { Log("Nothing to thin."); return; }
                float cell = SplatOps.EstimateSpacing(_model) * 2f;
                List<int> removed;
                SplatPoint[] gone;
                n = SplatOps.VoxelDownsample(_model, UseSel(), cell, out removed, out gone);
                if (n == 0) { Log("Thin: already minimal."); return; }
                _editUndo.Add(new EditUndo { IsAdd = false, IsDelete = true, Idx = removed.ToArray(), Before = gone });
                TrimUndo();
                MarkUndoAvailable();
                ReindexAfterRemoval(removed);
                AfterEdit("Thin: removed " + n + " splats (cell " + cell.ToString("0.000") + ").");
                return;
            }
            if (which == "smooth")
            {
                var idx = SplatOps.Affected(_model, UseSel());
                if (idx.Count == 0) { Log("Nothing to smooth."); return; }
                PushMutateUndo(idx);
                n = SplatOps.LaplacianSmooth(_model, UseSel(), 0.5f);
                AfterEdit("Smooth: relaxed " + n + " splats.");
                return;
            }
            if (which == "explode")
            {
                var idx = SplatOps.Affected(_model, UseSel());
                if (idx.Count == 0) { Log("Nothing to explode."); return; }
                PushMutateUndo(idx);
                n = SplatOps.Explode(_model, UseSel(), _explodeNum != null ? (float)_explodeNum.Value : 0.05f);
                AfterEdit("Exploded " + n + " splats along normals.");
                return;
            }
            if (which == "uohue")
            {
                int id = _hueIdNum != null ? (int)_hueIdNum.Value : 1;
                var table = UoHueTable(id);
                if (table == null) { Log("UO hue: no table for id " + id + "."); return; }
                var idx = SplatOps.Affected(_model, UseSel());
                if (idx.Count == 0) { Log("Nothing to hue."); return; }
                PushMutateUndo(idx);
                bool partial = _huePartialChk != null && _huePartialChk.Checked;
                n = SplatOps.ApplyUoHue(_model, UseSel(), table, partial);
                AfterEdit("UO hue " + id + (partial ? " (partial)" : " (full)") + ": " + n + " splats.");
                return;
            }
            if (which == "relight")
            {
                int preset = _relightCombo != null ? _relightCombo.SelectedIndex : 0;
                float lx = 0, ly = 1, lz = 0;
                if (preset == 1) { lx = 0; ly = 0.3f; lz = 1; }
                else if (preset == 2) { lx = -1; ly = 0.4f; lz = 0.3f; }
                var idx = SplatOps.Affected(_model, UseSel());
                if (idx.Count == 0) { Log("Nothing to relight."); return; }
                PushMutateUndo(idx);
                float rx, ry, rz;
                RotVals(out rx, out ry, out rz);
                n = SplatOps.Relight(_model, UseSel(), lx, ly, lz, 0.35f, 0.9f,
                    SplatRenderer.ObjectMatrix(rx, ry, rz));
                AfterEdit("Relight (" + _relightCombo.Text + "): " + n + " splats.");
                return;
            }
        }

        private void Box_Select(bool deleteOutside)
        {
            if (!EnsureSel() || _model == null) return;
            float x0 = (float)_boxX0.Value, x1 = (float)_boxX1.Value;
            float y0 = (float)_boxY0.Value, y1 = (float)_boxY1.Value;
            float z0 = (float)_boxZ0.Value, z1 = (float)_boxZ1.Value;
            if (!deleteOutside)
            {
                int n = 0;
                var pts = _model.Points;
                for (int i = 0; i < pts.Count; i++)
                {
                    var p = pts[i];
                    if (p.X >= Math.Min(x0, x1) && p.X <= Math.Max(x0, x1) &&
                        p.Y >= Math.Min(y0, y1) && p.Y <= Math.Max(y0, y1) &&
                        p.Z >= Math.Min(z0, z1) && p.Z <= Math.Max(z0, z1))
                    {
                        if (SubtractMode()) { if (_sel[i]) { _sel[i] = false; n--; } }
                        else if (!_sel[i]) { _sel[i] = true; n++; }
                    }
                }
                Log("Box select: " + Math.Abs(n) + (SubtractMode() ? " removed." : " added."));
                SelectionChanged();
            }
            else
            {
                var kill = new List<int>();
                var pts = _model.Points;
                for (int i = 0; i < pts.Count; i++)
                {
                    var p = pts[i];
                    if (!(p.X >= Math.Min(x0, x1) && p.X <= Math.Max(x0, x1) &&
                          p.Y >= Math.Min(y0, y1) && p.Y <= Math.Max(y0, y1) &&
                          p.Z >= Math.Min(z0, z1) && p.Z <= Math.Max(z0, z1)))
                        kill.Add(i);
                }
                if (kill.Count == 0) { Log("Box: everything already inside."); return; }
                PushDeleteUndo(kill);
                SplatOps.DeleteIndices(_model, kill);
                ReindexAfterRemoval(kill);
                AfterEdit("Box delete-outside: removed " + kill.Count + " splats.");
            }
        }

        #endregion

        #region AI select (SAM), mask import, spintable

        private static byte[] BitmapToPngBytes(Bitmap bmp)
        {
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }

        private static Bitmap Flatten(Bitmap src, Color bg)
        {
            var out_ = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(out_))
            {
                g.Clear(bg);
                g.DrawImage(src, 0, 0, src.Width, src.Height);
            }
            return out_;
        }

        private async Task<string> ProbeSamLoader()
        {
            // Known SAM model-loader node classes across packs. Returns the
            // first one the connected server actually has, else null.
            string[] cands = new string[]
            {
                "SAMLoader", "SamLoader", "SAM2Loader", "Sam2Loader",
                "SamAutomaticMaskGenerator", "GroundingDinoModelLoader"
            };
            try
            {
                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(15);
                    string baseUrl = comfyUrlTextBox.Text.Trim().TrimEnd('/');
                    foreach (string c in cands)
                    {
                        try
                        {
                            var r = await http.GetAsync(baseUrl + "/object_info/" + c);
                            if (r.IsSuccessStatusCode)
                            {
                                string body = await r.Content.ReadAsStringAsync();
                                if (!string.IsNullOrEmpty(body) && body.Contains("\"" + c + "\""))
                                    return c;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return null;
        }

        private async Task AiSelectAsync()
        {
            if (_model == null)
            {
                MessageBox.Show(this, "No model loaded.", "3D Editor");
                return;
            }
            _samStatusLabel.Text = "Rendering view…";
            try
            {
                // 1. Render the current view (uncropped, opaque) for masking.
                Bitmap view = RenderComposedModel(_model, 512, 512, _yaw, (float)pitchNumeric.Value,
                    zoomTrackBar.Value / 100f, DotScale(), false, 0, true, Color.Empty, UoMode(), 0f);
                Bitmap flat = Flatten(view, Color.Black);
                view.Dispose();
                byte[] png;
                using (flat) png = BitmapToPngBytes(flat);

                // 2. Upload.
                _samStatusLabel.Text = "Uploading…";
                if (_comfy == null) _comfy = new ComfyUI.ComfyUIClient(comfyUrlTextBox.Text.Trim());
                string serverName = await _comfy.UploadImage(png, "splat_view.png");
                Log("AI select: view uploaded as " + serverName + ".");

                // 3. Look for a usable local SAM path.
                _samStatusLabel.Text = "Probing SAM…";
                string loader = await ProbeSamLoader();
                if (loader == null)
                {
                    _samStatusLabel.Text = "No SAM found.";
                    Log("AI select: no SAM model loader on this server (checked SAMLoader, Sam2Loader, GroundingDinoModelLoader). " +
                        "Install a SAM pack (e.g. comfyui_segment_anything) to enable one-click AI select, " +
                        "or paint a mask in any image editor and use Load mask.");
                    MessageBox.Show(this,
                        "No SAM model loader found on the ComfyUI server.\n\n" +
                        "To enable AI Select, install a SAM pack (e.g. comfyui_segment_anything) on the server.\n\n" +
                        "Meanwhile, 'Load mask' works today: paint white where you want selected, load it, done.",
                        "3D Editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                _samStatusLabel.Text = "Found " + loader + ".";
                Log("AI select: found loader " + loader + " - full auto-wire needs its checkpoint name. " +
                    "Tell me which SAM pack + weights you use and I will wire it end to end. Mask import works meanwhile.");
                MessageBox.Show(this,
                    "Found '" + loader + "' on the server.\n\n" +
                    "Full one-click wiring needs the checkpoint filename for your SAM weights - " +
                    "tell me which SAM pack + weights file you use and it will be wired end to end.\n\n" +
                    "Meanwhile, 'Load mask' covers the same selection path.",
                    "3D Editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                _samStatusLabel.Text = "AI select failed.";
                Log("AI select failed: " + ex.Message);
            }
        }

        private void MaskImport_Click(object sender, EventArgs e)
        {
            if (_model == null)
            {
                MessageBox.Show(this, "No model loaded.", "3D Editor");
                return;
            }
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = "Mask image (*.png;*.jpg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp";
                dlg.Title = "Load selection mask (white = select)";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    if (!EnsureSel()) return;
                    Bitmap mask = null;
                    try
                    {
                        using (var src = new Bitmap(dlg.FileName))
                            mask = new Bitmap(src);
                    }
                    catch (Exception ex) { Log("Mask load failed: " + ex.Message); return; }
                    bool uo = UoMode();
                    float pitch = (float)pitchNumeric.Value;
                    int w, h;
                    double fit, cx, cy;
                    ViewportCam(out w, out h, out fit, out cx, out cy);
                    float rx, ry, rz;
                    RotVals(out rx, out ry, out rz);
                    float sx, sy, sz;
                    ScaleVals(out sx, out sy, out sz);
                    double[] robj = SplatRenderer.ScaledMatrix(
                        SplatRenderer.ObjectMatrix(rx, ry, rz), sx, sy, sz);
                    float tol = SplatOps.EstimateSpacing(_model) * 3f;
                    int n;
                    using (mask)
                        n = SplatSelect.FromMask(_model, _sel, mask, _yaw, pitch, uo, robj,
                            zoomTrackBar.Value / 100f, w, h, tol, SubtractMode());
                    Log("Mask select: " + Math.Abs(n) + (SubtractMode() ? " removed." : " added (front surface only)."));
                    SelectionChanged();
                }
                catch (Exception ex) { Log("Mask select failed: " + ex.Message); }
            }
        }

        private void Spintable_Click(object sender, EventArgs e)
        {
            if (_model == null)
            {
                MessageBox.Show(this, "No model loaded.", "3D Editor");
                return;
            }
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Folder for spintable frames (24 PNGs + GIF attempt)";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    Cursor.Current = Cursors.WaitCursor;
                    string baseName = Path.GetFileNameWithoutExtension(_plyPath.Length > 0 ? _plyPath : "model");
                    var frames = new List<Bitmap>();
                    try
                    {
                        for (int f = 0; f < 24; f++)
                        {
                            float yaw = f * 15f;
                            // Spins are orbit-style turntable frames.
                            Bitmap bmp = RenderComposedModel(_model, 256, 256, yaw, 20f, zoomTrackBar.Value / 100f,
                                DotScale(), false, 0, true, Color.Empty, false, yaw);
                            frames.Add(bmp);
                            string fp = Path.Combine(dlg.SelectedPath, string.Format("{0}_spin_{1:00}.png", baseName, f));
                            bmp.Save(fp, ImageFormat.Png);
                        }
                        Log("Spintable: saved 24 frames to " + dlg.SelectedPath + ".");
                        try
                        {
                            string gif = Path.Combine(dlg.SelectedPath, baseName + "_spin.gif");
                            SaveGif(gif, frames, 8);
                            Log("Spintable GIF: " + gif + ".");
                            MessageBox.Show(this, "Saved 24 frames + GIF:\r\n" + gif, "3D Editor");
                        }
                        catch (Exception gex)
                        {
                            Log("GIF encode failed (" + gex.Message + "); PNG sequence kept.");
                            MessageBox.Show(this, "Saved 24 PNG frames (GIF encode failed).", "3D Editor");
                        }
                    }
                    finally
                    {
                        foreach (var b in frames) b.Dispose();
                        Cursor.Current = Cursors.Default;
                    }
                }
                catch (Exception ex)
                {
                    Cursor.Current = Cursors.Default;
                    MessageBox.Show(this, "Spintable failed:\r\n" + ex.Message, "3D Editor");
                }
            }
        }

        private static void SaveGif(string path, List<Bitmap> frames, int delayCs)
        {
            if (frames == null || frames.Count == 0) throw new Exception("no frames");
            var enc = GetEncoder(ImageFormat.Gif);
            if (enc == null) throw new Exception("no GIF encoder");
            var ep = new EncoderParameters(1);
            try
            {
                ep.Param[0] = new EncoderParameter(Encoder.SaveFlag, (long)EncoderValue.MultiFrame);
                frames[0].Save(path, enc, ep);
                ep.Param[0] = new EncoderParameter(Encoder.SaveFlag, (long)EncoderValue.FrameDimensionTime);
                for (int i = 1; i < frames.Count; i++)
                {
                    SetGifDelay(frames[i], delayCs);
                    frames[i].SaveAdd(frames[i], ep);
                }
                ep.Param[0] = new EncoderParameter(Encoder.SaveFlag, (long)EncoderValue.Flush);
                frames[0].SaveAdd(ep);
            }
            finally { ep.Dispose(); }
        }

        private static void SetGifDelay(Bitmap frame, int delayCs)
        {
            try
            {
                var prop = (PropertyItem)FormatterServices.GetUninitializedObject(typeof(PropertyItem));
                prop.Id = 0x5100;
                prop.Len = 4;
                prop.Type = 4;
                prop.Value = BitConverter.GetBytes(delayCs);
                frame.SetPropertyItem(prop);
            }
            catch { }
        }

        private static ImageCodecInfo GetEncoder(ImageFormat fmt)
        {
            foreach (var c in ImageCodecInfo.GetImageEncoders())
                if (c.FormatID == fmt.Guid) return c;
            return null;
        }

        #endregion
    }
}
