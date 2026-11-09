#!/usr/bin/env bash
# Drives the AI round the circuit outside Unity, in a stand-in for the car physics, and checks it laps.
# Needs the .NET 8 SDK. Run from the repo root:
#   bash tools/ai-check/run.sh
# Prints one line per check, FAIL with details, and exits 1 if any check fails.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
out="$root/out/ai-check"
mkdir -p "$out"
dotnet build "$here/AiCheck.csproj" -nologo -v q -clp:ErrorsOnly -o "$out/bin"
dotnet "$out/bin/AiCheck.dll" "$root/unity/Assets/Raceday/Resources/Tracks/circuit.json" "$@"
