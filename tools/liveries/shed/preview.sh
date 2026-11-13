#!/bin/sh
# Bakes both options in design.py and writes one viewer with them toggled.
# Run from this folder after design.py, with the page's path as the one argument.
set -e
OBJ=../../../unity/Assets/Art/Vehicles/LibertyOmaha/LibertyOmaha.obj
for name in hinoki kintsugi; do
  node ../common/bake.js out/views out/$name out/baked/$name
  cp out/digits/Digits.png out/digits/Digits.json out/baked/$name/
done
python3 ../common/viewer.py "$OBJ" "$1" "Shed Monozukuri" "Hinoki=out/baked/hinoki" "Kintsugi=out/baked/kintsugi" --number 88
