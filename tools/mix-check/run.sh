#!/usr/bin/env bash
# Checks the game's mixer (groups, sliders, ducking, the pause muffle, the voice budget and what Medium's synths cost),
# then renders a race lap and a stretch of the menus before and after it to out/mix-check/.
# Needs the .NET 8 SDK and ffmpeg (to read the game's clips). Run from anywhere: bash tools/mix-check/run.sh
# Pass "check" to skip the renders. Prints one line per check, FAIL with details, and exits 1 if any check fails.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
out="$root/out/mix-check"
mkdir -p "$out"
dotnet build "$here/MixCheck.csproj" -c Release -nologo -v q -clp:ErrorsOnly -o "$out/bin"
dotnet "$out/bin/MixCheck.dll" "$root/unity/Assets" "$out" "$@"
