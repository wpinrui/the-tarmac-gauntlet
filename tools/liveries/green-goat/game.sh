#!/bin/sh
# Bakes Green Goat Racing's livery into the game, with its own outlined Bangers digits.
# Run from this folder after design.py and digits.js (see preview.sh).
set -e
MODEL=Assets/Art/Vehicles/KaiserScream/KaiserScream.obj
OUT=../../../unity/Assets/Art/Liveries/GreenGoatRacing
node ../common/bake.js out/views out/milk-riot "$OUT" --model "$MODEL"
cp out/digits/Digits.png out/digits/Digits.json "$OUT"/
