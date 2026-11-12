#!/usr/bin/env bash
# Makes the track surface's textures and racing line map with the game's own code outside Unity, checks them and
# writes preview images to out/track-surface. Needs the .NET 8 SDK. Run from the repo root:
#   bash tools/track-surface/run.sh
# Prints one line per check, FAIL with details, and exits 1 if any check fails.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
out="$root/out/track-surface"
mkdir -p "$out"
dotnet build "$here/TrackSurfaceCheck.csproj" -nologo -v q -clp:ErrorsOnly -o "$out/bin"
dotnet "$out/bin/TrackSurfaceCheck.dll" "$root/unity/Assets/Raceday/Resources/Tracks/circuit.json" "$out"
