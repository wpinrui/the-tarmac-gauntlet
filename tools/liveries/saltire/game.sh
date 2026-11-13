#!/bin/sh
# Bakes Saltire Racing's livery (thistle) into the game, with its own brushed marker digits.
# Run from this folder after digits.js and design.py (see design.py).
set -e
MODEL=Assets/Art/Vehicles/ToraDaruma/ToraDaruma.obj
OUT=../../../unity/Assets/Art/Liveries/SaltireRacing
node ../common/bake.js out/views out/thistle "$OUT" --model "$MODEL"
cp out/digits/Digits.png out/digits/Digits.json "$OUT"/
