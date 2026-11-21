# 10. Balance simulator

A chapter of the [Starship Sim GDD](../gdd.md). Section numbers are permanent ids; cross-references such as "section 4.3" find their chapter through the index.

Built in Phase 0 and run on every change.

- **What it is**: `SimRunner`, a console program that plays legacies with no UI, using the same `Sim.*` code as the game.
- **Policies** stand in for the player. Start with four: always send the best, always develop juniors, random, and a mixed policy that sends the best on mandates and juniors otherwise. Policies also choose objectives, split teams, answer calls, and spend captain's time by simple rules.
- **A run**: N legacies × M cycles per policy, from fixed seeds. The default is 200 legacies × 500 cycles.
- **Metrics per run**:
  - Mission result distribution, by objective template, by encounter, and by context.
  - How often a site offers only one objective, and how unattended objectives turn out.
  - Deaths and severe injuries per 100 crew-missions.
  - Skill growth curves by policy.
  - Calls and initiatives per mission, and per cycle.
  - Captain's time demanded against budget.
  - Morale distribution, departures, and poaching rate.
  - Usage and win-rate lift of each gear item, trait, and approach.
  - Storylet fire counts, including storylets that never fire.
  - Hull losses and captain successions per 100 cycles.
- **Alarms** (a target band per metric, checked in CI):
  - Too easy: full-success rate above the band for any policy.
  - Too deadly: death rate above the band, or legacies collapsing to an empty roster.
  - Dominant: one policy beats every other on both renown and survival; or one gear item, trait, or approach is chosen in over 70% of eligible cases with a large lift.
  - Dead content: an encounter, option, or storylet with zero occurrences.
  - Drift: any metric moving more than a set percentage from the last accepted baseline.
- **The key balance assertion**: "always best" and "always develop" must each lose to the mixed policy over 500 cycles. If either pure policy wins, the central trade-off is gone.
- **Golden seeds**: a few fixed legacies whose full event logs are compared across builds, to catch unintended changes in behaviour.
- **Output**: a JSON file and a one-page HTML summary per run, with the baseline alongside.

## 10.1 Mission bench

A second simulator mode that answers one question: is this mission balanced? It runs one mission many times (10,000 by default) and never runs cycles.

- **Where**: the `SimRunner` command line, and the Tuning page (`/dev/tuning`, developer mode only, slice 1f part one), beside the draft of the tuning file. Both call the same code, and the page's run under the saved tuning gives the command line's figures on the same inputs and seed. The page is reached from the editor's index (Mission tuning), the legacy selection screen (Tuning), Dev Play's panel (Tuning, with that legacy as the team source), and "Bench this" on each objective in Orders and the briefing (that objective and legacy, and the team as picked so far) and in the aftermath (the mission just finished, its team, and its lead). Its views are One mission and Every objective (each objective's full success, loss, deaths, and calls for each strength, 1,000 runs a cell).
- **Inputs**:
  - The mission: an objective template (encounters redrawn each run), or one fixed drawn mission (same encounters each run). The first tests the template; the second tests a specific offer.
  - The team, from one of three sources: a real team from a legacy or a test ship, generated teams at a named strength (rookie, mixed, or veteran), or a sweep across all three.
  - Loadout and operational parameters (priority, how readily to call in, and develop juniors), or a sweep across them.
  - A policy to answer calls, and the run count and base seed.
- **Isolation**: a bench run works on an in-memory copy. It never writes to a legacy, its event log, or its chronicle.
- **Output**:
  - Result distribution (full success, partial, incomplete, withdrawal, or loss).
  - Deaths and severe injuries per run, and which encounter caused them.
  - Calls and initiatives per run.
  - Experience earned per team member.
  - Per-encounter tier distribution, and how often each approach was chosen: the share of checks by approach position ("1st %" to "4th %", in the order the encounter lists them), and "narrowed %", the checks where an approach was unavailable.
  - Momentum and strain at each encounter, to show where runs are won or lost.
  - The same alarms as the full simulator, applied to this one mission.
  - With a sweep: the results side by side, so the gap between rookie and veteran teams is visible.
- **Reproducing a run**: every run is identified by its seed. Any single run, such as the worst one, can be replayed to read its full report; on the Tuning page the worst run's seed is a link, and a replay shows the mission log as the bridge draws it, under the saved tuning or the draft.
- **Speed**: 10,000 runs of one mission is a few seconds of pure computation, so the page can show results as they arrive. On the Tuning page a sweep of 10,000 runs a strength takes about 7 seconds, and with a draft (saved and draft, 60,000 runs) about 11.
