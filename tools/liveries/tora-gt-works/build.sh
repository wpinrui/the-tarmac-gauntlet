#!/bin/sh
# Draws, bakes and previews the design. Run from this folder after views.js has drawn out/views.
set -e
python3 design.py
node ../common/bake.js out/views out/folded out/folded-baked --model Assets/Art/Vehicles/ToraGiga/ToraGiga.obj
node ../common/digits.js --font font/SairaExtraCondensed-ExtraBold.ttf --edge 0.05 --out out/folded-baked
python3 ../common/viewer.py ../../../unity/Assets/Art/Vehicles/ToraGiga/ToraGiga.obj "$1" "Tora GT Works" Folded=out/folded-baked --number 8
