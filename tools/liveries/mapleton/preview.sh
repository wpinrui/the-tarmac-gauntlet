#!/bin/sh
# Bakes the livery and writes a viewer of it on the Fox.
# Run from this folder after digits.js and design.py, with the page's path as the one argument.
set -e
OBJ=../../../unity/Assets/Art/Vehicles/NusaFox/NusaFox.obj
node ../common/bake.js out/views out/mapleton out/baked/mapleton
cp out/digits/Digits.png out/digits/Digits.json out/baked/mapleton/
python3 ../common/viewer.py "$OBJ" "$1" "Mapleton Autosport" "Mapleton Autosport=out/baked/mapleton" --number 88
