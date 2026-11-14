#!/usr/bin/env bash
# Checks the proximity arrows' levels and bearings outside Unity.
# Needs the .NET 8 SDK. Run from the repo root:
#   bash tools/proximity-check/run.sh
# Prints one line per check, FAIL with details, and exits 1 if any check fails.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
out="$root/out/proximity-check"
mkdir -p "$out"
dotnet build "$here/ProximityCheck.csproj" -c Release -nologo -v q -clp:ErrorsOnly -o "$out/bin"
dotnet "$out/bin/ProximityCheck.dll"
