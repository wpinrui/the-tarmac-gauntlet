#!/bin/sh
# Bakes Happy Hour, the winning livery, into the game. Run from this folder after views.js and design.py.
set -e
node ../common/bake.js out/views out/happy-hour ../../../unity/Assets/Art/Liveries/SipVanessaRacing \
  --model Assets/Art/Vehicles/ArcherTachyon/ArcherTachyon.obj
