using System;
using System.IO;
using System.Linq;

namespace MeesaMultisMaker
{
    public partial class Form1
    {
        private void SearchTextBox_TextChanged(object sender, EventArgs e)
        {
            var text = searchTextBox.Text ?? string.Empty;
            var trimmed = text.Trim();

            if (useMulFiles)
            {
                // MUL-based search
                FilterPalette(trimmed);
            }
            else
            {
                // PNG-based search
                if (allImageFiles == null || allImageFiles.Length == 0) return;

                if (trimmed.Length == 0 || string.Equals(trimmed, "Search...", StringComparison.OrdinalIgnoreCase))
                {
                    filteredImageFiles = allImageFiles;
                }
                else
                {
                    filteredImageFiles = allImageFiles
                        .Where(f => Path.GetFileNameWithoutExtension(f).IndexOf(trimmed, StringComparison.OrdinalIgnoreCase) >= 0)
                        .ToArray();
                }

                imageCache.Clear();
                paletteListView.VirtualListSize = filteredImageFiles.Length;
                this.Text = (filteredImageFiles.Length != allImageFiles.Length)
                    ? $"Meesa Multis Maker - github.com/MeesaJarJar - {filteredImageFiles.Length} of {allImageFiles.Length} items"
                    : $"Meesa Multis Maker - github.com/MeesaJarJar - {allImageFiles.Length} items from {Path.GetFileName(artFolderPath)}";
                paletteListView.Invalidate();
            }
        }
    }
}
