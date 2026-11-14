# Phase 1 Outline: the thinnest game

Phase 1 is too large for one plan. It is cut into six slices, each ending with something to run or play. Each slice gets its own detailed plan (in this folder, in the style of the Phase 0 plan) written just before it is built, against the code as it then stands.

**Spec:** `docs/gdd.md`. **Starts after:** `2026-11-13-phase-0-skeleton.md`. **Gate for every slice:** `./check.sh` green, and the golden seeds updated only on purpose.

Order matters: the mission comes first because it is proven fun (the prototype), and the tuning loop comes second because the author wants to tune in play.

| Slice | Adds | Ends with | GDD |
|---|---|---|---|
| 1a. Mission resolver | `Sim.Missions`: content types for objective templates, encounters, and the tuning file; `Start` and `Advance`; checks with tiers; momentum and strain; one decision with the call rule; the six accepted fixes | The prototype's four walkthroughs reproduced as tests; missions resolve headless | 3.4, 3.5, 4.1 to 4.7 |
| 1b. Mission bench | `SimRunner bench`: one mission run thousands of times, with generated teams, a call policy, and the result, call, death, and experience figures | The author changes a number in the tuning file and sees the bench move, from the command line | 10.1, 4.7 |
| 1c. A mission in the browser | `Web` with Datastar; `Sim.Crew` with 8 crew; one single-cycle assignment template, two objective templates, and six encounters; orders, briefing, and resolution streamed beat by beat; a call answered in the page; legacy selection and autosave | The author plays one mission per cycle in the browser | 2.2, 7.4, 7.5 |
| 1d. Reports | `Sim.Narrative`: the grammar engine and the text report built from beats | A finished mission reads as a report, not a log | 6 |
| 1e. Life aboard | `Sim.Storylets` with the expression language and quality view; morale; 5 vignettes and 2 captain storylets; captain's time; the inbox and crew file; `Sim.Chronicle` with the chronicle list | A full cycle of both layers in the browser (the Phase 1 result) | 3.7, 3.8, 5.1, 5.2, 5.4 |
| 1f. Tuning in play | Developer-mode pages: the mission bench in the browser, and the tuning page that edits the tuning file and re-runs the bench on the same seeds, before and after side by side | The author tunes without leaving the game | 4.7, 10.1 |

## Decisions each slice plan must make first

Slices 1a and 1b are designed in `2026-11-14-phase-1ab-design.md`, which settles their two entries below and where module state lives (one JSON row per module per legacy, built in 1c).

- **1a:** the tuning file's schema, and how a mission in flight pins the tuning it started with.
- **1b:** what "rookie, mixed, and veteran" generated teams are, in numbers.
- **1c:** the crew and mission-in-flight tables; how Datastar streams beats and delivers a call.
- **1d:** the grammar file format.
- **1e:** the expression language's grammar (kept minimal: comparisons, `and`, `or`, and quality paths).
- **1f:** whether tuning edits write to the content folder directly or to a developer override file.

## What the author must write before each slice can be played

Placeholders work throughout, so nothing blocks the build.

- **1c:** names for 8 crew and 4 skills; text for 2 objective templates and 6 encounters.
- **1d:** report grammar lines.
- **1e:** 5 vignettes and 2 captain storylets.

## Carried forward from the prototype

- The prototype (`C:\Users\parker00\Dropbox\Work\f-prototype\mission-feel-prototype.html`) is the reference for slice 1a's behaviour, with the six fixes in GDD 4.2 to 4.4 applied on top. It still has the old rules.
- Its hash-by-key dice are replaced by `RngStream`, so the prototype's seeds will not reproduce; the walkthrough tests are rebuilt on new seeds.

## Carried forward from the Phase 0 review

The whole-branch review of Phase 0 (2026-11-13) left these for Phase 1. Each slice plan picks up the ones in its path.

Decide before slice 1a:

- **Where feature state lives.** `LegacyState` sits in `Sim.Core` and every handler gets the whole mutable object, so today a new mechanic must edit Core and the store's columns. That cuts against "a new mechanic is a new module".
- **SQLite schema version.** Add `PRAGMA user_version` and named insert columns before the first schema change.

Slice 1a (mission resolver):

- Pin a short `D6` and `Next` sequence; only `NextUInt64` is pinned today.
- Pass the content pack into `Simulation.Run`.
- Write one end-to-end test of bus, registry, `Commit`, `Load`, and continue. Document on `Send` that a throw means "discard the state and reload".
- Move the stream-pruning test to `EventBusTests`, and check that new-cycle streams survive.
- Replace the banned-token substring test with `Microsoft.CodeAnalysis.BannedApiAnalyzers`, and assert that `Persistence` depends only on Core.

Slice 1b (mission bench):

- Rewrite the `SimRunner` argument parser: a bad value, a flag with no value, or an unknown flag exits 2. Add exit-code and `--out` tests.
- Consider comparing full event logs for the golden seed, not only a hash.

When next touched:

- Event log: guard a `"null"` payload; test a duplicate name and version, and `ToEnvelope` on an unregistered type.
- Content loader: load the starter pack through `LoadDirectory` in a test; report `.yml` files and root-level files with a clear error.
- Store: make `Import` one transaction; strengthen `Events_are_isolated_by_legacy`; report a seed mismatch as a seed mismatch.
- Project files: strip `TargetFramework`, `ImplicitUsings`, and `Nullable` from each csproj.
- `PhaseEntered` carries `Cycle` in both payload and envelope; decide before real legacies exist.
