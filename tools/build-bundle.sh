#!/usr/bin/env bash
# Build the Live-Stock Market AssetBundle(s) headless with FF's Unity version and
# copy the result into embedded/ (versioned; embedded into the DLL by the csproj).
#   ./tools/build-bundle.sh            (takes a few minutes; log in unity/bundler/Logs/build.log)
set -euo pipefail
cd "$(dirname "$0")/.."
UNITY="/c/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe"
PROJ="$(pwd -W 2>/dev/null || pwd)/unity/bundler"
mkdir -p unity/bundler/Assets/Editor unity/bundler/Assets/Sheep unity/bundler/Assets/Pig unity/bundler/Assets/Pig/Audio unity/bundler/Logs embedded
cp tools/unity/BuildAssetBundles.cs unity/bundler/Assets/Editor/
cp art/sheep/Sheep01A.fbx art/sheep/SheepBody.png art/sheep/SheepHead.png art/sheep/SheepEye.png unity/bundler/Assets/Sheep/
cp art/Pig/export/Pig01A_game.fbx art/Pig/export/PigBase.png art/Pig/export/PigBlack.png art/Pig/export/PigSpotted.png art/Pig/export/PigNormal.png unity/bundler/Assets/Pig/
rm -f unity/bundler/Assets/Pig/Audio/*.wav unity/bundler/Assets/Pig/Audio/*.wav.meta; cp art/Pig/audio/Pig*.wav unity/bundler/Assets/Pig/Audio/ 2>/dev/null || echo "no pig audio in art/Pig/audio"
cp art/Pig/audio/UIShortPigGrunt.wav unity/bundler/Assets/Pig/Audio/PigClick.wav 2>/dev/null || echo "no click grunt (art/Pig/audio/UIShortPigGrunt.wav)"
"$UNITY" -batchmode -nographics -quit -projectPath "$PROJ" -executeMethod BuildAssetBundles.Build -logFile "$PROJ/Logs/build.log" || true
grep -a -E "SHEEP_MESH|PIG_MESH|PIG_CLIPS|PIG_HIERARCHY|PIG_AUDIO|BUILT_MODEL|BUNDLE_BUILD|error CS|Exception" unity/bundler/Logs/build.log | head -24
ok=1
for b in sheep pig; do if [ -f unity/bundler/AssetBundlesOut/$b ]; then cp unity/bundler/AssetBundlesOut/$b embedded/$b; ls -la embedded/$b; else echo "NO BUNDLE PRODUCED: $b"; ok=0; fi; done
[ $ok = 1 ] || exit 1
