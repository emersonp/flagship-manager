# Phase 1, slices 1a and 1b: design

**Slices:** 1a mission resolver, and 1b mission bench on the command line. Both are headless.
**Spec:** `docs/gdd.md` sections 3.4, 3.5, 4.1 to 4.7, and 10.1. **Builds on:** Phase 0 (`main` at 2026-11-13).
**Ends with:** the author changes a number in the tuning file, runs one command, and sees the same seeds before and after, side by side.

This is the design. The task-by-task plan is written from it, against compiled code, in the style of the Phase 0 plan.

## 1. Decisions

Settled with the author on 2026-11-14:

1. **Module state.** Each module owns its own state record, stored as one JSON row per module per legacy. No module edits the core or another module's columns.
2. **The resolver takes plain team data.** It knows nothing about how crew are stored. `Sim.Crew` (slice 1c) maps onto it.
3. **A mission copies its tuning at the start.** A finished mission replays exactly, whatever the tuning file says later.
4. **1a and 1b are designed, planned, and built together.**

Made here, and cheap to reverse:

5. **Content definitions move to `Sim.Core`.** GDD 7.1 gives `Sim.Core` the domain types, and feature modules may reference only the core. `Sim.Content` keeps the loader and validator. Phase 0's `SkillDef` and `ContentPack` move with them.
6. **Module state is built in 1c, not here.** The resolver is a pure function and holds no legacy state, so nothing in 1a or 1b needs it. The decision is recorded now so 1c does not reopen it. The SQLite schema version lands with it.
7. **Dice are keyed, not sequential.** Every roll is `RngStream.For(missionSeed, 0, key)` with a key such as `check:3`. One seed gives the same encounters and the same dice whatever team is sent, which is what makes a before-and-after comparison fair. Mission state stores the seed and no stream positions.
8. **The bench lives in its own library, `MissionBench`,** outside `Sim.*`, so `SimRunner` uses it now and `Web` uses it in slice 1f.

## 2. What 1a leaves out

Each is additive later; none changes the contract.

| Left out | Arrives | Until then |
|---|---|---|
| `leadPrefers` and `skipIf` expressions | 1e, with the expression language | An option is marked `bold` or `safe`; a slot is marked `skippable` |
| Per-encounter `outcomes` overrides | When the author first needs one | Tier effects come from the tuning file |
| Traits in the score | 1e | The score has no trait term |
| Variants, contexts, and report text | 1d | Beats carry ids only |
| Assignments, sites, and unattended objectives | 1c | The resolver is given one objective |
| Applying experience, injuries, and loyalty to crew | 1c | The result carries them as plain data |
| Bench alarms | When the alarm bands are set (still fog on the map) | The bench prints figures only |

## 3. Projects

| Project | Change |
|---|---|
| `src/Sim.Core/Content/` | New home of the definition records: `SkillDef`, `EncounterDef`, `ObjectiveDef`, `MissionTuning`, and `ContentPack` |
| `src/Sim.Content` | Loads `encounters/`, `objectives/`, and `tuning/`; validates cross-references |
| `src/Sim.Missions` (new) | The resolver: `MissionResolver.Start` and `Advance`, and its plain data types |
| `src/MissionBench` (new) | Team generation, call policies, the run loop, and the figures |
| `src/SimRunner` | New `bench` command; the argument parser is rewritten (bad usage exits 2) |
| `tests/Sim.Tests`, `tests/MissionBench.Tests` (new) | See section 8 |

## 4. Content

All ids below are placeholders. The author replaces the files; the schema stays.

`content/encounters/*.yaml`:

```yaml
- id: ENCOUNTER_01
  tags: [hazard]
  commsBlocked: false          # optional
  chain: ENCOUNTER_CHAIN       # optional; added once per mission after a disaster
  approaches:
    careful: { skill: SKILL_A, difficulty: 5, gearTag: scanner }
    fast:    { skill: SKILL_C, difficulty: 6 }    # authored at careful + 1
  decision:                    # optional
    id: DECISION_PROMPT_1
    stakes: 1                  # 1-5
    bold: [{ momentum: 1 }, { strain: 1 }]
    safe: []
```

`content/objectives/*.yaml`:

```yaml
- id: OBJECTIVE_TEMPLATE_1
  tags: [science]
  teamSize: [2, 4]
  need: 4                      # progress for a full success
  stages:
    - { slot: approach,     draw: { tags: [hazard] },    count: 1 }
    - { slot: complication, draw: { tags: [technical] }, count: [1, 2] }
    - { slot: objective,    draw: { tags: [social] },    count: 1 }
    - { slot: extraction,   draw: { tags: [hazard] },    count: 1, skippable: true }
```

Effects in 1a: `momentum`, `strain`, `progress`, `injuryRoll: light | severe`, and `withdraw`.

The loader rejects, all together: an unknown skill, an unknown chain target, a slot that no encounter can fill, a decision with stakes outside 1 to 5, and a missing tuning value. A fast approach that is not exactly one harder than careful is allowed, since the author may mean it; the bench lists such encounters.

The starter pack ports the prototype's nine encounters and six decisions, with fast re-authored at careful + 1. The abort decision is built into the resolver, with its stakes in the tuning file.

## 5. Tuning file

`content/tuning/missions.yaml`. Every value is required; there are no defaults in code, so a missing number is a load error, never a silent zero.

```yaml
# Every number the mission resolver uses. All are required. See docs/gdd.md section 4.
check:
  target: 7                  # added to an approach's difficulty
  tiers:                     # the lowest margin for each tier; below failure is a disaster
    critical: 5
    success: 0
    partial: -3
    failure: -7
  gearBonus: 2
  assistDivisor: 2           # assist = the helper's skill divided by this, rounded down
compounding:
  cap: 5
  momentum:
    critical: 1
    fastSuccess: 1           # a fast critical earns both
  strain:
    partial: 1
    failure: 2
    disaster: 3
    reliefOnSuccess: 1
  skipAtMomentum: 4
  skipCost: 2
  abortCallAt: 4
  collapseAt: 5
develop:
  reachMargin: -2            # a junior acts only at this expected margin or better
  supervisorPenalty: 1       # the supervising senior assists at skill minus this
calls:
  base: 3
  cautionWeight: 0.5
  disciplineWeight: 0.25
  autonomyWeight: 1
  boldCautionWeight: 2       # the lead prefers bold when priority * boldPriorityWeight
  boldPriorityWeight: 1      #   exceeds caution * boldCautionWeight
  withdrawOfferedAtStakes: 3
  abortStakes: 4
  backedLoyalty: 1
  overruledLoyalty: -1
experience:
  close: 3                   # margin within closeMargin of zero
  near: 2                    # margin within nearMargin of zero
  far: 1
  closeMargin: 3
  nearMargin: 6
  trivialAtMargin: 7         # at this margin or above, nothing is learned
  assistShare: 0.5
  mentorGap: 4
  mentorBonus: 1
injury:
  lightBelow: 7              # light roll: 2d6 below this is a light injury
  deadBelow: 5               # severe roll: 2d6 below this is death
  incapacitatedBelow: 8      # severe roll: 2d6 below this is incapacitation
  medicalBonus: 2
  maxLight: 2                # each light injury is -1 to every skill
result:
  partialAtProgress: 2
bench:
  teams:
    rookie:  { size: 3, skill: [1, 3], seniors: 0, seniorSkill: [1, 3] }
    mixed:   { size: 3, skill: [2, 4], seniors: 1, seniorSkill: [5, 6] }
    veteran: { size: 3, skill: [4, 7], seniors: 0, seniorSkill: [4, 7] }
```

The values are the prototype's, with the six accepted fixes applied. `MissionTuning` is copied into the mission state at `Start`. The `bench` block is not copied; it only shapes generated teams.

## 6. The resolver

```
MissionResolver.Start(ObjectiveDef, EncounterPool, Team, Loadout, Parameters, MissionTuning, ulong seed) → MissionState
MissionResolver.Advance(MissionState, string? answer) → MissionStep { State, Beats, Pending, Result }
```

- **Team member:** `Id`, `Seniority`, `Skills` (id to level), `Caution`, and `Discipline` (each −2 to +2). **Team:** members and `LeadId`. **Loadout:** gear tags. **Parameters:** `Priority`, `Risk`, `Autonomy` (each −1 to +1), and `DevelopJuniors`.
- **Mission state** is one serialisable record: the seed, the tuning copy, the drawn encounter ids, cursor and step, momentum, strain, progress, the team with injuries and status, the lead, the pending call, the counts of calls and notices, and the result. It round-trips through JSON at any point, including while a call is pending.
- **`Advance` never mutates its input.** It returns a new state, the beats since the last return, and either a pending call, a result, or neither only in error.
- **One encounter is four steps:** check, decision, strain, and advance. This is the prototype's loop, kept because it was played and liked.
  - *Check:* approach from lead caution plus risk; actor and helper by GDD 4.2, including the "develop juniors" reach rule; score, keyed 2d6, margin, and tier; tier effects from tuning; experience; an injury roll on failure or disaster; a chained encounter once per mission.
  - *Decision:* call or notice by GDD 4.4. Comms blocked forces a notice unless the loadout has `comms`.
  - *Strain:* collapse at the cap (one severe roll, the rest light). At the abort threshold, once per mission, the abort decision is always a call unless comms are blocked.
- **A call's answer** is `bold`, `safe`, or `withdraw` (offered at high stakes). Backing or overruling the lead changes their loyalty in the result.
- **Results:** full success, partial success, withdrawal, or loss, with per-member status, injuries, experience, and loyalty change.
- **Beats** are a closed set of records: stage, skip, check, effects, injury, lead change, chain, call, notice, answer, collapse, and result. Each check beat carries its parts (skill, assist, gear, momentum, and strain), so a report or a bench can explain any roll. Slice 1c turns beats into bus events; nothing here is on the bus.

## 7. The bench

```
SimRunner bench --objective ID [--runs 10000] [--seed N]
                [--team rookie|mixed|veteran|sweep] [--lead cautious|average|reckless]
                [--autonomy -1|0|1] [--risk -1|0|1] [--priority -1|0|1] [--develop]
                [--gear TAG,TAG] [--policy back-lead|bold|safe|withdraw]
                [--fixed-draw] [--compare TUNING_FILE] [--replay SEED]
                [--content DIR] [--out FILE]
```

- The default policy, `back-lead`, backs the lead's preference on every call except the abort call, where it takes the withdrawal. The accepted numbers were benched that way.
- Run *n* uses seed `base + n`, so a run is named by its seed. `--replay SEED` prints that one mission beat by beat.
- Generated teams are drawn from a separate keyed stream, so the same seed gives the same team under either tuning file.
- `--fixed-draw` draws the encounters once and reuses them; without it, each run redraws from the template.
- **`--compare FILE` is the tuning loop.** It runs the same seeds, teams, and policy under the pack's tuning and under the named file, and prints each figure as before, after, and change.
- **Figures** (GDD 10.1): result distribution; deaths and severe injuries per 100 crew-missions, and by encounter; calls and notices per run; experience per member, split junior and senior; per-encounter tier distribution and approach share; and mean momentum and strain entering each stage.
- With `--team sweep`: the three strengths side by side.
- The bench holds no legacy and writes nothing but `--out`.
- Exit codes: 0 on success, 1 on a content error, and 2 on bad usage (including a bad number or an unknown flag).

## 8. Tests

- **Walkthroughs.** The prototype's four guided walkthroughs, rebuilt on new seeds as fixed-seed tests that assert the beats which make each one a walkthrough (a call, a notice under blocked comms, a collapse, and a junior acting under supervision).
- **Rules, one test each:** every tier boundary; assist and supervised assist; the reach rule on both sides of −2; strain relief; abort is a call for a reckless lead; abort is a notice under blocked comms; collapse hands out one severe roll; the call rule at each autonomy setting; skip; chain once only; lead change on a casualty.
- **Properties:** the same inputs give the same step; a state serialised mid-call and restored gives the same remainder; changing the tuning file after `Start` changes nothing; a different team on the same seed draws the same encounters.
- **Pinned:** a short `D6` and `Next` sequence (carried from the Phase 0 review), and one golden mission's beat hash.
- **Bench:** fixed seeds give fixed figures; `--compare` against the same file shows no change; a sweep orders rookie below veteran on full success; the parser's exit codes.
- **Acceptance, run once and recorded, not a unit test:** the bench reproduces the prototype's accepted shape within a few points. Reckless lead loss near 13% with about 3.7 deaths per 100 crew-missions; "develop juniors" full success near 48% against 56%; and calls per mission for cautious, average, and reckless leads across autonomy near 3.1/2.5/1.2, 2.5/1.2/0.6, and 0.8/0.2/0.2. Dice differ from the prototype's, so the match is statistical, not exact.

## 9. Carried from the Phase 0 review into these slices

- Pin a `D6` and `Next` sequence.
- Rewrite the `SimRunner` parser, with exit-code and `--out` tests.
- Replace the banned-token test with `Microsoft.CodeAnalysis.BannedApiAnalyzers`, and assert that `Persistence` depends only on the core.
- Strip the repeated properties from each csproj when the two new projects are added.

Left for 1c, where a module first holds legacy state: module state rows, the schema version, the end-to-end bus-to-store test, and passing the content pack into `Simulation.Run`.
