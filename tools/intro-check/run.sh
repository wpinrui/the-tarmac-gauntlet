#!/usr/bin/env bash
# Checks the race preview outside Unity: the flyover and corner shots keep clear of the ground and turn smoothly,
# and the figures (year, lap times, podiums, winners, favourites) come out of a career as they should.
# Needs the .NET 8 SDK. Run from the repo root:
#   bash tools/intro-check/run.sh
# Prints one line per check, FAIL with details, and exits 1 if any check fails.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
out="$root/out/intro-check"
mkdir -p "$out"
dotnet build "$here/IntroCheck.csproj" -c Release -nologo -v q -clp:ErrorsOnly -o "$out/bin"
dotnet "$out/bin/IntroCheck.dll" "$root/unity/Assets/Raceday/Resources/Tracks/circuit.json" "$@"
