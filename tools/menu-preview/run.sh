#!/usr/bin/env bash
# Renders every management screen from a copy of the menu scripts to HTML pages and PNGs.
# Usage: bash tools/menu-preview/run.sh <unity Assets folder> <output folder>
# Needs the .NET 8 SDK, Python 3 and Node with Playwright.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
assets="$(cd "$1" && pwd)"
out="$2"
rm -rf "$out"
mkdir -p "$out"
dotnet build "$here/MenuPreview.csproj" -nologo -v q -clp:ErrorsOnly -p:ScriptsDir="$assets/Management/Scripts" -o "$out/bin" >/dev/null
dotnet "$out/bin/MenuPreview.dll" "$assets" "$out/html"
python3 "$here/page.py" "$out/html" "$assets/Management/Resources/Menus.uss" "$assets"
node "$here/shoot.js" "$out/html/pages" "$out/png" "${SCALE:-1}"
