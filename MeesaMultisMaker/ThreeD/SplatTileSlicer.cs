using System;
using System.Collections.Generic;

namespace MeesaMultisMaker.ThreeD
{
    /// <summary>
    /// UO tile slicer: hard-cut a splat cloud into per-tile columns on the
    /// viewer's floor grid.
    ///
    /// Bins live in floor space: points baked through the Rot/Scale dials
    /// (mpos) then yawed by the slice-time camera yaw -- the same ground
    /// coordinates the renderer uses. The grid itself is origin-centered
    /// with the exact extent formula the viewport ground grid draws, so
    /// bin edges ARE floor lines at any yaw or scale: rescaling shrinks
    /// the model onto fewer cells (each tile stays full), panning slides
    /// it across cells, orbiting re-cuts along the new floor view. Hard
    /// cut, no blending.
    ///
    /// Each tile keeps full height (Y preserved). Empty tiles are omitted
    /// but keep their (TX,TZ) coords for canvas mapping.
    /// </summary>
    public class TilePiece
    {
        public int TX;
        public int TZ;
        public List<int> Indices = new List<int>();
        // Baked-space bounds (after Rot/Scale, for info).
        public float MinX, MaxX, MinY, MaxY, MinZ, MaxZ;
        // Recenter target: bin center inverse-yawed WITHOUT the object
        // transform. BakePiece subtracts it from baked points and the
        // renderer applies yaw after, so R*C == slice-space bin center and
        // content renders centered. (Folding mpos in here shifts every tile
        // off its diamond -- see FinishPieces.)
        public float CenterX, CenterZ;
        public int Count { get { return Indices != null ? Indices.Count : 0; } }
        public string Label { get { return string.Format("Tile ({0},{1}) - {2} splats", TX, TZ, Count); } }
    }

    public class TileSliceResult
    {
        public List<TilePiece> Pieces = new List<TilePiece>();
        public int TilesX;
        public int TilesZ;
        // Floor-grid extent the bins were cut on (origin-centered [-e,e]).
        public double FootMinX, FootMaxX, FootMinZ, FootMaxZ;
        public double TileSizeX, TileSizeZ;
        public float ModMinX, ModMaxX, ModMinZ, ModMaxZ, ModMinY;
        public float SliceRX, SliceRY, SliceRZ, SliceSX, SliceSY, SliceSZ;
        public float SliceYaw;
    }

    public static class SplatTileSlicer
    {
        /// <summary>
        /// Slice the model into per-tile columns on the floor grid.
        /// Membership uses dials-baked points yawed by sliceYawDeg, so the
        /// bins track the model exactly as rendered (rescale = fewer,
        /// fuller tiles). mpos = ScaledMatrix(ObjectMatrix(...)) matching
        /// the viewport dials. yMin/yMax optionally restrict to a vertical
        /// band (baked Y).
        /// </summary>
        public static TileSliceResult Slice(PlySplatModel model, int tilesX, int tilesZ,
            double[] mpos, double yMin = double.NegativeInfinity,
            double yMax = double.PositiveInfinity,
            float sliceRX = 0f, float sliceRY = 0f, float sliceRZ = 0f,
            float sliceSX = 1f, float sliceSY = 1f, float sliceSZ = 1f,
            float sliceYawDeg = 0f)
        {
            var result = new TileSliceResult { TilesX = tilesX, TilesZ = tilesZ };
            if (model == null || model.Points == null || model.Points.Count == 0)
                return result;
            if (tilesX < 1) tilesX = 1;
            if (tilesZ < 1) tilesZ = 1;
            if (tilesX > 32) tilesX = 32;
            if (tilesZ > 32) tilesZ = 32;
            result.TilesX = tilesX;
            result.TilesZ = tilesZ;

            // Floor-grid extent from the load-time bounds (same numbers the
            // viewport ground grid uses): panning or editing points never
            // moves the floor, so the bins stay glued to its lines.
            FloorExtent(model, out double e, out double groundY);
            double cellX = 2 * e / tilesX;
            double cellZ = 2 * e / tilesZ;
            result.FootMinX = -e; result.FootMaxX = e;
            result.FootMinZ = -e; result.FootMaxZ = e;
            result.TileSizeX = cellX;
            result.TileSizeZ = cellZ;
            result.ModMinX = model.NMinX; result.ModMaxX = model.NMaxX;
            result.ModMinZ = model.NMinZ; result.ModMaxZ = model.NMaxZ;
            result.ModMinY = model.NMinY;
            result.SliceRX = sliceRX; result.SliceRY = sliceRY; result.SliceRZ = sliceRZ;
            result.SliceSX = sliceSX; result.SliceSY = sliceSY; result.SliceSZ = sliceSZ;
            result.SliceYaw = sliceYawDeg;

            double yr = sliceYawDeg * Math.PI / 180.0;
            double cosY = Math.Cos(yr), sinY = Math.Sin(yr);

            var pts = model.Points;
            bool hasM = mpos != null && mpos.Length >= 9;
            var grid = new Dictionary<long, TilePiece>();
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                double qx, qy, qz;
                if (hasM)
                    SplatRenderer.TransformPoint(mpos, p.X, p.Y, p.Z, out qx, out qy, out qz);
                else
                {
                    qx = p.X; qy = p.Y; qz = p.Z;
                }
                if (qy < yMin || qy > yMax) continue;
                double x1 = qx * cosY + qz * sinY;
                double z1 = -qx * sinY + qz * cosY;
                int tx = (int)Math.Floor((x1 + e) / cellX);
                int tz = (int)Math.Floor((z1 + e) / cellZ);
                if (tx < 0) tx = 0; else if (tx >= tilesX) tx = tilesX - 1;
                if (tz < 0) tz = 0; else if (tz >= tilesZ) tz = tilesZ - 1;
                long key = ((long)tx << 32) | (uint)tz;
                TilePiece piece;
                if (!grid.TryGetValue(key, out piece))
                {
                    piece = new TilePiece { TX = tx, TZ = tz };
                    grid[key] = piece;
                }
                piece.Indices.Add(i);
            }

            FinishPieces(model, result, grid);

            var sorted = new List<TilePiece>(grid.Values);
            sorted.Sort((a, b) =>
            {
                int c = a.TZ.CompareTo(b.TZ);
                return c != 0 ? c : a.TX.CompareTo(b.TX);
            });
            result.Pieces = sorted;
            return result;
        }

        /// <summary>
        /// Same origin-centered extent formula as the viewport ground grid.
        /// Load-time bounds: the floor must not follow point edits.
        /// </summary>
        internal static void FloorExtent(PlySplatModel model, out double e, out double groundY)
        {
            double foot = 0.8;
            double gy = 0;
            try
            {
                foot = Math.Max(
                    Math.Max(Math.Abs((double)model.NMinX), Math.Abs((double)model.NMaxX)),
                    Math.Max(Math.Abs((double)model.NMinZ), Math.Abs((double)model.NMaxZ)));
                gy = model.NMinY;
            }
            catch { }
            if (!(foot >= 0.8)) foot = 0.8;
            if (!(gy <= 1e9)) gy = 0;
            e = foot + 0.4;
            groundY = gy;
        }

        /// <summary>
        /// Raw-frame bounds plus recenter targets. The target MUST be the
        /// bin center inverse-yawed WITHOUT the object transform: BakePiece
        /// subtracts it in baked space and the renderer then applies the
        /// yaw, so render-yaw of (q - C) centers content iff R*C equals the
        /// slice-space bin center, i.e. C = R(-yaw)*B. Folding mpos in here
        /// shifts every tile off its diamond by (M-I)*B -- a systematic
        /// misplacement on all tiles (worst at the grid edges).
        /// </summary>
        private static void FinishPieces(PlySplatModel model, TileSliceResult result,
            Dictionary<long, TilePiece> grid)
        {
            double e = result.FootMaxX;
            double cellX = result.TileSizeX, cellZ = result.TileSizeZ;
            double yr = result.SliceYaw * Math.PI / 180.0;
            double cosY = Math.Cos(yr), sinY = Math.Sin(yr);
            var pts = model.Points;
            foreach (var piece in grid.Values)
            {
                float pMinX = float.MaxValue, pMaxX = float.MinValue;
                float pMinY = float.MaxValue, pMaxY = float.MinValue;
                float pMinZ = float.MaxValue, pMaxZ = float.MinValue;
                foreach (int i in piece.Indices)
                {
                    if (i < 0 || i >= pts.Count) continue;
                    var p = pts[i];
                    if (p.X < pMinX) pMinX = p.X; if (p.X > pMaxX) pMaxX = p.X;
                    if (p.Y < pMinY) pMinY = p.Y; if (p.Y > pMaxY) pMaxY = p.Y;
                    if (p.Z < pMinZ) pMinZ = p.Z; if (p.Z > pMaxZ) pMaxZ = p.Z;
                }
                piece.MinX = pMinX; piece.MaxX = pMaxX;
                piece.MinY = pMinY; piece.MaxY = pMaxY;
                piece.MinZ = pMinZ; piece.MaxZ = pMaxZ;
                // Bin center in slice (yaw) space, back to model frame via
                // inverse yaw. Deliberately WITHOUT the object transform:
                // BakePiece subtracts this in baked space and the renderer
                // applies yaw after, so content centers iff R*C == B.
                double bcx = -e + (piece.TX + 0.5) * cellX;
                double bcz = -e + (piece.TZ + 0.5) * cellZ;
                piece.CenterX = (float)(bcx * cosY - bcz * sinY);
                piece.CenterZ = (float)(bcx * sinY + bcz * cosY);
            }
        }

        /// <summary>
        /// Re-derive piece membership after a count-changing edit (delete,
        /// clone, undo): every surviving splat is re-binned into the SAME
        /// stored floor grid (same extent, same slice-time yaw), so tiles
        /// survive edits instead of being wiped. Empty pieces drop out. The
        /// dial snapshot refreshes to the transform used for centers here.
        /// </summary>
        public static void Reclassify(PlySplatModel model, TileSliceResult slice,
            double[] mpos, float rx, float ry, float rz,
            float scX, float scY, float scZ)
        {
            slice.Pieces.Clear();
            if (model == null || model.Points == null || model.Points.Count == 0)
                return;
            int tilesX = slice.TilesX, tilesZ = slice.TilesZ;
            double e = slice.FootMaxX; // stored as +e (FootMinX = -e)
            if (!(e > 0)) return;
            double cellX = slice.TileSizeX, cellZ = slice.TileSizeZ;
            if (!(cellX > 0) || !(cellZ > 0)) return;
            double yr = slice.SliceYaw * Math.PI / 180.0;
            double cosY = Math.Cos(yr), sinY = Math.Sin(yr);

            var grid = new Dictionary<long, TilePiece>();
            var pts = model.Points;
            bool hasM = mpos != null && mpos.Length >= 9;
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                double qx, qy, qz;
                if (hasM)
                    SplatRenderer.TransformPoint(mpos, p.X, p.Y, p.Z, out qx, out qy, out qz);
                else
                {
                    qx = p.X; qy = p.Y; qz = p.Z;
                }
                double x1 = qx * cosY + qz * sinY;
                double z1 = -qx * sinY + qz * cosY;
                int tx = (int)Math.Floor((x1 + e) / cellX);
                int tz = (int)Math.Floor((z1 + e) / cellZ);
                if (tx < 0) tx = 0; else if (tx >= tilesX) tx = tilesX - 1;
                if (tz < 0) tz = 0; else if (tz >= tilesZ) tz = tilesZ - 1;
                long key = ((long)tx << 32) | (uint)tz;
                TilePiece piece;
                if (!grid.TryGetValue(key, out piece))
                {
                    piece = new TilePiece { TX = tx, TZ = tz };
                    grid[key] = piece;
                }
                piece.Indices.Add(i);
            }

            FinishPieces(model, slice, grid);

            var sorted = new List<TilePiece>(grid.Values);
            sorted.Sort((a, b) =>
            {
                int c = a.TZ.CompareTo(b.TZ);
                return c != 0 ? c : a.TX.CompareTo(b.TX);
            });
            slice.Pieces = sorted;
            slice.SliceRX = rx; slice.SliceRY = ry; slice.SliceRZ = rz;
            slice.SliceSX = scX; slice.SliceSY = scY; slice.SliceSZ = scZ;
        }

        /// <summary>
        /// Bake a piece into a standalone model for rendering/export.
        /// Positions (and normals/covariances) take the current object
        /// transform; when recenter=true the tile center is subtracted so
        /// the sprite is centered on its own tile (correct for canvas
        /// placement at GridX/GridY). Render the result with identity
        /// Rot/Scale (0,0,0 / 1,1,1).
        /// </summary>
        public static PlySplatModel BakePiece(PlySplatModel model, TilePiece piece,
            double[] mpos, double[] robj, float scX, float scY, float scZ,
            bool recenter)
        {
            var out_ = new PlySplatModel();
            if (model == null || piece == null) return out_;
            if (scX <= 0) scX = 1f;
            if (scY <= 0) scY = 1f;
            if (scZ <= 0) scZ = 1f;
            bool hasM = mpos != null && mpos.Length >= 9;
            bool hasR = robj != null && robj.Length >= 9;
            float qw, qx, qy, qz;
            if (hasR)
                SplatRenderer.MatrixToQuat(robj, out qw, out qx, out qy, out qz);
            else
            {
                qw = 1; qx = qy = qz = 0;
            }
            foreach (int i in piece.Indices)
            {
                var p = model.Points[i];
                double bx, by, bz;
                if (hasM)
                    SplatRenderer.TransformPoint(mpos, p.X, p.Y, p.Z, out bx, out by, out bz);
                else
                {
                    bx = p.X; by = p.Y; bz = p.Z;
                }
                if (recenter)
                {
                    bx -= piece.CenterX;
                    bz -= piece.CenterZ;
                }
                var q = p;
                q.X = (float)bx; q.Y = (float)by; q.Z = (float)bz;
                // Covariance: rotate quaternion, stretch sigmas.
                if (hasR || scX != 1f || scY != 1f || scZ != 1f)
                {
                    float rw2, rx2, ry2, rz2;
                    QuatMultiply(qw, qx, qy, qz, p.Qw, p.Qx, p.Qy, p.Qz,
                        out rw2, out rx2, out ry2, out rz2);
                    q.Qw = rw2; q.Qx = rx2; q.Qy = ry2; q.Qz = rz2;
                    q.S0 = p.S0 * scX; q.S1 = p.S1 * scY; q.S2 = p.S2 * scZ;
                    double nx = p.Nx, ny = p.Ny, nz = p.Nz;
                    if (hasR)
                    {
                        double rx = robj[0] * nx + robj[1] * ny + robj[2] * nz;
                        double ry = robj[3] * nx + robj[4] * ny + robj[5] * nz;
                        double rz = robj[6] * nx + robj[7] * ny + robj[8] * nz;
                        nx = rx; ny = ry; nz = rz;
                    }
                    double nl = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                    if (nl > 1e-9)
                    {
                        q.Nx = (float)(nx / nl); q.Ny = (float)(ny / nl); q.Nz = (float)(nz / nl);
                    }
                }
                out_.Points.Add(q);
            }
            SplatCage.RecalcBounds(out_);
            out_.MinX = out_.NMinX; out_.MinY = out_.NMinY; out_.MinZ = out_.NMinZ;
            out_.MaxX = out_.NMaxX; out_.MaxY = out_.NMaxY; out_.MaxZ = out_.NMaxZ;
            return out_;
        }

        private static void QuatMultiply(float aw, float ax, float ay, float az,
            float bw, float bx, float by, float bz,
            out float rw, out float rx, out float ry, out float rz)
        {
            rw = aw * bw - ax * bx - ay * by - az * bz;
            rx = aw * bx + ax * bw + ay * bz - az * by;
            ry = aw * by - ax * bz + ay * bw + az * bx;
            rz = aw * bz + ax * by - ay * bx + az * bw;
            double n = Math.Sqrt((double)rw * rw + (double)rx * rx + (double)ry * ry + (double)rz * rz);
            if (n < 1e-9) { rw = 1; rx = ry = rz = 0; return; }
            rw = (float)(rw / n); rx = (float)(rx / n);
            ry = (float)(ry / n); rz = (float)(rz / n);
        }
    }
}
