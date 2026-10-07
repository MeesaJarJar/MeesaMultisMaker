using MeesaMultisMaker.ThreeD;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MeesaMultisMaker
{
    /// <summary>
    /// Geometry-preserving AI re-texture: repaint the loaded splat cloud
    /// from the current init image (the file in the Init box / Init preview).
    /// Only splat RGB changes; XYZ, scale, rotation, opacity and normals are
    /// untouched, so the ham keeps its exact shape with a fresh skin.
    /// WYSIWYG single-view projection using the live viewport camera.
    /// </summary>
    public partial class ThreeDEditorForm
    {
        private void RetextureFromInit()
        {
            if (_model == null || _model.Count == 0)
            {
                MessageBox.Show(this, "No model loaded.", "3D Editor");
                return;
            }
            if (!EnsureSel()) return;

            string initPath = initImageTextBox != null ? initImageTextBox.Text.Trim() : string.Empty;
            if (string.IsNullOrEmpty(initPath) || !File.Exists(initPath))
            {
                MessageBox.Show(this,
                    "No init image staged.\n\nGenerate one with \"Generate Init Image (2D only)\", Browse (...), or paste with Ctrl+V first — the Init preview shows what will be used.",
                    "3D Editor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            float blend = _retexStrNum != null ? (float)_retexStrNum.Value : 1f;
            blend = Math.Max(0f, Math.Min(1f, blend));
            if (blend <= 0f)
            {
                Log("Re-texture: blend is 0, nothing to do.");
                return;
            }
            bool frontOnly = _retexFrontChk == null || _retexFrontChk.Checked;

            Bitmap src32;
            try
            {
                using (var fs = new FileStream(initPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var tmp = new Bitmap(fs))
                {
                    src32 = new Bitmap(tmp.Width, tmp.Height, PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(src32))
                        g.DrawImage(tmp, 0, 0, tmp.Width, tmp.Height);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not read init image:\r\n" + ex.Message, "3D Editor");
                return;
            }

            try
            {
                // Live viewport camera — the same framing the user sees.
                bool uo = UoMode();
                float pitch = pitchNumeric != null ? (float)pitchNumeric.Value : 26.57f;
                int w = viewport != null ? Math.Max(64, viewport.Width) : 512;
                int h = viewport != null ? Math.Max(64, viewport.Height) : 512;
                float zoom = zoomTrackBar != null ? zoomTrackBar.Value / 100f : 1f;
                double[] robj = EditRobjScaled();

                int n = _model.Count;
                var sx = new float[n];
                var sy = new float[n];
                var dp = new float[n];
                SplatSelect.ProjectAll(_model, _yaw, pitch, uo, robj, zoom, w, h, sx, sy, dp);

                SplatSelect.DepthMap depth = null;
                float depthTol = SplatOps.EstimateSpacing(_model) * 3f;
                if (frontOnly)
                    depth = new SplatSelect.DepthMap(_model, _yaw, pitch, uo, robj, zoom, w, h, 4);

                // Lock source once for fast sampling.
                var rect = new Rectangle(0, 0, src32.Width, src32.Height);
                var data = src32.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                byte[] buf = null;
                int stride = 0;
                try
                {
                    stride = data.Stride;
                    buf = new byte[Math.Abs(stride) * src32.Height];
                    Marshal.Copy(data.Scan0, buf, 0, buf.Length);
                }
                finally { src32.UnlockBits(data); }

                int srcW = src32.Width, srcH = src32.Height;
                var idx = SplatOps.Affected(_model, UseSel());
                if (idx.Count == 0)
                {
                    Log("Re-texture: nothing in scope.");
                    return;
                }
                PushMutateUndo(idx);

                var pts = _model.Points;
                int painted = 0, skippedBg = 0, skippedOff = 0, skippedOcc = 0;
                foreach (int i in idx)
                {
                    float px = sx[i], py = sy[i];
                    float u = px / Math.Max(1, w);
                    float v = py / Math.Max(1, h);
                    if (u < 0f || v < 0f || u >= 1f || v >= 1f)
                    {
                        skippedOff++;
                        continue;
                    }
                    if (frontOnly && depth != null && !depth.IsFront(px, py, dp[i], depthTol))
                    {
                        skippedOcc++;
                        continue;
                    }
                    SampleBilinear(buf, stride, srcW, srcH, u * (srcW - 1), v * (srcH - 1),
                        out byte sr, out byte sg, out byte sb, out byte sa);
                    if (sa < 16)
                    {
                        skippedBg++;
                        continue;
                    }
                    // Init images are object-on-black: near-black source texels
                    // are background, not skin — keep the old color there so
                    // silhouette edges don't get a black fringe.
                    int lum = Math.Max(sr, Math.Max(sg, sb));
                    if (lum < 10)
                    {
                        skippedBg++;
                        continue;
                    }
                    var p = pts[i];
                    if (blend >= 1f)
                    {
                        p.R = sr; p.G = sg; p.B = sb;
                    }
                    else
                    {
                        p.R = (byte)(p.R + (sr - p.R) * blend);
                        p.G = (byte)(p.G + (sg - p.G) * blend);
                        p.B = (byte)(p.B + (sb - p.B) * blend);
                    }
                    pts[i] = p;
                    painted++;
                }

                AfterEdit(string.Format(
                    "Re-texture: repainted {0} splats from {1} ({2}, {3}, blend {4:0.00}). Skipped: {5} bg/black, {6} off-screen, {7} occluded.",
                    painted, Path.GetFileName(initPath), Scope(),
                    frontOnly ? "front-only" : "all depths", blend,
                    skippedBg, skippedOff, skippedOcc));
                Log("Tip: orbit to match the init viewpoint (e.g. S front) before re-texturing for tightest alignment.");
            }
            catch (Exception ex)
            {
                Log("Re-texture failed: " + ex.Message);
            }
            finally
            {
                try { src32.Dispose(); }
                catch { }
            }
        }

        private static void SampleBilinear(byte[] buf, int stride, int sw, int sh,
            float fx, float fy, out byte r, out byte g, out byte b, out byte a)
        {
            if (fx < 0) fx = 0;
            if (fy < 0) fy = 0;
            if (fx > sw - 1) fx = sw - 1;
            if (fy > sh - 1) fy = sh - 1;
            int x0 = (int)fx, y0 = (int)fy;
            int x1 = Math.Min(sw - 1, x0 + 1), y1 = Math.Min(sh - 1, y0 + 1);
            float tx = fx - x0, ty = fy - y0;

            int o00 = y0 * stride + x0 * 4;
            int o10 = y0 * stride + x1 * 4;
            int o01 = y1 * stride + x0 * 4;
            int o11 = y1 * stride + x1 * 4;

            // BGRA order in memory.
            float b00 = buf[o00], g00 = buf[o00 + 1], r00 = buf[o00 + 2], a00 = buf[o00 + 3];
            float b10 = buf[o10], g10 = buf[o10 + 1], r10 = buf[o10 + 2], a10 = buf[o10 + 3];
            float b01 = buf[o01], g01 = buf[o01 + 1], r01 = buf[o01 + 2], a01 = buf[o01 + 3];
            float b11 = buf[o11], g11 = buf[o11 + 1], r11 = buf[o11 + 2], a11 = buf[o11 + 3];

            float w00 = (1 - tx) * (1 - ty), w10 = tx * (1 - ty);
            float w01 = (1 - tx) * ty, w11 = tx * ty;
            b = (byte)Math.Max(0, Math.Min(255, b00 * w00 + b10 * w10 + b01 * w01 + b11 * w11 + 0.5f));
            g = (byte)Math.Max(0, Math.Min(255, g00 * w00 + g10 * w10 + g01 * w01 + g11 * w11 + 0.5f));
            r = (byte)Math.Max(0, Math.Min(255, r00 * w00 + r10 * w10 + r01 * w01 + r11 * w11 + 0.5f));
            a = (byte)Math.Max(0, Math.Min(255, a00 * w00 + a10 * w10 + a01 * w01 + a11 * w11 + 0.5f));
        }
    }
}
