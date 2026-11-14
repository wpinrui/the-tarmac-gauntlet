#!/bin/sh
# Bakes the livery and writes a viewer of it on the Jet Black.
# Run from this folder after digits.js and design.py, with the page's path as the one argument.
set -e
OBJ=../../../unity/Assets/Art/Vehicles/ArcherJetBlack/ArcherJetBlack.obj
node ../common/bake.js out/views out/bonnet-script out/baked/bonnet-script
cp out/digits/Digits.png out/digits/Digits.json out/baked/bonnet-script/
python3 ../common/viewer.py "$OBJ" "$1" "Vespro Corse" "Vespro Corse=out/baked/bonnet-script" --number 88
