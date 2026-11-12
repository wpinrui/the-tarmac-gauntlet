#!/usr/bin/env bash
# Checks the menus' UI sounds: which elements hover and click, which cue wins a frame, and that every cue has its clip.
# Needs the .NET 8 SDK. Run from anywhere: bash tools/ui-sounds-check/run.sh
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
out="$root/out/ui-sounds-check"
mkdir -p "$out"
dotnet build "$here/UiSoundsCheck.csproj" -c Release -nologo -v q -clp:ErrorsOnly -o "$out/bin"
dotnet "$out/bin/UiSoundsCheck.dll" "$root"
