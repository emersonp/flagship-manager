#!/usr/bin/env bash
# Start the game in the browser. The database is starship-sim.db in this directory (ignored by git).
set -euo pipefail
cd "$(dirname "$0")"
if ! command -v dotnet > /dev/null 2>&1; then
  export PATH="$HOME/.dotnet:$PATH"
fi
export ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://127.0.0.1:5080}"
# The content editor (/dev/content) writes files in content/, so it is on only when you run the game from here.
export Web__DeveloperMode="${Web__DeveloperMode:-true}"
echo "Starship Sim: ${ASPNETCORE_URLS%%;*} (database: $PWD/starship-sim.db)"
exec dotnet run --project src/Web -- "$@"
