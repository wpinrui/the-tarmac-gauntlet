#!/bin/sh
# Bakes every option in design.py and writes one viewer with them all, toggled.
# Run from this folder after design.py, with the page's path as the one argument.
set -e
OBJ=../../../unity/Assets/Art/Vehicles/LibertyFantasy/LibertyFantasy.obj
for name in shop-floor waterline shop-floor-beside shop-floor-behind shop-floor-dark shop-floor-tagged; do
  node ../common/bake.js out/views out/$name out/baked/$name
  cp out/digits/Digits.png out/digits/Digits.json out/baked/$name/
done
python3 ../common/viewer.py "$OBJ" "$1" "Dockyard Racing" \
  "Shop Floor=out/baked/shop-floor" "Waterline=out/baked/waterline" \
  "Anchor beside=out/baked/shop-floor-beside" "Anchor behind=out/baked/shop-floor-behind" \
  "Dark plank=out/baked/shop-floor-dark" "Red tag=out/baked/shop-floor-tagged" --number 88
