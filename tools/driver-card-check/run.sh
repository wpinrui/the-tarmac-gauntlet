#!/usr/bin/env bash
# Checks the race's driver card outside Unity: names, flags and overalls, and who shows as the next driver in the pit lane.
# Needs the .NET 8 SDK. Run from the repo root:
#   bash tools/driver-card-check/run.sh
# Prints one line per check, FAIL with details, and exits 1 if any check fails.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
out="$root/out/driver-card-check"
mkdir -p "$out"
dotnet build "$here/DriverCardCheck.csproj" -c Release -nologo -v q -clp:ErrorsOnly -o "$out/bin"
dotnet "$out/bin/DriverCardCheck.dll"
