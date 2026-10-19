using MeesaMultisMaker.Utils;
using MeesaMultisMaker.AI;
using MeesaMultisMaker.ComfyUI;
using MeesaMultisMaker.Controls;
using MeesaMultisMaker.Mul;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MeesaMultisMaker
{
    public partial class Form1
    {
        private ImageEditingForm _imageEditingForm;
        private bool _desatEyedropperArmed;
        private DesaturatePickKind _desatEyedropperKind = DesaturatePickKind.Targets;
        private Cursor _desatPrevCursor;

        // SAM Select gate (canvas host). Per-object buckets: each PlacedObject
        // reference owns its point list + live mask + run-id/CTS. Masks survive
        // selection changes; Esc keeps all; Clear drops all.
        private bool _samArmed;
        private sealed class CanvasSamBucket
        {
            public PlacedObject Target;
            public System.Collections.Generic.List<(int x, int y, int label)> Points = new System.Collections.Generic.List<(int x, int y, int label)>();
            public Bitmap Mask;
            public string Key;
            public int RunId;
            public System.Threading.CancellationTokenSource Cts;
            public long Seq;
            public bool Busy;
        }
        private readonly System.Collections.Generic.Dictionary<PlacedObject, CanvasSamBucket> _samBuckets = new System.Collections.Generic.Dictionary<PlacedObject, CanvasSamBucket>();
        private long _samSeq;
        private const int MaxSamBuckets = 32;
        private Cursor _samPrevCursor;

        /// <summary>
        /// Initialize the Image Editing Form (floating window)
        /// </summary>
        private void InitializeImageEditingPanel()
        {
            // Don't create it yet - only when user opens it
            _imageEditingForm = null;
            _desatEyedropperArmed = false;
            _samArmed = false;
        }

        /// <summary>
        /// Eyedropper arming from the floating Image Editing window: next left-click
        /// samples instead of selecting/dragging. Esc or re-clicking Pick cancels.
        /// </summary>
        private void ImageEditingEyedropperRequested(object sender, EyedropperRequestedEventArgs e)
        {
            _desatEyedropperArmed = true;
            _desatEyedropperKind = e?.Kind ?? DesaturatePickKind.Targets;
            try
            {
                if (designPictureBox != null)
                {
                    _desatPrevCursor = designPictureBox.Cursor;
                    designPictureBox.Cursor = Cursors.Cross;
                    try { designPictureBox.Focus(); } catch { }
                }
            }
            catch { }
            string what = _desatEyedropperKind == DesaturatePickKind.Protected ? "protected colors" : "target colors";
            _imageEditingForm?.SetStatus($"Click canvas to add {what} — Esc done",
                Color.FromArgb(100, 200, 255));
        }

        private void ImageEditingEyedropperCancelled(object sender, EventArgs e)
        {
            DisarmCanvasEyedropper("Eyedropper cancelled", Color.FromArgb(100, 200, 255));
        }

        private void DisarmCanvasEyedropper(string status, Color? color)
        {
            _desatEyedropperArmed = false;
            _desatEyedropperKind = DesaturatePickKind.Targets;
            try
            {
                if (designPictureBox != null)
                    designPictureBox.Cursor = _desatPrevCursor ?? Cursors.Default;
            }
            catch { }
            _desatPrevCursor = null;
            if (status != null)
                _imageEditingForm?.SetStatus(status, color ?? Color.FromArgb(100, 200, 255));
        }

        private void CancelCanvasEyedropper()
        {
            try { _imageEditingForm?.CancelEyedropper(); }
            catch { _desatEyedropperArmed = false; }
            if (_desatEyedropperArmed)
                DisarmCanvasEyedropper("Eyedropper cancelled", Color.FromArgb(100, 200, 255));
        }

        private void ImageEditingSamRequested(object sender, EventArgs e)
        {
            string missing = Utils.SamService.GetMissingPath();
            if (missing != null)
            {
                _imageEditingForm?.SetStatus("SAM files missing: " + missing, Color.Orange);
                try { _imageEditingForm?.CancelSamArming(); } catch { _samArmed = false; }
                _samArmed = false;
                return;
            }
            _samArmed = true;
            try
            {
                if (designPictureBox != null)
                {
                    _samPrevCursor = designPictureBox.Cursor;
                    designPictureBox.Cursor = Cursors.Cross;
                    try { designPictureBox.Focus(); } catch { }
                }
            }
            catch { }
            try { UpdateCanvasSamInfo(); } catch { }
            _imageEditingForm?.SetStatus("SAM armed: L-click +FG, R/Sft-click +BG — Esc done",
                Color.FromArgb(100, 200, 255));
        }

        private void ImageEditingSamCancelled(object sender, EventArgs e)
        {
            // Esc / re-click / Clear: disarm arming but KEEP all masks.
            DisarmCanvasSam(null);
        }

        private void ImageEditingSamClear(object sender, EventArgs e)
        {
            ClearCanvasSam("SAM cleared", Color.FromArgb(100, 200, 255));
        }

        private void ImageEditingSamShowChanged(object sender, EventArgs e)
        {
            try { designPictureBox?.Invalidate(); } catch { }
        }

        private void DisarmCanvasSam(string status)
        {
            _samArmed = false;
            try
            {
                if (designPictureBox != null)
                    designPictureBox.Cursor = _samPrevCursor ?? Cursors.Default;
            }
            catch { }
            _samPrevCursor = null;
            if (status != null)
                _imageEditingForm?.SetStatus(status, Color.FromArgb(100, 200, 255));
            try { designPictureBox?.Invalidate(); } catch { }
        }

        private void CancelCanvasSamArming()
        {
            try { _imageEditingForm?.CancelSamArming(); }
            catch { _samArmed = false; }
            if (_samArmed)
                DisarmCanvasSam("SAM disarmed — masks kept");
        }

        private void ClearCanvasSam(string status, Color? color)
        {
            try
            {
                foreach (var kv in _samBuckets)
                {
                    var b = kv.Value;
                    if (b == null) continue;
                    try { b.Cts?.Cancel(); } catch { }
                    try { b.Cts?.Dispose(); } catch { }
                    b.Cts = null;
                    try { b.Mask?.Dispose(); } catch { }
                    b.Mask = null;
                    b.Busy = false;
                }
            }
            catch { }
            try { _samBuckets.Clear(); } catch { }
            _samArmed = false;
            try
            {
                if (designPictureBox != null)
                    designPictureBox.Cursor = _samPrevCursor ?? Cursors.Default;
            }
            catch { }
            _samPrevCursor = null;
            try { _imageEditingForm?.SetSamInfo(0, false, false); } catch { }
            if (status != null)
                _imageEditingForm?.SetStatus(status, color ?? Color.FromArgb(100, 200, 255));
            try { designPictureBox?.Invalidate(); } catch { }
            RefreshCanvasSamPreview();
        }

        private static string SamKeyFor(PlacedObject obj, int w, int h)
        {
            try { return (obj?.GraphicId ?? "?") + "|" + w + "x" + h; }
            catch { return "?|" + w + "x" + h; }
        }

        private Bitmap GetCanvasSamSource(PlacedObject obj)
        {
            try
            {
                var pre = obj?.PreEditImage as Bitmap;
                if (pre != null) return pre;
                return obj?.Image as Bitmap;
            }
            catch { return null; }
        }

        private Bitmap GetCanvasSamMaskFor(PlacedObject obj, Bitmap source)
        {
            if (obj == null || source == null) return null;
            try
            {
                CanvasSamBucket bucket = null;
                if (!_samBuckets.TryGetValue(obj, out bucket) || bucket == null) return null;
                if (bucket.Mask == null) return null;
                try { if (!placedObjects.Contains(obj)) return null; }
                catch { return null; }
                try
                {
                    if (bucket.Mask.Width != source.Width || bucket.Mask.Height != source.Height) return null;
                }
                catch { return null; }
                try
                {
                    if (bucket.Key != SamKeyFor(obj, source.Width, source.Height)) return null;
                }
                catch { return null; }
                return bucket.Mask;
            }
            catch { return null; }
        }

        private void GetCanvasSamTotals(out int totalPoints, out bool hasAnyMask, out bool anyBusy)
        {
            totalPoints = 0;
            hasAnyMask = false;
            anyBusy = false;
            try
            {
                foreach (var kv in _samBuckets)
                {
                    var b = kv.Value;
                    if (b == null) continue;
                    try { if (b.Target == null || !placedObjects.Contains(b.Target)) continue; }
                    catch { continue; }
                    try { totalPoints += b.Points != null ? b.Points.Count : 0; }
                    catch { }
                    try { if (b.Mask != null) hasAnyMask = true; }
                    catch { }
                    try { if (b.Busy) anyBusy = true; }
                    catch { }
                }
            }
            catch { }
        }

        private void UpdateCanvasSamInfo()
        {
            try
            {
                int pts;
                bool hasMask;
                bool busy;
                GetCanvasSamTotals(out pts, out hasMask, out busy);
                try { _imageEditingForm?.SetSamInfo(pts, hasMask, busy); } catch { }
            }
            catch { }
        }

        private void PruneDeadSamBuckets()
        {
            try
            {
                if (_samBuckets.Count == 0) return;
                System.Collections.Generic.List<PlacedObject> dead = null;
                try
                {
                    foreach (var kv in _samBuckets)
                    {
                        try
                        {
                            if (kv.Key == null || kv.Value == null || kv.Value.Target == null || !placedObjects.Contains(kv.Key))
                            {
                                if (dead == null) dead = new System.Collections.Generic.List<PlacedObject>();
                                dead.Add(kv.Key);
                            }
                        }
                        catch { }
                    }
                }
                catch { }
                if (dead == null) return;
                foreach (var k in dead)
                {
                    try
                    {
                        CanvasSamBucket b = null;
                        try { _samBuckets.TryGetValue(k, out b); } catch { }
                        try { _samBuckets.Remove(k); } catch { }
                        if (b != null)
                        {
                            try { b.Cts?.Cancel(); } catch { }
                            try { b.Cts?.Dispose(); } catch { }
                            b.Cts = null;
                            try { b.Mask?.Dispose(); } catch { }
                            b.Mask = null;
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void EnforceSamBucketCap()
        {
            try
            {
                if (_samBuckets.Count <= MaxSamBuckets) return;
                var ordered = _samBuckets.OrderBy(kv => kv.Value != null ? kv.Value.Seq : 0).ToList();
                int toDrop = _samBuckets.Count - MaxSamBuckets;
                for (int i = 0; i < toDrop && i < ordered.Count; i++)
                {
                    var k = ordered[i].Key;
                    var b = ordered[i].Value;
                    try { _samBuckets.Remove(k); } catch { }
                    if (b != null)
                    {
                        try { b.Cts?.Cancel(); } catch { }
                        try { b.Cts?.Dispose(); } catch { }
                        try { b.Mask?.Dispose(); } catch { }
                    }
                }
            }
            catch { }
        }

        private void RemoveCanvasSamBucketFor(PlacedObject obj)
        {
            if (obj == null) return;
            try
            {
                CanvasSamBucket b = null;
                if (!_samBuckets.TryGetValue(obj, out b)) return;
                try { _samBuckets.Remove(obj); } catch { }
                if (b != null)
                {
                    try { b.Cts?.Cancel(); } catch { }
                    try { b.Cts?.Dispose(); } catch { }
                    b.Cts = null;
                    try { b.Mask?.Dispose(); } catch { }
                    b.Mask = null;
                }
                try { UpdateCanvasSamInfo(); } catch { }
                try { designPictureBox?.Invalidate(); } catch { }
            }
            catch { }
        }

        private void DropCanvasSamIfTargetChanged(PlacedObject current)
        {
            // Multi-object: selection changes never drop masks. Only prune dead refs.
            try { PruneDeadSamBuckets(); } catch { }
        }

        /// <summary>
        /// Topmost-first sampling via GetPaintOrder. Reuses the same draw-position
        /// math as DesignPictureBox_Paint: plain translation+scale path is exact,
        /// rotation/flip matrix path goes through Matrix.Invert, skew path uses a
        /// best-effort plain-rect fallback. Fully-transparent pixels are skipped
        /// (caller stays armed).
        /// </summary>
        private bool TryPickDesatColorFromCanvas(Point mouse, out Color picked)
        {
            picked = Color.Empty;
            int minZFilter = minZTrackBar?.Value ?? -127;
            int maxZFilter = maxZTrackBar?.Value ?? 127;

            foreach (var obj in GetPaintOrder().Reverse())
            {
                if (obj.Hidden) continue;
                if (!IsImageUsable(obj.Image)) continue;
                if (obj.Z < minZFilter || obj.Z > maxZFilter) continue;

                float drawX, drawY, w, h;
                GetObjectDrawPosition(obj, out drawX, out drawY, out w, out h);
                if (w <= 0 || h <= 0) continue;

                float imgXF, imgYF;
                if (obj.HasSkew)
                {
                    // Best-effort plain-rect fallback for the skew path.
                    float objScale = obj.Scale * zoom;
                    if (objScale <= 0) continue;
                    imgXF = (mouse.X - drawX) / objScale;
                    imgYF = (mouse.Y - drawY) / objScale;
                }
                else if (obj.Rotation != 0f || obj.FlipHorizontal || obj.FlipVertical)
                {
                    float centerX = drawX + w / 2f;
                    float centerY = drawY + h / 2f;
                    var pts = new PointF[] { new PointF(mouse.X, mouse.Y) };
                    try
                    {
                        using (var matrix = new Matrix())
                        {
                            matrix.Translate(centerX, centerY);
                            if (obj.Rotation != 0f) matrix.Rotate(obj.Rotation);
                            if (obj.FlipHorizontal || obj.FlipVertical)
                                matrix.Scale(obj.FlipHorizontal ? -1 : 1, obj.FlipVertical ? -1 : 1);
                            matrix.Translate(-centerX, -centerY);
                            matrix.Invert();
                            matrix.TransformPoints(pts);
                        }
                    }
                    catch { continue; }
                    float objScale = obj.Scale * zoom;
                    if (objScale <= 0) continue;
                    imgXF = (pts[0].X - drawX) / objScale;
                    imgYF = (pts[0].Y - drawY) / objScale;
                }
                else
                {
                    float objScale = obj.Scale * zoom;
                    if (objScale <= 0) continue;
                    imgXF = (mouse.X - drawX) / objScale;
                    imgYF = (mouse.Y - drawY) / objScale;
                }

                int imgX = (int)Math.Floor(imgXF);
                int imgY = (int)Math.Floor(imgYF);
                int iw, ih;
                try { iw = obj.Image.Width; ih = obj.Image.Height; }
                catch { continue; }
                if (imgX < 0 || imgY < 0 || imgX >= iw || imgY >= ih) continue;

                Color px;
                try
                {
                    var bmp = obj.Image as Bitmap;
                    if (bmp != null)
                        px = bmp.GetPixel(imgX, imgY);
                    else
                    {
                        using (var tmp = new Bitmap(obj.Image))
                            px = tmp.GetPixel(
                                Math.Max(0, Math.Min(tmp.Width - 1, imgX)),
                                Math.Max(0, Math.Min(tmp.Height - 1, imgY)));
                    }
                }
                catch { continue; }

                if (px.A == 0) continue; // transparent: look through to art underneath
                picked = Color.FromArgb(px.R, px.G, px.B);
                return true;
            }

            return false;
        }

        /// <summary>
        /// SAM point mapping: same draw-position math as the eyedropper
        /// (GetObjectDrawPosition plain exact, rotation/flip matrix inverse,
        /// skew best-effort) but returns obj.Image pixel coords and the hit
        /// object. Transparent pixels are ALLOWED (SAM needs bg points too).
        /// </summary>
        private bool TryPickSamPointFromCanvas(Point mouse, out PlacedObject target, out int imgX, out int imgY)
        {
            target = null;
            imgX = 0;
            imgY = 0;
            int minZFilter = minZTrackBar?.Value ?? -127;
            int maxZFilter = maxZTrackBar?.Value ?? 127;

            foreach (var obj in GetPaintOrder().Reverse())
            {
                if (obj.Hidden) continue;
                if (!IsImageUsable(obj.Image)) continue;
                if (obj.Z < minZFilter || obj.Z > maxZFilter) continue;

                float drawX, drawY, w, h;
                GetObjectDrawPosition(obj, out drawX, out drawY, out w, out h);
                if (w <= 0 || h <= 0) continue;

                float imgXF, imgYF;
                if (obj.HasSkew)
                {
                    float objScale = obj.Scale * zoom;
                    if (objScale <= 0) continue;
                    imgXF = (mouse.X - drawX) / objScale;
                    imgYF = (mouse.Y - drawY) / objScale;
                }
                else if (obj.Rotation != 0f || obj.FlipHorizontal || obj.FlipVertical)
                {
                    float centerX = drawX + w / 2f;
                    float centerY = drawY + h / 2f;
                    var pts = new PointF[] { new PointF(mouse.X, mouse.Y) };
                    try
                    {
                        using (var matrix = new Matrix())
                        {
                            matrix.Translate(centerX, centerY);
                            if (obj.Rotation != 0f) matrix.Rotate(obj.Rotation);
                            if (obj.FlipHorizontal || obj.FlipVertical)
                                matrix.Scale(obj.FlipHorizontal ? -1 : 1, obj.FlipVertical ? -1 : 1);
                            matrix.Translate(-centerX, -centerY);
                            matrix.Invert();
                            matrix.TransformPoints(pts);
                        }
                    }
                    catch { continue; }
                    float objScale = obj.Scale * zoom;
                    if (objScale <= 0) continue;
                    imgXF = (pts[0].X - drawX) / objScale;
                    imgYF = (pts[0].Y - drawY) / objScale;
                }
                else
                {
                    float objScale = obj.Scale * zoom;
                    if (objScale <= 0) continue;
                    imgXF = (mouse.X - drawX) / objScale;
                    imgYF = (mouse.Y - drawY) / objScale;
                }

                int ix = (int)Math.Floor(imgXF);
                int iy = (int)Math.Floor(imgYF);
                int iw, ih;
                try { iw = obj.Image.Width; ih = obj.Image.Height; }
                catch { continue; }
                if (ix < 0 || iy < 0 || ix >= iw || iy >= ih) continue;

                target = obj;
                imgX = ix;
                imgY = iy;
                return true;
            }

            return false;
        }

        private void AddCanvasSamPointAndRerun(PlacedObject target, int imgX, int imgY, int label)
        {
            string missing = Utils.SamService.GetMissingPath();
            if (missing != null)
            {
                _imageEditingForm?.SetStatus("SAM files missing: " + missing, Color.Orange);
                return;
            }
            if (target == null) return;
            try { PruneDeadSamBuckets(); } catch { }
            Bitmap segSource = GetCanvasSamSource(target);
            if (segSource == null)
            {
                _imageEditingForm?.SetStatus("SAM: no image to segment", Color.Orange);
                return;
            }
            string key = SamKeyFor(target, segSource.Width, segSource.Height);
            CanvasSamBucket bucket = null;
            if (!_samBuckets.TryGetValue(target, out bucket) || bucket == null)
            {
                bucket = new CanvasSamBucket { Target = target, Key = key, Seq = ++_samSeq };
                _samBuckets[target] = bucket;
                try { EnforceSamBucketCap(); } catch { }
                if (!_samBuckets.TryGetValue(target, out bucket) || bucket == null) return;
            }
            else if (bucket.Key != key)
            {
                // Same object, new source size: reset this bucket only.
                try { bucket.Cts?.Cancel(); } catch { }
                try { bucket.Cts?.Dispose(); } catch { }
                bucket.Cts = null;
                bucket.RunId++;
                bucket.Busy = false;
                try { bucket.Points.Clear(); } catch { }
                try { bucket.Mask?.Dispose(); } catch { }
                bucket.Mask = null;
                bucket.Key = key;
            }
            bucket.Points.Add((imgX, imgY, label));
            try { UpdateCanvasSamInfo(); } catch { }
            _ = RerunCanvasSamAsync(target);
        }

        private async Task RerunCanvasSamAsync(PlacedObject target)
        {
            CanvasSamBucket bucket = null;
            try
            {
                if (target == null || !_samBuckets.TryGetValue(target, out bucket) || bucket == null) return;
            }
            catch { return; }
            Bitmap segSource = GetCanvasSamSource(target);
            if (segSource == null) return;
            string curKey = null;
            try { curKey = SamKeyFor(target, segSource.Width, segSource.Height); }
            catch { return; }
            if (bucket.Key != curKey)
            {
                try { bucket.Points.Clear(); } catch { }
                try { bucket.Mask?.Dispose(); } catch { }
                bucket.Mask = null;
                bucket.Key = curKey;
                try { UpdateCanvasSamInfo(); } catch { }
                return;
            }
            System.Collections.Generic.List<(int x, int y, int label)> pts = null;
            try { pts = bucket.Points.Select(p => (p.x, p.y, p.label)).ToList(); }
            catch { return; }
            if (pts == null || pts.Count == 0) return;
            Bitmap segCopy;
            try { segCopy = new Bitmap(segSource); }
            catch { return; }

            int runId = ++bucket.RunId;
            try { bucket.Cts?.Cancel(); } catch { }
            try { bucket.Cts?.Dispose(); } catch { }
            var cts = new System.Threading.CancellationTokenSource();
            bucket.Cts = cts;
            bucket.Busy = true;
            string keyAtStart = bucket.Key;
            string gidAtStart = "?";
            try { gidAtStart = target.GraphicId ?? "?"; } catch { }
            try { UpdateCanvasSamInfo(); } catch { }
            _imageEditingForm?.SetStatus("SAM segmenting…", Color.FromArgb(100, 200, 255));
            Bitmap mask = null;
            try
            {
                mask = await Utils.SamService.RunSamAsync(segCopy, pts, cts.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                try
                {
                    CanvasSamBucket cur = null;
                    if (_samBuckets.TryGetValue(target, out cur) && ReferenceEquals(cur, bucket) && runId == bucket.RunId)
                    {
                        bucket.Busy = false;
                        try { UpdateCanvasSamInfo(); } catch { }
                        _imageEditingForm?.SetStatus("SAM failed (" + gidAtStart + "): " + ex.Message, Color.Orange);
                    }
                }
                catch { }
                return;
            }
            finally
            {
                try { segCopy.Dispose(); } catch { }
            }
            try
            {
                CanvasSamBucket cur = null;
                if (!_samBuckets.TryGetValue(target, out cur) || !ReferenceEquals(cur, bucket))
                {
                    try { mask?.Dispose(); } catch { }
                    return; // bucket cleared/evicted mid-run.
                }
                if (runId != bucket.RunId)
                {
                    try { mask?.Dispose(); } catch { }
                    return; // stale run: keep last completed mask live.
                }
                if (bucket.Key != keyAtStart)
                {
                    try { mask?.Dispose(); } catch { }
                    return;
                }
                Bitmap live = GetCanvasSamSource(target);
                try
                {
                    if (live == null || mask == null || mask.Width != live.Width || mask.Height != live.Height)
                    {
                        try { mask?.Dispose(); } catch { }
                        bucket.Busy = false;
                        _imageEditingForm?.SetStatus("SAM mask size mismatch — ignored", Color.Orange);
                        try { UpdateCanvasSamInfo(); } catch { }
                        return;
                    }
                }
                catch
                {
                    try { mask?.Dispose(); } catch { }
                    try { bucket.Busy = false; } catch { }
                    return;
                }
                try { bucket.Mask?.Dispose(); } catch { }
                bucket.Mask = mask;
                bucket.Busy = false;
                int n = 0;
                try { n = bucket.Points.Count; } catch { }
                try { UpdateCanvasSamInfo(); } catch { }
                _imageEditingForm?.SetStatus("SAM mask updated for " + gidAtStart + " (" + n + " pts) — desat/snap gated", Color.Green);
                try { designPictureBox?.Invalidate(); } catch { }
                RefreshCanvasSamPreview();
            }
            catch
            {
                try { mask?.Dispose(); } catch { }
                try { bucket.Busy = false; } catch { }
            }
        }

        private ImageEffectEventArgs BuildCanvasEffectArgsFromPanel()
        {
            try
            {
                var p = _imageEditingForm?.EditingPanel;
                if (p == null) return null;
                return new ImageEffectEventArgs
                {
                    Brightness = p.Brightness,
                    Contrast = p.Contrast,
                    Hue = p.Hue,
                    Saturation = p.Saturation,
                    PixelSize = p.PixelSize,
                    PixelizeEnabled = p.PixelizeEnabled,
                    PaletteColors = p.PaletteColors,
                    NoiseIntensity = p.NoiseIntensity,
                    DitherLevels = p.DitherLevels,
                    EdgeDarkening = p.EdgeDarkening,
                    ColorBands = p.ColorBands,
                    FillHoles = p.FillHoles,
                    RemoveBackground = p.RemoveBackgroundEnabled,
                    BackgroundMode = p.BackgroundMode,
                    BackgroundColor = p.BackgroundColor,
                    BackgroundThreshold = p.BackgroundThreshold,
                    DesaturateEnabled = p.DesaturateEnabled,
                    DesaturateTargets = new System.Collections.Generic.List<Color>(p.DesaturateTargets),
                    DesaturateProtected = new System.Collections.Generic.List<Color>(p.DesaturateProtected),
                    DesaturateTolerance = p.DesaturateTolerance,
                    DespeckleMinSize = p.DespeckleMinSize,
                    SnapNearGray = p.SnapNearGrayEnabled,
                    SnapTolerance = p.SnapTolerance
                };
            }
            catch { return null; }
        }

        private void RefreshCanvasSamPreview()
        {
            try
            {
                if (_imageEditingForm == null || selectedObjects.Count == 0) return;
                var args = BuildCanvasEffectArgsFromPanel();
                if (args == null) return;
                ImageEditingForm_RealTimePreview(this, args);
            }
            catch { }
        }

    }
}

namespace MeesaMultisMaker
{
    public class TextureEditorForm : Form
    {
        private string mulFolder;
        private List<int> validTextureIds = new List<int>();
        private List<int> filteredTextureIds = new List<int>();
        private Dictionary<int, Bitmap> textureCache = new Dictionary<int, Bitmap>();
        private Dictionary<int, Bitmap> _originalTextureImages = new Dictionary<int, Bitmap>();
        private Dictionary<int, Bitmap> _modifiedTextureImages = new Dictionary<int, Bitmap>();
        private Dictionary<int, Bitmap> _rawAIOutputImages = new Dictionary<int, Bitmap>();

        private int selectedTextureId = -1;
        private bool _showingOriginal = false;
        private Bitmap _previewBitmap;
        private ComfyUIClient _comfyClient;
        private System.Threading.CancellationTokenSource _textureGenCts;

        private SplitContainer mainSplit;
        private ListView textureListView;
        private ImageList textureImageList;
        private TextBox searchBox;
        private PictureBox previewBox;
        private Label statusLabel;
        private Label infoLabel;
        private AISettingsPanel aiSettingsPanel;
        private ImageEditingPanel imageEditingPanel;
        private bool _texEyedropperArmed;
        private DesaturatePickKind _texEyedropperKind = DesaturatePickKind.Targets;
        private Cursor _texPrevCursor;

        // SAM Select gate (texture host). Keyed to texture id + WxH.
        private bool _texSamArmed;
        private readonly System.Collections.Generic.List<(int x, int y, int label)> _texSamPoints = new System.Collections.Generic.List<(int x, int y, int label)>();
        private Bitmap _texSamMask;
        private int _texSamTextureId = -1;
        private string _texSamKey;
        private int _texSamRunId;
        private System.Threading.CancellationTokenSource _texSamCts;

        public TextureEditorForm()
        {
            InitializeComponent();
            BeginLoadMulFolder();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _previewBitmap?.Dispose();
                try { _texSamMask?.Dispose(); } catch { }
                _texSamMask = null;
                try { _texSamCts?.Cancel(); } catch { }
                try { _texSamCts?.Dispose(); } catch { }

                foreach (var bmp in textureCache.Values) bmp?.Dispose();
                foreach (var bmp in _originalTextureImages.Values) bmp?.Dispose();
                foreach (var bmp in _modifiedTextureImages.Values) bmp?.Dispose();
                foreach (var bmp in _rawAIOutputImages.Values) bmp?.Dispose();

                textureCache.Clear();
                _originalTextureImages.Clear();
                _modifiedTextureImages.Clear();
                _rawAIOutputImages.Clear();
            }

            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.Text = "Texture Editor - MeesaMultisMaker";
            this.Width = 1600;
            this.Height = 900;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = HolographicTheme.DarkBackground;
            this.KeyPreview = true;
            this.KeyDown += TextureEditorForm_KeyDown;

            mainSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 300,
                FixedPanel = FixedPanel.Panel1,
                BackColor = HolographicTheme.DarkBackground
            };
            HolographicTheme.ApplyToSplitContainer(mainSplit);

            var leftPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = HolographicTheme.PanelBackground
            };

            var paletteLabel = new Label
            {
                Text = "LAND TEXTURE PALETTE",
                Dock = DockStyle.Top,
                Height = 25,
                ForeColor = HolographicTheme.CyanAccent,
                BackColor = HolographicTheme.ControlBackground,
                Font = new Font("Consolas", 10, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter
            };

            searchBox = new TextBox
            {
                Dock = DockStyle.Top,
                Height = 25,
                Text = "Search texture id...",
                ForeColor = HolographicTheme.TextMuted
            };
            HolographicTheme.ApplyToTextBox(searchBox);
            searchBox.Enter += (s, e) =>
            {
                if (searchBox.Text == "Search texture id..." && searchBox.ForeColor == HolographicTheme.TextMuted)
                {
                    searchBox.Text = "";
                    searchBox.ForeColor = HolographicTheme.TextPrimary;
                }
            };
            searchBox.Leave += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(searchBox.Text))
                {
                    searchBox.Text = "Search texture id...";
                    searchBox.ForeColor = HolographicTheme.TextMuted;
                }
            };
            searchBox.TextChanged += SearchBox_TextChanged;

            textureListView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.LargeIcon,
                BackColor = HolographicTheme.InputBackground,
                ForeColor = HolographicTheme.TextPrimary,
                BorderStyle = BorderStyle.None,
                VirtualMode = true,
                VirtualListSize = 0,
                OwnerDraw = true,
                MultiSelect = true
            };
            textureImageList = new ImageList
            {
                ImageSize = new Size(80, 80),
                ColorDepth = ColorDepth.Depth32Bit
            };
            textureListView.LargeImageList = textureImageList;
            textureListView.RetrieveVirtualItem += TextureListView_RetrieveVirtualItem;
            textureListView.DrawItem += TextureListView_DrawItem;
            textureListView.CacheVirtualItems += TextureListView_CacheVirtualItems;
            textureListView.SelectedIndexChanged += TextureListView_SelectedIndexChanged;

            leftPanel.Controls.Add(textureListView);
            leftPanel.Controls.Add(searchBox);
            leftPanel.Controls.Add(paletteLabel);

            var rightPanel = new Panel { Dock = DockStyle.Fill, BackColor = HolographicTheme.DarkBackground };

            var topPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 68,
                BackColor = HolographicTheme.ControlBackground,
                Padding = new Padding(8)
            };

            var replaceButton = CreateThemedButton("Replace From File", 10, 8, 130, ButtonStyle.Accent);
            replaceButton.Click += (s, e) => ReplaceSelectedTextureFromFile();
            topPanel.Controls.Add(replaceButton);

            var restoreButton = CreateThemedButton("Restore", 145, 8, 70, ButtonStyle.Warning);
            restoreButton.Click += (s, e) => RestoreSelectedOriginal();
            topPanel.Controls.Add(restoreButton);

            var saveButton = CreateThemedButton("Save To MUL", 220, 8, 95, ButtonStyle.Success);
            saveButton.Click += (s, e) => SaveTexturesToMul();
            topPanel.Controls.Add(saveButton);

            infoLabel = new Label
            {
                Left = 10,
                Top = 40,
                Width = 500,
                Height = 20,
                ForeColor = HolographicTheme.TextSecondary,
                Font = new Font("Consolas", 9f),
                Text = "No texture selected"
            };
            topPanel.Controls.Add(infoLabel);

            var sidebarTabs = new TabControl
            {
                Dock = DockStyle.Right,
                Width = 360,
                Appearance = TabAppearance.Normal
            };

            var aiTab = new TabPage("AI") { BackColor = HolographicTheme.PanelBackground };
            aiSettingsPanel = new AISettingsPanel { Dock = DockStyle.Fill };
            aiSettingsPanel.AIRegenSelectedClicked += (s, e) => RegenerateSelectedTexture();
            aiSettingsPanel.RevertAIChangesClicked += (s, e) => RevertAIChanges();
            aiSettingsPanel.OldNewClicked += (s, e) => ToggleOldNew();
            aiSettingsPanel.BlackPixelSettingsChanged += (s, e) => ReapplyBlackPixelRemoval();
            aiSettingsPanel.SaveToMulClicked += (s, e) => SaveTexturesToMul();
            aiSettingsPanel.HideRegenAsOneButton();
            aiSettingsPanel.HideRevertOGButton();
            aiSettingsPanel.HideUnifyButton();
            aiSettingsPanel.HideSaveCanvasAsNewButton();
            aiSettingsPanel.StopAIGenerationClicked += (s, e) => CancelTextureGeneration();
            aiSettingsPanel.SetPushToJarJarEnabled(false);
            aiTab.Controls.Add(aiSettingsPanel);

            var imageFxTab = new TabPage("Image FX") { BackColor = HolographicTheme.PanelBackground };
            imageEditingPanel = new ImageEditingPanel { Dock = DockStyle.Fill };
            imageEditingPanel.EffectsChanged += (s, e) => ApplyImageEffectsToSelected();
            imageEditingPanel.ApplyClicked += (s, e) => ApplyImageEffectsToSelected();
            imageEditingPanel.ApplyToAllSelectedClicked += (s, e) => ApplyImageEffectsToSelected(applyToAllSelected: true);
            imageEditingPanel.ResetClicked += (s, e) => RestoreSelectedOriginal();
            imageEditingPanel.EyedropperRequested += TextureEyedropperRequested;
            imageEditingPanel.EyedropperCancelled += TextureEyedropperCancelled;
            imageEditingPanel.SamSelectRequested += TextureSamRequested;
            imageEditingPanel.SamSelectCancelled += TextureSamCancelled;
            imageEditingPanel.SamClearRequested += TextureSamClear;
            imageEditingPanel.SamShowChanged += TextureSamShowChanged;
            imageFxTab.Controls.Add(imageEditingPanel);

            sidebarTabs.TabPages.Add(aiTab);
            sidebarTabs.TabPages.Add(imageFxTab);

            previewBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(35, 35, 40),
                SizeMode = PictureBoxSizeMode.Zoom
            };
            previewBox.MouseDown += PreviewBox_EyedropperMouseDown;
            previewBox.Paint += PreviewBox_SamPaint;

            var statusPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 24,
                BackColor = HolographicTheme.ControlBackground
            };

            statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = HolographicTheme.TextSecondary,
                Font = new Font("Consolas", 9),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(5, 0, 0, 0),
                Text = "Ready"
            };
            statusPanel.Controls.Add(statusLabel);

            rightPanel.Controls.Add(previewBox);
            rightPanel.Controls.Add(sidebarTabs);
            rightPanel.Controls.Add(statusPanel);
            rightPanel.Controls.Add(topPanel);

            mainSplit.Panel1.Controls.Add(leftPanel);
            mainSplit.Panel2.Controls.Add(rightPanel);
            this.Controls.Add(mainSplit);
        }

        private Button CreateThemedButton(string text, int left, int top, int width, ButtonStyle style = ButtonStyle.Default)
        {
            var btn = new Button
            {
                Text = text,
                Left = left,
                Top = top,
                Width = width,
                Height = 25
            };
            HolographicTheme.ApplyToButton(btn, style);
            return btn;
        }

        private void BeginLoadMulFolder()
        {
            var config = AppConfig.Instance;
            mulFolder = config.MulFolderPath;

            if (string.IsNullOrEmpty(mulFolder) || !Directory.Exists(mulFolder))
            {
                mulFolder = config.FindMulFolder();
            }

            if (string.IsNullOrEmpty(mulFolder))
            {
                SetStatus("Warning: MUL folder not configured. Set it in Settings.");
                return;
            }

            string idxPath = Path.Combine(mulFolder, "texidx.mul");
            string mulPath = Path.Combine(mulFolder, "texmaps.mul");

            if (!File.Exists(idxPath) || !File.Exists(mulPath))
            {
                SetStatus($"No texture files found in: {mulFolder}");
                return;
            }

            SetStatus("Loading land textures...");
            validTextureIds = TexMapReader.GetValidTextureIds(mulFolder);
            filteredTextureIds = new List<int>(validTextureIds);
            PopulateTextureList();
            SetStatus($"Loaded {validTextureIds.Count} textures");
        }

        private void PopulateTextureList()
        {
            textureListView.VirtualListSize = filteredTextureIds.Count;
            textureListView.Invalidate();
        }

        private void SearchBox_TextChanged(object sender, EventArgs e)
        {
            string search = searchBox.Text.Trim();
            if (string.IsNullOrEmpty(search) || search == "Search texture id...")
            {
                filteredTextureIds = new List<int>(validTextureIds);
            }
            else
            {
                string s = search.ToUpperInvariant();
                if (s.StartsWith("0X")) s = s.Substring(2);

                filteredTextureIds = validTextureIds.Where(id =>
                {
                    string hex = id.ToString("X4");
                    return hex.Contains(s) || id.ToString().Contains(s);
                }).ToList();
            }

            PopulateTextureList();
        }

        private void TextureListView_RetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            if (e.ItemIndex < 0 || e.ItemIndex >= filteredTextureIds.Count)
            {
                e.Item = new ListViewItem("?");
                return;
            }

            int textureId = filteredTextureIds[e.ItemIndex];
            e.Item = new ListViewItem($"0x{textureId:X4}") { Tag = textureId };
        }

        private void TextureListView_DrawItem(object sender, DrawListViewItemEventArgs e)
        {
            using (var bg = new SolidBrush(e.Item.Selected ? HolographicTheme.SelectionCyan : HolographicTheme.InputBackground))
            {
                e.Graphics.FillRectangle(bg, e.Bounds);
            }

            if (!(e.Item.Tag is int textureId)) return;

            var img = GetTextureImage(textureId);
            if (img != null)
            {
                int maxSize = Math.Min(e.Bounds.Width - 6, e.Bounds.Height - 22);
                float scale = Math.Min((float)maxSize / img.Width, (float)maxSize / img.Height);
                scale = Math.Min(scale, 1f);

                int w = Math.Max(1, (int)(img.Width * scale));
                int h = Math.Max(1, (int)(img.Height * scale));
                int x = e.Bounds.Left + (e.Bounds.Width - w) / 2;
                int y = e.Bounds.Top + 2;

                e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                e.Graphics.DrawImage(img, x, y, w, h);
            }

            var textRect = new Rectangle(e.Bounds.Left, e.Bounds.Bottom - 18, e.Bounds.Width, 18);
            TextRenderer.DrawText(e.Graphics, e.Item.Text, textureListView.Font, textRect,
                e.Item.Selected ? Color.White : HolographicTheme.TextPrimary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);
        }

        private void TextureListView_CacheVirtualItems(object sender, CacheVirtualItemsEventArgs e)
        {
            int start = e.StartIndex;
            int end = Math.Min(e.EndIndex, filteredTextureIds.Count - 1);

            for (int i = start; i <= end; i++)
            {
                int id = filteredTextureIds[i];
                if (!textureCache.ContainsKey(id))
                {
                    var bmp = TexMapReader.LoadTexture(mulFolder, (ushort)id);
                    if (bmp != null)
                    {
                        textureCache[id] = bmp;
                    }
                }
            }
        }

        private void TextureListView_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (textureListView.SelectedIndices.Count == 0)
            {
                selectedTextureId = -1;
                infoLabel.Text = "No texture selected";
                DropTextureSamIfTargetChanged(-1);
                return;
            }

            int index = textureListView.SelectedIndices[0];
            if (index < 0 || index >= filteredTextureIds.Count) return;

            selectedTextureId = filteredTextureIds[index];
            _showingOriginal = false;
            aiSettingsPanel.SetOldNewButtonText("Old/New");
            DropTextureSamIfTargetChanged(selectedTextureId);
            UpdatePreviewFromSelected();

            int selectedCount = GetSelectedTextureIds().Count;
            if (selectedCount > 1)
            {
                SetStatus($"Selected {selectedCount} textures");
            }
        }

        private List<int> GetSelectedTextureIds()
        {
            var ids = new List<int>();
            foreach (int index in textureListView.SelectedIndices)
            {
                if (index >= 0 && index < filteredTextureIds.Count)
                {
                    ids.Add(filteredTextureIds[index]);
                }
            }
            return ids;
        }

        private Bitmap GetTextureImage(int textureId)
        {
            if (_modifiedTextureImages.TryGetValue(textureId, out var modified) && modified != null)
            {
                return modified;
            }

            if (textureCache.TryGetValue(textureId, out var cached) && cached != null)
            {
                return cached;
            }

            var bmp = TexMapReader.LoadTexture(mulFolder, (ushort)textureId);
            if (bmp != null)
            {
                textureCache[textureId] = bmp;
            }

            return bmp;
        }

        private void UpdatePreviewFromSelected()
        {
            if (selectedTextureId < 0)
            {
                SetPreviewImage(null);
                return;
            }

            Bitmap source;
            if (_showingOriginal && _originalTextureImages.TryGetValue(selectedTextureId, out var orig))
            {
                source = orig;
            }
            else
            {
                source = GetTextureImage(selectedTextureId);
            }

            if (source == null)
            {
                SetPreviewImage(null);
                infoLabel.Text = $"Texture 0x{selectedTextureId:X4} (failed to load)";
                return;
            }

            SetPreviewImage(new Bitmap(source));
            infoLabel.Text = $"Texture 0x{selectedTextureId:X4}  {source.Width}x{source.Height}";
            aiSettingsPanel.SetOldNewEnabled(_originalTextureImages.ContainsKey(selectedTextureId));
            aiSettingsPanel.SetPendingChanges(_modifiedTextureImages.Count);
            textureListView.Invalidate();
        }

        private void SetPreviewImage(Bitmap bmp)
        {
            var old = _previewBitmap;
            _previewBitmap = bmp;
            previewBox.Image = _previewBitmap;
            old?.Dispose();
        }

        private void EnsureOriginalStored(int textureId, Bitmap current)
        {
            if (textureId < 0 || current == null) return;

            if (!_originalTextureImages.ContainsKey(textureId))
            {
                _originalTextureImages[textureId] = new Bitmap(current);
            }
        }

        private void ReplaceModifiedTexture(int textureId, Bitmap newImage)
        {
            if (_modifiedTextureImages.TryGetValue(textureId, out var old))
            {
                old?.Dispose();
            }

            _modifiedTextureImages[textureId] = newImage;
            _showingOriginal = false;
            aiSettingsPanel.SetOldNewButtonText("Old/New");
            UpdatePreviewFromSelected();
        }

        private void ReplaceSelectedTextureFromFile()
        {
            if (selectedTextureId < 0)
            {
                SetStatus("Select a texture first.");
                return;
            }

            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff";
                if (ofd.ShowDialog() != DialogResult.OK) return;

                try
                {
                    var current = GetTextureImage(selectedTextureId);
                    if (current == null)
                    {
                        SetStatus("Could not load selected texture.");
                        return;
                    }

                    EnsureOriginalStored(selectedTextureId, current);

                    using (var loaded = new Bitmap(ofd.FileName))
                    {
                        int target = current.Width == 128 ? 128 : 64;
                        Bitmap replacement;

                        if (loaded.Width != target || loaded.Height != target)
                        {
                            replacement = new Bitmap(target, target, PixelFormat.Format32bppArgb);
                            using (var g = Graphics.FromImage(replacement))
                            {
                                g.Clear(Color.Transparent);
                                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                                g.DrawImage(loaded, 0, 0, target, target);
                            }
                        }
                        else
                        {
                            replacement = new Bitmap(loaded);
                        }

                        ReplaceModifiedTexture(selectedTextureId, replacement);
                        SetStatus($"Replaced texture 0x{selectedTextureId:X4}");
                    }
                }
                catch (Exception ex)
                {
                    SetStatus($"Replace failed: {ex.Message}");
                }
            }
        }

        private void ApplyImageEffectsToSelected(bool applyToAllSelected = false)
        {
            var targetIds = applyToAllSelected ? GetSelectedTextureIds() : new List<int> { selectedTextureId };
            targetIds = targetIds.Where(id => id >= 0).Distinct().ToList();

            if (targetIds.Count == 0)
                return;

            // Match-count readout (cheap: art is small; computed in the preview path). SAM-gated.
            try
            {
                int count = 0;
                Bitmap countSource = null;
                if (_originalTextureImages.TryGetValue(selectedTextureId, out var orig) && orig != null)
                    countSource = orig;
                else
                    countSource = GetTextureImage(selectedTextureId);
                if (countSource != null && imageEditingPanel.DesaturateEnabled)
                {
                    Bitmap gate = GetTexSamMaskFor(selectedTextureId, countSource);
                    count = ImageEffects.CountDesaturateMatches(countSource, imageEditingPanel.DesaturateTargets, imageEditingPanel.DesaturateTolerance, imageEditingPanel.DesaturateProtected, imageEditingPanel.DespeckleMinSize, gate);
                }
                imageEditingPanel.SetMatchCount(count);
            }
            catch { }

            try
            {
                int applied = 0;
                foreach (int textureId in targetIds)
                {
                    var current = GetTextureImage(textureId);
                    if (current == null)
                        continue;

                    EnsureOriginalStored(textureId, current);

                    Bitmap source = new Bitmap(_originalTextureImages[textureId]);
                    Bitmap texGate = GetTexSamMaskFor(textureId, source);
                    Bitmap result = ImageEffects.ApplyEffects(
                        source,
                        imageEditingPanel.Brightness,
                        imageEditingPanel.Contrast,
                        imageEditingPanel.Hue,
                        imageEditingPanel.Saturation,
                        imageEditingPanel.PixelSize,
                        imageEditingPanel.PixelizeEnabled,
                        imageEditingPanel.PaletteColors,
                        imageEditingPanel.NoiseIntensity,
                        imageEditingPanel.DitherLevels,
                        imageEditingPanel.EdgeDarkening,
                        imageEditingPanel.ColorBands,
                        imageEditingPanel.FillHoles,
                        imageEditingPanel.RemoveBackgroundEnabled,
                        imageEditingPanel.BackgroundMode,
                        imageEditingPanel.BackgroundColor,
                        imageEditingPanel.BackgroundThreshold,
                        imageEditingPanel.DesaturateEnabled ? imageEditingPanel.DesaturateTargets : null,
                        imageEditingPanel.DesaturateTolerance,
                        imageEditingPanel.SnapNearGrayEnabled,
                        imageEditingPanel.SnapTolerance,
                        imageEditingPanel.DesaturateProtected,
                        imageEditingPanel.DespeckleMinSize,
                        texGate
                    );
                    source.Dispose();

                    ReplaceModifiedTexture(textureId, result);
                    applied++;
                }

                if (applied > 0)
                {
                    if (applied == 1)
                        SetStatus($"Applied image FX to 0x{targetIds[0]:X4}");
                    else
                        SetStatus($"Applied image FX to {applied} textures");
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Image FX error: {ex.Message}");
            }
        }

        private void RestoreSelectedOriginal()
        {
            if (selectedTextureId < 0)
                return;

            if (_originalTextureImages.TryGetValue(selectedTextureId, out var original))
            {
                ReplaceModifiedTexture(selectedTextureId, new Bitmap(original));
                SetStatus($"Restored original texture 0x{selectedTextureId:X4}");
            }
        }

        private async Task<T> AwaitTexturePollWithCancel<T>(Task<T> pollTask, System.Threading.CancellationToken token, ComfyUIClient client)
        {
            while (!pollTask.IsCompleted)
            {
                try { await Task.Delay(500, token); }
                catch (OperationCanceledException)
                {
                    try { await client.Interrupt(); } catch { }
                    throw;
                }
            }
            return await pollTask;
        }

        private void CancelTextureGeneration()
        {
            var cts = _textureGenCts;
            if (cts == null) return;
            try { cts.Cancel(); } catch { }
            try
            {
                var url = aiSettingsPanel?.ComfyUrl;
                if (!string.IsNullOrWhiteSpace(url))
                {
                    var client = new ComfyUIClient(url);
                    _ = client.Interrupt();
                }
            }
            catch { }
            SetStatus("Stopping...");
        }

        private async void RegenerateSelectedTexture()
        {
            if (_textureGenCts != null)
            {
                SetStatus("Generation already running - press STOP to cancel.");
                aiSettingsPanel.SetStatus("Already running", Color.Orange);
                return;
            }
            _textureGenCts = new System.Threading.CancellationTokenSource();
            var textureCts = _textureGenCts;
            aiSettingsPanel.SetStopEnabled(true);
            try
            {
                await RegenerateSelectedTextureCore(textureCts);
            }
            catch (OperationCanceledException)
            {
                SetStatus("Stopped by user.");
                aiSettingsPanel.SetStatus("Stopped by user", Color.Orange);
            }
            catch (Exception ex)
            {
                SetStatus($"AI regeneration error: {ex.Message}");
                aiSettingsPanel.SetStatus($"Error: {ex.Message}", Color.Red);
            }
            finally
            {
                aiSettingsPanel.SetStopEnabled(false);
                _textureGenCts = null;
                textureCts.Dispose();
            }
        }

        private async Task RegenerateSelectedTextureCore(System.Threading.CancellationTokenSource textureCts)
        {
            if (selectedTextureId < 0)
            {
                SetStatus("Select a texture first.");
                aiSettingsPanel.SetStatus("Select a texture", Color.Orange);
                return;
            }

            var current = GetTextureImage(selectedTextureId);
            if (current == null)
            {
                SetStatus("Could not load selected texture.");
                return;
            }

            SetStatus("Regenerating selected texture with AI...");
            aiSettingsPanel.SetStatus("Regenerating...", HolographicTheme.CyanAccent);

            try
            {
                var selectedBackend = aiSettingsPanel.SelectedBackend;
                if (selectedBackend == AIBackend.ComfyUI)
                {
                    if (string.IsNullOrEmpty(aiSettingsPanel.ComfyUrl))
                    {
                        aiSettingsPanel.SetStatus("No URL configured", Color.Red);
                        return;
                    }
                    _comfyClient = new ComfyUIClient(aiSettingsPanel.ComfyUrl);
                }
                else
                {
                    bool available = await ImageGeneratorFactory.EnsureBackendAvailable(selectedBackend, this);
                    if (!available)
                    {
                        aiSettingsPanel.SetStatus("Models not available", Color.Red);
                        return;
                    }
                }

                EnsureOriginalStored(selectedTextureId, current);

                int sourceWidth = current.Width;
                int sourceHeight = current.Height;
                int aiWidth = aiSettingsPanel.ResolutionWidth;
                int aiHeight = aiSettingsPanel.ResolutionHeight;

                float scaleToAI = 1.0f;
                if (sourceWidth < aiWidth || sourceHeight < aiHeight)
                {
                    float scaleW = (float)aiWidth / sourceWidth;
                    float scaleH = (float)aiHeight / sourceHeight;
                    scaleToAI = Math.Max(scaleW, scaleH);
                }

                Bitmap imageForAI;
                if (scaleToAI > 1.0f)
                {
                    int scaledWidth = ((int)(sourceWidth * scaleToAI) + 7) / 8 * 8;
                    int scaledHeight = ((int)(sourceHeight * scaleToAI) + 7) / 8 * 8;

                    imageForAI = new Bitmap(scaledWidth, scaledHeight, PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(imageForAI))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.Clear(Color.Transparent);
                        g.DrawImage(current, 0, 0, scaledWidth, scaledHeight);
                    }
                    aiWidth = scaledWidth;
                    aiHeight = scaledHeight;
                }
                else
                {
                    imageForAI = new Bitmap(current);
                    aiWidth = sourceWidth;
                    aiHeight = sourceHeight;
                }

                Bitmap newBmp = null;

                if (selectedBackend != AIBackend.ComfyUI)
                {
                    throw new NotSupportedException("Only ComfyUI backend is supported.");
                }
                else
                {
                    byte[] imageBytes;
                    using (var ms = new MemoryStream())
                    {
                        imageForAI.Save(ms, ImageFormat.Png);
                        imageBytes = ms.ToArray();
                    }

                    string uploadedFilename = await _comfyClient.UploadImage(imageBytes, $"texture_{selectedTextureId}.png");

                    long effSeed = aiSettingsPanel.Seed ?? new Random().Next();
                    SetStatus($"Generating texture 0x{selectedTextureId:X4} with seed {effSeed}...");

                    string workflow;
                    if (Flux2KleinWorkflow.IsKlein(aiSettingsPanel.Checkpoint))
                        workflow = Flux2KleinImage2ImageWorkflow.CreateWorkflow(
                            aiSettingsPanel.Prompt,
                            aiSettingsPanel.NegativePrompt,
                            uploadedFilename,
                            aiWidth, aiHeight,
                            effSeed,
                            aiSettingsPanel.Sampler);
                    else
                        workflow = Image2ImageWorkflow.CreateWorkflow(
                            aiSettingsPanel.Prompt,
                            aiSettingsPanel.NegativePrompt,
                            uploadedFilename,
                            aiWidth, aiHeight,
                            aiSettingsPanel.Steps,
                            aiSettingsPanel.Cfg,
                            aiSettingsPanel.Denoise,
                            effSeed,
                            aiSettingsPanel.Sampler,
                            aiSettingsPanel.Scheduler,
                            aiSettingsPanel.Checkpoint);

                    string promptId = await _comfyClient.QueuePrompt(workflow);
                    if (string.IsNullOrEmpty(promptId))
                    {
                        aiSettingsPanel.SetStatus("Queue failed", Color.Red);
                        imageForAI.Dispose();
                        return;
                    }

                    var images = await AwaitTexturePollWithCancel(_comfyClient.GetGeneratedImages(promptId, maxAttempts: 120, pollIntervalMs: 1000), textureCts.Token, _comfyClient);
                    textureCts.Token.ThrowIfCancellationRequested();
                    if (images.Count == 0)
                    {
                        aiSettingsPanel.SetStatus("No images", Color.Red);
                        imageForAI.Dispose();
                        return;
                    }

                    byte[] newImageData = await _comfyClient.DownloadImage(images[0]);
                    if (newImageData == null || newImageData.Length < 8)
                    {
                        aiSettingsPanel.SetStatus("Download failed", Color.Red);
                        imageForAI.Dispose();
                        return;
                    }
                    using (var ms = new MemoryStream(newImageData))
                    {
                        newBmp = new Bitmap(ms);
                    }
                }

                imageForAI.Dispose();

                if (newBmp == null)
                {
                    aiSettingsPanel.SetStatus("Generation failed", Color.Red);
                    return;
                }

                if (newBmp.Width != sourceWidth || newBmp.Height != sourceHeight)
                {
                    var resizedBmp = new Bitmap(sourceWidth, sourceHeight, PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(resizedBmp))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.Clear(Color.Transparent);
                        g.DrawImage(newBmp, 0, 0, sourceWidth, sourceHeight);
                    }
                    newBmp.Dispose();
                    newBmp = resizedBmp;
                }

                if (_rawAIOutputImages.TryGetValue(selectedTextureId, out var oldRaw))
                {
                    oldRaw?.Dispose();
                }
                _rawAIOutputImages[selectedTextureId] = new Bitmap(newBmp);

                if (aiSettingsPanel.DropBlackPixels)
                {
                    newBmp = RemoveBlackPixels(newBmp, aiSettingsPanel.BlackThreshold);
                }

                ReplaceModifiedTexture(selectedTextureId, newBmp);
                aiSettingsPanel.SetOldNewEnabled(true);
                aiSettingsPanel.SetPendingChanges(_modifiedTextureImages.Count);
                aiSettingsPanel.SetStatus("Regenerated!", Color.LimeGreen);
                SetStatus($"Regenerated texture 0x{selectedTextureId:X4}");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                SetStatus($"AI regeneration error: {ex.Message}");
                aiSettingsPanel.SetStatus($"Error: {ex.Message}", Color.Red);
            }
        }

        private Bitmap RemoveBlackPixels(Bitmap source, int threshold)
        {
            var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);

            var sourceData = source.LockBits(new Rectangle(0, 0, source.Width, source.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var resultData = result.LockBits(new Rectangle(0, 0, result.Width, result.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

            try
            {
                int byteCount = Math.Abs(sourceData.Stride) * source.Height;
                byte[] srcPixels = new byte[byteCount];
                byte[] dstPixels = new byte[byteCount];

                System.Runtime.InteropServices.Marshal.Copy(sourceData.Scan0, srcPixels, 0, byteCount);

                int stride = sourceData.Stride;

                for (int y = 0; y < source.Height; y++)
                {
                    int rowOffset = y * stride;
                    for (int x = 0; x < source.Width; x++)
                    {
                        int offset = rowOffset + x * 4;
                        byte b = srcPixels[offset];
                        byte g = srcPixels[offset + 1];
                        byte r = srcPixels[offset + 2];
                        byte a = srcPixels[offset + 3];

                        if (r <= threshold && g <= threshold && b <= threshold)
                        {
                            dstPixels[offset] = 0;
                            dstPixels[offset + 1] = 0;
                            dstPixels[offset + 2] = 0;
                            dstPixels[offset + 3] = 0;
                        }
                        else
                        {
                            dstPixels[offset] = b;
                            dstPixels[offset + 1] = g;
                            dstPixels[offset + 2] = r;
                            dstPixels[offset + 3] = a;
                        }
                    }
                }

                System.Runtime.InteropServices.Marshal.Copy(dstPixels, 0, resultData.Scan0, byteCount);
            }
            finally
            {
                source.UnlockBits(sourceData);
                result.UnlockBits(resultData);
            }

            source.Dispose();
            return result;
        }

        private void ReapplyBlackPixelRemoval()
        {
            if (selectedTextureId < 0)
                return;

            if (!_rawAIOutputImages.TryGetValue(selectedTextureId, out var rawImage) || rawImage == null)
                return;

            Bitmap processed = new Bitmap(rawImage);
            if (aiSettingsPanel.DropBlackPixels)
            {
                processed = RemoveBlackPixels(processed, aiSettingsPanel.BlackThreshold);
            }

            ReplaceModifiedTexture(selectedTextureId, processed);
        }

        private void ToggleOldNew()
        {
            if (selectedTextureId < 0)
                return;

            if (!_originalTextureImages.ContainsKey(selectedTextureId) || !_modifiedTextureImages.ContainsKey(selectedTextureId))
            {
                SetStatus("No old/new pair available for selected texture.");
                return;
            }

            _showingOriginal = !_showingOriginal;
            aiSettingsPanel.SetOldNewButtonText(_showingOriginal ? "Show New" : "Old/New");
            UpdatePreviewFromSelected();
        }

        private void RevertAIChanges()
        {
            if (_originalTextureImages.Count == 0)
            {
                SetStatus("No AI changes to revert.");
                return;
            }

            foreach (var kvp in _originalTextureImages)
            {
                if (_modifiedTextureImages.TryGetValue(kvp.Key, out var old))
                {
                    old?.Dispose();
                }
                _modifiedTextureImages[kvp.Key] = new Bitmap(kvp.Value);
            }

            _showingOriginal = false;
            UpdatePreviewFromSelected();
            aiSettingsPanel.SetOldNewButtonText("Old/New");
            aiSettingsPanel.SetPendingChanges(_modifiedTextureImages.Count);
            aiSettingsPanel.SetStatus("Reverted", Color.LimeGreen);
            SetStatus("Reverted AI changes to originals.");
        }

        private void SaveTexturesToMul()
        {
            if (_modifiedTextureImages.Count == 0)
            {
                MessageBox.Show("No modified textures to save.", "Save Textures", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (string.IsNullOrEmpty(mulFolder) || !Directory.Exists(mulFolder))
            {
                MessageBox.Show("MUL folder not configured.", "Save Textures", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string idxPath = Path.Combine(mulFolder, "texidx.mul");
            string mulPath = Path.Combine(mulFolder, "texmaps.mul");
            if (!File.Exists(idxPath) || !File.Exists(mulPath))
            {
                MessageBox.Show("texidx.mul/texmaps.mul not found in configured MUL folder.", "Save Textures", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int saved = 0;
            int failed = 0;
            var errors = new List<string>();

            foreach (var kvp in _modifiedTextureImages)
            {
                try
                {
                    if (TexMapReader.SaveTexture(mulFolder, kvp.Key, kvp.Value))
                    {
                        saved++;
                    }
                    else
                    {
                        failed++;
                        errors.Add($"Texture 0x{kvp.Key:X4}: write failed");
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    errors.Add($"Texture 0x{kvp.Key:X4}: {ex.Message}");
                }
            }

            if (failed == 0)
            {
                MessageBox.Show($"Saved {saved} texture(s) to texmaps.mul.", "Save Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                SetStatus($"Saved {saved} textures.");
                aiSettingsPanel.SetStatus($"Saved {saved}", HolographicTheme.ButtonSuccess);
            }
            else
            {
                MessageBox.Show($"Saved {saved}, failed {failed}.\n\n{string.Join("\n", errors)}", "Save Partial", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetStatus($"Saved {saved}/{saved + failed} textures.");
                aiSettingsPanel.SetStatus($"Saved {saved}/{saved + failed}", HolographicTheme.ButtonWarning);
            }

            textureCache.Clear();
            validTextureIds = TexMapReader.GetValidTextureIds(mulFolder);
            filteredTextureIds = new List<int>(validTextureIds);
            PopulateTextureList();
            UpdatePreviewFromSelected();
        }

        private void SetStatus(string message)
        {
            if (statusLabel.InvokeRequired)
            {
                statusLabel.Invoke(new Action(() => SetStatus(message)));
                return;
            }
            statusLabel.Text = message;
        }

        private void TextureEditorForm_KeyDown(object sender, KeyEventArgs e)
        {
            // Esc disarms SAM arming but KEEPS the last mask; Clear SAM drops it.
            if (e.KeyCode == Keys.Escape && _texSamArmed)
            {
                try { imageEditingPanel?.CancelSamArming(); }
                catch { _texSamArmed = false; }
                if (_texSamArmed)
                    DisarmTextureSam(null);
                e.Handled = true;
                return;
            }
            // Esc cancels an armed desaturate eyedropper.
            if (e.KeyCode == Keys.Escape && _texEyedropperArmed)
            {
                try { imageEditingPanel?.CancelEyedropper(); }
                catch { _texEyedropperArmed = false; }
                if (_texEyedropperArmed)
                    DisarmTextureEyedropper("Eyedropper cancelled");
                e.Handled = true;
            }
        }

        private void TextureEyedropperRequested(object sender, EyedropperRequestedEventArgs e)
        {
            _texEyedropperArmed = true;
            _texEyedropperKind = e?.Kind ?? DesaturatePickKind.Targets;
            try
            {
                if (previewBox != null)
                {
                    _texPrevCursor = previewBox.Cursor;
                    previewBox.Cursor = Cursors.Cross;
                    try { previewBox.Focus(); } catch { }
                }
            }
            catch { }
            string what = _texEyedropperKind == DesaturatePickKind.Protected ? "protected colors" : "target colors";
            SetStatus($"Click preview to add {what} — Esc done");
        }

        private void TextureEyedropperCancelled(object sender, EventArgs e)
        {
            DisarmTextureEyedropper("Eyedropper cancelled");
        }

        private void TextureSamRequested(object sender, EventArgs e)
        {
            string missing = Utils.SamService.GetMissingPath();
            if (missing != null)
            {
                SetStatus("SAM files missing: " + missing);
                try { imageEditingPanel?.CancelSamArming(); } catch { _texSamArmed = false; }
                _texSamArmed = false;
                try { imageEditingPanel?.SetStatus("SAM files missing: " + missing, Color.Orange); } catch { }
                return;
            }
            _texSamArmed = true;
            try
            {
                if (previewBox != null)
                {
                    _texPrevCursor = previewBox.Cursor;
                    previewBox.Cursor = Cursors.Cross;
                    try { previewBox.Focus(); } catch { }
                }
            }
            catch { }
            try { imageEditingPanel?.SetSamInfo(_texSamPoints.Count, _texSamMask != null, false); } catch { }
            SetStatus("SAM armed: L-click +FG, R/Sft-click +BG — Esc done");
            try { imageEditingPanel?.SetStatus("SAM armed: L-click +FG, R/Sft-click +BG — Esc done", Color.FromArgb(100, 200, 255)); } catch { }
        }

        private void TextureSamCancelled(object sender, EventArgs e)
        {
            DisarmTextureSam(null);
        }

        private void TextureSamClear(object sender, EventArgs e)
        {
            ClearTextureSam("SAM cleared");
        }

        private void TextureSamShowChanged(object sender, EventArgs e)
        {
            try { previewBox?.Invalidate(); } catch { }
        }

        private void DisarmTextureSam(string status)
        {
            _texSamArmed = false;
            try
            {
                if (previewBox != null)
                    previewBox.Cursor = _texPrevCursor ?? Cursors.Default;
            }
            catch { }
            _texPrevCursor = null;
            if (status != null)
                SetStatus(status);
            try { previewBox?.Invalidate(); } catch { }
        }

        private void ClearTextureSam(string status)
        {
            try { _texSamCts?.Cancel(); } catch { }
            _texSamCts = null;
            _texSamRunId++;
            _texSamArmed = false;
            _texSamPoints.Clear();
            try { _texSamMask?.Dispose(); } catch { }
            _texSamMask = null;
            _texSamTextureId = -1;
            _texSamKey = null;
            try
            {
                if (previewBox != null)
                    previewBox.Cursor = _texPrevCursor ?? Cursors.Default;
            }
            catch { }
            _texPrevCursor = null;
            try { imageEditingPanel?.SetSamInfo(0, false, false); } catch { }
            if (status != null)
                SetStatus(status);
            try { previewBox?.Invalidate(); } catch { }
            // Refresh preview so the gate lifts immediately.
            try { ApplyImageEffectsToSelected(); } catch { }
        }

        private static string TexSamKeyFor(int textureId, int w, int h)
        {
            return textureId + "|" + w + "x" + h;
        }

        private Bitmap GetTexSamSource(int textureId)
        {
            try
            {
                if (_originalTextureImages.TryGetValue(textureId, out var orig) && orig != null)
                    return orig;
                return GetTextureImage(textureId);
            }
            catch { return null; }
        }

        private Bitmap GetTexSamMaskFor(int textureId, Bitmap source)
        {
            if (_texSamMask == null || source == null) return null;
            try
            {
                if (_texSamTextureId != textureId) return null;
                if (_texSamMask.Width != source.Width || _texSamMask.Height != source.Height) return null;
                if (_texSamKey != TexSamKeyFor(textureId, source.Width, source.Height)) return null;
                return _texSamMask;
            }
            catch { return null; }
        }

        private void DropTextureSamIfTargetChanged(int currentTextureId)
        {
            if (_texSamMask == null && _texSamPoints.Count == 0) return;
            try
            {
                if (_texSamTextureId != currentTextureId)
                {
                    try { _texSamCts?.Cancel(); } catch { }
                    _texSamCts = null;
                    _texSamRunId++;
                    _texSamPoints.Clear();
                    try { _texSamMask?.Dispose(); } catch { }
                    _texSamMask = null;
                    _texSamTextureId = -1;
                    _texSamKey = null;
                    try { imageEditingPanel?.SetSamInfo(0, false, false); } catch { }
                    try { previewBox?.Invalidate(); } catch { }
                }
            }
            catch { }
        }

        private void AddTexSamPointAndRerun(int imgX, int imgY, int label)
        {
            if (selectedTextureId < 0) return;
            string missing = Utils.SamService.GetMissingPath();
            if (missing != null)
            {
                SetStatus("SAM files missing: " + missing);
                try { imageEditingPanel?.SetStatus("SAM files missing: " + missing, Color.Orange); } catch { }
                return;
            }
            Bitmap segSource = GetTexSamSource(selectedTextureId);
            if (segSource == null)
            {
                SetStatus("SAM: no image to segment");
                return;
            }
            string key = TexSamKeyFor(selectedTextureId, segSource.Width, segSource.Height);
            if (_texSamTextureId != selectedTextureId || _texSamKey != key)
            {
                try { _texSamCts?.Cancel(); } catch { }
                _texSamCts = null;
                _texSamRunId++;
                _texSamPoints.Clear();
                try { _texSamMask?.Dispose(); } catch { }
                _texSamMask = null;
                _texSamTextureId = selectedTextureId;
                _texSamKey = key;
            }
            _texSamPoints.Add((imgX, imgY, label));
            try { imageEditingPanel?.SetSamInfo(_texSamPoints.Count, _texSamMask != null, true); } catch { }
            _ = RerunTextureSamAsync();
        }

        private async Task RerunTextureSamAsync()
        {
            int textureId = _texSamTextureId;
            if (textureId < 0) textureId = selectedTextureId;
            if (textureId < 0) return;
            Bitmap segSource = GetTexSamSource(textureId);
            if (segSource == null) return;
            var pts = _texSamPoints.Select(p => (p.x, p.y, p.label)).ToList();
            if (pts.Count == 0) return;
            Bitmap segCopy;
            try { segCopy = new Bitmap(segSource); }
            catch { return; }

            int runId = ++_texSamRunId;
            try { _texSamCts?.Cancel(); } catch { }
            var cts = new System.Threading.CancellationTokenSource();
            _texSamCts = cts;
            string keyAtStart = _texSamKey;
            int texAtStart = _texSamTextureId >= 0 ? _texSamTextureId : textureId;
            SetStatus("SAM segmenting…");
            try { imageEditingPanel?.SetStatus("SAM segmenting…", Color.FromArgb(100, 200, 255)); } catch { }
            try { imageEditingPanel?.SetSamInfo(_texSamPoints.Count, _texSamMask != null, true); } catch { }
            Bitmap mask = null;
            try
            {
                mask = await Utils.SamService.RunSamAsync(segCopy, pts, cts.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                if (runId != _texSamRunId) return;
                SetStatus("SAM failed: " + ex.Message);
                try { imageEditingPanel?.SetStatus("SAM failed: " + ex.Message, Color.Orange); } catch { }
                try { imageEditingPanel?.SetSamInfo(_texSamPoints.Count, _texSamMask != null, false); } catch { }
                return;
            }
            finally
            {
                try { segCopy.Dispose(); } catch { }
            }
            if (runId != _texSamRunId)
            {
                try { mask?.Dispose(); } catch { }
                return;
            }
            if (_texSamTextureId != texAtStart && _texSamTextureId >= 0)
            {
                try { mask?.Dispose(); } catch { }
                return;
            }
            // Adopt current selection if first run had no target yet.
            if (_texSamTextureId < 0)
            {
                _texSamTextureId = texAtStart;
                _texSamKey = keyAtStart;
            }
            if (_texSamKey != keyAtStart || _texSamTextureId != texAtStart)
            {
                try { mask?.Dispose(); } catch { }
                return;
            }
            try
            {
                if (mask.Width != segSource.Width || mask.Height != segSource.Height)
                {
                    try { mask?.Dispose(); } catch { }
                    SetStatus("SAM mask size mismatch — ignored");
                    try { imageEditingPanel?.SetSamInfo(_texSamPoints.Count, _texSamMask != null, false); } catch { }
                    return;
                }
            }
            catch
            {
                try { mask?.Dispose(); } catch { }
                return;
            }
            try { _texSamMask?.Dispose(); } catch { }
            _texSamMask = mask;
            if (_texSamTextureId < 0) _texSamTextureId = textureId;
            if (_texSamKey == null) _texSamKey = TexSamKeyFor(_texSamTextureId, mask.Width, mask.Height);
            int n = _texSamPoints.Count;
            SetStatus("SAM mask updated (" + n + " pts) — desat/snap gated");
            try { imageEditingPanel?.SetStatus("SAM mask updated (" + n + " pts) — desat/snap gated", Color.Green); } catch { }
            try { imageEditingPanel?.SetSamInfo(n, true, false); } catch { }
            try { previewBox?.Invalidate(); } catch { }
            try { ApplyImageEffectsToSelected(); } catch { }
        }

        private void DisarmTextureEyedropper(string status)
        {
            _texEyedropperArmed = false;
            _texEyedropperKind = DesaturatePickKind.Targets;
            try
            {
                if (previewBox != null)
                    previewBox.Cursor = _texPrevCursor ?? Cursors.Default;
            }
            catch { }
            _texPrevCursor = null;
            if (status != null)
                SetStatus(status);
        }

        private void PreviewBox_EyedropperMouseDown(object sender, MouseEventArgs e)
        {
            // SAM Select: L-click = FG, R-click or Shift+L-click = BG. Stays armed; auto-reruns.
            if (_texSamArmed && (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right))
            {
                int sx, sy;
                if (TryPickSamPointFromPreview(e.Location, out sx, out sy))
                {
                    bool isBg = e.Button == MouseButtons.Right || (ModifierKeys & Keys.Shift) == Keys.Shift;
                    AddTexSamPointAndRerun(sx, sy, isBg ? 0 : 1);
                    SetStatus((isBg ? "SAM +BG" : "SAM +FG") + " (" + sx + "," + sy + ") — segmenting… (Esc done)");
                }
                else
                {
                    SetStatus("SAM: click inside the image (Esc done)");
                }
                return;
            }
            if (!_texEyedropperArmed || e.Button != MouseButtons.Left)
                return;
            Color picked;
            if (TryPickDesatColorFromPreview(e.Location, out picked))
            {
                // Stays armed for rapid multi-picks; Esc / Stop disarms.
                imageEditingPanel.ConfirmEyedropper(picked, _texEyedropperKind);
                string where = _texEyedropperKind == DesaturatePickKind.Protected ? "protected" : "targets";
                SetStatus($"Added RGB({picked.R},{picked.G},{picked.B}) to {where} — click to add more (Esc done)");
            }
            else
            {
                SetStatus("Transparent pixel — click another pixel (Esc done)");
            }
        }

        /// <summary>
        /// SAM point mapping for the Zoom preview: direct preview-image coords
        /// via existing letterbox math. Transparent pixels ALLOWED (BG points).
        /// </summary>
        private bool TryPickSamPointFromPreview(Point mouse, out int imgX, out int imgY)
        {
            imgX = 0;
            imgY = 0;
            try
            {
                var img = previewBox?.Image;
                if (img == null) return false;
                int iw, ih;
                try { iw = img.Width; ih = img.Height; }
                catch { return false; }
                if (iw <= 0 || ih <= 0) return false;
                Size cb = previewBox.ClientSize;
                if (cb.Width <= 0 || cb.Height <= 0) return false;
                float scale = Math.Min((float)cb.Width / iw, (float)cb.Height / ih);
                if (scale <= 0) return false;
                float dw = iw * scale;
                float dh = ih * scale;
                float ox = (cb.Width - dw) / 2f;
                float oy = (cb.Height - dh) / 2f;
                int ix = (int)Math.Floor((mouse.X - ox) / scale);
                int iy = (int)Math.Floor((mouse.Y - oy) / scale);
                if (ix < 0 || iy < 0 || ix >= iw || iy >= ih) return false;
                imgX = ix;
                imgY = iy;
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Paint-time SAM overlay for the Zoom preview: cyan tint where the
        /// live mask is white, mapped through the same letterbox math.
        /// Never mutates art; hidden when Show SAM is off or no live mask.
        /// </summary>
        private void PreviewBox_SamPaint(object sender, PaintEventArgs e)
        {
            try
            {
                if (_texSamMask == null) return;
                if (imageEditingPanel != null && !imageEditingPanel.ShowSam) return;
                var img = previewBox?.Image;
                if (img == null) return;
                int iw, ih;
                try { iw = img.Width; ih = img.Height; }
                catch { return; }
                if (iw <= 0 || ih <= 0) return;
                if (_texSamMask.Width != iw || _texSamMask.Height != ih) return;
                if (_texSamTextureId != selectedTextureId) return;
                Size cb = previewBox.ClientSize;
                float scale = Math.Min((float)cb.Width / iw, (float)cb.Height / ih);
                if (scale <= 0) return;
                float dw = iw * scale;
                float dh = ih * scale;
                float ox = (cb.Width - dw) / 2f;
                float oy = (cb.Height - dh) / 2f;
                // Downsample mask to overlay stride for paint speed (textures are 64/128px; direct is fine).
                using (var overlay = new Bitmap(iw, ih, PixelFormat.Format32bppArgb))
                {
                    bool any = false;
                    for (int y = 0; y < ih; y++)
                    {
                        for (int x = 0; x < iw; x++)
                        {
                            Color mc;
                            try { mc = _texSamMask.GetPixel(x, y); }
                            catch { continue; }
                            if (mc.R > 127)
                            {
                                overlay.SetPixel(x, y, Color.FromArgb(90, 0, 255, 255));
                                any = true;
                            }
                        }
                    }
                    if (!any) return;
                    e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                    e.Graphics.DrawImage(overlay, ox, oy, dw, dh);
                }
            }
            catch { }
        }

        /// <summary>
        /// Direct image-pixel lookup from the Zoom preview display, accounting
        /// for SizeMode.Zoom letterboxing. Transparent clicks return false so
        /// the eyedropper stays armed.
        /// </summary>
        private bool TryPickDesatColorFromPreview(Point mouse, out Color picked)
        {
            picked = Color.Empty;
            try
            {
                var img = previewBox?.Image;
                if (img == null) return false;
                int iw, ih;
                try { iw = img.Width; ih = img.Height; }
                catch { return false; }
                if (iw <= 0 || ih <= 0) return false;

                Size cb = previewBox.ClientSize;
                if (cb.Width <= 0 || cb.Height <= 0) return false;
                float scale = Math.Min((float)cb.Width / iw, (float)cb.Height / ih);
                if (scale <= 0) return false;
                float dw = iw * scale;
                float dh = ih * scale;
                float ox = (cb.Width - dw) / 2f;
                float oy = (cb.Height - dh) / 2f;

                int imgX = (int)Math.Floor((mouse.X - ox) / scale);
                int imgY = (int)Math.Floor((mouse.Y - oy) / scale);
                if (imgX < 0 || imgY < 0 || imgX >= iw || imgY >= ih) return false;

                Color px;
                var bmp = img as Bitmap;
                if (bmp != null)
                    px = bmp.GetPixel(imgX, imgY);
                else
                {
                    using (var tmp = new Bitmap(img))
                        px = tmp.GetPixel(
                            Math.Max(0, Math.Min(tmp.Width - 1, imgX)),
                            Math.Max(0, Math.Min(tmp.Height - 1, imgY)));
                }
                if (px.A == 0) return false;
                picked = Color.FromArgb(px.R, px.G, px.B);
                return true;
            }
            catch { return false; }
        }
    }
}

namespace MeesaMultisMaker
{
    public partial class Form1
    {
        /// <summary>
        /// Show or focus the image editing window
        /// </summary>
        private void ShowImageEditingWindow()
        {
            if (_imageEditingForm == null || _imageEditingForm.IsDisposed)
            {
                _imageEditingForm = new ImageEditingForm
                {
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(this.Right - 320, this.Top + 100)
                };

                // Wire up events
                _imageEditingForm.ApplyEffects += ImageEditingForm_ApplyEffects;
                _imageEditingForm.ApplyEffectsToAll += ImageEditingForm_ApplyEffectsToAll;
                _imageEditingForm.ResetRequested += ImageEditingForm_ResetRequested;
                _imageEditingForm.RealTimePreview += ImageEditingForm_RealTimePreview;
                _imageEditingForm.EyedropperRequested += ImageEditingEyedropperRequested;
                _imageEditingForm.EyedropperCancelled += ImageEditingEyedropperCancelled;
                _imageEditingForm.SamSelectRequested += ImageEditingSamRequested;
                _imageEditingForm.SamSelectCancelled += ImageEditingSamCancelled;
                _imageEditingForm.SamClearRequested += ImageEditingSamClear;
                _imageEditingForm.SamShowChanged += ImageEditingSamShowChanged;

                _imageEditingForm.FormClosed += (s, e) => { _desatEyedropperArmed = false; _samArmed = false; _imageEditingForm = null; };
                _imageEditingForm.Show(this);

                // Update status with selection count
                int count = selectedObjects.Count;
                if (count == 0)
                {
                    _imageEditingForm.SetStatus("Ready - Select items to edit", Color.FromArgb(100, 200, 255));
                }
                else if (count == 1)
                {
                    _imageEditingForm.SetStatus($"Editing 1 item", Color.FromArgb(100, 200, 255));
                }
                else
                {
                    _imageEditingForm.SetStatus($"Editing {count} items", Color.FromArgb(100, 200, 255));
                }
            }
            else
            {
                if (!_imageEditingForm.Visible)
                    _imageEditingForm.Show(this);
                _imageEditingForm.BringToFront();
                _imageEditingForm.Focus();

                // Update status with current selection count
                int count = selectedObjects.Count;
                if (count == 0)
                {
                    _imageEditingForm.SetStatus("Ready - Select items to edit", Color.FromArgb(100, 200, 255));
                }
                else if (count == 1)
                {
                    _imageEditingForm.SetStatus($"Editing 1 item", Color.FromArgb(100, 200, 255));
                }
                else
                {
                    _imageEditingForm.SetStatus($"Editing {count} items", Color.FromArgb(100, 200, 255));
                }
            }
        }

        /// <summary>
        /// Apply image effects to all selected objects (or first palette item as a fallback)
        /// </summary>
        private void ImageEditingForm_ApplyEffects(object sender, ImageEffectEventArgs e)
        {
            if (selectedObjects.Count > 0)
            {
                PushUndo();

                int count = 0;
                foreach (var obj in selectedObjects.ToList())
                {
                    ApplyImageEffectsToObject(obj, e);
                    count++;
                }

                designPictureBox.Invalidate();
                _imageEditingForm?.SetStatus($"Applied image effects to {count} object(s)", Color.Green);
                return;
            }

            var paletteIds = GetSelectedPaletteItemIds();
            if (paletteIds.Count == 0)
            {
                _imageEditingForm?.SetStatus("No objects selected", Color.Orange);
                return;
            }

            ApplyImageEffectsToPaletteItems(new[] { paletteIds[0] }, e);
        }

        /// <summary>
        /// Apply image effects to all selected objects
        /// </summary>
        private void ImageEditingForm_ApplyEffectsToAll(object sender, ImageEffectEventArgs e)
        {
            if (selectedObjects.Count > 0)
            {
                PushUndo();

                int count = 0;
                foreach (var obj in selectedObjects.ToList())
                {
                    ApplyImageEffectsToObject(obj, e);
                    count++;
                }

                designPictureBox.Invalidate();
                _imageEditingForm?.SetStatus($"Applied effects to {count} object(s)", Color.Green);
                return;
            }

            var paletteIds = GetSelectedPaletteItemIds();
            if (paletteIds.Count == 0)
            {
                _imageEditingForm?.SetStatus("No objects selected", Color.Orange);
                return;
            }

            ApplyImageEffectsToPaletteItems(paletteIds, e);
        }

        /// <summary>
        /// Reset image editing controls to default values
        /// </summary>
        private void ImageEditingForm_ResetRequested(object sender, EventArgs e)
        {
            _imageEditingForm?.ResetControls();
            _imageEditingForm?.SetStatus("Reset to defaults", Color.FromArgb(100, 200, 255));
        }

        /// <summary>
        /// Real-time preview handler - applies effects immediately as controls change
        /// </summary>
        private void ImageEditingForm_RealTimePreview(object sender, ImageEffectEventArgs e)
        {
            if (selectedObjects.Count == 0)
            {
                const string hint = "Select items on the canvas to preview effects";
                if (_imageEditingForm?.StatusText != hint)
                    _imageEditingForm?.SetStatus(hint, Color.Orange);
                return;
            }

            // Apply to ALL selected objects for real-time preview
            foreach (var obj in selectedObjects.ToList())
            {
                ApplyImageEffectsToObject(obj, e, isPreview: true);
            }

            // Match-count readout (cheap: art is small). Uses the preview source
            // so the number matches what would actually change. SAM-gated per-object.
            try
            {
                int count = 0;
                if (e.DesaturateEnabled && e.DesaturateTargets != null && e.DesaturateTargets.Count > 0)
                {
                    foreach (var o in selectedObjects.ToList())
                    {
                        try
                        {
                            if (o == null) continue;
                            Bitmap src = (o.PreEditImage as Bitmap) ?? (o.Image as Bitmap);
                            if (src == null) continue;
                            Bitmap gate = GetCanvasSamMaskFor(o, src);
                            count += ImageEffects.CountDesaturateMatches(src, e.DesaturateTargets, e.DesaturateTolerance, e.DesaturateProtected, e.DespeckleMinSize, gate);
                        }
                        catch { }
                    }
                }
                _imageEditingForm?.SetMatchCount(count);
            }
            catch { }

            designPictureBox.Invalidate();
        }

        private List<ushort> GetSelectedPaletteItemIds()
        {
            var ids = new List<ushort>();
            if (paletteListView == null || filteredStaticIds == null)
                return ids;

            foreach (int index in paletteListView.SelectedIndices)
            {
                if (index >= 0 && index < filteredStaticIds.Count)
                    ids.Add(filteredStaticIds[index]);
            }

            return ids.Distinct().ToList();
        }

        private void ApplyImageEffectsToPaletteItems(IEnumerable<ushort> itemIds, ImageEffectEventArgs effects)
        {
            var ids = itemIds?.Distinct().ToList() ?? new List<ushort>();
            if (ids.Count == 0)
                return;

            int applied = 0;
            foreach (var itemId in ids)
            {
                var current = LoadStaticItemImage(itemId) as Bitmap;
                if (current == null)
                    continue;

                Bitmap source = new Bitmap(current);
                Bitmap result = null;
                try
                {
                    result = ImageEffects.ApplyEffects(
                        source,
                        effects.Brightness,
                        effects.Contrast,
                        effects.Hue,
                        effects.Saturation,
                        effects.PixelSize,
                        effects.PixelizeEnabled,
                        effects.PaletteColors,
                        effects.NoiseIntensity,
                        effects.DitherLevels,
                        effects.EdgeDarkening,
                        effects.ColorBands,
                        effects.FillHoles,
                        effects.RemoveBackground,
                        effects.BackgroundMode,
                        effects.BackgroundColor,
                        effects.BackgroundThreshold,
                        effects.DesaturateEnabled ? effects.DesaturateTargets : null,
                        effects.DesaturateTolerance,
                        effects.SnapNearGray,
                        effects.SnapTolerance,
                        effects.DesaturateProtected,
                        effects.DespeckleMinSize
                    );
                }
                finally
                {
                    source.Dispose();
                }

                if (result == null)
                    continue;

                lock (imageCache)
                {
                    imageCache[$"0x{itemId:X4}"] = result;
                }

                MarkArtModified(itemId, result);

                foreach (var obj in placedObjects)
                {
                    if (TryParseGraphicId(obj.GraphicId, out int objItemId) && objItemId == itemId)
                    {
                        obj.PreEditImage = obj.PreEditImage ?? new Bitmap(obj.Image);
                        obj.Image = new Bitmap(result);
                        obj.IsEdited = true;
                    }
                }

                applied++;
            }

            paletteListView?.Invalidate();
            designPictureBox?.Invalidate();

            if (applied > 0)
            {
                _imageEditingForm?.SetStatus($"Applied effects to {applied} palette item(s)", Color.Green);
            }
            else
            {
                _imageEditingForm?.SetStatus("No palette images could be processed", Color.Orange);
            }
        }

        /// <summary>
        /// Apply image effects to a single PlacedObject
        /// </summary>
        private void ApplyImageEffectsToObject(PlacedObject obj, ImageEffectEventArgs effects, bool isPreview = false)
        {
            if (obj.Image == null) return;

            try
            {
                // Backup original image if not already done
                if (obj.PreEditImage == null)
                {
                    obj.PreEditImage = new Bitmap(obj.Image);
                }

                // For preview mode, always use the PreEditImage as source
                // For final apply, also use PreEditImage to ensure clean application.
                // SAM gates desat/snap: mask applies only when WxH matches source.
                Bitmap source = obj.PreEditImage != null ? new Bitmap(obj.PreEditImage) : new Bitmap(obj.Image);
                Bitmap samGate = GetCanvasSamMaskFor(obj, source);
                Bitmap result = ImageEffects.ApplyEffects(
                    source,
                    effects.Brightness,
                    effects.Contrast,
                    effects.Hue,
                    effects.Saturation,
                    effects.PixelSize,
                    effects.PixelizeEnabled,
                    effects.PaletteColors,
                    effects.NoiseIntensity,
                    effects.DitherLevels,
                    effects.EdgeDarkening,
                    effects.ColorBands,
                    effects.FillHoles,
                    effects.RemoveBackground,
                    effects.BackgroundMode,
                    effects.BackgroundColor,
                    effects.BackgroundThreshold,
                    effects.DesaturateEnabled ? effects.DesaturateTargets : null,
                    effects.DesaturateTolerance,
                    effects.SnapNearGray,
                    effects.SnapTolerance,
                    effects.DesaturateProtected,
                    effects.DespeckleMinSize,
                    samGate
                );

                // Update object image
                if (!isPreview)
                {
                    // Only dispose the old image if this is a final apply (not preview)
                    obj.Image?.Dispose();
                }
                obj.Image = result;
                obj.IsEdited = true;

                source.Dispose();
            }
            catch (Exception ex)
            {
                _imageEditingForm?.SetStatus($"Error: {ex.Message}", Color.Red);
                outputTextBox.AppendText($"Image editing error on {obj.GraphicId}: {ex.Message}\r\n");
            }
        }

        /// <summary>
        /// Revert image edits for selected objects
        /// </summary>
        private void RevertImageEdits()
        {
            if (selectedObjects.Count == 0)
            {
                _imageEditingForm?.SetStatus("No objects selected", Color.Orange);
                return;
            }

            PushUndo();

            int count = 0;
            foreach (var obj in selectedObjects.ToList())
            {
                if (obj.PreEditImage != null)
                {
                    obj.Image?.Dispose();
                    obj.Image = new Bitmap(obj.PreEditImage);
                    obj.PreEditImage?.Dispose();
                    obj.PreEditImage = null;
                    obj.IsEdited = false;
                    count++;
                }
            }

            designPictureBox.Invalidate();
            _imageEditingForm?.SetStatus($"Reverted {count} object(s)", Color.Green);
            outputTextBox.AppendText($"Reverted image edits on {count} object(s)\r\n");
        }
    }
}
