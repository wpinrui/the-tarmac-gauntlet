#!/usr/bin/env bash
# Renders every car's engine sound, and each class's package, to WAV files in out/engine-audio/.
# Each file idles, blips the throttle, pulls through the gears flat out, lifts off and coasts back to idle.
# Needs the .NET 8 SDK. Run from the repo root:
#   bash tools/engine-audio/run.sh            every car and package
#   bash tools/engine-audio/run.sh d-14 b     one car, or one class's package
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
out="$root/out/engine-audio"
mkdir -p "$out"
dotnet build "$here/EngineAudition.csproj" -c Release -nologo -v q -clp:ErrorsOnly -o "$out/bin"
dotnet "$out/bin/EngineAudition.dll" "$out" "$@"
