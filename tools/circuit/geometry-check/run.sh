#!/usr/bin/env bash
# Builds the circuit with the game's own builder outside Unity, then checks its geometry.
# Needs the .NET 8 SDK. Run from the repo root:
#   bash tools/circuit/geometry-check/run.sh
# Prints one line per rule, FAIL with the worst spots, and exits 1 if any rule fails.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../../.." && pwd)"
out="$root/out/geometry-check"
mkdir -p "$out"
dotnet build "$here/GeometryCheck.csproj" -nologo -v q -clp:ErrorsOnly -o "$out/bin"
dotnet "$out/bin/GeometryCheck.dll" "$root/unity/Assets/Raceday/Resources/Tracks/circuit.json"
