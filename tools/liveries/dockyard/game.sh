#!/bin/sh
# Bakes Dockyard Racing's livery into the game, with its own outlined Shrikhand digits.
# Run from this folder after design.py and digits.js (see design.py).
set -e
MODEL=Assets/Art/Vehicles/LibertyFantasy/LibertyFantasy.obj
OUT=../../../unity/Assets/Art/Liveries/DockyardRacing
node ../common/bake.js out/views out/shop-floor "$OUT" --model "$MODEL"
cp out/digits/Digits.png out/digits/Digits.json "$OUT"/
