using System;
using System.Collections.Generic;

namespace MeesaMultisMaker.JarJar
{
    /// <summary>
    /// Represents a single entry in the MeesaJarJar lookup table.
    /// Maps a graphic ID to its atlas position and dimensions.
    /// </summary>
    public struct LookupEntry
    {
        public ushort AtlasIndex;
        public ushort X;
        public ushort Y;
        public ushort Width;
        public ushort Height;
        public ushort OriginalId;
        public ushort Flags;
        public ushort Reserved;

        /// <summary>
        /// Returns true if this entry represents a valid sprite (bit 0 of Flags is set).
        /// </summary>
        public bool IsValid => (Flags & 1) != 0;
    }

    /// <summary>
    /// Parses and provides access to the MeesaJarJar lookup.bin binary file.
    /// The lookup table maps graphic IDs (0x0000–0xFFFF) to atlas positions.
    /// </summary>
    public class LookupTable
    {
        private readonly byte[] _data;
        private readonly uint _entryCount;

        /// <summary>
        /// Number of entries in the lookup table.
        /// </summary>
        public uint EntryCount => _entryCount;

        /// <summary>
        /// Creates a LookupTable from raw binary data downloaded from the server.
        /// </summary>
        public LookupTable(byte[] data)
        {
            if (data == null || data.Length < 4)
                throw new ArgumentException("Invalid lookup table data.");

            _data = data;
            _entryCount = BitConverter.ToUInt32(data, 0);
        }

        /// <summary>
        /// Reads the lookup entry for a given graphic ID.
        /// Returns null if the ID is out of range or the entry is invalid (empty slot).
        /// </summary>
        public LookupEntry? GetEntry(ushort graphicId)
        {
            if (graphicId >= _entryCount)
                return null;

            int offset = 4 + (graphicId * 16);
            if (offset + 16 > _data.Length)
                return null;

            var entry = new LookupEntry
            {
                AtlasIndex = BitConverter.ToUInt16(_data, offset),
                X          = BitConverter.ToUInt16(_data, offset + 2),
                Y          = BitConverter.ToUInt16(_data, offset + 4),
                Width      = BitConverter.ToUInt16(_data, offset + 6),
                Height     = BitConverter.ToUInt16(_data, offset + 8),
                OriginalId = BitConverter.ToUInt16(_data, offset + 10),
                Flags      = BitConverter.ToUInt16(_data, offset + 12),
                Reserved   = BitConverter.ToUInt16(_data, offset + 14),
            };

            if (!entry.IsValid)
                return null;

            return entry;
        }

        /// <summary>
        /// Returns all valid entries as a dictionary keyed by graphic ID.
        /// </summary>
        public Dictionary<ushort, LookupEntry> GetAllValidEntries()
        {
            var result = new Dictionary<ushort, LookupEntry>();
            ushort max = (ushort)Math.Min(_entryCount, ushort.MaxValue);

            for (ushort id = 0; id < max; id++)
            {
                var entry = GetEntry(id);
                if (entry.HasValue)
                    result[id] = entry.Value;
            }

            return result;
        }
    }
}
