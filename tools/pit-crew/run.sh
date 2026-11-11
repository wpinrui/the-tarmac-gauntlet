#!/usr/bin/env bash
# Runs every garage's pit crew and drivers through stops with the game's own planners outside Unity and checks them.
# Needs the .NET 8 SDK. Run from the repo root:
#   bash tools/pit-crew/run.sh
# Prints one line per rule, FAIL with the worst cases, and exits 1 if any rule fails.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
out="$root/out/pit-crew"
mkdir -p "$out"
dotnet build "$here/PitCrewCheck.csproj" -nologo -v q -clp:ErrorsOnly -o "$out/bin"
dotnet "$out/bin/PitCrewCheck.dll" "$root/unity/Assets/Raceday/Resources/Tracks/circuit.json"
