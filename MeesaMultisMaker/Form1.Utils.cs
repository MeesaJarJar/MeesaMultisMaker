using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace MeesaMultisMaker
{
    public partial class Form1
    {
        private static Image LoadImageUnlocked(string path)
        {
            using (var src = Image.FromFile(path))
            {
                return new Bitmap(src);
            }
        }

        private Point GridToIso(int gridX, int gridY)
        {
            // world to screen with pan+zoom
            float isoX = (gridX - gridY) * (TILE_WIDTH / 2f);
            float isoY = (gridX + gridY) * (TILE_HEIGHT / 2f);
            int sx = canvasOffset.X + (int)Math.Round(isoX * zoom);
            int sy = canvasOffset.Y + (int)Math.Round(isoY * zoom);
            return new Point(sx, sy);
        }

        private Point IsoToGrid(int isoX, int isoY)
        {
            // screen to world with inverse zoom and pan
            float relX = (isoX - canvasOffset.X) / zoom;
            float relY = (isoY - canvasOffset.Y) / zoom;
            float fx = relX / (TILE_WIDTH / 2f);
            float fy = relY / (TILE_HEIGHT / 2f);
            float gx = (fx + fy) / 2f;
            float gy = (fy - fx) / 2f;
            int gridX = (int)Math.Round(gx, MidpointRounding.AwayFromZero);
            int gridY = (int)Math.Round(gy, MidpointRounding.AwayFromZero);
            return new Point(gridX, gridY);
        }

        private static string EnsureHexPrefix(string id)
        {
            if (id.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return id.ToUpperInvariant();
            int val;
            if (int.TryParse(id, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out val)) return $"0x{val:X4}";
            return id;
        }

        private static bool TryExtractHexId(string text, out string hex4)
        {
            hex4 = null;
            if (string.IsNullOrEmpty(text)) return false;
            var m = Regex.Matches(text, "0[xX]([0-9A-Fa-f]{4,})|([0-9A-Fa-f]{4,})");
            if (m.Count == 0) return false;
            string digits = null;
            var last = m[m.Count - 1];
            if (last.Groups[1].Success) digits = last.Groups[1].Value; else if (last.Groups[2].Success) digits = last.Groups[2].Value;
            if (string.IsNullOrEmpty(digits) || digits.Length < 4) return false;
            hex4 = digits.Substring(digits.Length - 4).ToUpperInvariant();
            return true;
        }

        private bool TryResolveIdToPath(string id, out string path)
        {
            path = null;
            if (string.IsNullOrWhiteSpace(id)) return false;
            if (idToPath.TryGetValue(id, out path))
            {
                if (File.Exists(path)) return true;
            }
            var withPrefix = EnsureHexPrefix(id);
            if (idToPath.TryGetValue(withPrefix, out path))
            {
                if (File.Exists(path)) return true;
            }
            var raw = id.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? id.Substring(2) : id;
            raw = raw.ToUpperInvariant();
            if (raw.Length == 3) raw = "0" + raw;
            if (idToPath.TryGetValue(raw, out path))
            {
                if (File.Exists(path)) return true;
            }
            path = null;
            return false;
        }
    }
}
