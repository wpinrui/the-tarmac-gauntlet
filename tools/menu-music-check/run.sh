#!/usr/bin/env bash
# Checks the HQ's menu music: shuffle order, fades, silence in a race and a fresh track on the way back.
# Needs the .NET 8 SDK. Run from anywhere: bash tools/menu-music-check/run.sh
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
out="$root/out/menu-music-check"
mkdir -p "$out"
dotnet build "$here/MenuMusicCheck.csproj" -c Release -nologo -v q -clp:ErrorsOnly -o "$out/bin"
dotnet "$out/bin/MenuMusicCheck.dll"
