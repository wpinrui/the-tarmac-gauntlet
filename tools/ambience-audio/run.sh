#!/usr/bin/env bash
# Renders the wind and wet-road hiss to WAV files in out/ambience-audio/, and checks the ambience mix rules.
# Needs the .NET 8 SDK. Run from the repo root:
#   bash tools/ambience-audio/run.sh            every moment
#   bash tools/ambience-audio/run.sh wind-lap   one moment
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
out="$root/out/ambience-audio"
mkdir -p "$out"
dotnet build "$here/AmbienceAudition.csproj" -c Release -nologo -v q -clp:ErrorsOnly -o "$out/bin"
dotnet "$out/bin/AmbienceAudition.dll" "$out" "$@"
