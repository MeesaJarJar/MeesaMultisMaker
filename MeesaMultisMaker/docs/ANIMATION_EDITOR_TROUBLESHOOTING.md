# Animation Editor - Troubleshooting Empty Lists

## Problem: Animation Editor opens but lists are empty

### Diagnostic Steps

1. **Check Debug Output**
   - Open Visual Studio's Output window (View ? Output)
   - Look for messages starting with "AnimationEditor:"
   - These will tell you what folder is being checked and what files were found

2. **Verify MUL Folder Configuration**
   - Go to Main Form ? Settings button
   - Check the "Art Folder" path
   - This should point to your UO installation folder (e.g., `C:\Program Files (x86)\Electronic Arts\Ultima Online Classic`)

3. **Check for Animation Files**
   Navigate to your UO folder and verify these files exist:
   
   **Monster animations:**
   - `anim.mul` + `anim.idx`
   - `anim2.mul` + `anim2.idx` 
   - `anim3.mul` + `anim3.idx`
   
   **People animations:**
   - `anim4.mul` + `anim4.idx`
   - `anim5.mul` + `anim5.idx`

4. **File Size Check**
   Animation files should have significant size:
   - `.mul` files: Usually 10MB - 100MB+ each
   - `.idx` files: Usually 100KB - 1MB+ each
   
   If files are 0 bytes or very small, they may be corrupted or placeholders.

### Common Issues

#### Issue 1: "MUL folder not configured"
**Cause:** Art folder path not set in Settings

**Solution:**
1. Click Settings button on main form
2. Browse to your UO installation folder
3. Click OK
4. Reopen Animation Editor

#### Issue 2: "No animation files found"
**Cause:** Animation files don't exist in the configured folder

**Solution:**
1. Verify you're pointing to the correct UO installation
2. Check if your UO client has animation files (some older or custom clients may not)
3. Try different animation types - not all clients have all 5 types

#### Issue 3: Animation files exist but still no bodies
**Cause:** Files may be corrupted or in unexpected format

**Solution:**
1. Check the Debug Output for specific error messages
2. Try re-installing UO client
3. Verify file sizes (should not be 0 bytes)
4. Check file permissions (ensure read access)

### Expected Behavior

When working correctly, you should see:

1. **Status Bar Message:**
   - "Loaded X body IDs" (where X > 0)
   
2. **Body IDs List:**
   - Populated with numbers (0, 1, 2, etc.)
   - Some may have names like "0 - Human Male"

3. **After Selecting Body:**
   - Actions list populates (Walk, Run, Stand, etc.)
   - Directions list populates (South, Southeast, etc.)
   - Frames list populates when animation loads

### Debug Output Examples

**Successful load:**
```
AnimationEditor: MUL folder from config: C:\Program Files (x86)\EA\Ultima Online Classic
AnimationEditor: MUL folder configured: C:\Program Files (x86)\EA\Ultima Online Classic
AnimationEditor: Loading body IDs for type Monster2 from C:\Program Files (x86)\EA\Ultima Online Classic
AnimationEditor: Found 256 body IDs
```

**Configuration issue:**
```
AnimationEditor: MUL folder from config: 
AnimationEditor: MUL folder not valid
```

**Missing files:**
```
AnimationEditor: Loading body IDs for type Monster2 from C:\UO
AnimationEditor: Found 0 body IDs
```

### UO Client Compatibility

The Animation Editor supports:

- **Classic OSI clients** - Full support for all animation types
- **RunUO/ServUO server files** - Full support
- **UOForever client** - Full support
- **ClassicUO** - Full support (uses same MUL files)

### Alternative: Use Test Files

If you don't have a UO installation, you can test with minimal files:

1. Download sample animation files from UO community resources
2. Place them in a test folder (e.g., `C:\UO_Test`)
3. Configure Settings to point to this folder
4. Reopen Animation Editor

### Still Not Working?

If lists remain empty after following all steps:

1. Copy the Debug Output messages
2. Note your UO client version
3. List the animation files you have (with sizes)
4. Report on Discord: https://discord.com/invite/MpBe7cJDqV

Include:
- Screenshot of empty Animation Editor
- Debug Output text
- Settings ? Art Folder path
- File list from your UO folder (anim*.mul, anim*.idx)

---

## Quick Checklist

- [ ] Settings ? Art Folder is configured
- [ ] Path points to valid UO installation
- [ ] anim.mul and anim.idx files exist in that folder
- [ ] Files are not 0 bytes
- [ ] Animation Editor reopened after configuring Settings
- [ ] Debug Output checked for error messages
- [ ] Tried different Animation Type dropdown options

If all checked and still empty, proceed to "Still Not Working?" section above.
