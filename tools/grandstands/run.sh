#!/usr/bin/env bash
# Builds the circuit and its grandstands with the game's own code outside Unity, then checks them.
# Needs the .NET 8 SDK. Run from the repo root:
#   bash tools/grandstands/run.sh [preview-folder]
# Prints one line per rule and exits 1 if any fails. With a folder, also writes the preview's data there.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
out="$root/out/grandstands"
mkdir -p "$out"
dotnet build "$here/StandCheck.csproj" -nologo -v q -clp:ErrorsOnly -o "$out/bin"
dotnet "$out/bin/StandCheck.dll" "$root/unity/Assets/Raceday/Resources/Tracks/circuit.json" "$@"
