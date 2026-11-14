#!/usr/bin/env bash
# Runs whole careers of 24-hour races outside Unity and reports how much of the field finishes, and why the rest
# retire: mechanical failures, crashes from one big hit, and crashes from damage piling up. Fails outside the plan's
# numbers: about 70% finish, retirements split about evenly, worn and old cars and Push well below the average.
# Needs the .NET 8 SDK. Run from the repo root:
#   bash tools/retirement-check/run.sh
# Prints the rates, then one line per check, FAIL with details, and exits 1 if any check fails.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
out="$root/out/retirement-check"
mkdir -p "$out"
dotnet build "$here/RetirementCheck.csproj" -c Release -nologo -v q -clp:ErrorsOnly -o "$out/bin"
dotnet "$out/bin/RetirementCheck.dll" "$@"
