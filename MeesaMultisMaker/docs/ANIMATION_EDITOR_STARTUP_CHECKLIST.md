# Animation Editor - Startup Checklist

## What to Check When Animation Editor Opens Empty

### ? Step 1: Check the Debug Output Window
1. In Visual Studio, go to **View ? Output**
2. Look for lines starting with **"AnimationEditor:"**
3. You should see messages like:
   ```
   AnimationEditor: MUL folder from config: C:\...\UO
   AnimationEditor: MUL folder configured: C:\...\UO
   AnimationEditor: Loading body IDs for type Monster2 from C:\...\UO
   AnimationEditor: Found X body IDs
   ```

### ? Step 2: Configure MUL Folder Path

**Option A: Use Browse MUL Button (Easiest)**
1. Click the **"Browse MUL..."** button in the Animation Editor
2. Navigate to your UO installation folder
3. Select the folder containing anim.mul files
4. Click OK

**Option B: Use Main Settings**
1. Close Animation Editor
2. On Main Form, click **Settings** button
3. Set "Art Folder" to your UO folder
4. Click OK
5. Reopen Animation Editor

### ? Step 3: Verify Animation Files Exist

Navigate to your UO folder and check for these files:

**Required files (at least one set):**
- `anim.mul` + `anim.idx` (Monster animations)
- `anim2.mul` + `anim2.idx` (Monster 2 animations)
- `anim3.mul` + `anim3.idx` (Monster 3 animations)
- `anim4.mul` + `anim4.idx` (People animations - 5 directions)
- `anim5.mul` + `anim5.idx` (People 2 animations - 8 directions)

**File size check:**
- `.mul` files should be **10MB - 100MB+** each
- `.idx` files should be **100KB - 1MB+** each
- If files are 0 bytes, they're corrupt/missing

### ? Step 4: Try Different Animation Types

Not all UO clients have all animation types. Try each one:

1. **Monster (anim.mul)** ? Try this first
2. **Monster 2 (anim2.mul)** ? Most common
3. **Monster 3 (anim3.mul)**
4. **People (anim4.mul)**
5. **People 2 (anim5.mul)**

### ? Step 5: Common UO Installation Paths

If you're not sure where UO is installed, check these locations:

**Windows paths:**
- `C:\Program Files (x86)\Electronic Arts\Ultima Online Classic`
- `C:\Program Files (x86)\UOForever\UO`
- `C:\Program Files\Electronic Arts\Ultima Online Classic`
- `C:\Ultima Online`
- `C:\UO`

**Check if files exist:**
```
C:\Program Files (x86)\Electronic Arts\Ultima Online Classic\anim.mul
C:\Program Files (x86)\Electronic Arts\Ultima Online Classic\anim.idx
```

## Expected Results When Working

### Status Bar Should Show:
? `"Loaded X body IDs"` (where X > 0)

### Body IDs List Should Show:
? Numbers like: `0`, `1`, `2`, `400`, `401`, etc.
? Some with names: `0 - Human Male`, `1 - Human Female`

### After Selecting a Body:
? **Actions** list populates (Walk, Run, Stand, Attack, etc.)
? **Directions** list populates (South, Southeast, East, etc.)
? **Frames** list populates when you select direction

### Preview Should Display:
? Animation frame image
? Yellow bounding box
? Cyan crosshair at center
? Info overlay (Frame #, Size, Center)

## Quick Tests

### Test 1: Debug Output Check
Run the app and look for this in Output window:
```
AnimationEditor: MUL folder from config: <path>
AnimationEditor: Loading body IDs for type Monster2 from <path>
AnimationEditor: Found 256 body IDs   <-- Good! Should be > 0
```

### Test 2: File Existence Check
Open Command Prompt and run:
```cmd
cd "C:\Program Files (x86)\Electronic Arts\Ultima Online Classic"
dir anim*.mul
dir anim*.idx
```
You should see file listings with sizes.

### Test 3: Browse MUL Button Test
1. Click "Browse MUL..." in Animation Editor
2. Navigate to UO folder
3. If files are missing, you'll get a warning
4. If files exist, status should update

## Still Not Working?

### Debug Checklist:
- [ ] Debug Output shows valid MUL folder path
- [ ] Animation files actually exist (not 0 bytes)
- [ ] Tried all 5 animation type dropdowns
- [ ] Clicked Browse MUL and selected correct folder
- [ ] UO client version is classic OSI (not custom)

### Report Issue With:
1. Screenshot of empty Animation Editor
2. Copy of Debug Output messages
3. File list from UO folder: `dir C:\Path\To\UO\anim*.* > files.txt`
4. UO client version/type

**Discord:** https://discord.com/invite/MpBe7cJDqV

## Success Indicator

You know it's working when you see:

```
???????????????????????????????????????
? Body IDs:                           ?
? ??????????????????????????????????? ?
? ? 0 - Human Male                  ? ?
? ? 1 - Human Female                ? ?
? ? 2                               ? ?
? ? 3                               ? ?
? ? ...                             ? ?
? ??????????????????????????????????? ?
? Actions:                            ?
? ??????????????????????????????????? ?
? ? 0 - Walk                        ? ?
? ? 2 - Run                         ? ?
? ? 4 - Stand                       ? ?
? ??????????????????????????????????? ?
???????????????????????????????????????
```

Status bar: `"Loaded 256 body IDs for Monster2"` ?
