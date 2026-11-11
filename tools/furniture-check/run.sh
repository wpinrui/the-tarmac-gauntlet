#!/usr/bin/env bash
# Checks where the trackside furniture stands and what the flag panels show, outside Unity. Needs the .NET 8
# SDK. Run from the repo root:
#   bash tools/furniture-check/run.sh
# Prints one line per check, FAIL with details, and exits 1 if any check fails. --list prints every piece.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
out="$root/out/furniture-check"
mkdir -p "$out"
dotnet build "$here/FurnitureCheck.csproj" -c Release -nologo -v q -clp:ErrorsOnly -o "$out/bin"
dotnet "$out/bin/FurnitureCheck.dll" "$root/unity/Assets/Raceday/Resources/Tracks/circuit.json" "$@"
