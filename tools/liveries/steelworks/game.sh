#!/bin/sh
# Bakes Steelworks Motorsport's livery (Steel Town) into the game, with its own Big Shoulders digits.
# Run from this folder after digits.js and design.py (see design.py).
set -e
MODEL=Assets/Art/Vehicles/SeorakHoeori/SeorakHoeori.obj
OUT=../../../unity/Assets/Art/Liveries/SteelworksMotorsport
node ../common/bake.js out/views out/steeltown "$OUT" --model "$MODEL"
cp out/digits/Digits.png out/digits/Digits.json "$OUT"/
