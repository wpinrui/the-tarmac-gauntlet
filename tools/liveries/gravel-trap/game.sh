#!/bin/sh
# Bakes Gravel Trap Racing's livery into the game. Run from this folder after design.py.
set -e
MODEL=Assets/Art/Vehicles/LibertyCollier/LibertyCollier.obj
node ../common/bake.js out/views out/debris-flag ../../../unity/Assets/Art/Liveries/GravelTrapRacing --model "$MODEL"
