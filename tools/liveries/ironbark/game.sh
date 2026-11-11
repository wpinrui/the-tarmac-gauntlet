#!/bin/sh
# Bakes Ironbark Racing's livery into the game. Run from this folder after design.py.
set -e
MODEL=Assets/Art/Vehicles/LibertyCollier/LibertyCollier.obj
node ../common/bake.js out/views out/southern-cross ../../../unity/Assets/Art/Liveries/IronbarkRacing --model "$MODEL"
