using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;

namespace MeesaMultisMaker.Utils
{
    /// <summary>
    /// Image processing utility methods for effects like brightness, contrast, hue, saturation, pixelize, and color palette reduction
    /// </summary>
    public static class ImageEffects
    {
        private static Random _noiseRandom = new Random();

        /// <summary>
        /// Applies brightness adjustment to an image (-1.0 to 1.0)
        /// </summary>
        public static Bitmap AdjustBrightness(Bitmap source, float brightness)
        {
            if (brightness == 0) return new Bitmap(source);

            var result = new Bitmap(source.Width, source.Height);
            int offset = (int)(brightness * 255);

            using (var g = Graphics.FromImage(result))
            {
                var attributes = new ImageAttributes();

                float[][] colorMatrix = {
                    new float[] {1, 0, 0, 0, 0},
                    new float[] {0, 1, 0, 0, 0},
                    new float[] {0, 0, 1, 0, 0},
                    new float[] {0, 0, 0, 1, 0},
                    new float[] {offset / 255f, offset / 255f, offset / 255f, 0, 1}
                };

                attributes.SetColorMatrix(new ColorMatrix(colorMatrix));
                g.DrawImage(source, new Rectangle(0, 0, result.Width, result.Height),
                    0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
            }

            return result;
        }

        /// <summary>
        /// Applies contrast adjustment to an image (-1.0 to 1.0)
        /// </summary>
        public static Bitmap AdjustContrast(Bitmap source, float contrast)
        {
            if (contrast == 0) return new Bitmap(source);

            var result = new Bitmap(source.Width, source.Height);
            float factor = (1f + contrast) * (1f + contrast);
            float translate = 0.5f - factor * 0.5f;

            using (var g = Graphics.FromImage(result))
            {
                var attributes = new ImageAttributes();

                float[][] colorMatrix = {
                    new float[] {factor, 0, 0, 0, 0},
                    new float[] {0, factor, 0, 0, 0},
                    new float[] {0, 0, factor, 0, 0},
                    new float[] {0, 0, 0, 1, 0},
                    new float[] {translate, translate, translate, 0, 1}
                };

                attributes.SetColorMatrix(new ColorMatrix(colorMatrix));
                g.DrawImage(source, new Rectangle(0, 0, result.Width, result.Height),
                    0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
            }

            return result;
        }

        /// <summary>
        /// Shifts the hue of an image (0-360 degrees)
        /// </summary>
        public static Bitmap AdjustHue(Bitmap source, float hueShift)
        {
            if (hueShift == 0) return new Bitmap(source);

            var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);

            BitmapData sourceData = source.LockBits(new Rectangle(0, 0, source.Width, source.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData resultData = result.LockBits(new Rectangle(0, 0, result.Width, result.Height),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

            int bytes = Math.Abs(sourceData.Stride) * source.Height;
            byte[] sourceBuffer = new byte[bytes];
            byte[] resultBuffer = new byte[bytes];

            Marshal.Copy(sourceData.Scan0, sourceBuffer, 0, bytes);

            for (int i = 0; i < bytes; i += 4)
            {
                byte b = sourceBuffer[i];
                byte g = sourceBuffer[i + 1];
                byte r = sourceBuffer[i + 2];
                byte a = sourceBuffer[i + 3];

                // Convert RGB to HSV
                float h, s, v;
                RgbToHsv(r, g, b, out h, out s, out v);

                // Shift hue
                h = (h + hueShift) % 360;
                if (h < 0) h += 360;

                // Convert back to RGB
                Color newColor = HsvToRgb(h, s, v);
                resultBuffer[i] = newColor.B;
                resultBuffer[i + 1] = newColor.G;
                resultBuffer[i + 2] = newColor.R;
                resultBuffer[i + 3] = a; // Preserve alpha
            }

            Marshal.Copy(resultBuffer, 0, resultData.Scan0, bytes);

            source.UnlockBits(sourceData);
            result.UnlockBits(resultData);

            return result;
        }

        /// <summary>
        /// Adjusts saturation of an image (-1.0 to 1.0)
        /// </summary>
        public static Bitmap AdjustSaturation(Bitmap source, float saturation)
        {
            if (saturation == 0) return new Bitmap(source);

            var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);

            BitmapData sourceData = source.LockBits(new Rectangle(0, 0, source.Width, source.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData resultData = result.LockBits(new Rectangle(0, 0, result.Width, result.Height),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

            int bytes = Math.Abs(sourceData.Stride) * source.Height;
            byte[] sourceBuffer = new byte[bytes];
            byte[] resultBuffer = new byte[bytes];

            Marshal.Copy(sourceData.Scan0, sourceBuffer, 0, bytes);

            float satFactor = 1f + saturation;

            for (int i = 0; i < bytes; i += 4)
            {
                byte b = sourceBuffer[i];
                byte g = sourceBuffer[i + 1];
                byte r = sourceBuffer[i + 2];
                byte a = sourceBuffer[i + 3];

                // Convert RGB to HSV
                float h, s, v;
                RgbToHsv(r, g, b, out h, out s, out v);

                // Adjust saturation
                s = Math.Max(0, Math.Min(1, s * satFactor));

                // Convert back to RGB
                Color newColor = HsvToRgb(h, s, v);
                resultBuffer[i] = newColor.B;
                resultBuffer[i + 1] = newColor.G;
                resultBuffer[i + 2] = newColor.R;
                resultBuffer[i + 3] = a; // Preserve alpha
            }

            Marshal.Copy(resultBuffer, 0, resultData.Scan0, bytes);

            source.UnlockBits(sourceData);
            result.UnlockBits(resultData);

            return result;
        }

        /// <summary>
        /// Applies pixelize effect to an image (supports pixelSize of 1 for no effect)
        /// </summary>
        public static Bitmap Pixelize(Bitmap source, int pixelSize)
        {
            if (pixelSize <= 1) return new Bitmap(source);

            var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);

            // Use direct pixel manipulation for finer control
            BitmapData sourceData = source.LockBits(new Rectangle(0, 0, source.Width, source.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData resultData = result.LockBits(new Rectangle(0, 0, result.Width, result.Height),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

            int bytes = Math.Abs(sourceData.Stride) * source.Height;
            byte[] sourceBuffer = new byte[bytes];
            byte[] resultBuffer = new byte[bytes];

            Marshal.Copy(sourceData.Scan0, sourceBuffer, 0, bytes);

            int stride = sourceData.Stride;
            int width = source.Width;
            int height = source.Height;

            // Process in blocks
            for (int blockY = 0; blockY < height; blockY += pixelSize)
            {
                for (int blockX = 0; blockX < width; blockX += pixelSize)
                {
                    // Calculate average color for this block
                    int totalR = 0, totalG = 0, totalB = 0, totalA = 0;
                    int count = 0;

                    int maxY = Math.Min(blockY + pixelSize, height);
                    int maxX = Math.Min(blockX + pixelSize, width);

                    for (int y = blockY; y < maxY; y++)
                    {
                        for (int x = blockX; x < maxX; x++)
                        {
                            int idx = y * stride + x * 4;
                            totalB += sourceBuffer[idx];
                            totalG += sourceBuffer[idx + 1];
                            totalR += sourceBuffer[idx + 2];
                            totalA += sourceBuffer[idx + 3];
                            count++;
                        }
                    }

                    if (count > 0)
                    {
                        byte avgB = (byte)(totalB / count);
                        byte avgG = (byte)(totalG / count);
                        byte avgR = (byte)(totalR / count);
                        byte avgA = (byte)(totalA / count);

                        // Fill block with average color
                        for (int y = blockY; y < maxY; y++)
                        {
                            for (int x = blockX; x < maxX; x++)
                            {
                                int idx = y * stride + x * 4;
                                resultBuffer[idx] = avgB;
                                resultBuffer[idx + 1] = avgG;
                                resultBuffer[idx + 2] = avgR;
                                resultBuffer[idx + 3] = avgA;
                            }
                        }
                    }
                }
            }

            Marshal.Copy(resultBuffer, 0, resultData.Scan0, bytes);

            source.UnlockBits(sourceData);
            result.UnlockBits(resultData);

            return result;
        }

        /// <summary>
        /// Reduces the color palette of an image to specified number of colors
        /// Uses median cut algorithm for better quality
        /// </summary>
        public static Bitmap ReduceColorPalette(Bitmap source, int colorCount)
        {
            // require at least 2 colors and cap at 256
            if (colorCount < 2 || colorCount > 256) return new Bitmap(source);

            var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);

            // Extract all colors from the image
            var colors = new System.Collections.Generic.List<Color>();
            for (int y = 0; y < source.Height; y++)
            {
                for (int x = 0; x < source.Width; x++)
                {
                    Color c = source.GetPixel(x, y);
                    if (c.A > 0) // Only consider non-transparent pixels
                        colors.Add(c);
                }
            }

            if (colors.Count == 0) return new Bitmap(source);

            // Generate palette by sampling colors from the image using k-means clustering
            var palette = GeneratePaletteFromColors(colors, colorCount);

            // Map each pixel to nearest palette color
            for (int y = 0; y < source.Height; y++)
            {
                for (int x = 0; x < source.Width; x++)
                {
                    Color original = source.GetPixel(x, y);
                    if (original.A == 0)
                    {
                        result.SetPixel(x, y, Color.Transparent);
                    }
                    else
                    {
                        Color nearest = FindNearestColor(original, palette);
                        result.SetPixel(x, y, Color.FromArgb(original.A, nearest));
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Applies multiple effects in sequence (updated to include new UO-style effects)
        /// </summary>
        public static Bitmap ApplyEffects(Bitmap source, float brightness, float contrast,
            float hue, float saturation, int pixelSize, bool pixelize, int paletteColors,
            int noiseIntensity = 0, int ditherLevels = 0, int edgeDarkening = 0, int colorBands = 0, bool fillHoles = false,
            bool removeBackground = false, string backgroundMode = "Auto (corners)", Color? customBackgroundColor = null, int backgroundThreshold = 30)
        {
            Bitmap result = new Bitmap(source);
            Bitmap temp;

            try
            {
                // Remove background FIRST (before any other effects)
                if (removeBackground)
                {
                    Color bgColor = customBackgroundColor ?? Color.White;
                    temp = RemoveBackground(result, backgroundMode, bgColor, backgroundThreshold);
                    result.Dispose();
                    result = temp;
                }

                // Fill holes before other effects
                if (fillHoles)
                {
                    temp = FillSmallHoles(result);
                    result.Dispose();
                    result = temp;
                }

                // Apply adjustments in order
                if (brightness != 0)
                {
                    temp = AdjustBrightness(result, brightness);
                    result.Dispose();
                    result = temp;
                }

                if (contrast != 0)
                {
                    temp = AdjustContrast(result, contrast);
                    result.Dispose();
                    result = temp;
                }

                if (hue != 0)
                {
                    temp = AdjustHue(result, hue);
                    result.Dispose();
                    result = temp;
                }

                if (saturation != 0)
                {
                    temp = AdjustSaturation(result, saturation);
                    result.Dispose();
                    result = temp;
                }

                if (pixelize && pixelSize > 1)
                {
                    temp = Pixelize(result, pixelSize);
                    result.Dispose();
                    result = temp;
                }

                // Apply color banding before palette reduction
                if (colorBands > 1)
                {
                    temp = ColorBanding(result, colorBands);
                    result.Dispose();
                    result = temp;
                }

                // Apply dithering
                if (ditherLevels > 1)
                {
                    temp = Dither(result, ditherLevels);
                    result.Dispose();
                    result = temp;
                }

                if (paletteColors > 0)
                {
                    temp = ReduceColorPalette(result, paletteColors);
                    result.Dispose();
                    result = temp;
                }

                // Apply noise last to add grain on top
                if (noiseIntensity > 0)
                {
                    temp = AddNoise(result, noiseIntensity, true);
                    result.Dispose();
                    result = temp;
                }

                // Apply edge darkening last for depth
                if (edgeDarkening > 0)
                {
                    temp = DarkenEdges(result, edgeDarkening);
                    result.Dispose();
                    result = temp;
                }

                return result;
            }
            catch
            {
                result?.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Fills small (1-2 pixel) holes in the image without expanding the outer borders.
        /// </summary>
        public static Bitmap FillSmallHoles(Bitmap source)
        {
            var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);

            BitmapData sourceData = source.LockBits(new Rectangle(0, 0, source.Width, source.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData resultData = result.LockBits(new Rectangle(0, 0, result.Width, result.Height),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

            int bytes = Math.Abs(sourceData.Stride) * source.Height;
            byte[] sourceBuffer = new byte[bytes];
            byte[] resultBuffer = new byte[bytes];

            Marshal.Copy(sourceData.Scan0, sourceBuffer, 0, bytes);
            Array.Copy(sourceBuffer, resultBuffer, bytes);

            int width = source.Width;
            int height = source.Height;
            int stride = Math.Abs(sourceData.Stride);

            // Two passes to fill up to 2x2 holes
            for (int pass = 0; pass < 2; pass++)
            {
                byte[] currentSource = new byte[bytes];
                Array.Copy(resultBuffer, currentSource, bytes);

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int idx = y * stride + x * 4;
                        byte a = currentSource[idx + 3];

                        // Only operate on fully or mostly transparent pixels
                        if (a < 128)
                        {
                            int solidNeighbors = 0;
                            int totalB = 0, totalG = 0, totalR = 0;

                            // Check 8 neighbors
                            for (int dy = -1; dy <= 1; dy++)
                            {
                                for (int dx = -1; dx <= 1; dx++)
                                {
                                    if (dx == 0 && dy == 0) continue;

                                    int nx = x + dx;
                                    int ny = y + dy;

                                    if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                                    {
                                        int nIdx = ny * stride + nx * 4;
                                        if (currentSource[nIdx + 3] >= 128)
                                        {
                                            solidNeighbors++;
                                            totalB += currentSource[nIdx];
                                            totalG += currentSource[nIdx + 1];
                                            totalR += currentSource[nIdx + 2];
                                        }
                                    }
                                }
                            }

                            // If pixel has 5 or more solid neighbors out of 8, it's a hole or an inner corner
                            if (solidNeighbors >= 5)
                            {
                                resultBuffer[idx] = (byte)(totalB / solidNeighbors);
                                resultBuffer[idx + 1] = (byte)(totalG / solidNeighbors);
                                resultBuffer[idx + 2] = (byte)(totalR / solidNeighbors);
                                // Make it fully opaque
                                resultBuffer[idx + 3] = 255;
                            }
                        }
                    }
                }
            }

            Marshal.Copy(resultBuffer, 0, resultData.Scan0, bytes);

            source.UnlockBits(sourceData);
            result.UnlockBits(resultData);

            return result;
        }

        /// <summary>
        /// Adds random noise to an image (UO-style grain effect)
        /// </summary>
        /// <param name="source">Source image</param>
        /// <param name="intensity">Noise intensity (0-100)</param>
        /// <param name="monochrome">If true, adds grayscale noise; if false, adds colored noise</param>
        public static Bitmap AddNoise(Bitmap source, int intensity, bool monochrome = true)
        {
            if (intensity <= 0) return new Bitmap(source);

            var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);

            BitmapData sourceData = source.LockBits(new Rectangle(0, 0, source.Width, source.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData resultData = result.LockBits(new Rectangle(0, 0, result.Width, result.Height),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

            int bytes = Math.Abs(sourceData.Stride) * source.Height;
            byte[] sourceBuffer = new byte[bytes];
            byte[] resultBuffer = new byte[bytes];

            Marshal.Copy(sourceData.Scan0, sourceBuffer, 0, bytes);

            // Scale intensity to a reasonable noise range
            double noiseScale = intensity / 100.0 * 50; // Max noise of +/-25 at full intensity

            for (int i = 0; i < bytes; i += 4)
            {
                byte a = sourceBuffer[i + 3];

                if (a == 0)
                {
                    // Keep transparent pixels transparent
                    resultBuffer[i] = 0;
                    resultBuffer[i + 1] = 0;
                    resultBuffer[i + 2] = 0;
                    resultBuffer[i + 3] = 0;
                    continue;
                }

                if (monochrome)
                {
                    int noise = (int)((_noiseRandom.NextDouble() - 0.5) * 2 * noiseScale);
                    resultBuffer[i] = ClampByte(sourceBuffer[i] + noise);
                    resultBuffer[i + 1] = ClampByte(sourceBuffer[i + 1] + noise);
                    resultBuffer[i + 2] = ClampByte(sourceBuffer[i + 2] + noise);
                }
                else
                {
                    resultBuffer[i] = ClampByte(sourceBuffer[i] + (int)((_noiseRandom.NextDouble() - 0.5) * 2 * noiseScale));
                    resultBuffer[i + 1] = ClampByte(sourceBuffer[i + 1] + (int)((_noiseRandom.NextDouble() - 0.5) * 2 * noiseScale));
                    resultBuffer[i + 2] = ClampByte(sourceBuffer[i + 2] + (int)((_noiseRandom.NextDouble() - 0.5) * 2 * noiseScale));
                }
                resultBuffer[i + 3] = a;
            }

            Marshal.Copy(resultBuffer, 0, resultData.Scan0, bytes);

            source.UnlockBits(sourceData);
            result.UnlockBits(resultData);

            return result;
        }

        /// <summary>
        /// Applies ordered dithering (Bayer matrix) for UO-style retro look
        /// </summary>
        /// <param name="source">Source image</param>
        /// <param name="levels">Number of color levels per channel (2-16)</param>
        public static Bitmap Dither(Bitmap source, int levels)
        {
            if (levels <= 1 || levels > 256) return new Bitmap(source);

            var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);

            // 4x4 Bayer matrix for ordered dithering
            int[,] bayerMatrix = new int[,]
            {
                {  0,  8,  2, 10 },
                { 12,  4, 14,  6 },
                {  3, 11,  1,  9 },
                { 15,  7, 13,  5 }
            };

            BitmapData sourceData = source.LockBits(new Rectangle(0, 0, source.Width, source.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData resultData = result.LockBits(new Rectangle(0, 0, result.Width, result.Height),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

            int bytes = Math.Abs(sourceData.Stride) * source.Height;
            byte[] sourceBuffer = new byte[bytes];
            byte[] resultBuffer = new byte[bytes];

            Marshal.Copy(sourceData.Scan0, sourceBuffer, 0, bytes);

            int stride = sourceData.Stride;
            int width = source.Width;
            int height = source.Height;

            double step = 255.0 / (levels - 1);
            double threshold = 256.0 / 16; // Scale factor for Bayer matrix

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * stride + x * 4;
                    byte a = sourceBuffer[idx + 3];

                    if (a == 0)
                    {
                        resultBuffer[idx] = 0;
                        resultBuffer[idx + 1] = 0;
                        resultBuffer[idx + 2] = 0;
                        resultBuffer[idx + 3] = 0;
                        continue;
                    }

                    double ditherValue = (bayerMatrix[y % 4, x % 4] - 7.5) * threshold / levels;

                    resultBuffer[idx] = QuantizeWithDither(sourceBuffer[idx], step, ditherValue);
                    resultBuffer[idx + 1] = QuantizeWithDither(sourceBuffer[idx + 1], step, ditherValue);
                    resultBuffer[idx + 2] = QuantizeWithDither(sourceBuffer[idx + 2], step, ditherValue);
                    resultBuffer[idx + 3] = a;
                }
            }

            Marshal.Copy(resultBuffer, 0, resultData.Scan0, bytes);

            source.UnlockBits(sourceData);
            result.UnlockBits(resultData);

            return result;
        }

        /// <summary>
        /// Darkens the edges of an image (subtle vignette for UO-style depth)
        /// </summary>
        /// <param name="source">Source image</param>
        /// <param name="intensity">Edge darkening intensity (0-100)</param>
        public static Bitmap DarkenEdges(Bitmap source, int intensity)
        {
            if (intensity <= 0) return new Bitmap(source);

            var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);

            BitmapData sourceData = source.LockBits(new Rectangle(0, 0, source.Width, source.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData resultData = result.LockBits(new Rectangle(0, 0, result.Width, result.Height),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

            int bytes = Math.Abs(sourceData.Stride) * source.Height;
            byte[] sourceBuffer = new byte[bytes];
            byte[] resultBuffer = new byte[bytes];

            Marshal.Copy(sourceData.Scan0, sourceBuffer, 0, bytes);

            int stride = sourceData.Stride;
            int width = source.Width;
            int height = source.Height;

            double centerX = width / 2.0;
            double centerY = height / 2.0;
            double maxDist = Math.Sqrt(centerX * centerX + centerY * centerY);
            double darkFactor = intensity / 100.0 * 0.5; // Max 50% darkening at edges

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * stride + x * 4;
                    byte a = sourceBuffer[idx + 3];

                    if (a == 0)
                    {
                        resultBuffer[idx] = 0;
                        resultBuffer[idx + 1] = 0;
                        resultBuffer[idx + 2] = 0;
                        resultBuffer[idx + 3] = 0;
                        continue;
                    }

                    double dx = x - centerX;
                    double dy = y - centerY;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    double normalizedDist = dist / maxDist;

                    // Apply smooth falloff
                    double darken = 1.0 - (normalizedDist * normalizedDist * darkFactor);
                    darken = Math.Max(0.5, darken); // Don't go too dark

                    resultBuffer[idx] = ClampByte((int)(sourceBuffer[idx] * darken));
                    resultBuffer[idx + 1] = ClampByte((int)(sourceBuffer[idx + 1] * darken));
                    resultBuffer[idx + 2] = ClampByte((int)(sourceBuffer[idx + 2] * darken));
                    resultBuffer[idx + 3] = a;
                }
            }

            Marshal.Copy(resultBuffer, 0, resultData.Scan0, bytes);

            source.UnlockBits(sourceData);
            result.UnlockBits(resultData);

            return result;
        }

        /// <summary>
        /// Applies color banding/posterization effect
        /// </summary>
        /// <param name="source">Source image</param>
        /// <param name="bands">Number of color bands per channel (2-32)</param>
        public static Bitmap ColorBanding(Bitmap source, int bands)
        {
            if (bands <= 1 || bands > 256) return new Bitmap(source);

            var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);

            BitmapData sourceData = source.LockBits(new Rectangle(0, 0, source.Width, source.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData resultData = result.LockBits(new Rectangle(0, 0, result.Width, result.Height),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

            int bytes = Math.Abs(sourceData.Stride) * source.Height;
            byte[] sourceBuffer = new byte[bytes];
            byte[] resultBuffer = new byte[bytes];

            Marshal.Copy(sourceData.Scan0, sourceBuffer, 0, bytes);

            double step = 255.0 / (bands - 1);

            for (int i = 0; i < bytes; i += 4)
            {
                byte a = sourceBuffer[i + 3];

                if (a == 0)
                {
                    resultBuffer[i] = 0;
                    resultBuffer[i + 1] = 0;
                    resultBuffer[i + 2] = 0;
                    resultBuffer[i + 3] = 0;
                    continue;
                }

                resultBuffer[i] = Quantize(sourceBuffer[i], step);
                resultBuffer[i + 1] = Quantize(sourceBuffer[i + 1], step);
                resultBuffer[i + 2] = Quantize(sourceBuffer[i + 2], step);
                resultBuffer[i + 3] = a;
            }

            Marshal.Copy(resultBuffer, 0, resultData.Scan0, bytes);

            source.UnlockBits(sourceData);
            result.UnlockBits(resultData);

            return result;
        }

        // Helper methods
        private static byte ClampByte(int value)
        {
            return (byte)Math.Max(0, Math.Min(255, value));
        }

        private static byte Quantize(byte value, double step)
        {
            int level = (int)Math.Round(value / step);
            return ClampByte((int)(level * step));
        }

        private static byte QuantizeWithDither(byte value, double step, double dither)
        {
            double adjusted = value + dither;
            int level = (int)Math.Round(adjusted / step);
            return ClampByte((int)(level * step));
        }

        // Helper methods for HSV conversion
        private static void RgbToHsv(byte r, byte g, byte b, out float h, out float s, out float v)
        {
            float rf = r / 255f;
            float gf = g / 255f;
            float bf = b / 255f;

            float max = Math.Max(rf, Math.Max(gf, bf));
            float min = Math.Min(rf, Math.Min(gf, bf));
            float delta = max - min;

            // Hue
            if (delta == 0)
                h = 0;
            else if (max == rf)
                h = 60 * (((gf - bf) / delta) % 6);
            else if (max == gf)
                h = 60 * (((bf - rf) / delta) + 2);
            else
                h = 60 * (((rf - gf) / delta) + 4);

            if (h < 0) h += 360;

            // Saturation
            s = max == 0 ? 0 : delta / max;

            // Value
            v = max;
        }

        private static Color HsvToRgb(float h, float s, float v)
        {
            float c = v * s;
            float x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            float m = v - c;

            float r = 0, g = 0, b = 0;

            if (h >= 0 && h < 60)
            {
                r = c; g = x; b = 0;
            }
            else if (h >= 60 && h < 120)
            {
                r = x; g = c; b = 0;
            }
            else if (h >= 120 && h < 180)
            {
                r = 0; g = c; b = x;
            }
            else if (h >= 180 && h < 240)
            {
                r = 0; g = x; b = c;
            }
            else if (h >= 240 && h < 300)
            {
                r = x; g = 0; b = c;
            }
            else
            {
                r = c; g = 0; b = x;
            }

            return Color.FromArgb(
                (int)((r + m) * 255),
                (int)((g + m) * 255),
                (int)((b + m) * 255)
            );
        }

        private static Color[] GeneratePaletteFromColors(System.Collections.Generic.List<Color> colors, int k)
        {
            if (colors == null || colors.Count == 0) return new Color[0];
            // get distinct colors up to reasonable limit
            var distinct = new System.Collections.Generic.List<Color>();
            var seen = new System.Collections.Generic.HashSet<int>();
            foreach (var c in colors)
            {
                int key = (c.R << 16) | (c.G << 8) | c.B;
                if (!seen.Contains(key)) { seen.Add(key); distinct.Add(c); }
                if (distinct.Count >= k * 8) break; // limit
            }

            if (distinct.Count <= k)
            {
                // pad if necessary
                var arr = distinct.ToArray();
                var res = new Color[k];
                for (int i = 0; i < k; i++) res[i] = arr[i % arr.Length];
                return res;
            }

            // prepare arrays
            var rnd = new Random(0);
            int n = distinct.Count;
            var pts = new int[n][];
            for (int i = 0; i < n; i++) pts[i] = new int[] { distinct[i].R, distinct[i].G, distinct[i].B };

            // initialize centroids by picking k evenly spaced
            var centroids = new double[k][];
            for (int i = 0; i < k; i++)
            {
                int idx = (int)((long)i * n / k);
                centroids[i] = new double[] { pts[idx][0], pts[idx][1], pts[idx][2] };
            }

            int[] labels = new int[n];
            bool changed = true;
            int maxIter = 12;

            for (int iter = 0; iter < maxIter && changed; iter++)
            {
                changed = false;
                // assignment
                for (int i = 0; i < n; i++)
                {
                    int best = 0; double bestDist = double.MaxValue;
                    for (int c = 0; c < k; c++)
                    {
                        double dx = pts[i][0] - centroids[c][0];
                        double dy = pts[i][1] - centroids[c][1];
                        double dz = pts[i][2] - centroids[c][2];
                        double d = dx * dx + dy * dy + dz * dz;
                        if (d < bestDist) { bestDist = d; best = c; }
                    }
                    if (labels[i] != best) { labels[i] = best; changed = true; }
                }

                // update centroids
                var sums = new double[k][]; var counts = new int[k];
                for (int c = 0; c < k; c++) sums[c] = new double[3];
                for (int i = 0; i < n; i++)
                {
                    int lab = labels[i];
                    sums[lab][0] += pts[i][0]; sums[lab][1] += pts[i][1]; sums[lab][2] += pts[i][2];
                    counts[lab]++;
                }
                for (int c = 0; c < k; c++)
                {
                    if (counts[c] == 0)
                    {
                        // reinitialize empty centroid
                        int idx = rnd.Next(n);
                        centroids[c][0] = pts[idx][0]; centroids[c][1] = pts[idx][1]; centroids[c][2] = pts[idx][2];
                    }
                    else
                    {
                        centroids[c][0] = sums[c][0] / counts[c];
                        centroids[c][1] = sums[c][1] / counts[c];
                        centroids[c][2] = sums[c][2] / counts[c];
                    }
                }
            }

            var palette = new Color[k];
            for (int c = 0; c < k; c++)
            {
                int r = (int)Math.Round(centroids[c][0]);
                int g = (int)Math.Round(centroids[c][1]);
                int b = (int)Math.Round(centroids[c][2]);
                r = Math.Max(0, Math.Min(255, r));
                g = Math.Max(0, Math.Min(255, g));
                b = Math.Max(0, Math.Min(255, b));
                palette[c] = Color.FromArgb(r, g, b);
            }

            return palette;
        }

        private static Color FindNearestColor(Color target, Color[] palette)
        {
            Color nearest = palette[0];
            int minDistance = int.MaxValue;

            foreach (Color color in palette)
            {
                int distance = Math.Abs(target.R - color.R) +
                              Math.Abs(target.G - color.G) +
                              Math.Abs(target.B - color.B);

                if (distance < minDistance)
                {
                    minDistance = distance;
                    nearest = color;
                }
            }

            return nearest;
        }

        /// <summary>
        /// Removes background from an image using color threshold.
        /// </summary>
        /// <param name="source">Source image</param>
        /// <param name="mode">Detection mode: "Auto (corners)", "White", "Black", or "Custom Color"</param>
        /// <param name="customColor">Custom color to remove (used when mode is "Custom Color")</param>
        /// <param name="threshold">Color distance threshold (0-255)</param>
        /// <returns>Image with background removed (transparent)</returns>
        public static Bitmap RemoveBackground(Bitmap source, string mode, Color customColor, int threshold)
        {
            var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);

            // Determine target background color
            Color targetColor;
            switch (mode)
            {
                case "White":
                    targetColor = Color.White;
                    break;
                case "Black":
                    targetColor = Color.Black;
                    break;
                case "Custom Color":
                    targetColor = customColor;
                    break;
                case "Auto (corners)":
                default:
                    targetColor = DetectBackgroundColorFromCorners(source);
                    break;
            }

            BitmapData sourceData = source.LockBits(new Rectangle(0, 0, source.Width, source.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData resultData = result.LockBits(new Rectangle(0, 0, result.Width, result.Height),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

            int bytes = Math.Abs(sourceData.Stride) * source.Height;
            byte[] sourceBuffer = new byte[bytes];
            byte[] resultBuffer = new byte[bytes];

            Marshal.Copy(sourceData.Scan0, sourceBuffer, 0, bytes);

            for (int i = 0; i < bytes; i += 4)
            {
                byte b = sourceBuffer[i];
                byte g = sourceBuffer[i + 1];
                byte r = sourceBuffer[i + 2];
                byte a = sourceBuffer[i + 3];

                // Calculate color distance from target
                int distance = ColorDistance(
                    Color.FromArgb(a, r, g, b),
                    targetColor
                );

                if (distance <= threshold)
                {
                    // Make pixel transparent
                    resultBuffer[i] = 0;
                    resultBuffer[i + 1] = 0;
                    resultBuffer[i + 2] = 0;
                    resultBuffer[i + 3] = 0;
                }
                else
                {
                    // Keep original pixel
                    resultBuffer[i] = b;
                    resultBuffer[i + 1] = g;
                    resultBuffer[i + 2] = r;
                    resultBuffer[i + 3] = a;
                }
            }

            Marshal.Copy(resultBuffer, 0, resultData.Scan0, bytes);

            source.UnlockBits(sourceData);
            result.UnlockBits(resultData);

            return result;
        }

        /// <summary>
        /// Detects background color by sampling image corners.
        /// </summary>
        private static Color DetectBackgroundColorFromCorners(Bitmap image)
        {
            var cornerColors = new System.Collections.Generic.List<Color>();

            try
            {
                // Sample corners
                cornerColors.Add(image.GetPixel(0, 0));
                cornerColors.Add(image.GetPixel(image.Width - 1, 0));
                cornerColors.Add(image.GetPixel(0, image.Height - 1));
                cornerColors.Add(image.GetPixel(image.Width - 1, image.Height - 1));

                // Sample edges (middle points)
                cornerColors.Add(image.GetPixel(image.Width / 2, 0));
                cornerColors.Add(image.GetPixel(image.Width / 2, image.Height - 1));
                cornerColors.Add(image.GetPixel(0, image.Height / 2));
                cornerColors.Add(image.GetPixel(image.Width - 1, image.Height / 2));
            }
            catch
            {
                // If sampling fails, default to white
                return Color.White;
            }

            // Return most common color
            var grouped = cornerColors
                .GroupBy(c => c.ToArgb())
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();

            return grouped != null ? Color.FromArgb(grouped.Key) : Color.White;
        }

        /// <summary>
        /// Calculates RGB distance between two colors (Manhattan distance).
        /// </summary>
        private static int ColorDistance(Color c1, Color c2)
        {
            return Math.Abs(c1.R - c2.R) + Math.Abs(c1.G - c2.G) + Math.Abs(c1.B - c2.B);
        }
    }
}
