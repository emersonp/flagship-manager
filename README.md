# Starship Sim

A starship captain management sim, played in the browser, with a headless simulator for testing the rules.

## Requirements

- .NET SDK 10.

## Checks

`./check.sh` is the gate: it builds, runs the tests, runs the headless simulator and the mission bench, and starts the web app on a scratch database to create and open a legacy.

## Run the game

`./run.sh` starts the game and prints its address, `http://127.0.0.1:5080` by default. Set `ASPNETCORE_URLS` to use another address. The database is `starship-sim.db` in the repository root, and git ignores it.

## Headless simulator

- `dotnet run --project src/SimRunner -- cycles` runs legacies through cycles.
- `dotnet run --project src/SimRunner -- bench` runs a mission bench.

## Where things live

- `content/` holds the game content.
- `docs/` holds the design, plans, research, and screens; `docs/gdd.md` is the game design document.
- `wayfinder/` holds the project map.
