#!/usr/bin/env bash
# Checks the pit stop and collision sounds: voice budgets, hit and scrape levels, the pit cues, the voice pool and the takes on disk.
# Needs the .NET 8 SDK. Run from anywhere: bash tools/race-sfx-check/run.sh
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
out="$root/out/race-sfx-check"
mkdir -p "$out"
dotnet build "$here/RaceSfxCheck.csproj" -c Release -nologo -v q -clp:ErrorsOnly -o "$out/bin"
dotnet "$out/bin/RaceSfxCheck.dll" "$root/unity/Assets/Raceday/Resources/RaceSfx"
