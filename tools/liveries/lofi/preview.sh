#!/bin/sh
# Bakes Side A and writes the viewer, with the livery's own digits. Run from this folder after
# digits.js and design.py, with the page's path as the one argument.
set -e
OBJ=../../../unity/Assets/Art/Vehicles/ToraGiga/ToraGiga.obj
node ../common/bake.js out/views out/side-a out/baked/side-a
cp out/digits/Digits.png out/digits/Digits.json out/baked/side-a/
python3 ../common/viewer.py "$OBJ" "$1" "Lo-fi Racing" "Side A=out/baked/side-a" --number 88
