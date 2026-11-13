#!/usr/bin/env bash
# Builds the floodlights with the game's own code outside Unity and checks the night's budget. Needs the .NET 8 SDK.
# Run from the repo root:
#   bash tools/night-budget/run.sh [scene-folder]
# Prints one line per rule and exits 1 if any fails. With a folder, also writes the scene for the night renders there
# (render/render.sh renders it).
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
out="$root/out/night-budget"
mkdir -p "$out"
dotnet build "$here/NightBudget.csproj" -nologo -v q -clp:ErrorsOnly -o "$out/bin"
dotnet "$out/bin/NightBudget.dll" "$root/unity/Assets/Raceday/Resources/Tracks/circuit.json" "$@"
