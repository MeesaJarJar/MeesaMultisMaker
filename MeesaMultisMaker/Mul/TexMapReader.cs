using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Collections.Generic;

namespace MeesaMultisMaker.Mul
{
    /// <summary>
    /// Reads land tile textures from texmaps.mul / texidx.mul.
    /// Textures are square (64×64 or 128×128) RGB555 images used by the
    /// UO client for deformed terrain where the 44×44 diamond land-tile
    /// art cannot be stretched convincingly.
    /// </summary>
    public static class TexMapReader
    {
        private const int EntrySize = 12;
        private const int Texture64DataSize = 64 * 64 * 2;
        private const int Texture128DataSize = 128 * 128 * 2;

        public static List<int> GetValidTextureIds(string mulFolder)
        {
            var result = new List<int>();

            string idxPath = Path.Combine(mulFolder, "texidx.mul");
            string mulPath = Path.Combine(mulFolder, "texmaps.mul");

            if (!File.Exists(idxPath) || !File.Exists(mulPath))
                return result;

            try
            {
                using (var idxStream = File.OpenRead(idxPath))
                using (var mulStream = File.OpenRead(mulPath))
                using (var reader = new BinaryReader(idxStream))
                {
                    int count = (int)(idxStream.Length / EntrySize);
                    for (int textureId = 0; textureId < count; textureId++)
                    {
                        int offset = reader.ReadInt32();
                        int length = reader.ReadInt32();
                        reader.ReadInt32(); // extra

                        if (offset < 0 || length <= 0)
                            continue;

                        if (length < Texture64DataSize)
                            continue;

                        if ((long)offset + length > mulStream.Length)
                            continue;

                        result.Add(textureId);
                    }
                }
            }
            catch
            {
                return new List<int>();
            }

            return result;
        }

        /// <summary>
        /// Load a texture from texmaps.mul by its texture ID
        /// (as found in LandTileData.TextureId).
        /// Returns null when the texture ID is 0, files are missing,
        /// or the entry is invalid.
        /// </summary>
        public static Bitmap LoadTexture(string mulFolder, ushort textureId)
        {
            if (textureId == 0)
                return null;

            string idxPath = Path.Combine(mulFolder, "texidx.mul");
            string mulPath = Path.Combine(mulFolder, "texmaps.mul");

            if (!File.Exists(idxPath) || !File.Exists(mulPath))
                return null;

            try
            {
                using (var idxStream = File.OpenRead(idxPath))
                using (var mulStream = File.OpenRead(mulPath))
                using (var idxReader = new BinaryReader(idxStream))
                using (var mulReader = new BinaryReader(mulStream))
                {
                    // Each index entry is 12 bytes: offset(4), length(4), extra(4)
                    long idxPosition = (long)textureId * 12;
                    if (idxPosition + 12 > idxStream.Length)
                        return null;

                    idxStream.Seek(idxPosition, SeekOrigin.Begin);

                    int offset = idxReader.ReadInt32();
                    int length = idxReader.ReadInt32();
                    int extra = idxReader.ReadInt32();

                    if (offset < 0 || length <= 0 || offset >= mulStream.Length)
                        return null;

                    // Determine texture dimensions from the data length.
                    // 64×64 = 8192 bytes,  128×128 = 32768 bytes  (2 bytes per pixel)
                    int size;
                    if (length >= Texture128DataSize)
                        size = 128;
                    else if (length >= Texture64DataSize)
                        size = 64;
                    else
                        return null;

                    mulStream.Seek(offset, SeekOrigin.Begin);

                    int pixelCount = size * size;
                    int dataBytes = pixelCount * 2;
                    if (offset + dataBytes > mulStream.Length)
                        return null;

                    byte[] rawPixels = mulReader.ReadBytes(dataBytes);

                    // Convert RGB555 → BGRA32
                    byte[] bgraPixels = new byte[pixelCount * 4];
                    for (int i = 0; i < pixelCount; i++)
                    {
                        ushort c16 = (ushort)(rawPixels[i * 2] | (rawPixels[i * 2 + 1] << 8));

                        if (c16 == 0)
                            continue; // leave BGRA as 0 (transparent)

                        int r = ((c16 >> 10) & 0x1F) * 255 / 31;
                        int g = ((c16 >> 5) & 0x1F) * 255 / 31;
                        int b = (c16 & 0x1F) * 255 / 31;

                        int idx = i * 4;
                        bgraPixels[idx + 0] = (byte)b;
                        bgraPixels[idx + 1] = (byte)g;
                        bgraPixels[idx + 2] = (byte)r;
                        bgraPixels[idx + 3] = 255;
                    }

                    var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
                    var rect = new Rectangle(0, 0, size, size);
                    var bmpData = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                    try
                    {
                        Marshal.Copy(bgraPixels, 0, bmpData.Scan0, bgraPixels.Length);
                    }
                    finally
                    {
                        bmp.UnlockBits(bmpData);
                    }

                    return bmp;
                }
            }
            catch
            {
                return null;
            }

        }

        public static bool SaveTexture(string mulFolder, int textureId, Bitmap image)
        {
            if (string.IsNullOrEmpty(mulFolder) || image == null || textureId < 0)
                return false;

            if (image.Width != image.Height)
                return false;

            if (image.Width != 64 && image.Width != 128)
                return false;

            string idxPath = Path.Combine(mulFolder, "texidx.mul");
            string mulPath = Path.Combine(mulFolder, "texmaps.mul");

            if (!File.Exists(idxPath) || !File.Exists(mulPath))
                return false;

            try
            {
                byte[] textureData = EncodeTexture(image);
                if (textureData == null || textureData.Length == 0)
                    return false;

                using (var mulStream = new FileStream(mulPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                using (var idxStream = new FileStream(idxPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                using (var idxWriter = new BinaryWriter(idxStream))
                {
                    mulStream.Seek(0, SeekOrigin.End);
                    int newOffset = (int)mulStream.Position;
                    mulStream.Write(textureData, 0, textureData.Length);

                    long idxPosition = (long)textureId * EntrySize;
                    if (idxPosition + EntrySize > idxStream.Length)
                    {
                        idxStream.SetLength(idxPosition + EntrySize);
                    }

                    idxStream.Seek(idxPosition, SeekOrigin.Begin);
                    idxWriter.Write(newOffset);
                    idxWriter.Write(textureData.Length);
                    idxWriter.Write(image.Width);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static byte[] EncodeTexture(Bitmap image)
        {
            int size = image.Width;
            var result = new byte[size * size * 2];

            var rect = new Rectangle(0, 0, size, size);
            var data = image.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int bytes = Math.Abs(data.Stride) * size;
                var source = new byte[bytes];
                Marshal.Copy(data.Scan0, source, 0, bytes);

                for (int y = 0; y < size; y++)
                {
                    int row = y * data.Stride;
                    for (int x = 0; x < size; x++)
                    {
                        int src = row + x * 4;
                        byte b = source[src + 0];
                        byte g = source[src + 1];
                        byte r = source[src + 2];
                        byte a = source[src + 3];

                        ushort color16;
                        if (a < 128)
                        {
                            color16 = 0;
                        }
                        else
                        {
                            int r5 = (r * 31 / 255) & 0x1F;
                            int g5 = (g * 31 / 255) & 0x1F;
                            int b5 = (b * 31 / 255) & 0x1F;
                            color16 = (ushort)((r5 << 10) | (g5 << 5) | b5);
                            if (color16 == 0)
                                color16 = 1;
                        }

                        int dst = (y * size + x) * 2;
                        result[dst] = (byte)(color16 & 0xFF);
                        result[dst + 1] = (byte)((color16 >> 8) & 0xFF);
                    }
                }
            }
            finally
            {
                image.UnlockBits(data);
            }

            return result;
        }
    }
}
