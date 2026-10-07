using System;
using System.IO;
using System.Text;

namespace MeesaMultisMaker.Mul
{
    public static class MapDiagnostics
    {
        public static string AnalyzeMapFile(string mapPath)
        {
            var sb = new StringBuilder();
            
            if (!File.Exists(mapPath))
            {
                return $"File not found: {mapPath}";
            }
            
            using (var fs = File.OpenRead(mapPath))
            using (var reader = new BinaryReader(fs))
            {
                long fileLength = fs.Length;
                sb.AppendLine($"Map File: {mapPath}");
                sb.AppendLine($"File Size: {fileLength} bytes");
                sb.AppendLine($"Expected blocks (196 bytes each): {fileLength / 196}");
                sb.AppendLine();
                
                // Read first block
                sb.AppendLine("=== FIRST BLOCK (Block 0,0) ===");
                uint header = reader.ReadUInt32();
                sb.AppendLine($"Block Header: 0x{header:X8}");
                sb.AppendLine();
                
                // Read first 10 tiles from first block
                sb.AppendLine("First 10 tiles in block 0:");
                for (int i = 0; i < 10; i++)
                {
                    ushort tileId = reader.ReadUInt16();
                    sbyte z = reader.ReadSByte();
                    sb.AppendLine($"  Tile {i}: ID=0x{tileId:X4} ({tileId}), Z={z}");
                }
                
                // Skip rest of first block
                fs.Seek(196, SeekOrigin.Begin);
                
                // Read second block
                sb.AppendLine();
                sb.AppendLine("=== SECOND BLOCK (Block 0,1 or 1,0?) ===");
                header = reader.ReadUInt32();
                sb.AppendLine($"Block Header: 0x{header:X8}");
                sb.AppendLine();
                
                sb.AppendLine("First 10 tiles in block 1:");
                for (int i = 0; i < 10; i++)
                {
                    ushort tileId = reader.ReadUInt16();
                    sbyte z = reader.ReadSByte();
                    sb.AppendLine($"  Tile {i}: ID=0x{tileId:X4} ({tileId}), Z={z}");
                }
                
                // Now let's try to find tile at world coordinate 1,1
                sb.AppendLine();
                sb.AppendLine("=== ANALYZING TILE AT WORLD COORDINATE (1,1) ===");
                
                // Tile 1,1 is in block 0,0, at position 1,1 within the block
                // Block 0,0 starts at byte 0
                // Header is 4 bytes
                // Each tile is 3 bytes (ushort tileId + sbyte z)
                // Within an 8x8 block, tiles are stored in row-major order
                // So tile (1,1) = row 1, col 1 = index 1*8 + 1 = 9
                
                int blockX = 1 / 8;  // = 0
                int blockY = 1 / 8;  // = 0
                int tileX = 1 % 8;   // = 1
                int tileY = 1 % 8;   // = 1
                int tileIndexInBlock = tileY * 8 + tileX;  // = 9
                
                sb.AppendLine($"Block coordinates: ({blockX}, {blockY})");
                sb.AppendLine($"Tile within block: ({tileX}, {tileY})");
                sb.AppendLine($"Tile index in block: {tileIndexInBlock}");
                
                // Calculate file position
                // For now assume blocks are in row-major order (blocksX = 768, blocksY = 512)
                int blocksX = 6144 / 8;  // 768
                long blockIndex = blockY * blocksX + blockX;  // = 0
                long blockOffset = blockIndex * 196;
                long tileOffset = blockOffset + 4 + (tileIndexInBlock * 3);
                
                sb.AppendLine($"Block index: {blockIndex}");
                sb.AppendLine($"File offset: {tileOffset}");
                
                fs.Seek(tileOffset, SeekOrigin.Begin);
                ushort tile_1_1_id = reader.ReadUInt16();
                sbyte tile_1_1_z = reader.ReadSByte();
                
                sb.AppendLine($"Tile at (1,1): ID=0x{tile_1_1_id:X4} ({tile_1_1_id}), Z={tile_1_1_z}");
                sb.AppendLine();
                
                // Check if it's water (water tiles are typically 0x00A8-0x00AB or 0x0136-0x01AF)
                bool isWater = (tile_1_1_id >= 0x00A8 && tile_1_1_id <= 0x00AB) || 
                               (tile_1_1_id >= 0x0136 && tile_1_1_id <= 0x01AF);
                sb.AppendLine($"Is this water? {isWater}");
                
                // Also check tile at 0,0
                sb.AppendLine();
                sb.AppendLine("=== TILE AT WORLD COORDINATE (0,0) ===");
                fs.Seek(4, SeekOrigin.Begin);  // Skip block header
                ushort tile_0_0_id = reader.ReadUInt16();
                sbyte tile_0_0_z = reader.ReadSByte();
                sb.AppendLine($"Tile at (0,0): ID=0x{tile_0_0_id:X4} ({tile_0_0_id}), Z={tile_0_0_z}");
                bool isWater00 = (tile_0_0_id >= 0x00A8 && tile_0_0_id <= 0x00AB) || 
                                 (tile_0_0_id >= 0x0136 && tile_0_0_id <= 0x01AF);
                sb.AppendLine($"Is this water? {isWater00}");
                
                // Check several tiles around 0,0 and 1,1
                sb.AppendLine();
                sb.AppendLine("=== FIRST 64 TILES (8x8 block 0) ===");
                fs.Seek(4, SeekOrigin.Begin);
                for (int y = 0; y < 8; y++)
                {
                    StringBuilder row = new StringBuilder();
                    for (int x = 0; x < 8; x++)
                    {
                        ushort tid = reader.ReadUInt16();
                        sbyte tz = reader.ReadSByte();
                        bool water = (tid >= 0x00A8 && tid <= 0x00AB) || 
                                     (tid >= 0x0136 && tid <= 0x01AF);
                        row.Append(water ? "W" : ".");
                        row.Append($"{tid:X3} ");
                    }
                    sb.AppendLine($"Row {y}: {row}");
                }
            }
            
            return sb.ToString();
        }
    }
}
