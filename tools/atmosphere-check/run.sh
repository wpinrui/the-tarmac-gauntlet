#!/usr/bin/env bash
# Checks the cloud map, the cloud shadows, the volumetric clouds' noise and the grade as the sun breaks through, outside
# Unity. Needs the .NET 8 SDK. Run from the repo root:
#   bash tools/atmosphere-check/run.sh
# Prints one line per check, FAIL with details, and exits 1 if any check fails.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
out="$root/out/atmosphere-check"
mkdir -p "$out"
dotnet build "$here/AtmosphereCheck.csproj" -c Release -nologo -v q -clp:ErrorsOnly -o "$out/bin"
dotnet "$out/bin/AtmosphereCheck.dll" "$@"
