#!/bin/sh
# Bakes every option in design.py and writes one viewer with them all, toggled.
# Run from this folder after design.py, with the page's path as the one argument.
set -e
OBJ=../../../unity/Assets/Art/Vehicles/ArcherCrescendo/ArcherCrescendo.obj
for name in stone brick; do
  node ../common/bake.js out/views out/$name out/baked/$name
done
python3 ../common/viewer.py "$OBJ" "$1" "Greystone Archer" "Grey stone=out/baked/stone" "Papaya brick=out/baked/brick" --number 88
