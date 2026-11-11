#!/usr/bin/env bash
# Lays out the landscape with the game's own code outside Unity and checks it. Needs the .NET 8 SDK.
# Run from the repo root:
#   bash tools/landscape/check/run.sh [--out <folder>]
# Prints one line per check, FAIL with details, and exits 1 if any fails. --out also writes each look
# for tools/landscape/preview.html.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../../.." && pwd)"
out="$root/out/landscape-check"
mkdir -p "$out"
dotnet build "$here/LandscapeCheck.csproj" -c Release -nologo -v q -clp:ErrorsOnly -o "$out/bin"
dotnet "$out/bin/LandscapeCheck.dll" "$root/unity/Assets/Raceday/Resources/Tracks/circuit.json" "$@"
