#!/usr/bin/env bash
# Checks the sun path and sky colours outside Unity. Needs the .NET 8 SDK. Run from the repo root:
#   bash tools/sky-check/run.sh
# Prints one line per check, FAIL with details, and exits 1 if any check fails. --table prints the sky hour by hour.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
out="$root/out/sky-check"
mkdir -p "$out"
dotnet build "$here/SkyCheck.csproj" -c Release -nologo -v q -clp:ErrorsOnly -o "$out/bin"
dotnet "$out/bin/SkyCheck.dll" "$@"
