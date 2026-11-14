#!/bin/sh
# Bakes Vespro Corse's livery into the game, with its own Bodoni digits.
# Run from this folder after digits.js and design.py (see design.py).
set -e
MODEL=Assets/Art/Vehicles/ArcherJetBlack/ArcherJetBlack.obj
OUT=../../../unity/Assets/Art/Liveries/VesproCorse
node ../common/bake.js out/views out/bonnet-script "$OUT" --model "$MODEL"
cp out/digits/Digits.png out/digits/Digits.json "$OUT"/
