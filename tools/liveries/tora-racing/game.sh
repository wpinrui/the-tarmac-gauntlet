#!/bin/sh
# Bakes the game's atlas and digits into unity/Assets/Art/Liveries/TeamToraRacing. Run from this folder.
set -e
OUT=../../../unity/Assets/Art/Liveries/TeamToraRacing
python3 design.py
node ../common/bake.js out/views out/folded "$OUT" --model Assets/Art/Vehicles/ToraGiga/ToraGiga.obj
node ../common/digits.js --font font/ChakraPetch-Bold.ttf --edge 0.05 --out "$OUT"
