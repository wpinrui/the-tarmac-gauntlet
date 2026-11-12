#!/bin/sh
# Bakes North Star Racing's livery into the game. Run from this folder after marks.py and design.py.
set -e
MODEL=Assets/Art/Vehicles/KaiserOboe/KaiserOboe.obj
node ../common/bake.js out/views out/frozen-lake ../../../unity/Assets/Art/Liveries/NorthStarRacing --model "$MODEL"
