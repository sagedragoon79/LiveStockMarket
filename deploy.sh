#!/usr/bin/env bash
# Deploy the built Live-Stock Market DLL into the game's Mods folder.
#
# MelonLoader 0.7 keeps a loaded mod DLL open while Farthest Frontier runs, so
# this waits for the game to exit before copying. Run it in a spare shell right
# after a build; it blocks until the game closes, copies, and returns.
#
# Usage: ./deploy.sh [Debug|Release]      (default: Debug)
set -euo pipefail

CONFIG="${1:-Debug}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SRC="$HERE/bin/$CONFIG/LiveStockMarket.dll"
DST="C:/Program Files (x86)/Steam/steamapps/common/Farthest Frontier/Farthest Frontier (Mono)/Mods/LiveStockMarket.dll"

if [ ! -f "$SRC" ]; then
  echo "No build at $SRC — run: dotnet msbuild -restore -p:Configuration=$CONFIG LiveStockMarket.csproj" >&2
  exit 1
fi

until ! tasklist 2>/dev/null | grep -qi "Farthest"; do
  echo "$(date +%H:%M:%S) Farthest Frontier is running — waiting to deploy…"
  sleep 20
done

cp -f "$SRC" "$DST"
echo "Deployed $SRC"
echo "     -> $DST"
echo "New code takes effect on the next game launch."
