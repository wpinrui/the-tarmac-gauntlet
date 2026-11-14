#!/usr/bin/env bash
# Drives the cars' reflection probes round the lap with the full field, outside Unity, and checks no car's paint jumps
# between probes. Needs the .NET 8 SDK. Run from the repo root:
#   bash tools/reflection-check/run.sh [share.csv]
# Prints one line per rule and exits 1 if any fails. With a file, also writes the watched car's share of its own probe,
# a frame a row.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
out="$root/out/reflection-check"
mkdir -p "$out"
dotnet build "$here/ReflectionCheck.csproj" -nologo -v q -clp:ErrorsOnly -o "$out/bin"
dotnet "$out/bin/ReflectionCheck.dll" "$root/unity/Assets/Raceday/Resources/Tracks/circuit.json" "$@"
