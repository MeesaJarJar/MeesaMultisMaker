using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MeesaMultisMaker.Mul
{
    /// <summary>
    /// Best-effort writer for tiledata.mul item entries (old 37-byte and High Seas 41-byte formats).
    /// Copies raw entry bytes from one item ID to another; never throws to callers' save path
    /// (callers wrap in try/catch). Backup is handled by StaticArtWriter.BackupArtFiles.
    /// </summary>
    public static class TileDataWriter
    {
        private const int OLD_LAND_ENTRY_SIZE = 26;
        private const int OLD_ITEM_ENTRY_SIZE = 37;
        private const int HS_LAND_ENTRY_SIZE = 30;
        private const int HS_ITEM_ENTRY_SIZE = 41;

        private static bool IsHighSeas(long fileLength)
        {
            return fileLength > 2000000;
        }

        private static void GetLayout(long fileLength, out bool hs, out int itemEntrySize, out long itemDataStart)
        {
            hs = IsHighSeas(fileLength);
            int landEntrySize = hs ? HS_LAND_ENTRY_SIZE : OLD_LAND_ENTRY_SIZE;
            itemEntrySize = hs ? HS_ITEM_ENTRY_SIZE : OLD_ITEM_ENTRY_SIZE;
            long landGroupSize = 4L + (32L * landEntrySize);
            long landDataSize = 512L * landGroupSize;
            if (fileLength < landDataSize && hs)
            {
                hs = false;
                landEntrySize = OLD_LAND_ENTRY_SIZE;
                itemEntrySize = OLD_ITEM_ENTRY_SIZE;
                landGroupSize = 4L + (32L * landEntrySize);
                landDataSize = 512L * landGroupSize;
            }
            itemDataStart = landDataSize;
        }

        private static long EntryOffset(long itemDataStart, int itemEntrySize, int itemId)
        {
            long groupSize = 4L + (32L * itemEntrySize);
            long group = itemId / 32;
            long indexInGroup = itemId % 32;
            return itemDataStart + group * groupSize + 4 + indexInGroup * itemEntrySize;
        }

        /// <summary>
        /// Copy the raw tiledata item entry from fromId to toId. Returns false if unsafe/impossible.
        /// </summary>
        public static bool CopyItemEntry(string mulFolder, int fromId, int toId)
        {
            if (fromId < 0 || toId < 0 || fromId > 0xFFFF || toId > 0xFFFF)
                return false;
            string path = Path.Combine(mulFolder, "tiledata.mul");
            if (!File.Exists(path))
                return false;
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
                {
                    GetLayout(fs.Length, out _, out int itemEntrySize, out long itemDataStart);
                    long groupSize = 4L + (32L * itemEntrySize);
                    long srcOff = EntryOffset(itemDataStart, itemEntrySize, fromId);
                    long dstOff = EntryOffset(itemDataStart, itemEntrySize, toId);
                    long needEnd = dstOff + itemEntrySize;

                    // Extend with zeros if the destination is beyond EOF (new slots past tiledata range).
                    if (needEnd > fs.Length)
                    {
                        if (needEnd > itemDataStart + groupSize * 2048)
                            return false; // sanity cap: 64k items
                        fs.Seek(0, SeekOrigin.End);
                        long pad = needEnd - fs.Length;
                        byte[] zeros = new byte[Math.Min(pad, 65536)];
                        while (pad > 0)
                        {
                            int n = (int)Math.Min(pad, zeros.Length);
                            fs.Write(zeros, 0, n);
                            pad -= n;
                        }
                    }
                    if (srcOff + itemEntrySize > fs.Length)
                        return false;

                    byte[] buf = new byte[itemEntrySize];
                    fs.Seek(srcOff, SeekOrigin.Begin);
                    int read = fs.Read(buf, 0, itemEntrySize);
                    if (read != itemEntrySize)
                        return false;
                    fs.Seek(dstOff, SeekOrigin.Begin);
                    fs.Write(buf, 0, itemEntrySize);
                    fs.Flush();
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Copy entries for a remap (origId -&gt; newId). Returns log lines for OutputLog.
        /// </summary>
        public static List<string> CopyItemEntries(string mulFolder, Dictionary<int, int> mapping)
        {
            var lines = new List<string>();
            if (mapping == null)
                return lines;
            foreach (var kvp in mapping)
            {
                try
                {
                    if (CopyItemEntry(mulFolder, kvp.Key, kvp.Value))
                        lines.Add($"TileData 0x{kvp.Key:X4} copied to 0x{kvp.Value:X4}");
                }
                catch { }
            }
            return lines;
        }

        /// <summary>
        /// Set the 20-char ASCII name of an item entry. Best-effort.
        /// </summary>
        public static bool SetItemName(string mulFolder, int itemId, string name)
        {
            if (itemId < 0 || itemId > 0xFFFF)
                return false;
            string path = Path.Combine(mulFolder, "tiledata.mul");
            if (!File.Exists(path))
                return false;
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
                {
                    GetLayout(fs.Length, out _, out int itemEntrySize, out long itemDataStart);
                    long off = EntryOffset(itemDataStart, itemEntrySize, itemId);
                    long nameOff = off + itemEntrySize - 20;
                    if (nameOff < 0 || nameOff + 20 > fs.Length)
                        return false;
                    byte[] nameBytes = new byte[20];
                    if (!string.IsNullOrEmpty(name))
                    {
                        byte[] src = Encoding.ASCII.GetBytes(name);
                        Array.Copy(src, nameBytes, Math.Min(src.Length, 20));
                    }
                    fs.Seek(nameOff, SeekOrigin.Begin);
                    fs.Write(nameBytes, 0, 20);
                    fs.Flush();
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Set the flags of an item entry (low 32 bits always; high bits only in High Seas format).
        /// </summary>
        public static bool SetItemFlags(string mulFolder, int itemId, ulong flags)
        {
            if (itemId < 0 || itemId > 0xFFFF)
                return false;
            string path = Path.Combine(mulFolder, "tiledata.mul");
            if (!File.Exists(path))
                return false;
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
                {
                    GetLayout(fs.Length, out bool hs, out int itemEntrySize, out long itemDataStart);
                    long off = EntryOffset(itemDataStart, itemEntrySize, itemId);
                    if (off + (hs ? 8 : 4) > fs.Length)
                        return false;
                    fs.Seek(off, SeekOrigin.Begin);
                    using (var bw = new BinaryWriter(fs, Encoding.ASCII, true))
                    {
                        if (hs)
                            bw.Write(flags);
                        else
                            bw.Write((uint)(flags & 0xFFFFFFFF));
                    }
                    fs.Flush();
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
