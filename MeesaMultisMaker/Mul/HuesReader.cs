using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace MeesaMultisMaker.Mul
{
    /// <summary>
    /// Reads hue color palettes from hues.mul.
    /// 
    /// File format:
    /// - 3000 hue groups, each 708 bytes:
    ///   - 4-byte header (int32)
    ///   - 8 hue entries, each 88 bytes:
    ///     - 32 color values (ushort each, 64 bytes) in RGB555 format
    ///     - 2-byte table start
    ///     - 2-byte table end
    ///     - 20-byte name (ASCII, null-terminated)
    ///
    /// Hue index 0 means "no hue" (use original colors).
    /// Actual hue indices are 1-based; internally stored 0-based.
    /// </summary>
    public class HuesReader
    {
        private const int MAX_HUES = 3000;
        private const int COLORS_PER_HUE = 32;
        private const int HUE_ENTRY_SIZE = 88;   // 32*2 + 2 + 2 + 20
        private const int HUE_GROUP_SIZE = 708;   // 4 + 8*88

        private Color[][] _hues;
        private bool _isLoaded;

        public bool IsLoaded => _isLoaded;

        /// <summary>
        /// Load hues.mul from the specified MUL folder.
        /// </summary>
        public bool Load(string mulFolder)
        {
            string huesPath = Path.Combine(mulFolder, "hues.mul");
            if (!File.Exists(huesPath))
                return false;

            _isLoaded = false;

            try
            {
                using (var fs = new FileStream(huesPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var reader = new BinaryReader(fs))
                {
                    int totalHues = MAX_HUES * 8;
                    _hues = new Color[totalHues][];

                    int hueIndex = 0;

                    for (int group = 0; group < MAX_HUES; group++)
                    {
                        long groupStart = (long)group * HUE_GROUP_SIZE;
                        if (groupStart + HUE_GROUP_SIZE > fs.Length)
                            break;

                        fs.Seek(groupStart, SeekOrigin.Begin);
                        reader.ReadInt32(); // header

                        for (int entry = 0; entry < 8; entry++)
                        {
                            var colors = new Color[COLORS_PER_HUE];

                            for (int c = 0; c < COLORS_PER_HUE; c++)
                            {
                                ushort rgb555 = reader.ReadUInt16();
                                colors[c] = Rgb555ToColor(rgb555);
                            }

                            reader.ReadUInt16(); // tableStart
                            reader.ReadUInt16(); // tableEnd
                            reader.ReadBytes(20); // name

                            _hues[hueIndex] = colors;
                            hueIndex++;
                        }
                    }

                    _isLoaded = hueIndex > 0;
                    System.Diagnostics.Debug.WriteLine($"HuesReader: Loaded {hueIndex} hues from hues.mul");
                    return _isLoaded;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading hues.mul: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get the color palette for a hue. Hue IDs are 1-based;
        /// ID 0 means "no hue". Returns null if the hue is not found.
        /// </summary>
        public Color[] GetHueColors(int hueId)
        {
            if (!_isLoaded || _hues == null)
                return null;

            // Hue IDs are 1-based; 0 means no hue
            int index = (hueId & 0x3FFF) - 1;
            if (index < 0 || index >= _hues.Length)
                return null;

            return _hues[index];
        }

        /// <summary>
        /// Applies a hue to an image, returning a new hued copy.
        /// If partialHue is true, only gray pixels are recolored.
        /// If partialHue is false, all pixels are recolored.
        /// </summary>
        public Bitmap ApplyHue(Image source, int hueId, bool partialHue)
        {
            var colors = GetHueColors(hueId);
            if (colors == null || source == null)
                return source != null ? new Bitmap(source) : null;

            var src = new Bitmap(source);
            var rect = new Rectangle(0, 0, src.Width, src.Height);
            var data = src.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);

            int byteCount = data.Stride * data.Height;
            byte[] pixels = new byte[byteCount];
            Marshal.Copy(data.Scan0, pixels, 0, byteCount);

            for (int i = 0; i < byteCount; i += 4)
            {
                byte b = pixels[i];
                byte g = pixels[i + 1];
                byte r = pixels[i + 2];
                byte a = pixels[i + 3];

                if (a == 0) continue;

                bool isGray = (r == g && g == b);

                if (partialHue && !isGray)
                    continue;

                // Map the pixel's brightness to the 32-entry hue palette.
                // Use the brightest channel as the luminance key.
                int lum = Math.Max(r, Math.Max(g, b));
                int idx = (lum * 31) / 255;
                if (idx < 0) idx = 0;
                if (idx > 31) idx = 31;

                Color hc = colors[idx];
                pixels[i] = hc.B;
                pixels[i + 1] = hc.G;
                pixels[i + 2] = hc.R;
                // keep original alpha
            }

            Marshal.Copy(pixels, 0, data.Scan0, byteCount);
            src.UnlockBits(data);
            return src;
        }

        /// <summary>
        /// Converts a 16-bit RGB555 value to a Color.
        /// </summary>
        private static Color Rgb555ToColor(ushort rgb555)
        {
            int r = ((rgb555 >> 10) & 0x1F) * 255 / 31;
            int g = ((rgb555 >> 5) & 0x1F) * 255 / 31;
            int b = (rgb555 & 0x1F) * 255 / 31;
            return Color.FromArgb(255, r, g, b);
        }
    }
}
