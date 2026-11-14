#!/usr/bin/env bash
# Checks how the race ends outside Unity, for each way your race can end (your car takes the flag, a teammate takes it,
# your car is out) and each view you can be in (driving, the broadcast, live timing): live timing shuts once your race
# is over, the cooldown lap ends, and the camera follows the right car.
# Needs the .NET 8 SDK. Run from the repo root:
#   bash tools/race-end-check/run.sh
# Prints one line per check, FAIL with details, and exits 1 if any check fails.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
out="$root/out/race-end-check"
mkdir -p "$out"
dotnet build "$here/RaceEndCheck.csproj" -c Release -nologo -v q -clp:ErrorsOnly -o "$out/bin"
dotnet "$out/bin/RaceEndCheck.dll" "$@"
