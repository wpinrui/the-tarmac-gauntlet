#!/bin/sh
# Bakes both options and writes a viewer of them on the Fox.
# Run from this folder after digits.js and design.py, with the page's path as the one argument.
set -e
OBJ=../../../unity/Assets/Art/Vehicles/NusaFox/NusaFox.obj
for o in cottage flannel; do
  node ../common/bake.js out/views out/$o out/baked/$o
  cp out/digits/Digits.png out/digits/Digits.json out/baked/$o/
done
python3 ../common/viewer.py "$OBJ" "$1" "Mapleton Autosport" "Cottage Country=out/baked/cottage" "Flannel=out/baked/flannel" --number 88
