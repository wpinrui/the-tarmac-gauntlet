#!/bin/sh
# Bakes Scuderia Feretti's livery into the game. Run from this folder after design.py.
set -e
MODEL=Assets/Art/Vehicles/FerettiVortice/FerettiVortice.obj
node ../common/bake.js out/views out/feretti ../../../unity/Assets/Art/Liveries/ScuderiaFeretti --model "$MODEL"
