#!/bin/sh
# Bakes Team Archer's livery into the game. Run from this folder after marks.py and design.py.
set -e
MODEL=Assets/Art/Vehicles/ArcherTachyon/ArcherTachyon.obj
node ../common/bake.js out/views out/clubman-logo1 ../../../unity/Assets/Art/Liveries/TeamArcher --model "$MODEL"
