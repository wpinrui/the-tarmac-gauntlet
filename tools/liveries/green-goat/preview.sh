#!/bin/sh
# Bakes every option in design.py and writes one viewer with them all, toggled.
# Run from this folder after design.py, with the page's path as the one argument.
set -e
OBJ=../../../unity/Assets/Art/Vehicles/KaiserScream/KaiserScream.obj
for name in spilt-milk overload; do
  node ../common/bake.js out/views out/$name out/baked/$name
done
cp out/digits/Digits.png out/digits/Digits.json out/baked/overload/
python3 ../common/viewer.py "$OBJ" "$1" "Green Goat Racing" "Spilt Milk=out/baked/spilt-milk" "Overload=out/baked/overload" --number 88
