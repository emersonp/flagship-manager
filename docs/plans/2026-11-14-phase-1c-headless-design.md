# Phase 1, slice 1c, part one: a whole cycle, headless

**Slice:** 1c is built in two parts. This is the first: missions, crew, and storage working together through the event bus, with `SimRunner` playing whole cycles. The second part puts the browser on top and adds no rules.
**Spec:** `docs/gdd.md` sections 2.2, 3.1, 3.4, 4.1 to 4.7, 7.1 to 7.4. **Builds on:** slices 1a and 1b (`main` at 2026-11-14).
**Ends with:** `SimRunner cycles` plays legacies in which a site arrives, a team is sent, the mission resolves with its calls answered, the crew grow, are hurt, heal, and die, and the whole legacy is stored and reloaded mid-mission without changing what happens next.

This is the design. The task-by-task plan is written from it against compiled code, as before.

## 1. Decisions

Settled with the author on 2026-11-14:

1. **One away team per cycle** in this slice. Most sites offer one objective; a site with several is the frequent exception, and its other objectives resolve unattended.
2. **Eight authored crew, no recruitment.** Results apply for real: experience per skill, injuries that heal over weeks, and permanent death. The roster can only shrink.
3. **A fifth outcome, `Incomplete`:** the team played every stage and achieved too little. `Withdrawal` now means only that someone chose to pull out.
4. **Withdrawing when the lead preferred the safe option is neutral** for their loyalty.
5. **"Develop juniors" is carried by the senior.** A supervised check is made at the best-qualified score minus a penalty: 1, plus 1 for every four levels between senior and junior. The reach margin moves to +2. Benched at 10,000 missions: a mixed team gives up about seven points of full success for 2.6 times the junior's experience.
6. **The dice will change.** The author expects to move away from 2d6, so every roll goes through one seam (section 3).

Made here, and cheap to reverse:

7. **Modules read each other through views declared in the core.** A feature module may not reference another, but missions need to know who is fit to send. The core declares a small read-only interface (`IRosterView`); the crew module provides it. This is the typed beginning of the GDD's quality view; the expression language in slice 1e sits on top of the same views.
8. **Any module can guard a command.** Advancing out of resolution must be refused while a call is waiting, but the cycle module owns that command and may not know about missions. A guard is a check registered against a command type that runs before its handler and can refuse it with a reason.
9. **A refused command changes nothing.** Guards and handlers validate before they touch state, and refuse by throwing `CommandRejectedException`. The caller shows the reason.
10. **Time is counted in weeks in the core.** `LegacyState.Week` advances by the cycle's length (one week in this slice). Healing is due at a week, never at a cycle (GDD 2.3).
11. **Beats become one event type.** `MissionBeat(missionId, index, beat)` carries each beat, between `MissionStarted` and `MissionEnded`. Other modules react to `MissionEnded`; the narrative module (slice 1d) reads the beats. Finer-grained events are added when a subscriber needs one.
12. **The mission state is read strictly.** It carries a format version, and an unknown or missing member is an error, not a silent default.

## 2. Changes to the resolver contract

Made first, before any mission state is stored.

| Change | Now | After |
|---|---|---|
| Experience | One number per crew member | A map by skill, in the mission state and the result; the helper's share is on the check beat |
| Answering a call | `Advance(state, answer)` applies to whatever is pending | `Advance(state, CallReply(callId, answer))`; a reply to the wrong call, or when none is pending, is refused |
| Outcomes | Four | Five, with `Incomplete` |
| Loyalty on withdrawal | Always overruling | Neutral when the lead preferred safe |
| Stored form | No version; tolerant | `Version = 1`; strict |
| Team data | `Team` and `TeamMember` live in `Sim.Missions` | They move to `Sim.Core`, so the crew module can produce them |
| "Develop juniors" | Junior's skill plus the senior's minus 1 | Best-qualified score minus the carried penalty (decision 5) |

The golden mission, the walkthrough seeds, and the shape-test bands are re-pinned once, in this task, and the bench figures in GDD 4.7 are re-measured.

## 3. The dice seam

- The tuning file gains `check.dice` and `injury.dice`, each `{ count, sides }`, both `2` and `6` today.
- The resolver rolls only through one `Dice` type: `Roll(stream)` and `Average`. The "develop juniors" reach rule uses `Average`, so it stays right when the dice change.
- Tier margins and injury thresholds are already in the tuning file, so a change to another number and size of dice is a file edit plus a bench.
- A different mechanic (a pool, a single die with advantage, or exploding dice) replaces that one type and nothing else. The golden values are re-pinned and the shape tests say whether the game still has its shape.

## 4. Core additions

| Addition | What it is |
|---|---|
| Module state | `context.Module<T>()` returns the calling module's own state record, created empty on first use. `LegacyState` holds them by module name; the store keeps one JSON row each. A registry maps names to types, as the event registry does for events. |
| Views | `bus.Provide<TView>(factory)` and `context.View<TView>()`. View interfaces live in `Sim.Core`. A view is read-only and is built from the providing module's state. |
| Guards | `bus.Guard<TCommand>(order, name, check)`; a check returns a reason to refuse, or nothing. |
| `CommandRejectedException` | Carries the reason. Thrown by guards and handlers before any change. |
| `LegacyState.Week` | Starts at 1. |
| `Team`, `TeamMember`, `IRosterView` | Moved or added under `Sim.Core`. `IRosterView` answers: who is on the roster, who is fit for an away team this week, and a crew member as a `TeamMember`. |

## 5. A cycle in this slice

| Phase | What happens | Events |
|---|---|---|
| Orders | The missions module draws the cycle's site from the assignment template: how many objectives by weight, and which, by tag. | `SiteArrived` |
| Briefing | `AssignTeam(objective, crew, lead, gear, parameters)`. Guarded: the phase, the team size, every member fit and on the roster, the lead a member, gear within the loadout limit, and one team per cycle. Sending nobody is allowed. | `TeamAssigned` |
| Resolution | The mission starts and runs to its first call or its end. `AnswerCall(mission, callId, answer)` runs it on. `AdvancePhase` is refused while a call waits. | `MissionStarted`, `MissionBeat`, `MissionEnded` |
| Aftermath | Entering it, each objective with no team resolves by one roll against its authored difficulty. | `ObjectiveUnattended` |
| Advance | The week moves on. Injuries due this week heal. | `CycleAdvanced`, `CrewRecovered` |

The crew module applies a mission's result when it ends: experience by skill (and any level gained), light injuries, incapacitation, death, the lead's loyalty, and the away-mission count.

A mission's seed is drawn from the legacy's stream named for the mission, so no two missions in a legacy share dice.

## 6. Crew

State per crew member: id, seniority, skills (`level` and `xp` each), caution, discipline, status (active, recovering, or dead), light injuries with the week each heals, the week an incapacitated member returns, loyalty, and away missions.

Rules, all numbers in a new `content/tuning/crew.yaml`:

- **Levels.** Experience toward the next level is `base × (level + 1)`, with `base` 10. A skill stops at 10.
- **Light injury.** −1 to every skill until it heals, 2 weeks later. This is the same penalty the resolver already applies within a mission.
- **Incapacitated.** Unfit for 6 weeks.
- **Dead.** Permanent. The crew member stays in the state, for the chronicle later.
- **Loyalty.** Starts at 50; moves by the mission's loyalty change times 2; held between 0 and 100.

Not in this slice: morale, ambitions, relationships, ranks and promotion, training, ageing, and the captain. Slice 1e and Phase 2 bring them.

## 7. Content

All placeholders; the author replaces them (in the editor, once it exists).

- `content/crew/starting.yaml`: the eight crew of a new legacy. The eight used to bench the mission numbers become the starter roster, so the bench and the game start from the same people.
- `content/assignments/base.yaml`: one single-cycle assignment template with `offer: { 1: 6, 2: 3, 3: 1 }`, a draw by objective tag, and a loadout limit.
- `content/gear/base.yaml`: gear items, each with one tag (`scanner`, `tools`, `comms`, and `medical`). No requisition yet: every item is available, up to the loadout limit.
- Objectives gain `ifUnattended: { difficulty: N }`. Its effects on renown wait for the chronicle module.
- `content/tuning/crew.yaml`, read as strictly as the mission tuning.

## 8. Storage

- A `module_state` table: legacy, module name, and JSON. `Commit` writes the core state, every module's state, and the new events in one transaction.
- `PRAGMA user_version` is set, and a database from an older version is refused with a clear message. There are no real legacies yet, so there is no migration.
- The developer export includes module states (format version 2).
- Missions in flight live inside the missions module's state, as the GDD says: resolver state as JSON.

## 9. `SimRunner cycles`

- Each legacy now plays real cycles with a simple captain: send the fittest available team of the objective's smallest size, led by its most senior member, with the first gear items up to the limit; answer calls by the bench's `back-lead` policy.
- If too few crew are fit, nobody is sent and the objective is unattended.
- The summary gains, per legacy: missions by outcome, deaths, crew still alive, and the highest skill level reached. The headline shows the totals.
- The golden seed is re-pinned, on purpose, for the first time since Phase 0.

## 10. Tests

- **Resolver contract:** each change in section 2, including a reply to the wrong call, a stored state with an unknown member, and the carried rule on a flat team (develop never beats best qualified).
- **Dice seam:** a mission under 3d4 (or any other count and size) resolves, and the reach rule follows the new average.
- **Core:** module state is created, kept, and isolated per module; a view reflects its module's state; a guard refuses before the handler runs and leaves the state untouched.
- **Crew:** levelling across a threshold, injury and healing by week, incapacitation, death, loyalty bounds, and fitness as the view reports it.
- **Missions on the bus:** the site draw and its weights; every briefing guard; a mission that waits on a call and resumes; advance refused while waiting; unattended objectives.
- **End to end:** one legacy played through the bus and the SQLite store, stopped while a call is waiting, reloaded, and continued, gives the same event log as an uninterrupted run.
- **Golden:** one seed's event log hash over ten cycles.

## 11. Left for part two (the browser) and after

- The web project, Datastar, legacy selection, and the three screens.
- Seeds are never sent to the browser as numbers (a 64-bit seed does not survive JavaScript).
- The content editor is the slice after 1c.
- Renown, the chronicle, reports, and storylets: slices 1d and 1e.
