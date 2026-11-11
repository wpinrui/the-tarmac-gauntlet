#!/usr/bin/env bash
# Builds the floodlights with the game's own code outside Unity, then checks them. Needs the .NET 8 SDK.
# Run from the repo root:
#   bash tools/night-lights/run.sh [preview-folder]
# Prints one line per rule and exits 1 if any fails. With a folder, also writes the night preview's data there.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
out="$root/out/night-lights"
mkdir -p "$out"
dotnet build "$here/NightCheck.csproj" -nologo -v q -clp:ErrorsOnly -o "$out/bin"
dotnet "$out/bin/NightCheck.dll" "$root/unity/Assets/Raceday/Resources/Tracks/circuit.json" "$@"
