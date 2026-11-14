#!/bin/sh
# Draws, bakes and previews both designs. Run from this folder after views.js has drawn out/views.
set -e
python3 design.py
for n in shatter folded; do
  node ../common/bake.js out/views out/$n out/$n-baked --model Assets/Art/Vehicles/ToraGiga/ToraGiga.obj
  node ../common/digits.js --edge 0.09 --out out/$n-baked
done
python3 ../common/viewer.py ../../../unity/Assets/Art/Vehicles/ToraGiga/ToraGiga.obj "$1" "Team Tora Racing" Shatter=out/shatter-baked Folded=out/folded-baked --number 8
