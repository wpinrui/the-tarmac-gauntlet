#!/bin/sh
# Bakes the thistle livery and writes a viewer of it on the Daruma.
# Run from this folder after design.py, with the page's path as the one argument.
set -e
OBJ=../../../unity/Assets/Art/Vehicles/ToraDaruma/ToraDaruma.obj
node ../common/bake.js out/views out/thistle out/baked/thistle
cp out/digits/Digits.png out/digits/Digits.json out/baked/thistle/
python3 ../common/viewer.py "$OBJ" "$1" "Saltire Racing" "Thistle=out/baked/thistle" --number 88
