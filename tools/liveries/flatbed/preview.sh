#!/bin/sh
# Bakes the Tow Yard livery and writes a viewer of it on the Firefly.
# Run from this folder after design.py, with the page's path as the one argument.
set -e
OBJ=../../../unity/Assets/Art/Vehicles/KaiserFirefly/KaiserFirefly.obj
node ../common/bake.js out/views out/towyard out/baked/towyard
cp out/digits/Digits.png out/digits/Digits.json out/baked/towyard/
python3 ../common/viewer.py "$OBJ" "$1" "Flatbed Legends" "Tow Yard=out/baked/towyard" --number 88
