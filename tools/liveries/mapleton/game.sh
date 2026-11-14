#!/bin/sh
# Bakes Mapleton Autosport's livery into the game, with its own Fraunces digits.
# Run from this folder after digits.js and design.py (see design.py).
set -e
MODEL=Assets/Art/Vehicles/NusaFox/NusaFox.obj
OUT=../../../unity/Assets/Art/Liveries/MapletonAutosport
node ../common/bake.js out/views out/mapleton "$OUT" --model "$MODEL"
cp out/digits/Digits.png out/digits/Digits.json "$OUT"/
