#!/usr/bin/env bash
# Renders every shot at 15:00, 19:00, 20:43 (Ivan's frame) and 22:00, with and without floodlights, then the sheets.
#   bash tools/night-smoke/run.sh out_dir
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
out="${1:-$here/../../out/night-smoke}"
for hour in 15 19 20.72 22; do
  for floods in 0 1; do
    (python3 "$here/render.py" "$out" "$hour" "$floods" ivan && REUSE=1 python3 "$here/render.py" "$out" "$hour" "$floods" lockup) 2>&1 | grep -E "rendered|Error|Traceback" &
  done
done
wait
python3 "$here/sheet.py" "$out"
