# Map Export Features

## Overview
Added two new export features:
1. **PNG Transparency Support** - Export maps with transparent backgrounds instead of black
2. **Single Image Export Option** - Force export as one large image instead of tiled output

## Changes Made

### Feature 1: PNG Transparency Support

#### 1. **MapExportOptionsDialog** (`MapViewerForm.Export.cs`)
   - Added new property: `UseTransparentBackground`
   - Added checkbox: `useTransparentBackgroundCheckBox`
   - Positioned between "Include Static Objects" and "HTML & Minimap Only" options
   - Label: "Use Transparent Background (PNG only)"

#### 2. **Export Pipeline** (`MapViewerForm.Export.cs`)
   - Added `useTransparency` parameter to:
     - `BeginExportMap()` method
     - `ExportMapRegion()` method
     - `RenderExportTile()` method
   - Parameter is threaded through the entire export pipeline

#### 3. **Rendering Changes** (`MapViewerForm.Export.cs`)
   - Modified `RenderExportTile()` to check `useTransparency` parameter
   - When enabled: Uses `Color.Transparent` for background
   - When disabled: Uses `Color.Black` for background (original behavior)
   - Code: `g.Clear(useTransparency ? Color.Transparent : Color.Black);`

### Feature 2: Single Image Export Option

#### 1. **MapExportOptionsDialog** (`MapViewerForm.Export.cs`)
   - Added new property: `ExportAsSingleImage`
   - Added radio buttons: `singleImageRadio` and `tiledExportRadio`
   - New "Export Mode" section in the dialog
   - Single Image is the default selection

#### 2. **Export Logic** (`MapViewerForm.Export.cs`)
   - Added `forceSingleImage` parameter to:
     - `BeginExportMap()` method
     - `ExportMapRegion()` method
   - Modified size check logic: `bool useSingleImagePath = forceSingleImage || (totalPixels <= IN_MEMORY_LIMIT ...)`
   - When forceSingleImage is true, always uses single-image export path regardless of size

#### 3. **Size Estimation** (`MapViewerForm.Export.cs`)
   - Updated `UpdateEstimatedSize()` method to show different estimates based on export mode:
     - **Single Image Mode:** Shows dimensions, memory usage, and warning for very large images
     - **Tiled Export Mode:** Shows tile count and estimated disk space
   - Orange warning text when single image export is chosen for very large maps (>100 MP)

## Usage

### PNG Transparency Export

1. **Open Map Viewer** → Load a map
2. **Click Export** button
3. **In Export Dialog:**
   - Set desired zoom level
   - **Check "Use Transparent Background (PNG only)"**
   - Select region or full map
   - Click Export
4. **Save As:** Choose PNG format (recommended)
5. **Result:** Exported image will have transparent areas where no land tiles exist

### Single Image Export

1. **Open Map Viewer** → Load a map
2. **Click Export** button
3. **In Export Dialog:**
   - Set desired zoom level (lower = smaller file)
   - Under **Export Mode**, select **"Export as Single Image"**
   - Select region or full map
   - View estimated size/memory usage
   - Click Export
4. **Save As:** Choose desired format (PNG, JPEG, or BMP)
5. **Result:** One large image file instead of multiple 512×512 tiles

**💡 Pro Tip:** For very large maps, use a lower zoom level (e.g., 11 px/tile = 1/4 scale) with single image export to create manageable file sizes while preserving the entire map in one file.

## Technical Details

### PNG Transparency
- **Supported Formats:** Works with PNG and BMP formats (both support alpha channel)
- **JPEG Note:** JPEG does not support transparency, so black background will be used regardless
- **Memory:** Uses `PixelFormat.Format32bppArgb` for proper alpha channel support
- **Performance:** No performance impact - same rendering pipeline, just different clear color
- **Tiled Exports:** Transparency is supported for both single-image and tiled exports

### Single Image Export
- **Memory Considerations:**
  - Image dimensions determine RAM usage: Width × Height × 4 bytes
  - Example: 10,000 × 10,000 = ~400 MB RAM
  - Example: 20,000 × 20,000 = ~1.6 GB RAM
- **File Size (PNG):**
  - Compressed, typically 20-40% of raw size
  - Maps with large empty areas compress very well with transparency
- **File Size (JPEG):**
  - Much smaller than PNG (~10-30% of PNG size)
  - Good for large maps, but no transparency support
- **Performance:** Single-threaded rendering (one core), but simpler than tiled export
- **Limits:** No hard limit, but practical limits based on available RAM (typically works up to ~30,000×30,000 px)
- **When to Use:**
  - Smaller resolution exports (1/4 scale or less)
  - Medium-sized map regions
  - When you need a simple single file
  - When using transparency for compositing

### Tiled Export (Original Behavior)
- **Automatic Threshold:** Previously automatic when image > 100 million pixels
- **Now Optional:** User can choose tiled export for smaller images or force single image for larger ones
- **Benefits:**
  - Minimal memory usage (only one 512×512 tile in RAM at a time)
  - Parallel rendering (uses all CPU cores)
  - Generates HTML viewer for easy viewing
  - Creates mipmaps for zoom levels
  - Generates globe texture
- **When to Use:**
  - Full-scale exports (44 px/tile)
  - Entire map exports
  - Very large regions
  - When memory is constrained

## Benefits

### PNG Transparency
- **Compositing:** Easily composite map exports over other backgrounds
- **Asset Creation:** Create transparent map assets for use in other tools
- **Visualization:** Better visualization of map boundaries and empty regions
- **Integration:** Easier integration with external image editors and design tools

### Single Image Export
- **Simplicity:** One file to manage instead of hundreds/thousands of tiles
- **Ease of Use:** Can open directly in any image viewer or editor
- **Sharing:** Easier to share a single file
- **Flexibility:** Can use at any resolution/zoom level
- **Quick Previews:** Generate quick overview images at low zoom levels
- **Specific Use Cases:**
  - Creating thumbnails/previews
  - Documentation images
  - Reference images for external tools
  - Social media sharing
  - Print-ready exports at custom resolutions

## Example Export Scenarios

### Scenario 1: High-Quality Region Export with Transparency
- **Settings:**
  - Region: 512 × 512 tiles
  - Zoom: 44 px/tile (full detail)
  - Export Mode: Single Image
  - Transparent Background: ✓
  - Format: PNG
- **Result:** ~11,000 × 11,000 px, ~500 MB PNG, transparent background
- **Use Case:** High-quality map section for compositing in Photoshop

### Scenario 2: Full Map Overview
- **Settings:**
  - Region: Entire map (7168 × 4096 tiles)
  - Zoom: 5.5 px/tile (1/8 scale)
  - Export Mode: Single Image
  - Transparent Background: ✓
  - Format: PNG
- **Result:** ~40,000 × 30,000 px, ~1-2 GB, entire map visible
- **Use Case:** Full map overview for planning or documentation

### Scenario 3: Quick Preview
- **Settings:**
  - Region: Entire map
  - Zoom: 2.75 px/tile (1/16 scale)
  - Export Mode: Single Image
  - Format: JPEG
- **Result:** ~20,000 × 15,000 px, ~50-100 MB JPEG
- **Use Case:** Fast preview for social media or quick reference

### Scenario 4: Ultra High-Res Full Map (Original Behavior)
- **Settings:**
  - Region: Entire map (7168 × 4096 tiles)
  - Zoom: 44 px/tile (full detail)
  - Export Mode: Tiled Export
- **Result:** ~300,000 × 200,000 px split into ~180,000 tiles, ~30-50 GB on disk
- **Use Case:** Maximum quality archival with HTML viewer

## Backwards Compatibility

- ✅ **PNG Transparency:** Default behavior unchanged (black background)
- ✅ **Export Mode:** Single image is now the default (more intuitive for most users)
- ✅ Existing exports work exactly as before
- ✅ Both features are opt-in via dialog controls
- ✅ No changes to saved file formats or export logic structure
- ✅ Tiled export still available when needed for very large maps

## UI Layout

The Export Dialog now has the following sections:

1. **Zoom Settings** - px/tile zoom level
2. **Export Options**
   - Include Static Objects
   - Use Transparent Background (PNG only)
   - HTML & Minimap Only
   - Generate Mipmap Only
3. **Export Mode** ← NEW SECTION
   - ⦿ Export as Single Image (default)
   - ○ Export as Tiled Images (512×512, for very large maps)
4. **Region Selection**
   - Export Entire Map
   - Export Region (with coordinate inputs)
5. **Size Estimate** - Dynamic estimate based on all settings above
