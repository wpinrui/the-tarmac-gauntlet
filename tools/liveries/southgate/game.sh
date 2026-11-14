#!/bin/sh
# Bakes Southgate Motorsport's Savanna livery into the game, with its own Cinzel digits.
# Run from this folder after bonnet.py, design.py and digits.js (see design.py).
set -e
MODEL=Assets/Art/Vehicles/SeorakHwaeum/SeorakHwaeum.obj
OUT=../../../unity/Assets/Art/Liveries/SouthgateMotorsport
node ../common/bake.js out/views out/savanna "$OUT" --model "$MODEL"
cp out/digits/Digits.png out/digits/Digits.json "$OUT"/
