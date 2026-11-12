#!/usr/bin/env bash
# Checks every setting survives a restart: one launch changes them all and quits, a second launch checks each came back.
# Needs the .NET 8 SDK. Run from anywhere: bash tools/settings-check/run.sh
# Prints one line per check, FAIL with details, and exits 1 if any check fails.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
out="$root/out/settings-check"
mkdir -p "$out"
dotnet build "$here/SettingsCheck.csproj" -c Release -nologo -v q -clp:ErrorsOnly -o "$out/bin"
prefs="$out/prefs.json"
rm -f "$prefs"
dotnet "$out/bin/SettingsCheck.dll" first "$prefs" "$root/unity/Assets"
dotnet "$out/bin/SettingsCheck.dll" second "$prefs" "$root/unity/Assets"
