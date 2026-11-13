#!/usr/bin/env bash
# Builds the podium and plays its ceremonies with the game's own code outside Unity, then checks them.
# Needs the .NET 8 SDK. Run from the repo root:
#   bash tools/podium/run.sh [preview-folder]
# Prints one line per rule and exits 1 if any fails. With a folder, also writes the preview there: open
# preview.html from it (served over http, as it fetches its data).
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
out="$root/out/podium"
mkdir -p "$out"
dotnet build "$here/PodiumCheck.csproj" -nologo -v q -clp:ErrorsOnly -o "$out/bin"
dotnet "$out/bin/PodiumCheck.dll" "$root/unity/Assets/Raceday/Resources/Tracks/circuit.json" "$root/unity/Assets/TeamHq/Resources/Crew/crew_suit.json" "$@"
if [ $# -gt 0 ]; then
  cp "$here/preview.html" "$1/"
  for f in crew_suit.json crew_suit_mesh.bytes crew_suit_anim.bytes; do cp "$root/unity/Assets/TeamHq/Resources/Crew/$f" "$1/"; done
  mkdir -p "$1/crew"
  cp "$root"/unity/Assets/TeamHq/Resources/Crew/crew_suit_t*_c.jpg "$1/crew/"
fi
