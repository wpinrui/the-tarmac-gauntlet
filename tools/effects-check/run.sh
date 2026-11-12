#!/usr/bin/env bash
# Heats and cools every car's brake discs outside Unity and checks the glow. Needs the .NET 8 SDK. Run from anywhere:
#   bash tools/effects-check/run.sh
# Prints one line per check, FAIL with details, and exits 1 if any check fails.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
out="$root/out/effects-check"
mkdir -p "$out"
dotnet build "$here/EffectsCheck.csproj" -c Release -nologo -v q -clp:ErrorsOnly -o "$out/bin"
dotnet "$out/bin/EffectsCheck.dll"
