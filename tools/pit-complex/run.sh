#!/usr/bin/env bash
# Builds the pit complex with the game's own builders outside Unity and checks it. Needs the .NET 8 SDK.
# Run from the repo root:
#   bash tools/pit-complex/run.sh             checks only
#   bash tools/pit-complex/run.sh <out dir>   also writes the preview's meshes there
# Prints one line per rule, FAIL with the worst spots, and exits 1 if any rule fails.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
out="$root/out/pit-complex"
mkdir -p "$out"
dotnet build "$here/PitCheck.csproj" -nologo -v q -clp:ErrorsOnly -o "$out/bin"
dotnet "$out/bin/PitCheck.dll" "$root/unity/Assets/Raceday/Resources/Tracks/circuit.json" "$@"
