#!/bin/sh
# Bakes Lo-fi Racing's Side A livery into the game, with its own marker digits.
# Run from this folder after digits.js and design.py (see design.py).
set -e
MODEL=Assets/Art/Vehicles/ToraGiga/ToraGiga.obj
OUT=../../../unity/Assets/Art/Liveries/LofiRacing
node ../common/bake.js out/views out/side-a "$OUT" --model "$MODEL"
cp out/digits/Digits.png out/digits/Digits.json "$OUT"/
