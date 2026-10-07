using MeesaMultisMaker.ThreeD;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace MeesaMultisMaker
{
    /// <summary>
    /// Quad-view bounding-box cage: shows the model's box in every quad
    /// viewport and lets Top/Front/Side corners be dragged individually
    /// (skew/scale via trilinear deform). Persp shows the box read-only.
    /// Whole-model op: Apply pushes one undo step, Cancel restores.
    /// </summary>
    public partial class ThreeDEditorForm
    {
        private SplatCage _cage;
        private bool _cageDragging;
        private int _cageQuad = -1;
        private int _cageCorner = -1;
        private double _cageDepth; // view-depth held fixed during a drag
        private float _cageGrabDX, _cageGrabDY; // corner-screen minus mouse
        private double _cageOrigCX, _cageOrigCY, _cageOrigCZ; // model frame
        private double _cageObjX, _cageObjY, _cageObjZ; // object frame at grab
        private readonly double[] _cageInv = new double[9];
        private SplatPoint[] _cageDragBefore; // positions at drag start (one undo step per drag)
        private bool _cageMoved;

        private void CageToggle()
        {
            if (_model == null) { MessageBox.Show(this, "No model loaded.", "3D Editor"); return; }
            if (_cage != null) { CageCancel(); return; }
            try
            {
                _cage = SplatCage.Init(_model);
                _cageDragging = false;
                _cageCorner = -1;
                SetQuad(true);
                Log("Cage on: drag the cyan corners in Top / Front / Side. Apply bakes + undo, Cancel restores.");
                SetStatus("Cage editing.", Color.Cyan);
                QueueRender();
            }
            catch (Exception ex) { _cage = null; Log("Cage failed: " + ex.Message); }
        }

        private void CageApply()
        {
            if (_cage == null || _model == null) return;
            try
            {
                int n = _model.Points.Count;
                if (_cage.Before == null || _cage.Before.Length != n)
                {
                    Log("Cage stale (model changed); discarded.");
                    DiscardCage();
                    return;
                }
                var idx = new int[n];
                for (int i = 0; i < n; i++) idx[i] = i;
                DropCageUndos(); // drags collapse into this one step
                _editUndo.Add(new EditUndo { IsAdd = false, IsDelete = false, Idx = idx, Before = _cage.Before });
                TrimUndo();
                MarkUndoAvailable();
                SplatCage.RecalcBounds(_model);
                Log("Cage applied (" + n + " splats).");
                DiscardCage();
                RefreshSlicesKeepActive("cage");
                QueueRender();
            }
            catch (Exception ex) { Log("Cage apply failed: " + ex.Message); }
        }

        private void CageCancel()
        {
            if (_cage == null) return;
            try
            {
                if (_model != null && _cage.Before != null && _cage.Before.Length == _model.Points.Count)
                {
                    for (int i = 0; i < _cage.Before.Length; i++)
                        _model.Points[i] = _cage.Before[i];
                    SplatCage.RecalcBounds(_model);
                }
                Log("Cage cancelled (restored).");
            }
            catch (Exception ex) { Log("Cage cancel failed: " + ex.Message); }
            DropCageUndos();
            DiscardCage();
            RefreshSlicesKeepActive("cage");
            QueueRender();
        }

        private void DiscardCage()
        {
            _cage = null;
            _cageDragging = false;
            _cageQuad = -1;
            _cageCorner = -1;
            _cageDragBefore = null;
            _cageMoved = false;
        }

        private void ThreeDEditorForm_KeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (e.Control && e.KeyCode == Keys.Z && !e.Shift)
                {
                    // Never steal text-box undo (prompt box, numbers, LLM
                    // boxes): those keep their native Ctrl+Z.
                    var ac = ActiveControl as Control;
                    Control c = ac;
                    while (c != null)
                    {
                        if (c is TextBoxBase) return;
                        c = c.Parent;
                    }
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    UndoEdit();
                }
            }
            catch { }
        }

        private double[] CageMPos()
        {
            float rx, ry, rz;
            RotVals(out rx, out ry, out rz);
            float sx, sy, sz;
            ScaleVals(out sx, out sy, out sz);
            return SplatRenderer.ScaledMatrix(SplatRenderer.ObjectMatrix(rx, ry, rz), sx, sy, sz);
        }

        private void QuadViewParams(int qi, out float yaw, out float pitch, out bool uo,
            out double fit, out double cx, out double cy)
        {
            var box = quadViews[qi];
            int w = Math.Max(64, box.ClientSize.Width);
            int h = Math.Max(64, box.ClientSize.Height);
            float zoom = zoomTrackBar.Value / 100f;
            float[] yaws = new float[] { 0f, _yaw, _yaw + 90f, _yaw };
            float[] pitches = new float[] { 90f, 0f, 0f, (float)pitchNumeric.Value };
            yaw = yaws[qi];
            pitch = pitches[qi];
            uo = (qi == 3) && UoMode();
            fit = Math.Min(w, h) * 0.42 * zoom;
            cx = w * 0.5;
            cy = h * 0.52;
        }

        private PointF[] CageCornersScreen(int qi, double[] mpos)
        {
            float yaw, pitch;
            bool uo;
            double fit, cx, cy;
            QuadViewParams(qi, out yaw, out pitch, out uo, out fit, out cx, out cy);
            var pts = new PointF[8];
            for (int k = 0; k < 8; k++)
            {
                double qx, qy, qz;
                SplatRenderer.TransformPoint(mpos, _cage.CX[k], _cage.CY[k], _cage.CZ[k], out qx, out qy, out qz);
                float sx, sy;
                SplatRenderer.ProjectPoint((float)qx, (float)qy, (float)qz, yaw, pitch, uo, fit, cx, cy, out sx, out sy);
                pts[k] = new PointF(sx, sy);
            }
            return pts;
        }

        private void DrawCageOverlay(Bitmap bmp, int qi)
        {
            if (_cage == null || _model == null || bmp == null) return;
            double[] mpos = CageMPos();
            PointF[] pts = CageCornersScreen(qi, mpos);
            using (var g = Graphics.FromImage(bmp))
            {
                using (var pen = new Pen(Color.Yellow, 1f))
                {
                    for (int e = 0; e < 12; e++)
                        g.DrawLine(pen, pts[SplatCage.Edges[e, 0]], pts[SplatCage.Edges[e, 1]]);
                }
                for (int k = 0; k < 8; k++)
                {
                    Color c = (_cageDragging && k == _cageCorner) ? Color.Red : Color.Cyan;
                    float x = pts[k].X - 3.5f, y = pts[k].Y - 3.5f;
                    using (var br = new SolidBrush(c))
                        g.FillRectangle(br, x, y, 7, 7);
                    g.DrawRectangle(Pens.Black, x, y, 7, 7);
                }
            }
        }

        private int QuadIndex(object sender)
        {
            if (quadViews == null) return -1;
            for (int i = 0; i < quadViews.Length; i++)
                if (quadViews[i] == sender) return i;
            return -1;
        }

        private void QuadBox_MouseDown(object sender, MouseEventArgs e)
        {
            if (!_quad || _cage == null || _model == null) return;
            int qi = QuadIndex(sender);
            if (qi < 0 || qi > 2) return; // Top/Front/Side drag; Persp is read-only
            if (e.Button != MouseButtons.Left) return;
            try
            {
                var box = (PictureBox)sender;
                double[] mpos = CageMPos();
                PointF[] pts = CageCornersScreen(qi, mpos);
                int best = -1;
                double bd = 9 * 9;
                for (int k = 0; k < 8; k++)
                {
                    double dx = pts[k].X - e.X, dy = pts[k].Y - e.Y;
                    double d = dx * dx + dy * dy;
                    if (d < bd) { bd = d; best = k; }
                }
                if (best < 0) return;
                float yaw, pitch;
                bool uo;
                double fit, cx, cy;
                QuadViewParams(qi, out yaw, out pitch, out uo, out fit, out cx, out cy);
                double yr = yaw * Math.PI / 180.0;
                double pr = pitch * Math.PI / 180.0;
                double cosY = Math.Cos(yr), sinY = Math.Sin(yr);
                double sinP = Math.Sin(pr);
                // corner in object frame; depth axis held fixed for the drag
                double ox, oy, oz;
                SplatRenderer.TransformPoint(mpos, _cage.CX[best], _cage.CY[best], _cage.CZ[best], out ox, out oy, out oz);
                double z1 = -ox * sinY + oz * cosY;
                _cageDepth = oy * sinP + z1 * Math.Cos(pr);
                _cageGrabDX = pts[best].X - e.X;
                _cageGrabDY = pts[best].Y - e.Y;
                _cageOrigCX = _cage.CX[best];
                _cageOrigCY = _cage.CY[best];
                _cageOrigCZ = _cage.CZ[best];
                _cageObjX = ox;
                _cageObjY = oy;
                _cageObjZ = oz;
                SplatCage.Invert3x3(mpos, _cageInv);
                _cageDragBefore = _model.Points.ToArray();
                _cageMoved = false;
                _cageQuad = qi;
                _cageCorner = best;
                _cageDragging = true;
                box.Capture = true;
                box.Cursor = Cursors.SizeAll;
            }
            catch { _cageDragging = false; }
        }

        private void QuadBox_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_cageDragging || _cage == null || _model == null) return;
            if (e.Button != MouseButtons.Left || !_quad)
            {
                // Button released off-control, or quad exited mid-drag
                // (double-click): drop the drag rather than going stale.
                _cageDragging = false;
                _cageQuad = -1;
                try
                {
                    var b = sender as PictureBox;
                    if (b != null) { b.Capture = false; b.Cursor = Cursors.Default; }
                }
                catch { }
                return;
            }
            if (QuadIndex(sender) != _cageQuad) return;
            try
            {
                float yaw, pitch;
                bool uo;
                double fit, cx, cy;
                QuadViewParams(_cageQuad, out yaw, out pitch, out uo, out fit, out cx, out cy);
                double yr = yaw * Math.PI / 180.0;
                double pr = pitch * Math.PI / 180.0;
                double cosY = Math.Cos(yr), sinY = Math.Sin(yr);
                double cosP = Math.Cos(pr), sinP = Math.Sin(pr);
                // screen -> object frame, holding the grab-time depth
                double tx = e.X + _cageGrabDX, ty = e.Y + _cageGrabDY;
                double x1 = (tx - cx) / fit;
                double y2 = -(ty - cy) / fit;
                double d = _cageDepth;
                double y = y2 * cosP + d * sinP;
                double z1 = -y2 * sinP + d * cosP;
                double x = x1 * cosY - z1 * sinY;
                double z = x1 * sinY + z1 * cosY;
                // object delta -> model delta through the inverse object matrix
                double dx = x - _cageObjX, dy = y - _cageObjY, dz = z - _cageObjZ;
                int k = _cageCorner;
                double nx = _cageOrigCX + _cageInv[0] * dx + _cageInv[1] * dy + _cageInv[2] * dz;
                double ny = _cageOrigCY + _cageInv[3] * dx + _cageInv[4] * dy + _cageInv[5] * dz;
                double nz = _cageOrigCZ + _cageInv[6] * dx + _cageInv[7] * dy + _cageInv[8] * dz;
                if (nx != _cage.CX[k] || ny != _cage.CY[k] || nz != _cage.CZ[k])
                {
                    _cage.CX[k] = nx;
                    _cage.CY[k] = ny;
                    _cage.CZ[k] = nz;
                    _cageMoved = true;
                }
                _cage.Deform(_model);
                QueueRender();
            }
            catch { }
        }

        private void QuadBox_MouseUp(object sender, MouseEventArgs e)
        {
            if (!_cageDragging) return;
            try
            {
                var box = sender as PictureBox;
                if (box != null)
                {
                    box.Capture = false;
                    box.Cursor = Cursors.Default;
                }
            }
            catch { }
            _cageDragging = false;
            _cageQuad = -1;
            try
            {
                // One undo step per completed drag (Ctrl+Z walks drags back).
                if (_cageMoved && _cageDragBefore != null && _model != null &&
                    _cageDragBefore.Length == _model.Points.Count)
                {
                    int n = _model.Points.Count;
                    var idx = new int[n];
                    for (int i = 0; i < n; i++) idx[i] = i;
                    _editUndo.Add(new EditUndo { IsAdd = false, IsDelete = false, Idx = idx, Before = _cageDragBefore, IsCage = true });
                    TrimUndo();
                    MarkUndoAvailable();
                }
            }
            catch { }
            _cageDragBefore = null;
            _cageMoved = false;
            try
            {
                if (_model != null) SplatCage.RecalcBounds(_model);
                QueueRender();
            }
            catch { }
        }

        /// <summary>
        /// Drop trailing cage-drag steps (they collapse into Apply's single
        /// step, or are meaningless after Cancel's restore).
        /// </summary>
        private void DropCageUndos()
        {
            try
            {
                while (_editUndo.Count > 0 && _editUndo[_editUndo.Count - 1].IsCage)
                    _editUndo.RemoveAt(_editUndo.Count - 1);
                MarkUndoAvailable();
            }
            catch { }
        }
    }
}
