#!/usr/bin/env bash
# Renders the tyre and surface sounds to WAV files in out/tyre-audio/: each moment alone, and over a D class engine.
# Needs the .NET 8 SDK. Run from the repo root:
#   bash tools/tyre-audio/run.sh            every moment
#   bash tools/tyre-audio/run.sh kerb       one moment
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
out="$root/out/tyre-audio"
mkdir -p "$out"
dotnet build "$here/TyreAudition.csproj" -c Release -nologo -v q -clp:ErrorsOnly -o "$out/bin"
dotnet "$out/bin/TyreAudition.dll" "$out" "$@"
