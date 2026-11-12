#!/bin/sh
# Bakes Duct Tape Dynamics' livery into the game, with its own brushed Caveat Brush digits.
# Run from this folder after design.py and digits.js (see design.py).
set -e
MODEL=Assets/Art/Vehicles/NusaCarrier/NusaCarrier.obj
OUT=../../../unity/Assets/Art/Liveries/DuctTapeDynamics
node ../common/bake.js out/views out/rust "$OUT" --model "$MODEL"
cp out/digits/Digits.png out/digits/Digits.json "$OUT"/
