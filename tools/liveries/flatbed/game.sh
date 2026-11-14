#!/bin/sh
# Bakes Flatbed Legends' livery (Tow Yard) into the game, with its own slab digits.
# Run from this folder after digits.js and design.py (see design.py).
set -e
MODEL=Assets/Art/Vehicles/KaiserFirefly/KaiserFirefly.obj
OUT=../../../unity/Assets/Art/Liveries/FlatbedLegends
node ../common/bake.js out/views out/towyard "$OUT" --model "$MODEL"
cp out/digits/Digits.png out/digits/Digits.json "$OUT"/
