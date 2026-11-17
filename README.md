# Starship Sim

A starship captain management sim, played in the browser, with a headless simulator for testing the rules.

## Requirements

- .NET SDK 10.

## Checks

`./check.sh` is the gate: it builds, runs the tests, runs the headless simulator and the mission bench, and starts the web app on a scratch database and a scratch copy of `tests/fixtures/content/` to create and open a legacy and to edit content through the editor, then starts it again with developer mode off to check that the editor is gone.

`tests/browser/play.sh` plays one cycle in a real browser through `playwright-cli`, double-clicking every action; it is not part of the gate.

`tests/browser/edit.sh` drives the content editor in a real browser on a scratch copy of `tests/fixtures/content/`: it renames content through the forms, plays until a renamed encounter appears, and checks that refused saves write nothing. It is not part of the gate either.

## Run the game

`./run.sh` (or `./run.ps1` in PowerShell) starts the game and prints its address, `http://127.0.0.1:5080` by default. Set `ASPNETCORE_URLS` to use another address. The database is `starship-sim.db` in the repository root, and git ignores it.

A `starship-sim.db` from an earlier build is refused at start with a message; delete it to start fresh.

## Edit the content

`./run.sh` turns developer mode on, which adds a content editor at `/dev/content` (`http://127.0.0.1:5080/dev/content` by default). Developer mode is off unless a run turns it on, and with it off the editor does not exist.

- The editor has a form for each kind of content, and text pages for `pack.yaml` and the two tuning files. An encounter's form lists its approaches (one to four, each with a name, skill, difficulty, and what the team must have for it to be available), then a tab for each approach with its words and the effects of each tier, then "What plays here".
- Lines are what the mission log and the aftermath report say. Each has a beat, criteria, and text; the most specific line whose criteria hold wins. An encounter's or an objective's own words are written inside it, in its form; the Lines page holds the shared lines, which belong to no one encounter. A line's text is one sentence or a script of speech, notes, and narration. The encounter and objective forms end with "What plays here": every beat the item produces, with a box for its own words or the line that plays there now, and a live preview. The line form previews a shared line against sample missions, and the lines page lints them. The game's own pack in `content/` holds generated fiction for trying this out; the test fixture stays placeholder. Symbols give a line varied words, and pronoun sets give crew their pronouns.
- Each cycle opens on the captain's itinerary. Storylets arrive there when what they require holds (story flags, and since part two morale, loyalty, and facts about the last cycle), and must be answered before the orders; vignettes arrive there too, with a small effect or one small choice. They are written in `content/storylets/base.yaml` and `content/vignettes/base.yaml`, which the editor shows as text files and checks on save; an encounter's outcomes and a decision's answers can set the flags. Crew morale moves with missions and rest, and a crew member at Low or High morale takes a minus or a plus on their checks.
- A save is checked as a whole pack first. If it would break the pack, it is refused with the reasons and nothing is written.
- A saved edit is live at once, with no restart.
- Names and gear reach games in progress. A legacy's crew keep the levels they started with; a change to a crew member's levels applies to new legacies.
- Git is the record. The editor writes the files in `content/`, so review and commit the changed files there.

## Headless simulator

- `dotnet run --project src/SimRunner -- cycles` runs legacies through cycles.
- `dotnet run --project src/SimRunner -- bench` runs a mission bench. For each encounter it reports the share of checks taken by each approach ("1st %" to "4th %") and "narrowed %", the checks where an approach was unavailable.

## Where things live

- `content/` holds the game content, which can change freely. The tests, golden hashes, bench figures, and scripts use the placeholder pack in `tests/fixtures/content/` instead (see `tests/fixtures/README.md`).
- `docs/` holds the design, plans, research, and screens; `docs/gdd.md` is the index of the game design document, whose chapters are in `docs/gdd/`, and `docs/conventions.md` says how ids and names are written.
- `wayfinder/` holds the project map.
