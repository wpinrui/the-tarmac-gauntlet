#!/bin/sh
# Bakes both picks and writes the viewer. Run from this folder after views.js and design.py,
# with the page's path as the one argument.
set -e
OBJ=../../../unity/Assets/Art/Vehicles/ArcherTachyon/ArcherTachyon.obj
for L in happy-hour lace-lager; do node ../common/bake.js out/views out/$L out/baked/$L; done
python3 ../common/viewer.py "$OBJ" "$1" "Sip Vanessa Racing" "Happy Hour=out/baked/happy-hour" "Lace & Lager=out/baked/lace-lager" --number 88
