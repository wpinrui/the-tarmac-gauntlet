#!/bin/sh
# Bakes Greystone Archer's livery (grey stone) into the game. Run from this folder after marks.py and design.py.
set -e
MODEL=Assets/Art/Vehicles/ArcherCrescendo/ArcherCrescendo.obj
node ../common/bake.js out/views out/stone ../../../unity/Assets/Art/Liveries/GreystoneArcher --model "$MODEL"
