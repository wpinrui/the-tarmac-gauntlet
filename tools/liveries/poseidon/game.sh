#!/bin/sh
# Bakes Poseidon Racing's livery into the game. Run from this folder after marks.py and design.py.
set -e
MODEL=Assets/Art/Vehicles/ToraHanabi/ToraHanabi.obj
node ../common/bake.js out/views out/poseidon ../../../unity/Assets/Art/Liveries/PoseidonRacing --model "$MODEL"
