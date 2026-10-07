namespace MeesaMultisMaker
{
    public class ImageInfo
    {
        public string FilePath { get; set; }
        public string GraphicId { get; set; }

        /// <summary>
        /// The item ID (int for Tecmo Expanded Art support, up to 0x3FFFF)
        /// </summary>
        public int ItemId { get; set; }

        /// <summary>
        /// True if this item is loaded from MUL files, false if from PNG files
        /// </summary>
        public bool IsMulItem { get; set; }

        /// <summary>
        /// True if this is an empty/unassigned art slot in the MUL file
        /// </summary>
        public bool IsEmptySlot { get; set; }
    }
}
