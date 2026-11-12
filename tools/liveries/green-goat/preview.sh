#!/bin/sh
# Bakes every option in design.py and writes one viewer with them all, toggled.
# Run from this folder after design.py, with the page's path as the one argument.
set -e
OBJ=../../../unity/Assets/Art/Vehicles/KaiserScream/KaiserScream.obj
for name in spilt-milk overload milk-riot; do
  node ../common/bake.js out/views out/$name out/baked/$name
done
for name in overload milk-riot; do cp out/digits/Digits.png out/digits/Digits.json out/baked/$name/; done
python3 ../common/viewer.py "$OBJ" "$1" "Green Goat Racing" "Spilt Milk=out/baked/spilt-milk" "Overload=out/baked/overload" "Milk Riot=out/baked/milk-riot" --number 88
