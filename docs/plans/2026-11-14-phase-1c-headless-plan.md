# Phase 1, Slice 1c, Part One: A Whole Cycle, Headless: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Missions, crew, and storage work together through the event bus, and `SimRunner cycles` plays whole legacies that can be stored and reloaded mid-mission.

**Architecture:** Feature modules (`Sim.Cycle`, `Sim.Crew`, `Sim.Missions`) each own one state record and talk only through commands, events, guards, and read-only views declared in `Sim.Core`. A new `GameHost` library wires them together for `SimRunner` now and `Web` later. `Persistence` stores the core state, one JSON row per module, and the event log in one transaction.

**Tech Stack:** .NET 10, C#, xUnit 2, YamlDotNet 18.1.0, Microsoft.Data.Sqlite 11.0.0, System.Text.Json.

**Spec:** `docs/plans/2026-11-14-phase-1c-headless-design.md`, which argues from `docs/gdd.md` sections 2.2, 3.1, 3.4, 4.1 to 4.7, and 7.1 to 7.4. Vocabulary from `GLOSSARY.md`.

**How this plan differs from the earlier ones:** it gives exact interfaces, rules, and test cases, not finished code. Each task's implementer writes the code test-first. Where a task says "re-pin", find the new value by running the code; never weaken an assertion to make it pass.

## Global Constraints

- Target framework `net10.0`; nullable enabled; warnings are errors. These are set once in `Directory.Build.props`; a csproj never repeats them.
- Every `if`, `else`, `for`, `foreach`, and `while` body is in braces on their own lines, even one-liners (rule IDE0011).
- Comments are terse and explain why, never what. Each test has a one-line intent comment only where the name does not say it.
- Everything under `src/Sim.*` is pure: no database, no clock, and no ambient randomness. The banned-API analyser fails the build otherwise.
- `Sim.*` projects reference only `Sim.Core`. Feature modules never reference each other. An event, command, or type that more than one module needs is declared in `Sim.Core`.
- A command is refused by throwing `CommandRejectedException` before any state changes. Guards and handlers validate first and mutate after.
- Randomness on the bus comes only from `IBusContext.Rng(streamName)`. Mission dice stay keyed by mission seed and encounter id.
- Every rule number comes from a tuning file (`MissionTuning` or `CrewTuning`). No rule constant is written in module code.
- Every roll of the dice goes through `DiceDef.Roll` and `DiceDef.Average`. Nothing else may assume two six-sided dice.
- Events are versioned and additive: a field is never removed or repurposed. Enums are stored by name; a stored enum member is never renamed or removed.
- All time-based rules count weeks (`LegacyState.Week`), never cycles.
- Use the terms in `GLOSSARY.md` (legacy, cycle, site, objective, mission, away team, team lead, call, notice, check, beat). Never "save", "turn", or "campaign".
- Creative content is the author's. Use obvious placeholders (`CREW_01`, `GEAR_01`, `ASSIGNMENT_TEMPLATE_1`); never invent names, species, or story text.
- Commit messages are plain: no co-author trailer and no tool attribution of any kind.
- Prose and messages use the Oxford comma.
- Write files as UTF-8 without a byte-order mark and with a final newline.
- On this machine the SDK is at `~/.dotnet`. Run `export PATH=$HOME/.dotnet:$PATH` first.
- All commands run from the repository root, `~/repos/starship-sim`. The gate is `./check.sh`: it must exit 0 with pristine output at the end of every task.

## File Structure

| Path | Responsibility |
|---|---|
| `src/Sim.Core/Team.cs` | `Team`, `TeamMember` (moved from `Sim.Missions`) |
| `src/Sim.Core/MissionContracts.cs` | `MissionOutcome`, `MemberStatus`, `CrewResult`, `MissionResult`, and the events `MissionStarted` and `MissionEnded` |
| `src/Sim.Core/CycleContracts.cs` | `AdvancePhase`, `FoundLegacy`, `PhaseEntered`, `CycleAdvanced`, `LegacyFounded` (moved from `Sim.Cycle`, plus two new) |
| `src/Sim.Core/Views.cs` | `IRosterView` |
| `src/Sim.Core/SimJson.cs` | The one JSON options object for stored state: enums by name, unknown members refused |
| `src/Sim.Core/Content/DiceDef.cs`, `CrewContent.cs` | The dice seam; crew, gear, and assignment definitions, and `CrewTuning` |
| `src/Sim.Core/EventBus.cs`, `Messages.cs`, `LegacyState.cs` | Guards, views, module state, `CommandRejectedException`, and `Week` |
| `src/Sim.Crew/` (new project) | `CrewModule`, `CrewState`, and the roster view |
| `src/Sim.Missions/MissionsModule.cs`, `MissionsState.cs` | The site, the briefing, missions on the bus, and unattended objectives |
| `src/GameHost/` (new project) | `Game.Build(pack)`: registers every module and returns the bus and registries |
| `src/Persistence/` | Module state rows, the schema version, and export format 2 |
| `src/SimRunner/AutoCaptain.cs`, `Simulation.cs` | A simple captain that plays cycles, and the summary |
| `content/crew/`, `content/gear/`, `content/assignments/`, `content/tuning/crew.yaml` | The starter roster, gear, assignment template, and crew numbers (placeholders) |

---

### Task 1: The resolver contract

**Files:**
- Create: `src/Sim.Core/Team.cs`, `src/Sim.Core/MissionContracts.cs`, `src/Sim.Core/SimJson.cs`, `src/Sim.Core/Content/DiceDef.cs`
- Modify: `src/Sim.Missions/MissionTypes.cs`, `MissionState.cs`, `Beats.cs`, `MissionResolver.cs`; `src/Sim.Core/Content/MissionTuning.cs`; `src/Sim.Content/ContentLoader.Tuning.cs`; `content/tuning/missions.yaml`; `src/MissionBench/*.cs`; `src/SimRunner/Cli.cs`
- Test: `tests/Sim.Tests/MissionRulesTests.cs`, `MissionWalkthroughTests.cs`, `MissionContentTests.cs`, `TestContent.Missions.cs`; `tests/MissionBench.Tests/*.cs`; `tests/SimRunner.Tests/CliTests.cs`

**Interfaces:**
- Consumes: the resolver, bench, and command line as they stand on the branch.
- Produces, in namespace `Sim.Core`:
  - `TeamMember(string Id, int Seniority, IReadOnlyDictionary<string, int> Skills, int Caution, int Discipline)` and `Team(IReadOnlyList<TeamMember> Members, string LeadId)`, moved unchanged.
  - `enum MissionOutcome { FullSuccess, PartialSuccess, Withdrawal, Loss, Incomplete }` and `enum MemberStatus { Active, Incapacitated, Dead }`, moved; `Incomplete` is new and last.
  - `CrewResult(string Id, MemberStatus Status, int Injury, IReadOnlyDictionary<string, double> Experience, int Loyalty)` and `MissionResult(MissionOutcome Outcome, int Progress, int Need, int Calls, int Notices, IReadOnlyList<CrewResult> Crew)`, moved; `Experience` is now a map from skill id to experience.
  - `static class SimJson { public static readonly JsonSerializerOptions Options; }`: `JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false)` and `UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow`. `MissionJson.Options` becomes this object.
- Produces, in namespace `Sim.Core.Content`: `DiceDef(int Count, int Sides)` with `int Roll(RngStream stream)` (the sum of `Count` rolls of `1 + stream.Next(Sides)`) and `double Average` (`Count * (Sides + 1) / 2.0`). `MissionTuning` gains `required DiceDef CheckDice`, `required DiceDef InjuryDice`, and `required int SupervisorGapDivisor`.
- Produces, in `Sim.Missions`:
  - `CallReply(string CallId, CallAnswer Answer)`; `PendingCall.Key` is renamed `CallId` (same values).
  - `MissionResolver.Advance(MissionState state, CallReply? reply = null)`.
  - `enum CallStanding { Backed, Overruled, Neutral }`; `AnswerBeat(CallAnswer Answer, CallStanding Standing, string LeadId)` replaces the `Backed` flag.
  - `CheckBeat` gains a last field `double HelperExperience`.
  - `MemberState.Experience` is a `Dictionary<string, double>` by skill id.
  - `MissionState.Version` (`required int`), set to 1 by `Start`; `MissionJson.Deserialize` throws `InvalidOperationException` for any other version.

**Rules:**
1. **Experience by skill.** A check's experience goes to the actor and helper under the approach's skill id.
2. **Replies.** A reply when no call is pending, or whose `CallId` is not the pending call's, throws `ArgumentException` and changes nothing. No reply while a call is pending returns the waiting step, as now.
3. **Incomplete.** A mission that plays every stage with progress below `PartialAtProgress` ends `Incomplete`. `Withdrawal` comes only from a withdraw effect.
4. **Neutral withdrawal.** Answering `Withdraw` when the lead prefers `Safe` changes no loyalty (`CallStanding.Neutral`). Answering `Safe` to the abort call is `Backed` or `Overruled` as before.
5. **Dice.** Checks roll `T.CheckDice`; injury rolls roll `T.InjuryDice`. Delete the `AverageRoll` constant. `content/tuning/missions.yaml` gains `check.dice: { count: 2, sides: 6 }` and `injury.dice: { count: 2, sides: 6 }`. The loader requires `count >= 1` and `sides >= 2`.
6. **"Develop juniors" is carried by the senior.** With `best` the fit members ordered by skill for this check, `senior = best[0]`, `junior = best[^1]`, and `second = best[1]`:
   - `carried = max(0, Skill(senior) + Skill(second) / AssistDivisor − SupervisorPenalty − (Skill(senior) − Skill(junior)) / SupervisorGapDivisor − Skill(junior))` (integer division).
   - The junior acts when "develop juniors" is on, the team has more than one fit member, and `Skill(junior) + carried + gear + momentum − strain + CheckDice.Average − target >= ReachMargin`.
   - Then the actor is the junior, the helper is the senior, and the assist is `carried`. Otherwise best qualified applies, unchanged.
   - Tuning: `develop.reachMargin: 2`, `develop.supervisorPenalty: 1`, and new `develop.supervisorGapDivisor: 4` (must be 1 or more).
7. **Stored form.** `MissionJson` reads strictly: an unknown member or a wrong version is an error.

**Tests to add or change** (keep every existing assertion unless a rule above changes it):
- Each rule above has at least one rule test in `MissionRulesTests.cs`: experience lands under the approach's skill for actor and helper; a reply to the wrong call id, and a reply with nothing pending, throw and leave the state's JSON unchanged; a mission that completes with low progress is `Incomplete`; a neutral withdrawal leaves loyalty at 0; a mission under `CheckDice = (3, 4)` resolves and every check's `Roll` is between 3 and 12; the carried formula on a hand-built team (assert actor, helper, and assist); on a team of three with equal skills, the supervised score is lower than the best-qualified score.
- A state whose JSON has an unknown member, and one with `Version` 2, fail to deserialise.
- Loader: a tuning file with `check.dice.sides: 1` is an error naming the value.
- `MissionBench.Tests`: add `Develop_juniors_never_beats_best_qualified_on_generated_teams` (for rookie, mixed, and veteran at 2,000 runs, full successes with develop on are no more than with it off). Change the develop shape test's bands only as far as this: the gap in full success is between 2 and 25 points, the junior's (`CREW_05`) experience with develop is more than 1.3 times without, and the senior's (`CREW_03`) is lower. Experience per member is the sum over skills.
- Re-pin: the golden mission hash, and each walkthrough seed (the lowest seed from 1 to 5,000 for which that test's own assertions hold). If a shape band other than the develop one fails, stop and report BLOCKED with the figures.

- [ ] **Step 1:** Move the shared types to `Sim.Core`, fix the usings, and run `./check.sh` to confirm nothing changed in behaviour. Commit: "Move team and mission result types to the core".
- [ ] **Step 2:** For each rule in turn: write its failing tests, see them fail, implement, see them pass.
- [ ] **Step 3:** Re-pin the golden hash and walkthrough seeds. Run `./check.sh`.
- [ ] **Step 4:** Run `dotnet run --no-build --project src/SimRunner -- bench --objective OBJECTIVE_TEMPLATE_1 --runs 10000 --team sweep --gear scanner,tools` with and without `--develop`, and paste both summaries into your report.
- [ ] **Step 5:** Commit: "Change the resolver contract: experience by skill, call replies, the incomplete outcome, the dice seam, and carried supervision".

---

### Task 2: Guards, views, module state, and weeks in the core

**Files:**
- Create: `src/Sim.Core/CycleContracts.cs`, `src/Sim.Core/Views.cs`
- Modify: `src/Sim.Core/EventBus.cs`, `Messages.cs`, `LegacyState.cs`; `src/Sim.Cycle/CycleModule.cs`; `src/SimRunner/Simulation.cs`
- Test: `tests/Sim.Tests/EventBusTests.cs`, `CycleModuleTests.cs`; `tests/SimRunner.Tests/SimulationTests.cs`

**Interfaces:**
- Consumes: `EventBus.Handle`, `Subscribe`, and `Send`; `IBusContext`; `LegacyState`; `Team` and `TeamMember` from Task 1.
- Produces, in `Sim.Core`:
  - `sealed class CommandRejectedException(string reason) : Exception(reason)`.
  - `EventBus.Guard<TCommand>(int order, string name, Func<TCommand, IBusContext, string?> check)`: guards for a command run in `(order, name)` order before its handler, for top-level and nested sends alike; the first non-null reason throws `CommandRejectedException`. A duplicate `(command type, order, name)` throws at registration.
  - `EventBus.Module<TState>(string name) where TState : class, new()`: declares a module's state type. `EventBus.ModuleTypes` is an `IReadOnlyDictionary<string, Type>`. A duplicate name or type throws.
  - `IBusContext.Module<TState>()`: the state object for that declared type, created on first use and kept in `LegacyState.Modules` (a `Dictionary<string, object>` keyed by module name). An undeclared type throws `InvalidOperationException`.
  - `LegacyState.Module<TState>(string name) where TState : class, new()`: the same lookup without a bus, for views and stores.
  - `EventBus.Provide<TView>(Func<LegacyState, TView> factory) where TView : class` and `IBusContext.View<TView>()`. `EventBus.View<TView>(LegacyState state)` gives the same view outside a command (for `SimRunner` and `Web`). An unprovided view throws `InvalidOperationException`; providing one twice throws.
  - `LegacyState.Week` (`int`, starts at 1).
  - `interface IRosterView { IReadOnlyList<string> Roster { get; } bool IsFit(string crewId); TeamMember? Member(string crewId); }`. `Roster` lists living crew ids in roster order. `Member` returns null for an unknown or dead crew member.
  - Moved from `Sim.Cycle` to `Sim.Core` with their `[EventType]` names and versions unchanged: `AdvancePhase`, `PhaseEntered(int Cycle, CyclePhase Phase)`, and `CycleAdvanced(int Cycle)`. New: command `FoundLegacy`, and events `[EventType("LegacyFounded", 1)] LegacyFounded()` and `[EventType("WeekAdvanced", 1)] WeekAdvanced(int Week)`.

**Rules:**
1. `FoundLegacy` is sent once, first. It publishes `LegacyFounded`, then `PhaseEntered(1, Orders)`. Sending it twice, or sending `AdvancePhase` before it, is refused. Record that a legacy is founded in a `CycleState` module state (`bool Founded`).
2. When the cycle wraps, `CycleModule` adds one week, then publishes `CycleAdvanced`, `WeekAdvanced`, and `PhaseEntered`, in that order. The length of a cycle is a constant of one week in `CycleModule` for now, named, with a comment that transit will vary it.
3. A guard's refusal leaves `LegacyState` exactly as it was, including `NextSequence` and the random streams, when the guarded command is the top-level one.
4. Document on `EventBus.Send`: if a handler or subscriber throws anything, the caller must discard the state and reload it.

**Tests:**
- Guards: run in order; a refusal carries the reason; the handler does not run; the state's JSON (via `SimJson`) is unchanged; a nested send is guarded too; a duplicate registration throws.
- Module state: created once and returned again; two modules' states are separate objects; an undeclared type throws; a duplicate name throws.
- Views: a view reflects its module's state after a command changes it; an unprovided view throws.
- Cycle: `FoundLegacy` publishes the two events; a second one is refused; `AdvancePhase` before founding is refused; the week advances on the wrap, and the three events come in order.
- Re-pin the simulator's golden hash (it now starts with `FoundLegacy` and gains `WeekAdvanced`), and fix the event-count assertions to match.

- [ ] **Step 1:** Move the cycle messages to the core and confirm `./check.sh` is unchanged. Commit: "Move the cycle's messages to the core".
- [ ] **Step 2:** Guards and `CommandRejectedException`, test-first.
- [ ] **Step 3:** Module state and views, test-first.
- [ ] **Step 4:** `FoundLegacy`, weeks, and `IRosterView`; `Simulation.Run` sends `FoundLegacy` first. Re-pin the golden hash.
- [ ] **Step 5:** `./check.sh`, then commit: "Add guards, views, module state, and weeks to the core".

---

### Task 3: Storing module state

**Files:**
- Modify: `src/Persistence/ILegacyStore.cs`, `SqliteLegacyStore.cs`, `LegacyExport.cs`
- Test: `tests/Persistence.Tests/SqliteLegacyStoreTests.cs`

**Interfaces:**
- Consumes: `LegacyState.Modules`, `LegacyState.Week`, `EventBus.ModuleTypes`, and `SimJson.Options` from Tasks 1 and 2.
- Produces:
  - `ILegacyStore.Load(Guid legacyId, IReadOnlyDictionary<string, Type> moduleTypes)`; `Create` and `Commit` keep their signatures and now write every module state.
  - `LegacyExport.Import(ILegacyStore store, string json, IReadOnlyDictionary<string, Type> moduleTypes)`. The snapshot's `FormatVersion` is 2 and it carries the module states by name.

**Rules:**
1. Table `module_state(legacy_id TEXT NOT NULL, module TEXT NOT NULL, json TEXT NOT NULL, PRIMARY KEY (legacy_id, module))`. `Commit` upserts every module in `state.Modules`, the core row (now including `week`), and the new events, in one transaction.
2. Module states are serialised with `SimJson.Options`. A stored module whose name is not in `moduleTypes` is an error on load, naming the module.
3. The schema carries `PRAGMA user_version = 2`. A database that already has a `legacy` table and any other version is refused when the store opens: `InvalidOperationException("This database is from an older build (schema N). Delete it or export its legacies with that build.")`. A new database is created at version 2.
4. Every `INSERT` names its columns.
5. An export of format version 1 is refused on import, like any unknown version.
6. A failed `Commit` changes nothing, as now: add module state to the existing rollback test.

**Tests:**
- A legacy with two module states (use small test classes) round-trips through `Create`, `Commit`, and `Load`, including `Week`.
- A module state changed and committed is what `Load` returns; an unknown stored module fails with its name.
- An old database (create one with `user_version` 1 and a `legacy` table) is refused.
- Export and import round-trip module states, and the re-export is byte-identical; a version 1 file is refused.
- `Events_are_isolated_by_legacy`: commit a different event to the second legacy too, and assert each legacy reads only its own.

- [ ] **Step 1:** Write the failing tests. **Step 2:** See them fail. **Step 3:** Implement. **Step 4:** `./check.sh`. **Step 5:** Commit: "Store module state, version the schema, and export format 2".

---

### Task 4: Crew, gear, and assignment content

**Files:**
- Create: `src/Sim.Core/Content/CrewContent.cs`; `src/Sim.Content/ContentLoader.Crew.cs`; `content/crew/starting.yaml`, `content/gear/base.yaml`, `content/assignments/base.yaml`, `content/tuning/crew.yaml`
- Modify: `src/Sim.Core/Content/ContentPack.cs`, `MissionContent.cs`; `src/Sim.Content/ContentLoader.cs`, `ContentLoader.Missions.cs`, `ContentLoader.Tuning.cs`; `content/objectives/base.yaml`
- Test: `tests/Sim.Tests/CrewContentTests.cs` (new), and the existing content tests' pack fixtures

**Interfaces:**
- Consumes: the loader's `Items<T>`, `Parse<T>`, `TuningReader`, and error style.
- Produces, in `Sim.Core.Content`:
  - `CrewDef(string Id, int Seniority, IReadOnlyDictionary<string, int> Skills, int Caution, int Discipline)`.
  - `GearDef(string Id, string Tag)`.
  - `AssignmentDef(string Id, IReadOnlyList<string> ObjectiveTags, IReadOnlyDictionary<int, int> Offer, int LoadoutLimit)`: `Offer` maps a number of objectives to its weight.
  - `ObjectiveDef` gains a last field `int? UnattendedDifficulty`.
  - `CrewTuning` with required init properties: `int XpBase`, `int MaxLevel`, `int LightInjuryWeeks`, `int IncapacitatedWeeks`, `int StartingLoyalty`, `int LoyaltyPerPoint`, `int LoyaltyMin`, and `int LoyaltyMax`.
  - `ContentPack(Id, Version, Skills, Encounters, Objectives, Tuning, Bench, Crew, Gear, Assignments, CrewTuning)`: the four new members come last.

**Content formats** (all placeholders):

```yaml
# content/crew/starting.yaml: the roster of a new legacy
- id: CREW_01
  seniority: 5
  skills: { SKILL_A: 6, SKILL_B: 5, SKILL_C: 4, SKILL_D: 3 }
  caution: 2
  discipline: 2
```

Write all eight from the benchmark crew in `tests/Sim.Tests/TestContent.Missions.cs` (seniority, the four skills in order A to D, caution, and discipline).

```yaml
# content/gear/base.yaml
- { id: GEAR_01, tag: scanner }
- { id: GEAR_02, tag: tools }
- { id: GEAR_03, tag: comms }
- { id: GEAR_04, tag: medical }
```

```yaml
# content/assignments/base.yaml
- id: ASSIGNMENT_TEMPLATE_1
  objectives: { tags: [] }        # every objective template whose tags include all of these
  offer: { 1: 6, 2: 3 }           # how many objectives a site offers, by weight. One is the rule
  loadoutLimit: 2
```

```yaml
# content/tuning/crew.yaml: every number the crew module uses. All are required.
growth:
  xpBase: 10                 # experience to the next level is xpBase * (level + 1)
  maxLevel: 10
injury:
  lightWeeks: 2              # a light injury is -1 to every skill until it heals
  incapacitatedWeeks: 6
loyalty:
  starting: 50
  perPoint: 2                # a mission's loyalty change is multiplied by this
  min: 0
  max: 100
```

In `content/objectives/base.yaml`, add `ifUnattended: { difficulty: 6 }` to `OBJECTIVE_TEMPLATE_1` and `ifUnattended: { difficulty: 5 }` to `OBJECTIVE_TEMPLATE_2`.

**Rules (all reported together, in the loader's existing style):**
1. Crew: the id pattern; `seniority` 1 or more; every skill id known; levels 0 to `maxLevel`; `caution` and `discipline` from −2 to 2. A pack with no crew is an error.
2. Gear: the id pattern; a non-empty tag.
3. Assignments: every `offer` key and weight is 1 or more; at least as many objective templates match the tags as the largest `offer` key; `loadoutLimit` is 0 or more. A pack with no assignment is an error.
4. `ifUnattended.difficulty`, when present, is 1 or more.
5. `tuning/crew.yaml` is read as strictly as the mission tuning: a missing, unknown, or malformed value is an error; `min` must not exceed `starting`, and `starting` must not exceed `max`; `xpBase`, `maxLevel`, and both week counts are 1 or more.

**Tests:** the starter pack loads with 8 crew, 4 gear, 1 assignment, and the crew tuning values above; one test per rule, asserting the file and that the message names the item; a pack fixture helper so older tests keep loading (give them the repository's crew, gear, assignment, and crew tuning files, as they already do for the mission tuning).

- [ ] **Step 1:** Write the failing tests. **Step 2:** See them fail. **Step 3:** Implement the definitions, loader, and content files. **Step 4:** `./check.sh`. **Step 5:** Commit: "Add crew, gear, and assignment content, and the crew tuning file".

---

### Task 5: The crew module

**Files:**
- Create: project `src/Sim.Crew` (references `Sim.Core` only; add to `StarshipSim.slnx`; reference it from `tests/Sim.Tests`), with `CrewState.cs`, `CrewModule.cs`, and `RosterView.cs`
- Test: `tests/Sim.Tests/CrewModuleTests.cs`

**Interfaces:**
- Consumes: `EventBus.Module`, `Provide`, `Subscribe`; `IRosterView`; `LegacyFounded`, `WeekAdvanced`, `MissionEnded`, `MissionResult`, `CrewResult`, `MemberStatus`; `ContentPack.Crew` and `CrewTuning`. `MissionEnded` is declared in Task 1's `MissionContracts.cs` as `[EventType("MissionEnded", 1)] MissionEnded(string MissionId, string ObjectiveId, MissionResult Result)`; if it is not there yet, add it there.
- Produces, in `Sim.Crew`:
  - `CrewModule.Register(EventBus bus, ContentPack pack)`: declares module `"crew"` with state `CrewState`, provides `IRosterView`, and subscribes at order 100.
  - `enum CrewStatus { Active, Recovering, Dead }`.
  - `CrewState { List<CrewMember> Members }`; `CrewMember { string Id; int Seniority; Dictionary<string, SkillState> Skills; int Caution; int Discipline; CrewStatus Status; List<int> LightInjuriesHealAtWeek; int? ReturnsAtWeek; int Loyalty; int AwayMissions; }`; `SkillState { int Level; double Xp; }`. All are plain classes with settable properties, so they serialise with `SimJson`.
  - Events, in `Sim.Crew`: `[EventType("SkillRaised", 1)] SkillRaised(string CrewId, string SkillId, int Level)`, `[EventType("CrewInjured", 1)] CrewInjured(string CrewId, MemberStatus Outcome, int LightInjuries, int? ReturnsAtWeek)`, `[EventType("CrewDied", 1)] CrewDied(string CrewId, string MissionId)`, and `[EventType("CrewRecovered", 1)] CrewRecovered(string CrewId)`.

**Rules:**
1. On `LegacyFounded`: the roster is created from `pack.Crew`, each at `StartingLoyalty`, with `Xp` 0.
2. On `MissionEnded`, for each crew result, in the result's order:
   - Experience: add each skill's amount to that skill's `Xp`. While `Level < MaxLevel` and `Xp >= XpBase * (Level + 1)`, subtract that threshold and raise the level, publishing `SkillRaised` each time. At `MaxLevel`, `Xp` stays at 0.
   - `Injury` light injuries: add that many entries of `Week + LightInjuryWeeks`.
   - `Incapacitated`: status `Recovering`, `ReturnsAtWeek = Week + IncapacitatedWeeks`.
   - `Dead`: status `Dead`; publish `CrewDied`. The member stays in the state.
   - Publish one `CrewInjured` when the member took a light injury or was incapacitated (not when dead).
   - Loyalty: add `result.Loyalty * LoyaltyPerPoint`, held within `LoyaltyMin` and `LoyaltyMax`.
   - `AwayMissions` goes up by one for everyone on the team, including the dead.
3. On `WeekAdvanced`: drop light injuries due at or before the new week. A `Recovering` member due at or before the new week becomes `Active` with `ReturnsAtWeek` cleared. Publish one `CrewRecovered` per member who healed anything or returned. The dead never recover.
4. The view: `Roster` is every member not `Dead`, in state order. `IsFit` is true only for `Active`. `Member(id)` gives a `TeamMember` whose skill levels are each lowered by the number of unhealed light injuries, never below 0; null for dead or unknown.

**Tests:** founding creates eight members at loyalty 50; a level is gained exactly at the threshold, two levels in one mission, and none past the maximum; light injuries lower the view's skills and heal on the due week, not before; incapacitation makes a member unfit until the due week; death removes them from the roster and the view but not the state; loyalty stops at both bounds; away missions count; each event is published with the right fields; the state round-trips through `SimJson`.

Drive the module through a real `EventBus` with `CycleModule` registered: send `FoundLegacy`, publish `MissionEnded` from a tiny test command handler, and send `AdvancePhase` to move the weeks.

- [ ] **Step 1:** Create the project and write the failing tests. **Step 2:** See them fail. **Step 3:** Implement. **Step 4:** `./check.sh`. **Step 5:** Commit: "Add the crew module".

---

### Task 6: Missions on the bus

**Files:**
- Create: `src/Sim.Missions/MissionsState.cs`, `MissionsModule.cs`
- Test: `tests/Sim.Tests/MissionsModuleTests.cs`

**Interfaces:**
- Consumes: `MissionResolver.Start` and `Advance`, `CallReply`, `Beat`; `EventBus.Guard`, `Module`, `Subscribe`, `Handle`; `IBusContext.View<IRosterView>()` and `Rng`; `PhaseEntered`, `AdvancePhase`, `MissionEnded`; `ContentPack` (assignments, objectives, encounters, gear, and tuning); `DiceDef`. `MissionStarted` is declared in `Sim.Core` as `[EventType("MissionStarted", 1)] MissionStarted(string MissionId, string ObjectiveId, string TemplateId, IReadOnlyList<string> CrewIds, string LeadId)`; add it to `MissionContracts.cs` if Task 1 did not.
- Produces, in `Sim.Missions`:
  - `MissionsModule.Register(EventBus bus, ContentPack pack)`: declares module `"missions"` with state `MissionsState`; subscribes at order 200 (after crew).
  - Commands: `AssignTeam(string ObjectiveId, IReadOnlyList<string> CrewIds, string LeadId, IReadOnlyList<string> GearIds, Parameters Parameters)` and `AnswerCall(string MissionId, string CallId, CallAnswer Answer)`.
  - Events: `[EventType("SiteArrived", 1)] SiteArrived(int Cycle, string AssignmentId, IReadOnlyList<SiteObjective> Objectives)`, `[EventType("TeamAssigned", 1)] TeamAssigned(string ObjectiveId, IReadOnlyList<string> CrewIds, string LeadId, IReadOnlyList<string> GearIds, Parameters Parameters)`, `[EventType("MissionBeat", 1)] MissionBeat(string MissionId, int Index, Beat Beat)`, and `[EventType("ObjectiveUnattended", 1)] ObjectiveUnattended(string ObjectiveId, string TemplateId, int Roll, int Target, bool Succeeded)`.
  - State: `MissionsState { SiteState? Site; }`; `SiteState { int Cycle; string AssignmentId; List<SiteObjective> Objectives; Briefing? Briefing; MissionInFlight? Mission; }`; `SiteObjective(string Id, string TemplateId)`; `Briefing(string ObjectiveId, IReadOnlyList<string> CrewIds, string LeadId, IReadOnlyList<string> GearIds, Parameters Parameters)`; `MissionInFlight { string Id; string ObjectiveId; MissionState State; int BeatCount; }`.

**Rules:**
1. **Orders.** On `PhaseEntered` for `Orders`: replace the site. Take the pack's first assignment template. Choose how many objectives by the `offer` weights (stream `"site:offer"`), then draw that many distinct objective templates whose tags include all the assignment's tags, in ordinal id order, one stream draw each (`"site:draw:{n}"`). Objective ids are `o_{cycle}_{n}` from 1. Publish `SiteArrived`.
2. **Briefing.** `AssignTeam` is refused, with a reason that names what is wrong, unless: the phase is `Briefing`; no team is assigned this cycle; the objective is at this site; the team's size is within the template's `teamSize`; the crew ids are distinct, on the roster, and fit; the lead is on the team; the gear ids are known, distinct, and no more than the assignment's `loadoutLimit`. On success, store the briefing and publish `TeamAssigned`.
3. **Resolution.** On `PhaseEntered` for `Resolution`, if there is a briefing: the mission id is `m_{cycle}_1`; the team comes from the roster view (`Member`), with the briefing's lead; the gear tags are the gear items' tags; the seed is `context.Rng($"mission:{id}").NextUInt64()`. Publish `MissionStarted`, then `Start` and `Advance`, publishing one `MissionBeat` per beat with a running index from 0. If it ends, publish `MissionEnded` with the result.
4. **Calls.** `AnswerCall` is refused unless a mission is waiting and both ids match. Otherwise it advances the mission with a `CallReply`, publishes the new beats, and publishes `MissionEnded` if it ended. A reply the resolver rejects (an answer that is not an option) is refused with the resolver's message, before any change.
5. **A waiting call blocks the cycle.** A guard on `AdvancePhase` (order 200, name `"missions"`) refuses with "A call is waiting for an answer." while the phase is `Resolution` and the mission has a pending call.
6. **Aftermath.** On `PhaseEntered` for `Aftermath`: for each site objective with no team and an authored `UnattendedDifficulty`, in order, roll `Tuning.CheckDice` on stream `"unattended:{objectiveId}"` against `difficulty + CheckTarget`; it succeeds when the roll is at least the target. Publish `ObjectiveUnattended`.
7. The mission stays in the state, finished, until the next site replaces it.

**Tests** (a real bus with `CycleModule`, `CrewModule`, and `MissionsModule` on the repository's pack):
- Over 300 cycles' worth of `SiteArrived`, sites with one objective are more common than with two, both occur, and no site repeats a template.
- Each briefing refusal, one test each, asserting the reason and that the state's JSON is unchanged. A valid team is accepted once; a second is refused.
- Sending nobody: the cycle runs through, and every objective with a difficulty gets an `ObjectiveUnattended` whose `Succeeded` matches `Roll >= Target`.
- A mission that raises a call (find a seed and team that do): `AdvancePhase` is refused while it waits; `AnswerCall` with the wrong mission or call id is refused; the right answer runs it on; `MissionBeat` indexes are 0, 1, 2, and so on with no gap across the pause; `MissionEnded` comes last, once.
- After `MissionEnded`, the crew module has applied the result (the team's `AwayMissions` is 1).
- The whole legacy state, with a call waiting, round-trips through `SimJson` per module and continues to the same events as the original.
- Two missions in one legacy have different seeds.

- [ ] **Step 1:** Write the failing tests. **Step 2:** See them fail. **Step 3:** Implement. **Step 4:** `./check.sh`. **Step 5:** Commit: "Run missions through the event bus: site, briefing, calls, and unattended objectives".

---

### Task 7: A captain for the simulator, and the end-to-end test

**Files:**
- Create: project `src/GameHost` (references `Sim.Core`, `Sim.Cycle`, `Sim.Crew`, `Sim.Missions`; add to the solution) with `Game.cs`; `src/SimRunner/AutoCaptain.cs`
- Modify: `src/SimRunner/Simulation.cs`, `Cli.cs`, `SimRunner.csproj` (reference `GameHost`); `tests/SimRunner.Tests/SimRunner.Tests.csproj` (reference `Persistence` and `GameHost`); `tests/Sim.Tests/ArchitectureTests.cs`
- Test: `tests/SimRunner.Tests/SimulationTests.cs`, `EndToEndTests.cs` (new), `CliTests.cs`

**Interfaces:**
- Consumes: every module's `Register`; `EventTypeRegistry.FromAssemblies`; `ILegacyStore`; `Bench.Answer(CallPolicy, PendingCall)`.
- Produces:
  - `GameHost.Game.Build(ContentPack pack)` returns `Game { EventBus Bus; EventTypeRegistry Events; IReadOnlyDictionary<string, Type> ModuleTypes; }` with `Sim.Cycle`, `Sim.Crew`, and `Sim.Missions` registered, in that order.
  - `AutoCaptain.Step(Game game, LegacyState state, ContentPack pack)` returns the events of the one command it sent: the next thing a simple captain does. `AutoCaptain.PlayCycle(...)` steps until the cycle number changes and returns all the events.
  - `Simulation.Run(SimOptions options, ContentPack pack)`; `LegacySummary` gains `IReadOnlyDictionary<MissionOutcome, int> Missions`, `int Deaths`, `int Alive`, and `int HighestLevel`.

**Rules:**
1. The captain, by phase:
   - Not founded: `FoundLegacy`.
   - `Briefing`, with no team yet: take the site's first objective. If at least its template's smallest team size of crew are fit, send that many: the fit crew ordered by the sum of their skill levels (highest first, ties by roster order), led by the most senior of them (ties by roster order), with the first gear items in pack order up to the loadout limit, and default parameters. If too few are fit, send nobody.
   - `Resolution`, with a call waiting: answer by `Bench.Answer(CallPolicy.BackLead, call)`.
   - Otherwise: `AdvancePhase`.
2. `Simulation.Run` plays each legacy for the asked number of cycles through the captain, collecting envelopes for the hash as before.
3. The headline becomes: `Content CORE v1: N legacies x M cycles, E events. Missions: A full, B partial, C incomplete, D withdrawn, F lost. Deaths: X. OK.`
4. `ArchitectureTests`: `GameHost` may reference only `Sim.*` projects.

**Tests:**
- The captain sends the expected team on a hand-checkable case (the starter roster, cycle 1), and sends nobody when too few are fit.
- A run of 3 legacies for 20 cycles: every legacy has at least one mission; outcome counts sum to the missions started; `Alive + Deaths` is 8.
- **End to end** (`EndToEndTests`): play one legacy for 6 cycles through `Game`, `AutoCaptain.Step`, and a `SqliteLegacyStore` on a temporary file, committing after every step and, after every step, discarding the state and loading it again from the store. The event log read back from the store equals, envelope for envelope, the log of the same legacy played straight through in memory. Choose a seed for which at least one step is taken while a call is waiting (assert that it was).
- Re-pin the golden seed (seed 1, 10 cycles) on purpose, and update the `cycles` assertions in `CliTests`.

- [ ] **Step 1:** Create `GameHost` and write the failing tests. **Step 2:** See them fail. **Step 3:** Implement. **Step 4:** `./check.sh` (its `cycles` line still runs 20 legacies for 50 cycles; paste the headline into your report). **Step 5:** Commit: "Play whole cycles in the simulator, and prove a legacy reloads mid-mission".

---

### Task 8: Record what was built

**Files:**
- Modify: `GLOSSARY.md`, `docs/gdd.md`, `docs/plans/phase-1-outline.md`, `wayfinder/map.md`

The controller supplies the exact text with the task, because it depends on figures measured after Task 7: the re-measured bench figures for GDD 4.7, the reworded "develop juniors" rule in GDD 4.2, the fifth outcome in GDD 4.3, the dice seam in GDD 4.7, and new glossary terms (**Roster**, **Fit**, and **Week**).

- [ ] **Step 1:** Make the edits exactly as supplied. **Step 2:** Commit: "Record the headless cycle in the glossary, GDD, outline, and map".

---

## Done when

- `./check.sh` exits 0 on a clean clone.
- `SimRunner cycles` plays legacies with real missions and prints the outcome and death totals.
- A legacy stored and reloaded after every command, including while a call waits, produces the same event log as one played straight through.
- Changing `check.dice` in the tuning file changes the dice the resolver rolls, with no code change.

## Deliberately not in this part

The web project and Datastar, legacy selection, the content editor, renown and the chronicle, reports, storylets, morale, ranks, training, ageing, the captain as a crew member, several teams per cycle, multi-cycle assignments, transit, and requisition.
