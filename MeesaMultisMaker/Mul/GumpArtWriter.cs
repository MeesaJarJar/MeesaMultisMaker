using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace MeesaMultisMaker.Mul
{
    /// <summary>
    /// Writes GUMP art to gumpart.mul and gumpidx.mul
    /// 
    /// GUMP format:
    /// - Index file: 12 bytes per entry (offset, length, extra)
    /// - Extra field contains width (high word) and height (low word)
    /// - Data is run-length encoded: [color:ushort][runLength:ushort] per run
    /// - Row lookup table at start of each gump data
    /// </summary>
    public static class GumpArtWriter
    {
        /// <summary>
        /// Save a bitmap as a GUMP to the mul or UOP files
        /// </summary>
        /// <param name="mulFolder">Path to folder containing gumpart.mul/gumpidx.mul or gumpartLegacyMUL.uop</param>
        /// <param name="gumpId">GUMP ID to save</param>
        /// <param name="image">Bitmap to save</param>
        /// <returns>True if successful</returns>
        public static bool SaveGump(string mulFolder, int gumpId, Bitmap image)
        {
            if (string.IsNullOrEmpty(mulFolder) || image == null)
                return false;

            string gumpIdxPath = Path.Combine(mulFolder, "gumpidx.mul");
            string gumpMulPath = Path.Combine(mulFolder, "gumpart.mul");

            // If MUL files don't exist, try UOP format
            if (!File.Exists(gumpIdxPath) || !File.Exists(gumpMulPath))
            {
                if (UopGumpWriter.UopFileExists(mulFolder))
                    return UopGumpWriter.SaveGump(mulFolder, gumpId, image);
                return false;
            }

            try
            {
                // Encode the image to GUMP format
                byte[] gumpData = EncodeGump(image);
                if (gumpData == null)
                    return false;

                // Calculate extra field (width in high word, height in low word)
                int extra = (image.Width << 16) | (image.Height & 0xFFFF);

                // Append data to gumpart.mul and update index
                using (var mulStream = new FileStream(gumpMulPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                using (var idxStream = new FileStream(gumpIdxPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    // Seek to end of mul file to append new data
                    mulStream.Seek(0, SeekOrigin.End);
                    int newOffset = (int)mulStream.Position;

                    // Write the gump data
                    mulStream.Write(gumpData, 0, gumpData.Length);

                    // Update index entry
                    long idxPosition = (long)gumpId * 12;

                    // Ensure index file is large enough
                    if (idxPosition + 12 > idxStream.Length)
                    {
                        idxStream.SetLength(idxPosition + 12);
                    }

                    idxStream.Seek(idxPosition, SeekOrigin.Begin);
                    using (var idxWriter = new BinaryWriter(idxStream, System.Text.Encoding.Default, leaveOpen: true))
                    {
                        idxWriter.Write(newOffset);           // Offset
                        idxWriter.Write(gumpData.Length);     // Length
                        idxWriter.Write(extra);               // Extra (dimensions)
                    }
                }

                // Clear the cache so the new gump can be loaded
                GumpArtReader.ClearCache();

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GumpArtWriter: Failed to save gump 0x{gumpId:X4}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Encode a bitmap to GUMP format
        /// </summary>
        private static byte[] EncodeGump(Bitmap image)
        {
            int width = image.Width;
            int height = image.Height;

            if (width <= 0 || height <= 0 || width > 4096 || height > 4096)
                return null;

            // Read pixel data from bitmap
            var rect = new Rectangle(0, 0, width, height);
            var data = image.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            byte[] pixels;
            try
            {
                int byteCount = Math.Abs(data.Stride) * height;
                pixels = new byte[byteCount];
                Marshal.Copy(data.Scan0, pixels, 0, byteCount);
            }
            finally
            {
                image.UnlockBits(data);
            }

            // Encode each row using RLE
            var rowDataList = new byte[height][];
            int totalDataSize = 0;

            for (int y = 0; y < height; y++)
            {
                var rowStream = new MemoryStream();
                using (var rowWriter = new BinaryWriter(rowStream))
                {
                    int x = 0;
                    while (x < width)
                    {
                        // Get current pixel color
                        int pixelIndex = (y * width + x) * 4;
                        byte b = pixels[pixelIndex + 0];
                        byte g = pixels[pixelIndex + 1];
                        byte r = pixels[pixelIndex + 2];
                        byte a = pixels[pixelIndex + 3];

                        // Convert to RGB555 (or 0 for transparent)
                        ushort color16;
                        if (a < 128)
                        {
                            color16 = 0; // Transparent
                        }
                        else
                        {
                            // RGB555 format: ARRRRRGGGGGBBBBB (A is always 1 for opaque)
                            int r5 = (r * 31 / 255) & 0x1F;
                            int g5 = (g * 31 / 255) & 0x1F;
                            int b5 = (b * 31 / 255) & 0x1F;
                            color16 = (ushort)((1 << 15) | (r5 << 10) | (g5 << 5) | b5);

                            // Handle edge case where color would be 0 (make it slightly different)
                            if (color16 == 0)
                                color16 = 1;
                        }

                        // Count run length of same color
                        int runLength = 1;
                        int maxRun = Math.Min(width - x, ushort.MaxValue);

                        while (runLength < maxRun)
                        {
                            int nextPixelIndex = (y * width + x + runLength) * 4;
                            byte nb = pixels[nextPixelIndex + 0];
                            byte ng = pixels[nextPixelIndex + 1];
                            byte nr = pixels[nextPixelIndex + 2];
                            byte na = pixels[nextPixelIndex + 3];

                            ushort nextColor16;
                            if (na < 128)
                            {
                                nextColor16 = 0;
                            }
                            else
                            {
                                int nr5 = (nr * 31 / 255) & 0x1F;
                                int ng5 = (ng * 31 / 255) & 0x1F;
                                int nb5 = (nb * 31 / 255) & 0x1F;
                                nextColor16 = (ushort)((1 << 15) | (nr5 << 10) | (ng5 << 5) | nb5);
                                if (nextColor16 == 0) nextColor16 = 1;
                            }

                            if (nextColor16 != color16)
                                break;

                            runLength++;
                        }

                        // Write run: [color:ushort][runLength:ushort]
                        rowWriter.Write(color16);
                        rowWriter.Write((ushort)runLength);

                        x += runLength;
                    }
                }

                rowDataList[y] = rowStream.ToArray();
                totalDataSize += rowDataList[y].Length;
            }

            // Build final gump data with lookup table
            var outputStream = new MemoryStream();
            using (var writer = new BinaryWriter(outputStream))
            {
                // Write lookup table (offset in DWORDs from start of data)
                int currentOffset = height; // Start after lookup table (in DWORDs)
                for (int y = 0; y < height; y++)
                {
                    writer.Write(currentOffset);
                    currentOffset += rowDataList[y].Length / 4;
                    if (rowDataList[y].Length % 4 != 0)
                        currentOffset++; // Round up to next DWORD
                }

                // Write row data
                for (int y = 0; y < height; y++)
                {
                    writer.Write(rowDataList[y]);

                    // Pad to DWORD boundary if needed
                    int padding = (4 - (rowDataList[y].Length % 4)) % 4;
                    for (int i = 0; i < padding; i++)
                        writer.Write((byte)0);
                }
            }

            return outputStream.ToArray();
        }

        /// <summary>
        /// Create a backup of the gump files before modifying
        /// </summary>
        public static bool BackupGumpFiles(string mulFolder, string backupFolder = null)
        {
            if (string.IsNullOrEmpty(mulFolder))
                return false;

            string gumpIdxPath = Path.Combine(mulFolder, "gumpidx.mul");
            string gumpMulPath = Path.Combine(mulFolder, "gumpart.mul");

            if (!File.Exists(gumpIdxPath) || !File.Exists(gumpMulPath))
                return false;

            try
            {
                if (string.IsNullOrEmpty(backupFolder))
                {
                    backupFolder = Path.Combine(mulFolder, "backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                }

                if (!Directory.Exists(backupFolder))
                {
                    Directory.CreateDirectory(backupFolder);
                }

                File.Copy(gumpIdxPath, Path.Combine(backupFolder, "gumpidx.mul"), true);
                File.Copy(gumpMulPath, Path.Combine(backupFolder, "gumpart.mul"), true);

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GumpArtWriter: Backup failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Delete a GUMP entry (marks it as invalid in the index)
        /// </summary>
        public static bool DeleteGump(string mulFolder, int gumpId)
        {
            if (string.IsNullOrEmpty(mulFolder))
                return false;

            string gumpIdxPath = Path.Combine(mulFolder, "gumpidx.mul");

            if (!File.Exists(gumpIdxPath))
                return false;

            try
            {
                using (var idxStream = new FileStream(gumpIdxPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    long idxPosition = (long)gumpId * 12;

                    if (idxPosition + 12 > idxStream.Length)
                        return false;

                    idxStream.Seek(idxPosition, SeekOrigin.Begin);
                    using (var idxWriter = new BinaryWriter(idxStream, System.Text.Encoding.Default, leaveOpen: true))
                    {
                        idxWriter.Write(-1);  // Invalid offset
                        idxWriter.Write(0);   // Zero length
                        idxWriter.Write(0);   // Zero extra
                    }
                }

                GumpArtReader.ClearCache();
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GumpArtWriter: Failed to delete gump 0x{gumpId:X4}: {ex.Message}");
                return false;
            }
        }
    }
}
