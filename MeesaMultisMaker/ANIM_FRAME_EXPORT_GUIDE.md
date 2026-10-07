# ANIM_FRAME_EXPORT_GUIDE.md

## Goal
Load one mobile animation frame from Ultima Online data (UOForever), then export it as `.png`.

## 1) Files to check (in order)

### Legacy MUL path
Look for:
- `anim.mul` + `anim.idx`
- `anim2.mul` + `anim2.idx`
- `anim3.mul` + `anim3.idx`
- `anim4.mul` + `anim4.idx`
- `anim5.mul` + `anim5.idx` (if present)

### UOP path
Look for:
- `AnimationFrame1.uop` (and possibly `AnimationFrame2.uop`, `AnimationFrame3.uop`, `AnimationFrame4.uop`)
- `AnimationSequence.uop` (mapping/index data)

UOForever installs may contain both. Prefer UOP when present, then fall back to MUL.

---

## 2) Unified loader shape

Create one entry point:

`Bitmap LoadMobileFrame(string dataPath, int bodyId, int action, int direction, int frameIndex)`

Pipeline:
1. Resolve body/action/direction to a concrete animation record (respect `body.def` / `bodyconv.def` remaps).
2. Locate payload in UOP or IDX/MUL.
3. Decode animation block.
4. Select `frameIndex`.
5. Render pixels to `Bitmap`.
6. Save PNG.

---

## 3) MUL decoding essentials

Each `anim.idx` entry is 12 bytes:
- `lookup` (`int32`): offset in `.mul`
- `length` (`int32`): byte length
- `extra` (`int32`): metadata (often unused for frames)

At `lookup` in `.mul`:
1. Read 256-color palette (`256 * ushort`, ARGB1555-like).
2. Read `frameCount` (`int32`).
3. Read frame offset table (`frameCount * int32`).
4. Seek to selected frame offset.
5. Read frame header:
   - `centerX` (`short`)
   - `centerY` (`short`)
   - `width` (`short`)
   - `height` (`short`)
6. Decode RLE packets until terminator (`0x7FFF7FFF`).

Typical packet decode pattern used by UO tools:
- `header = ReadInt32()`
- `runLength = header & 0xFFF`
- signed `xOffset` and `yOffset` packed in upper bits (sign-extend packed fields)
- then read `runLength` palette indices and plot them.

> If bit packing appears off, compare directly with `ClassicUO` / `UOFiddler` packet decode logic. The structure above is correct; bit extraction must match exactly.

---

## 4) UOP decoding essentials

UOP is a container format:
1. Read UOP header and file block table.
2. Enumerate entries (hashed names, compressed/uncompressed sizes, data offsets).
3. Decompress entry payload when compressed (`zlib/deflate`).
4. Decode payload as animation frame block (same/similar frame format as MUL).
5. Use sequence/mapping data (`AnimationSequence.uop`) to resolve `(bodyId, action, direction)` to an entry.

---

## 5) Render/export rules

- Palette index `0` => transparent pixel.
- Convert palette color to 32bpp ARGB.
- Plot with frame center offsets:
  - destination X/Y is based on `centerX/centerY` plus packet offsets.
- Save as:
  - `frame_{bodyId}_{action}_{direction}_{frameIndex}.png`

---

## 6) Minimal verification checklist

- Same body/action/direction/frame renders the same in your tool and UOFiddler.
- Frame orientation is correct (not mirrored or flipped).
- Transparency is correct (background fully transparent).
- Missing/invalid entries are handled without crashes.

---

## 7) Practical implementation notes

- Reuse proven packet decode math from a known-good implementation pattern (`ClassicUO` / `UOFiddler` style).
- Keep your own wrapper API and export logic separate from decode internals.
- Add debug output for:
  - source used (`UOP` vs `MUL`)
  - resolved entry index
  - frame count
  - selected frame index

---

## 8) Minimal C# export skeleton (shape only)

Use this structure in your codebase:

- `bool TryResolveAnimation(int bodyId, int action, int direction, out ResolvedAnim anim)`
- `Bitmap DecodeMulFrame(string dataPath, ResolvedAnim anim, int frameIndex)`
- `Bitmap DecodeUopFrame(string dataPath, ResolvedAnim anim, int frameIndex)`
- `void SaveFramePng(Bitmap bmp, string outPath)`

Pseudo-flow:
1. Resolve remaps and animation group.
2. If UOP record exists, decode UOP frame.
3. Else decode MUL frame.
4. Validate frame bounds.
5. Export PNG.

This keeps frame export deterministic and easy to test.
