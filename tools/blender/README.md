# Blender pipeline for the pig

These scripts turn the Auto-Rig Pro source (`art/Pig/PigARPRigged.blend`) into the game-ready FBX that `tools/build-bundle.sh` bundles (`art/Pig/export/Pig01A_game.fbx`). They run headless. Requirements:

- Blender 5.2 (`C:\Program Files\Blender Foundation\Blender 5.2\blender.exe`; the blend was saved by 5.2 and 5.1 warns about data loss).
- The Auto-Rig Pro add-on enabled in that Blender (step 1 only).

Run each command from the repo root.

## 1. Export from Auto-Rig Pro

```bash
"/c/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --python tools/blender/arp_export.py -- art/Pig/PigARPRigged.blend art/Pig/export/Pig01A.fbx
```

Adds a Decimate modifier (ratio 0.19, applied to the export copy only), sets the ARP export properties (Unity, universal rig, bake all actions, units x100, no twist or bend bones), and calls the ARP exporter. The output keeps every deform bone that carries weights, face included (111 bones), and a harmless `Could not find parent in the Mped armature: "c_traj"` warning is expected.

## 2. Prune to the game skeleton

```bash
"/c/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --python tools/blender/prune_pig.py -- art/Pig/export/Pig01A.fbx art/Pig/export/Pig01A_game.fbx
```

Deletes the facial bones and folds their weights into the jaw and skull, caps influences at four, puts each action on its own NLA strip (the FBX exporter only bakes the active action in all-actions mode), and exports armature plus mesh with six takes: `Walk`, `Graze`, `GrazeStart`, `GrazeLoop`, `GrazeEnd`, `Death`. Result: 40 bones, 3,040 triangles. The pruned scene is saved beside the output as `pig_game_rig.blend`.

## 3. Check the poses

```bash
"/c/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --python tools/blender/render_pig.py -- art/Pig/export/pig_game_rig.blend /tmp/pig_renders
```

Writes three Workbench renders (walk from the side, graze and death from an isometric angle) with `PigBase.png` applied. Look for stretched limbs or a collapsed face before bundling.

## Measure a gait clip

```bash
"/c/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --python tools/blender/measure_gait.py -- art/Pig/export/pig_game_rig.blend Walk
```

Samples the foot bones per frame in armature space and prints the clip's cadence (cycles per second), stride (in body lengths per cycle) and natural speed. Takes a `.blend` or an `.fbx`, an action name (an exact name, or a suffix after `|`), and optionally a comma-separated list of bone-name keywords (default `foot,toes,hand,finger`). Use it to pick the blend tree's `timeScale`s in `tools/unity/BuildAssetBundles.cs`: the vanilla goat (`Goat_Female01A.fbx` from the AssetRipper export, action `Goat_Female01A|Walk_A01|Base Layer`) walks at 1.0 cycles per second and runs at 1.79, and the game moves it at 2.0 m/s (6.0 fleeing).

## Cut the pig sounds

```bash
"/c/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --python tools/blender/cut_pig_sounds.py -- <grunts.mp3> <breathing.mp3> art/Pig/audio
```

Uses Blender's `aud` module (audaspace) and numpy, no add-on needed. Cuts the grunt reel into `PigGrunt01..17.wav` and `PigSqueal01..06.wav` at the segment times listed in the script (found with a 20 ms RMS envelope; grunts are the short, low segments, squeals the long, bright ones), pads each by 40 ms with 20 ms fades and normalizes the peak to 0.8. The breathing track becomes `PigBreathing.wav`, a 15 s loop cut from a steady stretch with the last 0.75 s crossfaded into the head so it loops without a click, peak 0.5. All clips are mono 22.05 kHz 16-bit WAV; the Unity build stores them as Vorbis in the pig bundle. To re-cut from different recordings, edit the segment tables at the top of the script.

### The click grunt

`art/Pig/audio/UIShortPigGrunt.wav` is the author's own recording and ships as is: `tools/build-bundle.sh` copies it into the bundler as `PigClick.wav`, and the Unity import keeps its channels, sample rate and samples. `cut_pig_sounds.py` never touches it. If its lead-in ever reads as lag in play, this optional script writes a copy that starts 20 ms before the grunt's onset (mono, 22.05 kHz, peak 0.8); it is not part of the build:

```bash
"/c/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --python tools/blender/prep_click_grunt.py -- art/Pig/audio/UIShortPigGrunt.wav <out.wav>
```

## 4. Inspect any FBX

```bash
"/c/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --python tools/blender/inspect_fbx.py -- art/Pig/export/Pig01A_game.fbx
```

Prints the object tree, bone hierarchy, mesh size and vertex groups.

## Then bundle

```bash
./tools/build-bundle.sh
```

The Unity Editor script (`tools/unity/BuildAssetBundles.cs`, `BuildPig`) imports the FBX as a Generic rig, builds the animator controller to the game's parameter contract and writes `embedded/pig`. Textures come from `art/Pig/export/*.png` (1024 px).
