using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace MeesaMultisMaker.Mul
{
    /// <summary>
    /// Reads land tile textures from art.mul and artidx.mul
    /// Land tiles are 44x44 diamond-shaped textures stored in the first 0x4000 entries
    /// </summary>
    public static class LandTileArtReader
    {
        private const int LAND_TILE_SIZE = 44;
        private const int MAX_LAND_TILES = 0x4000; // 16384 - land tiles are 0x0000 to 0x3FFF

        /// <summary>
        /// Load a land tile texture from art.mul
        /// </summary>
        public static Bitmap LoadLandTile(string mulFolder, ushort tileId)
        {
            // Land tiles must be < 0x4000
            if (tileId >= MAX_LAND_TILES)
            {
                System.Diagnostics.Debug.WriteLine($"LandTileArtReader: TileID {tileId:X4} is too high for land tiles (max is 0x3FFF)");
                return null;
            }

            string artIdxPath = Path.Combine(mulFolder, "artidx.mul");
            string artMulPath = Path.Combine(mulFolder, "art.mul");

            if (!File.Exists(artIdxPath))
            {
                System.Diagnostics.Debug.WriteLine($"LandTileArtReader: artidx.mul not found at {artIdxPath}");
                return null;
            }

            if (!File.Exists(artMulPath))
            {
                System.Diagnostics.Debug.WriteLine($"LandTileArtReader: art.mul not found at {artMulPath}");
                return null;
            }

            try
            {
                using (var idxStream = File.OpenRead(artIdxPath))
                using (var mulStream = File.OpenRead(artMulPath))
                using (var idxReader = new BinaryReader(idxStream))
                using (var mulReader = new BinaryReader(mulStream))
                {
                    // IMPORTANT: Land tiles are in the FIRST 0x4000 entries
                    // Each index entry is 12 bytes: offset(4), length(4), extra(4)
                    // DO NOT add 0x4000 to the tile ID!
                    long idxPosition = tileId * 12;

                    if (idxPosition + 12 > idxStream.Length)
                    {
                        System.Diagnostics.Debug.WriteLine($"LandTileArtReader: Index position {idxPosition} is beyond file length {idxStream.Length}");
                        return null;
                    }

                    idxStream.Seek(idxPosition, SeekOrigin.Begin);

                    int offset = idxReader.ReadInt32();
                    int length = idxReader.ReadInt32();
                    int extra = idxReader.ReadInt32();

                    System.Diagnostics.Debug.WriteLine($"LandTileArtReader: TileID 0x{tileId:X4} -> Offset: {offset}, Length: {length}, Extra: {extra}");

                    // Check if entry is valid
                    if (offset < 0 || length <= 0 || offset >= mulStream.Length)
                    {
                        System.Diagnostics.Debug.WriteLine($"LandTileArtReader: Invalid entry for tile 0x{tileId:X4}");
                        return null;
                    }

                    mulStream.Seek(offset, SeekOrigin.Begin);

                    // Read header: 4 bytes (should be 0 for land tiles)
                    uint header = mulReader.ReadUInt32();
                    System.Diagnostics.Debug.WriteLine($"LandTileArtReader: Header value: 0x{header:X8}");

                    // Land tiles are stored as 44x44 with run-length encoding
                    // Each row has a different number of pixels (diamond shape)
                    // Row 0: 2 pixels, Row 1: 4 pixels, ..., Row 21: 44 pixels (center)
                    // Then it shrinks back down

                    Bitmap bmp = new Bitmap(LAND_TILE_SIZE, LAND_TILE_SIZE, PixelFormat.Format32bppArgb);

                    // Read diamond pattern
                    for (int y = 0; y < LAND_TILE_SIZE; y++)
                    {
                        // Calculate how many pixels in this row
                        int pixelsInRow;
                        if (y < 22)
                            pixelsInRow = 2 + (y * 2); // Growing: 2, 4, 6, ..., 44
                        else
                            pixelsInRow = 2 + ((43 - y) * 2); // Shrinking: 42, 40, ..., 2

                        // Calculate starting X position (center the row)
                        int startX = (LAND_TILE_SIZE - pixelsInRow) / 2;

                        // Read pixels for this row
                        for (int x = 0; x < pixelsInRow; x++)
                        {
                            if (mulStream.Position + 2 > mulStream.Length)
                            {
                                System.Diagnostics.Debug.WriteLine($"LandTileArtReader: Unexpected end of file at row {y}, pixel {x}");
                                break;
                            }

                            ushort color16 = mulReader.ReadUInt16();

                            // Convert 16-bit RGB555 to 32-bit ARGB
                            Color color = ConvertRgb555ToColor(color16);

                            bmp.SetPixel(startX + x, y, color);
                        }
                    }

                    System.Diagnostics.Debug.WriteLine($"LandTileArtReader: Successfully loaded tile 0x{tileId:X4}");
                    return bmp;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LandTileArtReader: Exception loading tile 0x{tileId:X4}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Convert UO's RGB555 format to System.Drawing.Color
        /// </summary>
        private static Color ConvertRgb555ToColor(ushort color16)
        {
            // UO uses RGB555 format: bit 15 is unused, bits 14-10 are R, bits 9-5 are G, bits 4-0 are B
            int r = ((color16 >> 10) & 0x1F) * 255 / 31;
            int g = ((color16 >> 5) & 0x1F) * 255 / 31;
            int b = (color16 & 0x1F) * 255 / 31;

            // If all bits are 0, it's transparent
            if (color16 == 0)
                return Color.Transparent;

            return Color.FromArgb(255, r, g, b);
        }
    }
}
