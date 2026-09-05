#!/usr/bin/env bash
# Build the Live-Stock Market AssetBundle(s) headless with FF's Unity version and
# copy the result into embedded/ (versioned; embedded into the DLL by the csproj).
#   ./tools/build-bundle.sh            (takes a few minutes; log in unity/bundler/Logs/build.log)
set -euo pipefail
cd "$(dirname "$0")/.."
UNITY="/c/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe"
PROJ="$(pwd -W 2>/dev/null || pwd)/unity/bundler"
mkdir -p unity/bundler/Assets/Editor unity/bundler/Assets/Sheep unity/bundler/Logs embedded
cp tools/unity/BuildAssetBundles.cs unity/bundler/Assets/Editor/
cp art/sheep/Sheep01A.fbx art/sheep/SheepBody.png art/sheep/SheepHead.png art/sheep/SheepEye.png unity/bundler/Assets/Sheep/
"$UNITY" -batchmode -nographics -quit -projectPath "$PROJ" -executeMethod BuildAssetBundles.Build -logFile "$PROJ/Logs/build.log" || true
grep -a "SHEEP_MESH\|SHEEP_BONES\|BUILT_MODEL\|BUNDLE_BUILD\|error CS\|Exception" unity/bundler/Logs/build.log | head -20
if [ -f unity/bundler/AssetBundlesOut/sheep ]; then cp unity/bundler/AssetBundlesOut/sheep embedded/sheep; ls -la embedded/sheep; else echo "NO BUNDLE PRODUCED"; exit 1; fi
