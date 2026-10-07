using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;

namespace MeesaMultisMaker.Utils
{
    /// <summary>
    /// Splits a sprite sheet containing multiple objects into individual sprites.
    /// Handles non-grid-aligned sprites with automatic background removal and object detection.
    /// </summary>
    public class SpriteSheetSplitter
    {
        /// <summary>
        /// Represents a detected sprite with its image and original position.
        /// </summary>
        public class DetectedSprite
        {
            public Bitmap Image { get; set; }
            public Rectangle Bounds { get; set; }
            public int Index { get; set; }
            public Point CenterPoint { get; set; }

            public DetectedSprite(Bitmap image, Rectangle bounds, int index)
            {
                Image = image;
                Bounds = bounds;
                Index = index;
                CenterPoint = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
            }
        }

        /// <summary>
        /// Configuration options for sprite detection.
        /// </summary>
        public class SplitOptions
        {
            /// <summary>
            /// Background color to remove. If null, auto-detects from image corners.
            /// </summary>
            public Color? BackgroundColor { get; set; } = null;

            /// <summary>
            /// Color tolerance for background removal (0-255). Higher = more aggressive removal.
            /// </summary>
            public int ColorTolerance { get; set; } = 30;

            /// <summary>
            /// Minimum sprite width in pixels. Smaller objects are ignored.
            /// </summary>
            public int MinSpriteWidth { get; set; } = 5;

            /// <summary>
            /// Minimum sprite height in pixels. Smaller objects are ignored.
            /// </summary>
            public int MinSpriteHeight { get; set; } = 5;

            /// <summary>
            /// Minimum number of pixels an object must have to be considered a sprite.
            /// </summary>
            public int MinPixelCount { get; set; } = 25;

            /// <summary>
            /// Padding to add around each extracted sprite (in pixels).
            /// </summary>
            public int Padding { get; set; } = 2;

            /// <summary>
            /// Sort order for extracted sprites.
            /// </summary>
            public SpriteSortOrder SortOrder { get; set; } = SpriteSortOrder.LeftToRightTopToBottom;

            /// <summary>
            /// If true, removes semi-transparent "halo" pixels around sprites.
            /// </summary>
            public bool RemoveHalo { get; set; } = true;
        }

        public enum SpriteSortOrder
        {
            LeftToRightTopToBottom,  // Row by row, left to right
            TopToBottomLeftToRight,  // Column by column, top to bottom
            None                      // Keep original detection order
        }

        /// <summary>
        /// Splits a sprite sheet into individual sprites.
        /// </summary>
        public static List<DetectedSprite> SplitSpriteSheet(Bitmap source, SplitOptions options = null)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            options = options ?? new SplitOptions();

            // Step 1: Detect background color if not specified
            Color bgColor = options.BackgroundColor ?? DetectBackgroundColor(source);

            // Step 2: Create transparency mask
            Bitmap processedImage = RemoveBackground(source, bgColor, options.ColorTolerance, options.RemoveHalo);

            // Step 3: Detect connected components (separate objects)
            var components = DetectConnectedComponents(processedImage, options.MinPixelCount);

            // Step 4: Extract each sprite with its bounding box
            var sprites = new List<DetectedSprite>();
            int index = 0;

            foreach (var component in components)
            {
                Rectangle bounds = GetBoundingBox(component);

                // Filter out tiny objects
                if (bounds.Width < options.MinSpriteWidth || bounds.Height < options.MinSpriteHeight)
                    continue;

                // Add padding
                bounds = ExpandBounds(bounds, options.Padding, processedImage.Width, processedImage.Height);

                // Extract sprite
                Bitmap sprite = ExtractSprite(processedImage, bounds);

                sprites.Add(new DetectedSprite(sprite, bounds, index++));
            }

            processedImage.Dispose();

            // Step 5: Sort sprites
            SortSprites(sprites, options.SortOrder);

            // Reassign indices after sorting
            for (int i = 0; i < sprites.Count; i++)
                sprites[i].Index = i;

            return sprites;
        }

        /// <summary>
        /// Detects the background color by sampling the image corners.
        /// </summary>
        private static Color DetectBackgroundColor(Bitmap image)
        {
            var cornerColors = new List<Color>();

            using (var fastBmp = new FastBitmap(image))
            {
                // Sample corners
                cornerColors.Add(fastBmp.GetPixel(0, 0));
                cornerColors.Add(fastBmp.GetPixel(image.Width - 1, 0));
                cornerColors.Add(fastBmp.GetPixel(0, image.Height - 1));
                cornerColors.Add(fastBmp.GetPixel(image.Width - 1, image.Height - 1));

                // Sample edges (middle points)
                cornerColors.Add(fastBmp.GetPixel(image.Width / 2, 0));
                cornerColors.Add(fastBmp.GetPixel(image.Width / 2, image.Height - 1));
                cornerColors.Add(fastBmp.GetPixel(0, image.Height / 2));
                cornerColors.Add(fastBmp.GetPixel(image.Width - 1, image.Height / 2));
            }

            // Return most common color
            return cornerColors
                .GroupBy(c => c.ToArgb())
                .OrderByDescending(g => g.Count())
                .First()
                .Key
                .ToColor();
        }

        /// <summary>
        /// Removes background color and converts it to transparency.
        /// </summary>
        private static Bitmap RemoveBackground(Bitmap source, Color bgColor, int tolerance, bool removeHalo)
        {
            Bitmap result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);

            using (var srcFast = new FastBitmap(source))
            using (var dstFast = new FastBitmap(result))
            {
                for (int y = 0; y < source.Height; y++)
                {
                    for (int x = 0; x < source.Width; x++)
                    {
                        Color pixel = srcFast.GetPixel(x, y);

                        // Check if color matches background
                        if (ColorDistance(pixel, bgColor) <= tolerance)
                        {
                            dstFast.SetPixel(x, y, Color.Transparent);
                        }
                        else
                        {
                            // Keep original pixel
                            dstFast.SetPixel(x, y, pixel);
                        }
                    }
                }
            }

            // Optional: Remove semi-transparent halo around sprites
            if (removeHalo)
            {
                RemoveHaloEffect(result);
            }

            return result;
        }

        /// <summary>
        /// Removes semi-transparent pixels that border transparent areas (anti-aliasing artifacts).
        /// </summary>
        private static void RemoveHaloEffect(Bitmap image)
        {
            using (var fast = new FastBitmap(image))
            {
                for (int y = 1; y < image.Height - 1; y++)
                {
                    for (int x = 1; x < image.Width - 1; x++)
                    {
                        Color pixel = fast.GetPixel(x, y);

                        // Skip fully opaque or fully transparent pixels
                        if (pixel.A == 255 || pixel.A == 0)
                            continue;

                        // Check if this semi-transparent pixel borders transparency
                        bool bordersTransparency = false;
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                if (dx == 0 && dy == 0) continue;
                                if (fast.GetPixel(x + dx, y + dy).A == 0)
                                {
                                    bordersTransparency = true;
                                    break;
                                }
                            }
                            if (bordersTransparency) break;
                        }

                        // If it borders transparency and is less than 50% opaque, make it fully transparent
                        if (bordersTransparency && pixel.A < 128)
                        {
                            fast.SetPixel(x, y, Color.Transparent);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Detects connected components (separate objects) using flood fill.
        /// </summary>
        private static List<List<Point>> DetectConnectedComponents(Bitmap image, int minPixelCount)
        {
            var components = new List<List<Point>>();
            bool[,] visited = new bool[image.Width, image.Height];

            using (var fast = new FastBitmap(image))
            {
                for (int y = 0; y < image.Height; y++)
                {
                    for (int x = 0; x < image.Width; x++)
                    {
                        if (visited[x, y]) continue;

                        Color pixel = fast.GetPixel(x, y);
                        if (pixel.A == 0) continue; // Skip transparent pixels

                        // Start flood fill to find connected component
                        var component = FloodFill(fast, visited, x, y);

                        if (component.Count >= minPixelCount)
                        {
                            components.Add(component);
                        }
                    }
                }
            }

            return components;
        }

        /// <summary>
        /// Flood fill algorithm to find connected non-transparent pixels.
        /// </summary>
        private static List<Point> FloodFill(FastBitmap image, bool[,] visited, int startX, int startY)
        {
            var component = new List<Point>();
            var stack = new Stack<Point>();
            stack.Push(new Point(startX, startY));

            while (stack.Count > 0)
            {
                Point p = stack.Pop();

                if (p.X < 0 || p.X >= image.Width || p.Y < 0 || p.Y >= image.Height)
                    continue;

                if (visited[p.X, p.Y])
                    continue;

                Color pixel = image.GetPixel(p.X, p.Y);
                if (pixel.A == 0) // Transparent pixel
                    continue;

                visited[p.X, p.Y] = true;
                component.Add(p);

                // Add neighbors (8-connectivity)
                stack.Push(new Point(p.X - 1, p.Y));
                stack.Push(new Point(p.X + 1, p.Y));
                stack.Push(new Point(p.X, p.Y - 1));
                stack.Push(new Point(p.X, p.Y + 1));
                stack.Push(new Point(p.X - 1, p.Y - 1));
                stack.Push(new Point(p.X + 1, p.Y - 1));
                stack.Push(new Point(p.X - 1, p.Y + 1));
                stack.Push(new Point(p.X + 1, p.Y + 1));
            }

            return component;
        }

        /// <summary>
        /// Gets the bounding box for a component.
        /// </summary>
        private static Rectangle GetBoundingBox(List<Point> component)
        {
            int minX = component.Min(p => p.X);
            int maxX = component.Max(p => p.X);
            int minY = component.Min(p => p.Y);
            int maxY = component.Max(p => p.Y);

            return new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        /// <summary>
        /// Expands bounds by padding, clamped to image dimensions.
        /// </summary>
        private static Rectangle ExpandBounds(Rectangle bounds, int padding, int imageWidth, int imageHeight)
        {
            return new Rectangle(
                Math.Max(0, bounds.X - padding),
                Math.Max(0, bounds.Y - padding),
                Math.Min(imageWidth - bounds.X + padding, bounds.Width + padding * 2),
                Math.Min(imageHeight - bounds.Y + padding, bounds.Height + padding * 2)
            );
        }

        /// <summary>
        /// Extracts a sprite from the image.
        /// </summary>
        private static Bitmap ExtractSprite(Bitmap source, Rectangle bounds)
        {
            Bitmap sprite = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);

            using (Graphics g = Graphics.FromImage(sprite))
            {
                g.Clear(Color.Transparent);
                g.DrawImage(source,
                    new Rectangle(0, 0, bounds.Width, bounds.Height),
                    bounds,
                    GraphicsUnit.Pixel);
            }

            return sprite;
        }

        /// <summary>
        /// Sorts sprites according to the specified order.
        /// </summary>
        private static void SortSprites(List<DetectedSprite> sprites, SpriteSortOrder order)
        {
            switch (order)
            {
                case SpriteSortOrder.LeftToRightTopToBottom:
                    // Group by rows (similar Y coordinates), then sort by X within each row
                    sprites.Sort((a, b) =>
                    {
                        int rowA = a.CenterPoint.Y / 50; // Group into rows (~50px tolerance)
                        int rowB = b.CenterPoint.Y / 50;
                        if (rowA != rowB)
                            return rowA.CompareTo(rowB);
                        return a.CenterPoint.X.CompareTo(b.CenterPoint.X);
                    });
                    break;

                case SpriteSortOrder.TopToBottomLeftToRight:
                    // Group by columns (similar X coordinates), then sort by Y within each column
                    sprites.Sort((a, b) =>
                    {
                        int colA = a.CenterPoint.X / 50; // Group into columns (~50px tolerance)
                        int colB = b.CenterPoint.X / 50;
                        if (colA != colB)
                            return colA.CompareTo(colB);
                        return a.CenterPoint.Y.CompareTo(b.CenterPoint.Y);
                    });
                    break;

                case SpriteSortOrder.None:
                    // Keep original detection order
                    break;
            }
        }

        /// <summary>
        /// Calculates color distance (Euclidean distance in RGB space).
        /// </summary>
        private static int ColorDistance(Color c1, Color c2)
        {
            int r = c1.R - c2.R;
            int g = c1.G - c2.G;
            int b = c1.B - c2.B;
            return (int)Math.Sqrt(r * r + g * g + b * b);
        }

        /// <summary>
        /// Fast bitmap access helper class.
        /// </summary>
        private class FastBitmap : IDisposable
        {
            private Bitmap bitmap;
            private BitmapData bitmapData;
            private byte[] pixels;
            private int stride;

            public int Width => bitmap.Width;
            public int Height => bitmap.Height;

            public FastBitmap(Bitmap bitmap)
            {
                this.bitmap = bitmap;
                bitmapData = bitmap.LockBits(
                    new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                    ImageLockMode.ReadWrite,
                    PixelFormat.Format32bppArgb);

                stride = Math.Abs(bitmapData.Stride);
                pixels = new byte[stride * bitmap.Height];
                System.Runtime.InteropServices.Marshal.Copy(bitmapData.Scan0, pixels, 0, pixels.Length);
            }

            public Color GetPixel(int x, int y)
            {
                int index = y * stride + x * 4;
                byte b = pixels[index];
                byte g = pixels[index + 1];
                byte r = pixels[index + 2];
                byte a = pixels[index + 3];
                return Color.FromArgb(a, r, g, b);
            }

            public void SetPixel(int x, int y, Color color)
            {
                int index = y * stride + x * 4;
                pixels[index] = color.B;
                pixels[index + 1] = color.G;
                pixels[index + 2] = color.R;
                pixels[index + 3] = color.A;
            }

            public void Dispose()
            {
                System.Runtime.InteropServices.Marshal.Copy(pixels, 0, bitmapData.Scan0, pixels.Length);
                bitmap.UnlockBits(bitmapData);
            }
        }
    }

    public static class ColorExtensions
    {
        public static Color ToColor(this int argb)
        {
            return Color.FromArgb(argb);
        }
    }
}
