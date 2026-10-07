using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MeesaMultisMaker.Mul
{
    /// <summary>
    /// Flags for static items from TileData
    /// </summary>
    [Flags]
    public enum TileFlag : ulong
    {
        None = 0x00000000,
        Background = 0x00000001,
        Weapon = 0x00000002,
        Transparent = 0x00000004,
        Translucent = 0x00000008,
        Wall = 0x00000010,
        Damaging = 0x00000020,
        Impassable = 0x00000040,
        Wet = 0x00000080,
        Unknown1 = 0x00000100,
        Surface = 0x00000200,
        Bridge = 0x00000400,
        Generic = 0x00000800,
        Window = 0x00001000,
        NoShoot = 0x00002000,
        ArticleA = 0x00004000,
        ArticleAn = 0x00008000,
        Internal = 0x00010000,
        Foliage = 0x00020000,
        PartialHue = 0x00040000,
        NoHouse = 0x00080000,
        Map = 0x00100000,
        Container = 0x00200000,
        Wearable = 0x00400000,
        LightSource = 0x00800000,
        Animation = 0x01000000,
        HoverOver = 0x02000000,
        NoDiagonal = 0x04000000,
        Armor = 0x08000000,
        Roof = 0x10000000,
        Door = 0x20000000,
        StairBack = 0x40000000,
        StairRight = 0x80000000,
        // High-seas era flags (64-bit)
        AlphaBlend = 0x0100000000,
        UseNewArt = 0x0200000000,
        ArtUsed = 0x0400000000,
        NoShadow = 0x1000000000,
        PixelBleed = 0x2000000000,
        PlayAnimOnce = 0x4000000000,
        MultiMovable = 0x10000000000,
    }

    /// <summary>
    /// Data for a land tile from TileData
    /// </summary>
    public class LandTileData
    {
        public TileFlag Flags { get; set; }
        public ushort TextureId { get; set; }
        public string Name { get; set; }
    }

    /// <summary>
    /// Data for a static item from TileData
    /// </summary>
    public class ItemTileData
    {
        public TileFlag Flags { get; set; }
        public byte Weight { get; set; }
        public byte Quality { get; set; }  // Also Layer for wearables, Light for light sources
        public ushort MiscData { get; set; }
        public byte Unknown1 { get; set; }
        public byte Quantity { get; set; }
        public ushort Animation { get; set; }
        public byte Unknown2 { get; set; }
        public byte Hue { get; set; }
        public ushort StackingOffset { get; set; }  // Also Unknown4
        public byte Height { get; set; }
        public string Name { get; set; }

        /// <summary>
        /// Get a human-readable list of flags
        /// </summary>
        public string GetFlagsString()
        {
            if (Flags == TileFlag.None)
                return "None";

            var flags = new List<string>();
            foreach (TileFlag flag in Enum.GetValues(typeof(TileFlag)))
            {
                if (flag != TileFlag.None && Flags.HasFlag(flag))
                {
                    flags.Add(flag.ToString());
                }
            }
            return string.Join(", ", flags);
        }
    }

    /// <summary>
    /// Reads tile metadata from tiledata.mul
    /// Supports multiple formats: Old (32-bit flags), High Seas (64-bit flags), and various private server variants
    /// </summary>
    public class TileDataReader
    {
        private readonly Dictionary<ushort, LandTileData> _landTiles = new Dictionary<ushort, LandTileData>();
        private readonly Dictionary<ushort, ItemTileData> _itemTiles = new Dictionary<ushort, ItemTileData>();
        private bool _isLoaded = false;
        private bool _isHighSeas = false;  // High Seas client uses 64-bit flags

        public bool IsLoaded => _isLoaded;

        // Size constants for different formats
        // Old format (pre-High Seas): 32-bit flags
        private const int OLD_LAND_ENTRY_SIZE = 26;  // 4 flags + 2 texId + 20 name
        private const int OLD_ITEM_ENTRY_SIZE = 37;  // 4 flags + 13 data + 20 name

        // High Seas format: 64-bit flags  
        private const int HS_LAND_ENTRY_SIZE = 30;   // 8 flags + 2 texId + 20 name
        private const int HS_ITEM_ENTRY_SIZE = 41;   // 8 flags + 13 data + 20 name

        /// <summary>
        /// Load tiledata.mul from the specified folder
        /// </summary>
        public bool Load(string mulFolder)
        {
            string tiledataPath = Path.Combine(mulFolder, "tiledata.mul");
            if (!File.Exists(tiledataPath))
                return false;

            _landTiles.Clear();
            _itemTiles.Clear();
            _isLoaded = false;

            try
            {
                using (var fs = new FileStream(tiledataPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var reader = new BinaryReader(fs))
                {
                    long fileLength = fs.Length;

                    // Detect format by analyzing file structure
                    // Try to detect based on expected sizes
                    _isHighSeas = DetectHighSeasFormat(fileLength);

                    System.Diagnostics.Debug.WriteLine($"TileData: File size={fileLength}, Detected HighSeas={_isHighSeas}");

                    int landEntrySize = _isHighSeas ? HS_LAND_ENTRY_SIZE : OLD_LAND_ENTRY_SIZE;
                    int itemEntrySize = _isHighSeas ? HS_ITEM_ENTRY_SIZE : OLD_ITEM_ENTRY_SIZE;

                    // Read land tiles (512 groups of 32 tiles each)
                    // Each group has a 4-byte header
                    int landGroupSize = 4 + (32 * landEntrySize);
                    long landDataSize = 512L * landGroupSize;

                    if (fileLength < landDataSize)
                    {
                        System.Diagnostics.Debug.WriteLine($"TileData: File too small for land data. Expected at least {landDataSize}, got {fileLength}");
                        // Try with old format
                        if (_isHighSeas)
                        {
                            _isHighSeas = false;
                            landEntrySize = OLD_LAND_ENTRY_SIZE;
                            itemEntrySize = OLD_ITEM_ENTRY_SIZE;
                            landGroupSize = 4 + (32 * landEntrySize);
                            landDataSize = 512L * landGroupSize;
                        }
                    }

                    for (int group = 0; group < 512; group++)
                    {
                        long groupStart = (long)group * landGroupSize;
                        if (groupStart + landGroupSize > fileLength)
                        {
                            System.Diagnostics.Debug.WriteLine($"TileData: Stopping land read at group {group}, file too short");
                            break;
                        }

                        fs.Seek(groupStart, SeekOrigin.Begin);

                        // Skip group header (4 bytes)
                        reader.ReadInt32();

                        for (int i = 0; i < 32; i++)
                        {
                            if (fs.Position + landEntrySize > fileLength)
                                break;

                            ushort tileId = (ushort)(group * 32 + i);
                            var landTile = ReadLandTile(reader);
                            _landTiles[tileId] = landTile;
                        }
                    }

                    // Calculate where item data starts
                    long itemDataStart = landDataSize;

                    // Read static items
                    // Item groups also have 4-byte headers, 32 items per group
                    int itemGroupSize = 4 + (32 * itemEntrySize);

                    fs.Seek(itemDataStart, SeekOrigin.Begin);

                    int itemId = 0;
                    int maxItems = 0x10000; // Maximum 65536 items

                    while (fs.Position + 4 <= fileLength && itemId < maxItems)
                    {
                        long groupStart = fs.Position;

                        // Check if we have enough data for the header
                        if (groupStart + 4 > fileLength)
                            break;

                        // Skip group header (4 bytes)
                        reader.ReadInt32();

                        for (int i = 0; i < 32 && itemId < maxItems; i++)
                        {
                            // Check if we have enough data for this item
                            if (fs.Position + itemEntrySize > fileLength)
                            {
                                // Try to read what we can
                                break;
                            }

                            try
                            {
                                var itemTile = ReadItemTile(reader);
                                _itemTiles[(ushort)itemId] = itemTile;
                            }
                            catch (EndOfStreamException)
                            {
                                // Stop reading if we hit end of stream
                                System.Diagnostics.Debug.WriteLine($"TileData: EndOfStream at item {itemId}");
                                break;
                            }
                            itemId++;
                        }

                        // If we couldn't read a full group, stop
                        if (fs.Position < groupStart + itemGroupSize && fs.Position >= fileLength - itemEntrySize)
                            break;
                    }

                    _isLoaded = _itemTiles.Count > 0;
                    System.Diagnostics.Debug.WriteLine($"TileData loaded: {_landTiles.Count} land tiles, {_itemTiles.Count} items, HighSeas={_isHighSeas}");
                    return _isLoaded;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading tiledata.mul: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Detect if this is High Seas format based on file size
        /// </summary>
        private bool DetectHighSeasFormat(long fileLength)
        {
            // Calculate expected sizes for both formats
            // Land: 512 groups * (4 header + 32 * entry_size)
            // Items: variable number of groups

            // Old format land data size
            long oldLandSize = 512L * (4 + 32 * OLD_LAND_ENTRY_SIZE); // 428,032 bytes

            // High Seas land data size would be: 512L * (4 + 32 * HS_LAND_ENTRY_SIZE) = 493,568 bytes

            // If the file is large enough for HS land data plus substantial item data, it's likely HS
            // HS tiledata is typically around 3MB+
            // Old tiledata is typically around 1.6MB

            // A more reliable check: see if the file size makes sense for High Seas
            // HS with ~32k items: 493,568 + 32768/32 * (4 + 32*41) = ~1.8MB minimum
            // With more items it grows

            // Simple heuristic: files larger than 2MB are likely High Seas
            if (fileLength > 2000000)
                return true;

            // Files between 1.5MB and 2MB - check more carefully
            // If file is too small even for old format's land data, something is wrong
            if (fileLength < oldLandSize)
            {
                System.Diagnostics.Debug.WriteLine($"TileData: File unusually small ({fileLength} bytes), trying old format");
                return false;
            }

            return false;
        }

        private LandTileData ReadLandTile(BinaryReader reader)
        {
            var tile = new LandTileData();

            if (_isHighSeas)
            {
                tile.Flags = (TileFlag)reader.ReadUInt64();
            }
            else
            {
                tile.Flags = (TileFlag)reader.ReadUInt32();
            }

            tile.TextureId = reader.ReadUInt16();
            tile.Name = ReadFixedString(reader, 20);

            return tile;
        }

        private ItemTileData ReadItemTile(BinaryReader reader)
        {
            var item = new ItemTileData();

            if (_isHighSeas)
            {
                item.Flags = (TileFlag)reader.ReadUInt64();
            }
            else
            {
                item.Flags = (TileFlag)reader.ReadUInt32();
            }

            item.Weight = reader.ReadByte();
            item.Quality = reader.ReadByte();
            item.MiscData = reader.ReadUInt16();
            item.Unknown1 = reader.ReadByte();
            item.Quantity = reader.ReadByte();
            item.Animation = reader.ReadUInt16();
            item.Unknown2 = reader.ReadByte();
            item.Hue = reader.ReadByte();
            item.StackingOffset = reader.ReadUInt16();
            item.Height = reader.ReadByte();
            item.Name = ReadFixedString(reader, 20);

            return item;
        }

        private string ReadFixedString(BinaryReader reader, int length)
        {
            byte[] bytes = reader.ReadBytes(length);
            int nullIndex = Array.IndexOf(bytes, (byte)0);
            if (nullIndex >= 0)
            {
                return Encoding.ASCII.GetString(bytes, 0, nullIndex);
            }
            return Encoding.ASCII.GetString(bytes);
        }

        /// <summary>
        /// Get land tile data by tile ID
        /// </summary>
        public LandTileData GetLandTile(ushort tileId)
        {
            if (_landTiles.TryGetValue(tileId, out var tile))
                return tile;
            return null;
        }

        /// <summary>
        /// Get static item data by item ID
        /// </summary>
        public ItemTileData GetItemTile(ushort itemId)
        {
            if (_itemTiles.TryGetValue(itemId, out var item))
                return item;
            return null;
        }
    }
}
