#!/usr/bin/env bash
# Rolls the weather for thousands of races outside Unity and checks it. Needs the .NET 8 SDK. Run from the repo root:
#   bash tools/weather-check/run.sh
# Prints one line per check, FAIL with details, and exits 1 if any check fails.
# --table SEED prints that race's weather hour by hour; --races N prints a line for each of the first N races;
# --json SEED... prints those races every five minutes as JSON.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
out="$root/out/weather-check"
mkdir -p "$out"
dotnet build "$here/WeatherCheck.csproj" -c Release -nologo -v q -clp:ErrorsOnly -o "$out/bin"
dotnet "$out/bin/WeatherCheck.dll" "$@"
