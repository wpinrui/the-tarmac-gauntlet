#!/bin/sh
# Bakes the Steel Town livery and writes a viewer of it on the Hoeori.
# Run from this folder after design.py, with the page's path as the one argument.
set -e
OBJ=../../../unity/Assets/Art/Vehicles/SeorakHoeori/SeorakHoeori.obj
node ../common/bake.js out/views out/steeltown out/baked/steeltown
cp out/digits/Digits.png out/digits/Digits.json out/baked/steeltown/
python3 ../common/viewer.py "$OBJ" "$1" "Steelworks Motorsport" "Steel Town=out/baked/steeltown" --number 88
