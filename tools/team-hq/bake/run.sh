#!/usr/bin/env bash
# Bakes the Team HQ's lightmap and reflection probes into unity/Assets/TeamHq/Resources/Lighting.
# Needs the .NET 8 SDK and Blender as a Python module (pip install bpy). Pass --quick for a fast, noisy look.
# Run it after any change to HqGeometry's lit parts, the props' places or HqLighting: until then the game lights the
# room live and logs that the bake is out of date.
set -euo pipefail
cd "$(dirname "$0")"
out=../../../out/team-hq-bake
mkdir -p "$out"
bash ../geometry-check/run.sh bake "$out/scene.json"
python3 bake.py "$out/scene.json" "$@"
