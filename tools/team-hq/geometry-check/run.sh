#!/usr/bin/env bash
# Builds the team building outside Unity and checks it. Pass a folder to also write hq.obj there.
set -euo pipefail
cd "$(dirname "$0")"
dotnet run --project GeometryCheck.csproj -- "$@"
