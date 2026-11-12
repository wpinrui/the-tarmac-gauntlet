#!/usr/bin/env bash
# Works out every car's wheel blur from its OBJ outside Unity and checks it. Needs the .NET 8 SDK. Run from anywhere:
#   bash tools/wheel-check/run.sh [preview-dir]
# Prints one line per check, FAIL with details, and exits 1 if any check fails. With a folder, also writes each car's
# blur profile there as profile-<Car>.csv.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
out="$root/out/wheel-check"
mkdir -p "$out"
dotnet build "$here/WheelCheck.csproj" -c Release -nologo -v q -clp:ErrorsOnly -o "$out/bin"
dotnet "$out/bin/WheelCheck.dll" "$root/unity/Assets/Art/Vehicles" "${1:-}"
