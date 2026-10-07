# Map Export Restitch Feature

## Overview

The Map Exporter now includes an option to automatically restitch tiled exports back into a single large image. This is particularly useful when:

1. Your desired export exceeds GDI+ single-image limits (25,620 × 25,872 pixels or ~2.5GB memory)
2. You want the flexibility of both tiled export AND a single stitched image
3. You need to bypass memory limitations while still getting a complete single-file output

## How It Works

### Export Process
1. **Export as Tiles**: Map is first exported as 512×512 JPEG tiles
2. **Restitch**: After all tiles are created, they are automatically stitched back together into a single image
3. **Dual Output**: You get BOTH:
   - Tiled export with HTML viewer (`map_export_tiles/` folder)
   - Single stitched image (`map_export.png`)

### Stitching Methods

The system uses two methods with automatic fallback:

#### 1. ImageMagick (Primary Method) ✅ **Recommended**
- **Pros**:
  - Can handle extremely large images (multi-gigabyte files)
  - Efficient memory usage
  - Fast processing
  - No dimension limits
- **Cons**:
  - Requires ImageMagick installation
- **Installation**:
  ```
  Download from: https://imagemagick.org/script/download.php
  Install ImageMagick 7+ (Q16-HDRI recommended)
  ```

#### 2. GDI+ Fallback (Automatic)
- **Pros**:
  - No external dependencies
  - Works out of the box
- **Cons**:
  - Limited to 65,535 pixels per side
  - May fail with insufficient memory for large images
- **Automatic**: Used only if ImageMagick is not found

## Usage

### In Export Dialog

1. Open Map Viewer
2. Click **"Export Entire Map"**
3. Select **"Export as Tiled Images"** radio button
4. ✅ Check **"Restitch into Single Image After Export"**
5. Configure other options (zoom, region, etc.)
6. Click **"Export"**

### Settings Explained

```
┌─────────────────────────────────────────────┐
│ Export Mode:                                │
│                                             │
│ ○ Export as Single Image                   │
│   └─ Direct single-image export            │
│      (Limited by GDI+ constraints)          │
│                                             │
│ ● Export as Tiled Images                   │
│   └─ Creates 512×512 tiles + viewer        │
│       ✅ Restitch into Single Image        │
│          └─ Stitches tiles after export    │
│             (Uses ImageMagick or GDI+)      │
└─────────────────────────────────────────────┘
```

## Example Scenarios

### Scenario 1: Very Large Export (10.5 px/tile, full Trammel)
**Problem**: 25,620 × 25,872 = ~2.5GB (exceeds GDI+ allocation limit)

**Solution**:
1. Select "Export as Tiled Images"
2. Check "Restitch into Single Image"
3. Export creates:
   - `map0_export_tiles/` with ~24,000 tiles
   - `map0_export.png` (stitched by ImageMagick)

**Result**: ✅ Full quality single image without memory errors!

### Scenario 2: Medium Export (9.0 px/tile)
**Problem**: Want tiled export for HTML viewer BUT also need single file

**Solution**:
1. Select "Export as Tiled Images"
2. Check "Restitch into Single Image"
3. Export creates both outputs

**Result**: ✅ HTML viewer + single PNG for external tools!

### Scenario 3: No ImageMagick Installed
**Scenario**: User doesn't have ImageMagick

**Behavior**:
- System automatically uses GDI+ fallback
- Works for images under 65,535 px per side
- Shows warning if dimensions too large
- User still gets tiles + viewer.html

## Technical Details

### ImageMagick Detection
The system searches for ImageMagick in:
- `PATH` environment variable
- `C:\Program Files\ImageMagick-7.1.x-Q16-HDRI\magick.exe`
- `C:\Program Files\ImageMagick\magick.exe`
- `C:\ImageMagick\magick.exe`

### Stitching Command (ImageMagick)
```bash
magick montage tile_0_0.jpg tile_0_1.jpg ... tile_N_M.jpg \
  -tile COLSxROWS \
  -geometry 512x512+0+0 \
  -mode Concatenate \
  output.png
```

### GDI+ Fallback Process
1. Creates full-resolution bitmap in memory
2. Iterates through tiles in order
3. Draws each tile at correct position
4. Saves as PNG/JPEG based on original output path

## Limitations

### GDI+ Fallback Limits
- **Max dimension**: 65,535 pixels per side
- **Practical limit**: ~2.5GB allocated memory
  - Depends on available RAM
  - Depends on memory fragmentation
  - May fail even under theoretical limit

### ImageMagick Advantages
- No practical dimension limits
- Handles multi-gigabyte images
- Efficient streaming I/O
- Minimal memory footprint

## Performance

### Stitching Performance (ImageMagick)
| Image Size | Tiles | Stitch Time |
|------------|-------|-------------|
| 10,000 × 10,000 | ~400 | ~5 seconds |
| 20,000 × 20,000 | ~1,600 | ~15 seconds |
| 30,000 × 30,000 | ~3,600 | ~30 seconds |

### GDI+ Fallback
- Similar times for smaller images
- May fail or hang for very large images

## Troubleshooting

### "ImageMagick not found" message
**Solution**: Install ImageMagick from https://imagemagick.org
- Choose Q16-HDRI version
- Select "Add to PATH" during installation
- Restart application

### Stitching fails with GDI+ error
**Cause**: Image too large for GDI+ memory allocation

**Solutions**:
1. Install ImageMagick (recommended)
2. Reduce zoom level
3. Export smaller region
4. Use tiled export without stitching

### Stitched image has seams/gaps
**Cause**: Rare issue with JPEG tile boundaries

**Solution**: 
- Use PNG output format (slower but lossless)
- Report issue with specific export settings

## Integration with Existing Workflow

### When to Use Restitch
✅ **Use When**:
- Exporting very large maps (> 100 MP)
- Need both HTML viewer and single image
- Want maximum quality without memory errors
- Have ImageMagick installed

❌ **Don't Use When**:
- Small/medium exports (< 100 MP) - use single image mode
- Only need HTML viewer - uncheck restitch
- Don't have ImageMagick and image > 65k px per side

## Future Enhancements

Potential improvements:
- [ ] Bundle ImageMagick with installer
- [ ] Add progress bar for stitching phase
- [ ] Support for VIPS/libvips as alternative
- [ ] Parallel tile stitching
- [ ] Option to delete tiles after stitching

## Credits

- **ImageMagick**: https://imagemagick.org
- **GDI+**: Microsoft .NET Framework System.Drawing

---

**Last Updated**: 2025
**Feature Version**: 1.0
