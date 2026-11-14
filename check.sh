#!/usr/bin/env bash
# The gate every change must pass: build, tests, and a headless simulator run.
set -euo pipefail
cd "$(dirname "$0")"
dotnet build --nologo -v quiet
dotnet test --nologo --no-build -v quiet
dotnet run --no-build --project src/SimRunner -- cycles --legacies 20 --cycles 50 --seed 1 --content content
