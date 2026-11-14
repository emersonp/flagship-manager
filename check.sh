#!/usr/bin/env bash
# The gate every change must pass: build, tests, a headless simulator run, and a mission bench.
set -euo pipefail
cd "$(dirname "$0")"
dotnet build --nologo -v quiet
dotnet test --nologo --no-build -v quiet
dotnet run --no-build --project src/SimRunner -- cycles --legacies 20 --cycles 50 --seed 1 --content content
dotnet run --no-build --project src/SimRunner -- bench --objective OBJECTIVE_TEMPLATE_1 --runs 2000 --team sweep --content content > /dev/null
echo "Mission bench: 3 x 2000 missions. OK."
