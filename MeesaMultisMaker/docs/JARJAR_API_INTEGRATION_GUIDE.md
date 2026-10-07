# MeesaJarJar.com API Integration Guide

## Overview

The MeesaJarJar artwork server hosts sprite atlases for a custom Ultima Online client. External software can **query** existing artwork and **replace** individual sprites via HTTP API calls.

**Base URL:** `https://meesajarjar.com`

All endpoints return JSON (except binary file downloads and image responses). All endpoints support CORS (`Access-Control-Allow-Origin: *`).

---

## Architecture Summary

Sprites are packed into **atlas textures** — large 2048×2048 PNG images. A binary **lookup table** (`lookup.bin`) maps each graphic ID to its atlas index and pixel coordinates within that atlas.

```
Atlas System:
  lookup.bin          → maps graphic_id → { atlas_index, x, y, width, height }
  atlas_0000.png      → 2048x2048 PNG containing many sprites
  atlas_0001.png      → ...
  sprites.jarjar      → binary sprite data (client-specific)
```

Each sprite has a **graphic ID** (0x0000–0xFFFF). The lookup table is an array of 16-byte entries indexed by graphic ID.

---

## API Endpoints

### 1. List Available Files

```
GET /gameArt.php?action=list
```

**Response:**
```json
{
  "status": "ok",
  "count": 15,
  "files": [
    "atlas_0000.png",
    "atlas_0001.png",
    "lookup.bin",
    "sprites.jarjar"
  ]
}
```

---

### 2. Get File Hashes (for change detection)

```
GET /gameArt.php?action=hashes
```

**Response:**
```json
{
  "status": "ok",
  "timestamp": 1750000000,
  "files": {
    "atlas_0000.png": "d41d8cd98f00b204e9800998ecf8427e",
    "atlas_0001.png": "098f6bcd4621d373cade4e832627b4f6",
    "lookup.bin": "5d41402abc4b2a76b9719d911017c592",
    "sprites.jarjar": "7d793037a0760186574b0282f2f435e7"
  }
}
```

Use MD5 hash comparison to detect which files have changed since your last sync.

---

### 3. Get Full Manifest (hashes + sizes + timestamps)

```
GET /gameArt.php?action=manifest
```

**Response:**
```json
{
  "status": "ok",
  "timestamp": 1750000000,
  "version": "1.0",
  "baseUrl": "https://meesajarjar.com/gameArt.php?action=download&file=",
  "files": {
    "atlas_0000.png": {
      "hash": "d41d8cd98f00b204e9800998ecf8427e",
      "size": 4194304,
      "modified": 1750000000
    },
    "lookup.bin": {
      "hash": "5d41402abc4b2a76b9719d911017c592",
      "size": 1048576,
      "modified": 1749999000
    }
  }
}
```

---

### 4. Download a File

```
GET /gameArt.php?action=download&file=atlas_0000.png
```

**Allowed file extensions:** `.png`, `.bin`, `.jarjar`

**Response:** Binary file with headers:
- `Content-Type: image/png` (for PNGs) or `application/octet-stream`
- `Content-Length: <size>`
- `Content-Disposition: attachment; filename="atlas_0000.png"`
- `X-File-Hash: <md5>`

**Error (404):**
```json
{ "error": "File not found: some_file.png" }
```

---

### 5. Extract a Single Sprite from an Atlas

```
GET /get-sprite.php?atlas=<index>&x=<x>&y=<y>&w=<width>&h=<height>
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `atlas` | int | Atlas index (0-N) |
| `x` | int | X pixel position in atlas |
| `y` | int | Y pixel position in atlas |
| `w` | int | Sprite width in pixels (1–2048) |
| `h` | int | Sprite height in pixels (1–2048) |

**Response:** PNG image (`Content-Type: image/png`) with the extracted sprite region.

**Example:**
```
GET /get-sprite.php?atlas=3&x=128&y=256&w=44&h=88
```
Returns a 44×88 PNG extracted from `atlas_0003.png` at position (128, 256).

---

### 6. Replace a Single Sprite (Primary Integration Endpoint)

```
POST /updateGraphic.php
Content-Type: multipart/form-data
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `hexid` | string | **Yes** | Graphic ID in hex (e.g., `"0A1B"` or `"0x0A1B"`) |
| `image` | file | **Yes** | PNG image file upload |
| `auth_token` | string | No | Authentication token (if server has `JARJAR_AUTH_TOKEN` env var set) |

**Critical constraints:**
- The uploaded image dimensions **must exactly match** the existing sprite dimensions in the lookup table. The server will reject mismatched sizes.
- The image should be PNG format with alpha transparency.
- The `0x` prefix on `hexid` is optional — both `"0A1B"` and `"0x0A1B"` work.

**Success Response (200):**
```json
{
  "status": "ok",
  "message": "Graphic updated successfully",
  "graphic_id": "0x0A1B",
  "atlas": "atlas_0003.png",
  "position": { "x": 128, "y": 256 },
  "size": { "width": 44, "height": 88 },
  "timestamp": 1750000000,
  "new_hash": "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4"
}
```

**Error Responses:**

| HTTP Code | Condition |
|-----------|-----------|
| 400 | Missing `hexid` or `image`, or invalid graphic ID (not 0x0001–0xFFFF) |
| 403 | Invalid `auth_token` (when server authentication is enabled) |
| 405 | Non-POST request |
| 500 | Graphic ID not in lookup table, dimension mismatch, atlas file missing, or GD image processing failure |

**Dimension mismatch error (500):**
```json
{
  "status": "error",
  "message": "Image dimensions (64x64) don't match sprite (44x88). Resize required."
}
```

**Graphic not found error (500):**
```json
{
  "status": "error",
  "message": "Graphic 0x0A1B not found in lookup table"
}
```

---

### 7. Save Full Atlas Image (bulk replace)

```
POST /save-atlas.php
Content-Type: application/json
```

**Request Body:**
```json
{
  "atlasId": 3,
  "imageData": "data:image/png;base64,iVBORw0KGgo...",
  "type": "art"
}
```

| Field | Type | Description |
|-------|------|-------------|
| `atlasId` | int | Atlas index number |
| `imageData` | string | Full atlas image as base64-encoded PNG (with or without data URI prefix) |
| `type` | string | Asset type: `"art"` (default), `"texmap"`, `"gump"`, `"font"` |

**Atlas ID limits by type:**
| Type | Max Atlas ID |
|------|-------------|
| `art` | 50 |
| `texmap` | 20 |
| `gump` | 50 |
| `font` | 10 |

**Success Response:**
```json
{
  "success": true,
  "message": "Saved atlas_0003.png with backup",
  "type": "art",
  "path": "atlas_0003.png"
}
```

**⚠️ Warning:** This endpoint has **no authentication**. It replaces the entire atlas image. Use `updateGraphic.php` for single sprite replacements.

---

### 8. Backup Management

```
GET /backup-manager.php?action=list&type=art
GET /backup-manager.php?action=list&type=art&atlas=3
GET /backup-manager.php?action=restore&type=art&file=atlas_0003_backup_2026-01-17T03-45-00.png
GET /backup-manager.php?action=preview&type=art&file=atlas_0003_backup_2026-01-17T03-45-00.png
GET /backup-manager.php?action=delete&type=art&file=atlas_0003_backup_2026-01-17T03-45-00.png
```

---

## Lookup Table Binary Format (`lookup.bin`)

The lookup table is the key to mapping graphic IDs to atlas positions. You must download and parse it to know a sprite's current dimensions before replacing it.

### Binary Structure

```
Offset 0x00: [4 bytes] Entry count (uint32, little-endian)
Offset 0x04: [16 bytes × N] Entry array, indexed by graphic ID
```

### Entry Structure (16 bytes each, little-endian)

| Offset | Size | Type | Field |
|--------|------|------|-------|
| 0 | 2 | uint16 | `AtlasIndex` — which atlas PNG file (0 = `atlas_0000.png`) |
| 2 | 2 | uint16 | `X` — X pixel position in atlas |
| 4 | 2 | uint16 | `Y` — Y pixel position in atlas |
| 6 | 2 | uint16 | `Width` — sprite width in pixels |
| 8 | 2 | uint16 | `Height` — sprite height in pixels |
| 10 | 2 | uint16 | `OriginalId` — original art.mul ID |
| 12 | 2 | uint16 | `Flags` — bit 0: is valid (1 = valid entry, 0 = empty slot) |
| 14 | 2 | uint16 | `Reserved` — padding, always 0 |

### Reading an Entry

To look up graphic ID `0x0A1B` (decimal 2587):

```
offset = 4 + (2587 * 16)  = byte 41396
```

Read 16 bytes at that offset, interpret as little-endian uint16 fields.

If `Flags & 1 == 0`, the entry is invalid/empty (no sprite exists for that ID).

### Python Example

```python
import struct

def read_lookup_entry(lookup_data: bytes, graphic_id: int):
    entry_count = struct.unpack_from('<I', lookup_data, 0)[0]
    if graphic_id >= entry_count:
        return None
    
    offset = 4 + (graphic_id * 16)
    fields = struct.unpack_from('<HHHHHHHH', lookup_data, offset)
    
    entry = {
        'atlas_index': fields[0],
        'x': fields[1],
        'y': fields[2],
        'width': fields[3],
        'height': fields[4],
        'original_id': fields[5],
        'flags': fields[6],
        'reserved': fields[7],
    }
    
    if (entry['flags'] & 1) == 0:
        return None  # Invalid/empty entry
    
    return entry
```

### C# Example

```csharp
struct LookupEntry
{
    public ushort AtlasIndex, X, Y, Width, Height, OriginalId, Flags, Reserved;
}

LookupEntry ReadEntry(byte[] lookupData, int graphicId)
{
    int offset = 4 + (graphicId * 16);
    return new LookupEntry
    {
        AtlasIndex = BitConverter.ToUInt16(lookupData, offset),
        X          = BitConverter.ToUInt16(lookupData, offset + 2),
        Y          = BitConverter.ToUInt16(lookupData, offset + 4),
        Width      = BitConverter.ToUInt16(lookupData, offset + 6),
        Height     = BitConverter.ToUInt16(lookupData, offset + 8),
        OriginalId = BitConverter.ToUInt16(lookupData, offset + 10),
        Flags      = BitConverter.ToUInt16(lookupData, offset + 12),
        Reserved   = BitConverter.ToUInt16(lookupData, offset + 14),
    };
}
```

---

## Complete Integration Workflow

### Step-by-step: Query a sprite, generate new artwork, and replace it

```
1. DOWNLOAD LOOKUP TABLE
   GET /gameArt.php?action=download&file=lookup.bin
   → Save locally, parse binary format above

2. LOOK UP THE TARGET GRAPHIC
   Parse entry for graphic ID (e.g., 0x0A1B) from lookup.bin
   → Get: atlas_index=3, x=128, y=256, width=44, height=88

3. DOWNLOAD THE CURRENT SPRITE (for reference/input)
   GET /get-sprite.php?atlas=3&x=128&y=256&w=44&h=88
   → Returns PNG of the current sprite

4. GENERATE NEW ARTWORK
   (Your AI/software generates a new image)
   → MUST be exactly 44x88 pixels (matching lookup dimensions)
   → MUST be PNG format with alpha transparency

5. UPLOAD THE REPLACEMENT
   POST /updateGraphic.php
   Content-Type: multipart/form-data
   Fields:
     hexid = "0A1B"
     image = <your_new_44x88.png>
     auth_token = "<token_if_required>"
   → Server replaces sprite in atlas_0003.png at (128,256)

6. VERIFY THE UPDATE
   GET /gameArt.php?action=hashes
   → Check that atlas_0003.png hash changed
   
   GET /get-sprite.php?atlas=3&x=128&y=256&w=44&h=88
   → Visually confirm new sprite
```

### Python Example: Full Replacement Flow

```python
import requests
import struct
from io import BytesIO

BASE_URL = "https://meesajarjar.com"
AUTH_TOKEN = ""  # Set if server requires authentication

def get_sprite_info(graphic_id_hex: str) -> dict:
    """Download lookup.bin and read entry for a graphic ID."""
    graphic_id = int(graphic_id_hex, 16)
    
    r = requests.get(f"{BASE_URL}/gameArt.php?action=download&file=lookup.bin")
    r.raise_for_status()
    lookup_data = r.content
    
    entry_count = struct.unpack_from('<I', lookup_data, 0)[0]
    if graphic_id >= entry_count:
        raise ValueError(f"Graphic ID 0x{graphic_id:04X} out of range (max {entry_count})")
    
    offset = 4 + (graphic_id * 16)
    fields = struct.unpack_from('<HHHHHHHH', lookup_data, offset)
    
    if (fields[6] & 1) == 0:
        raise ValueError(f"Graphic ID 0x{graphic_id:04X} is not a valid entry")
    
    return {
        'atlas_index': fields[0],
        'x': fields[1],
        'y': fields[2],
        'width': fields[3],
        'height': fields[4],
        'graphic_id_hex': f"{graphic_id:04X}",
    }

def download_current_sprite(info: dict) -> bytes:
    """Download the current sprite as PNG bytes."""
    r = requests.get(
        f"{BASE_URL}/get-sprite.php",
        params={
            'atlas': info['atlas_index'],
            'x': info['x'],
            'y': info['y'],
            'w': info['width'],
            'h': info['height'],
        }
    )
    r.raise_for_status()
    return r.content

def replace_sprite(graphic_id_hex: str, png_bytes: bytes) -> dict:
    """Upload a new PNG to replace a sprite. Returns server response."""
    data = {'hexid': graphic_id_hex}
    if AUTH_TOKEN:
        data['auth_token'] = AUTH_TOKEN
    
    files = {'image': ('sprite.png', BytesIO(png_bytes), 'image/png')}
    
    r = requests.post(f"{BASE_URL}/updateGraphic.php", data=data, files=files)
    r.raise_for_status()
    return r.json()

# Usage:
info = get_sprite_info("0A1B")
print(f"Sprite is {info['width']}x{info['height']} in atlas {info['atlas_index']}")

current_png = download_current_sprite(info)
# ... generate new artwork at exactly info['width'] x info['height'] ...

# new_png_bytes = your_ai_generate(current_png, info['width'], info['height'])
# result = replace_sprite("0A1B", new_png_bytes)
# print(result)
```

### curl Examples

```bash
# List all art files
curl "https://meesajarjar.com/gameArt.php?action=list"

# Get file hashes
curl "https://meesajarjar.com/gameArt.php?action=hashes"

# Download lookup table
curl -o lookup.bin "https://meesajarjar.com/gameArt.php?action=download&file=lookup.bin"

# Download a specific atlas
curl -o atlas_0003.png "https://meesajarjar.com/gameArt.php?action=download&file=atlas_0003.png"

# Extract a sprite
curl -o sprite.png "https://meesajarjar.com/get-sprite.php?atlas=3&x=128&y=256&w=44&h=88"

# Replace a sprite
curl -X POST "https://meesajarjar.com/updateGraphic.php" \
  -F "hexid=0A1B" \
  -F "image=@new_sprite.png" \
  -F "auth_token=YOUR_TOKEN"
```

---

## Other Asset Type Endpoints

The same query patterns apply to other asset types, each served by its own PHP endpoint:

| Asset Type | Endpoint | Atlas Path |
|------------|----------|------------|
| Art (items/statics) | `gameArt.php` | `attached_assets/atlas_images/atlas_NNNN.png` |
| Texmaps (ground) | `gameTexmaps.php` | `attached_assets/texmaps/texmap_atlas_NNNN.png` |
| Gumps (UI) | `gameGumps.php` | `attached_assets/gumps/gump_atlas_NNNN.png` |
| Music | `gameMusic.php` | `attached_assets/music/music_NN.mp3` |
| Sounds | `gameSounds.php` | `attached_assets/sounds/sound_NNNN.mp3` |
| Data files | `gameData.php` | `attached_assets/data/*.jar*` |

All support `?action=list`, `?action=manifest`, and `?action=download&file=...`.

---

## Error Handling Summary

| HTTP Code | Meaning |
|-----------|---------|
| 200 | Success |
| 400 | Bad request (missing params, invalid hex ID, bad dimensions, invalid action) |
| 403 | Authentication failed or file type not allowed |
| 404 | File or atlas not found |
| 405 | Wrong HTTP method (e.g., GET on a POST-only endpoint) |
| 500 | Server error (GD failure, lookup miss, file I/O error) |

All error responses are JSON:
```json
{ "status": "error", "message": "Human-readable error description" }
```
or:
```json
{ "error": "Human-readable error description" }
```

---

## Important Notes

1. **Sprite dimensions must match exactly.** The server reads the expected size from `lookup.bin` and rejects uploads that don't match. Download the lookup table first to know the required dimensions.
2. **Use PNG with alpha transparency.** The atlas system expects RGBA PNGs. Sprites are composited over a transparent background.
3. **The server creates automatic backups** of the atlas before each update. Up to 50 backups are retained per atlas.
4. **Atlas textures are 2048×2048 pixels.** Individual sprites within are typically small (22×100, 44×44, etc.).
5. **Graphic IDs are 16-bit unsigned integers** (0x0001–0xFFFF). ID 0x0000 is reserved.
6. **Connected game clients poll for changes** every 30 seconds via hash comparison. After you update a sprite, clients will auto-download the new atlas within ~30 seconds.
7. **Authentication is optional** — the server only enforces `auth_token` if the `JARJAR_AUTH_TOKEN` environment variable is set on the server. If unset, writes are unauthenticated.
