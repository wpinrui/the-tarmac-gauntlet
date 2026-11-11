#!/bin/sh
# Bakes Team Seorak's livery into the game. Run from this folder after lockup.py and design.py.
set -e
MODEL=Assets/Art/Vehicles/CheonmaCX24Si/CheonmaCX24Si.obj
node ../common/bake.js out/views out/ridgeline ../../../unity/Assets/Art/Liveries/TeamSeorak --model "$MODEL"
