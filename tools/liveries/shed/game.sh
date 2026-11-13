#!/bin/sh
# Bakes Shed Monozukuri Co.'s livery (hinoki) into the game, with its own Shippori Mincho digits.
# Run from this folder after digits.js and design.py (see design.py).
set -e
MODEL=Assets/Art/Vehicles/LibertyOmaha/LibertyOmaha.obj
OUT=../../../unity/Assets/Art/Liveries/ShedMonozukuriCo
node ../common/bake.js out/views out/hinoki "$OUT" --model "$MODEL"
cp out/digits/Digits.png out/digits/Digits.json "$OUT"/
