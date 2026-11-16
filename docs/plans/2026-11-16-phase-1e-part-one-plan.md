# Slice 1e, part one (a storylet from a mission): implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** something that happens on a mission changes a numeric story flag on the ship or a crew member, and a storylet whose flag requirements hold arrives in the aftermath as a script with two to four answers whose fixed effects the player sees.

**Architecture:** `Sim.Missions` only reports the flag changes a mission made, in its result. A new pure module, `Sim.Storylets`, owns the flags, offers storylets when the aftermath begins, and applies an answer's effects, reaching crew only through a command. A storylet's words become lines in the pack's one pool, as an encounter's do, and are rendered on view. The bridge draws waiting storylets from stored state, so a reload shows them.

**Tech stack:** .NET 10 (`global.json`), xUnit, Blazor static components with Datastar 1.0.5 (`docs/research/datastar-1.0.5.md`), YAML content, SQLite store.

**Spec:** `docs/plans/2026-11-16-phase-1e-design.md`. Read it with this plan. **Design system:** `docs/design/bridge-design.md`.

## Global constraints

- `dotnet` is not on PATH: `export PATH="$HOME/.dotnet:$PATH"`. The gate is `./check.sh`. Its one known failure is `ContentSpeedTests`, slow only when the whole Web.Tests assembly runs; treat the gate as green if that is the only failure, and run the script's remaining steps by hand.
- Commit messages are one plain sentence in the style of `git log`, with no attribution and no mention of any AI tool, ever. Every commit names its paths. Never edit `AGENTS.md` or `docs/personal_todo.md`; if the to-do list changes, commit it alone.
- Braces on every body. Comments are terse and say why. Plain interface words. Tokens for every colour. Every page works at 400 pixels wide with no page scroll, and nothing scrolls inside a card.
- Test first: each task's tests are written and seen to fail before its code.
- `src/Sim.*` stays pure, and feature modules never reference each other (the architecture test enforces both). `Sim.Storylets` references only `Sim.Core`. Nothing in this slice draws randomness.
- `tests/fixtures/content/` holds placeholders only. `content/` may gain generated words for the two storylets of design section 4.8 and nothing else.
- Vocabulary follows `GLOSSARY.md`: "story flag" (here "flag"), "storylet", "aftermath". There is no "mark", "quest", or "event" in the interface.
- A task that changes what the store holds raises `SchemaVersion` in `src/Persistence/SqliteLegacyStore.cs` and `CurrentFormatVersion` in `src/Persistence/LegacyExport.cs` by one each, with their pinned tests. They are 6 and 4 today.
- Golden hashes move only where a task says so, with a comment saying why.
- A refused request answers HTTP 422 with the page or form drawn again, as everywhere else.
- `tests/browser/*.sh` need `playwright-cli`, which is not installed: update them where a task says, check with `bash -n`, and say they were not run. For a by-hand browser check use headless Firefox on a scratch database, a scratch copy of the content, and a free port (see how `check.sh` starts the app).

## Review focus

1. **A flag lands on someone who dies on that mission.** It is not stored, and no storylet arrives for them. (Task 2)
2. **More storylets are due than the cycle allows** (two crew with the same flag and a ship storylet, limit 2). The same two arrive on every run, highest stakes first; the third arrives next cycle. (Task 2)
3. **An answer is posted twice, for a storylet that is not waiting, or with an option it does not have.** Refused; nothing changes. (Tasks 2 and 3)
4. **No team is sent, but a storylet has been waiting since an earlier cycle.** It is shown, it is answerable, the aftermath cannot be left until it is, and the simulator does not loop. (Tasks 2 and 3)
5. **The author deletes or breaks a storylet while it is waiting in a saved game.** It is withdrawn at the next request; it never blocks the legacy. (Task 2)

---

### Task 1: a mission changes flags

**Files:** `src/Sim.Core/Content/MissionContent.cs`, `src/Sim.Core/MissionContracts.cs`, `src/Sim.Content/ContentLoader.Missions.cs`, `ContentModels.cs`, `ContentReader.cs`, `ContentWriter.cs`, `ContentFaithfulness.cs`, `src/Sim.Missions/Beats.cs`, `MissionState.cs`, `MissionResolver.cs`, `src/Web/Bridge/BeatLines.cs` (tags), `src/Web/Forms/MissionKinds.cs`, `src/Web/Components/Content/EffectRows.razor`, `ApproachTabs.razor`, both persistence version files; `tests/fixtures/content/encounters/base.yaml`; tests beside each.

**Produces:**

```csharp
// Sim.Core.Content
// On is "ship", "actor", "lead", or "team" in a mission's effects. Set false means add.
public sealed record FlagChange(string Flag, int Amount, bool Set, string On);
public sealed record EffectDef(int Momentum = 0, int Strain = 0, int Progress = 0, InjurySeverity? InjuryRoll = null,
    bool Withdraw = false, IReadOnlyList<FlagChange>? Flags = null);   // null, never empty, when there are none: keeps record equality
public static class FlagIds { public static bool IsValid(string id); }  // lower-case letters, digits, dots, hyphens; 1 to 40

// Sim.Core
public sealed record MissionFlag(string Flag, int Amount, bool Set, string? CrewId, string EncounterId);   // CrewId null: the ship
// MissionResult gains, last: IReadOnlyList<MissionFlag> Flags
// CheckBeat gains, last: IReadOnlyList<MissionFlag>? Flags = null   (what this check's outcome changed)

// Sim.Content: EffectModel gains string? Flag, int? FlagAmount, bool FlagSet, string? FlagOn.
// Web.Forms: the Bold and Safe row sets gain fields "flag", "flagMode" ("add" or "set"), "flagAmount", "flagOn";
//   the approaches row set gains, for each tier T, "out.T.flag", "out.T.flagMode", "out.T.flagAmount", "out.T.flagOn".
```

**Rules pinned here (design 4.2):**
- YAML: `{ flag: flag-a, add: 1, on: ship }` or `{ flag: flag-a, set: 2, on: lead }` as one entry of an effect list. Exactly one of `add` and `set`; `on` required; `on: actor` only in an approach's outcome. `set` is 0 or more. Loader messages: "A flag change needs add or set, not both.", "A flag change needs on: ship, actor, lead, or team.", "on: actor is only for an approach's outcome.", "Flag id '{id}' must be 1 to 40 lower-case letters, digits, dots, or hyphens."
- The resolver appends one `MissionFlag` per target when an outcome or a decision's answer applies: `ship` gives `CrewId` null; `actor` the one who made the check; `lead` the current lead; `team` one for each member still standing, in team order. Nothing else about the mission changes: no dice, no momentum.
- The mission's result carries every `MissionFlag` in the order made. A check's beat carries its own; an `EffectsBeat` already carries its `EffectDef`.
- The log's tags for a beat that changed a flag: `flag-a +1, ship` / `flag-a = 2, CREW_02`.
- The form's outcome holds one flag change per tier and a decision's effect row one per row; labels "Flag", "Add or set", "Amount", "On". A hand-written outcome with two flag changes is kept by the reader and writer and marked as something the form cannot keep, through the existing lossy mechanism.
- Fixture: `ENCOUNTER_01`'s first approach gains `partial: [{ strain: 1 }, { flag: flag-a, add: 1, on: ship }]`, and `ENCOUNTER_04`'s first approach gains a `disaster` outcome equal to its default plus `{ flag: flag-b, add: 1, on: actor }`.

- [ ] **Step 1:** Tests. `MissionContentTests`: one per loader message; `A_flag_change_loads_on_an_outcome_and_on_a_decision`. `MissionRulesTests`: `A_ship_flag_from_an_outcome_is_in_the_result_with_no_crew`, `An_actor_flag_names_the_one_who_made_the_check`, `A_team_flag_names_each_member_still_standing`, `A_lead_flag_from_a_decision_names_the_lead_at_that_moment`, `A_flag_change_alters_nothing_else_about_the_mission` (same seed with and without the flag: identical beats apart from `Flags`). `ContentWriterTests`, `ContentFaithfulnessTests`: add, set, each `on`, and two changes in one outcome read back equal. `ContentMissionFormsTests`: saving a tier with `out.partial.flag=flag-a`, `flagMode=add`, `flagAmount=1`, `flagOn=ship` writes the entry; opening and saving the fixture unchanged leaves the file unchanged. `BeatLinesTests`: the two tag forms.
- [ ] **Step 2:** Run them. Expected: compile failures, then failures.
- [ ] **Step 3:** Implement. Raise both persistence versions (7 and 5).
- [ ] **Step 4:** `dotnet test`. Both golden hashes move, because every mission result gained `Flags`; confirm on one walkthrough that every other field of every beat is unchanged, then re-pin with the comment "a mission's result and a check's beat carry the flags they changed (1e part one, Task 1)".
- [ ] **Step 5:** `./check.sh`, then commit: "Let an encounter's outcome or a decision's answer change a story flag, and report it in the mission's result".

### Task 2: the storylets module, headless

**Files:** new project `src/Sim.Storylets/` (`StoryletsModule.cs`, `StoryletsState.cs`), `StarshipSim.slnx`, `src/Sim.Core/Content/StoryletContent.cs` (new), `ContentPack.cs`, `MissionTuning.cs` or a new tuning record, `src/Sim.Core/Narrative/NarrativeFacts.cs`, `src/Sim.Content/ContentLoader.Storylets.cs` (new), `ContentLoader.cs`, `ContentLoader.Narrative.cs`, `ContentFiles.cs`, `src/Sim.Crew/CrewModule.cs`, `src/GameHost/Game.cs`, `src/SimRunner/AutoCaptain.cs`, `Simulation.cs`, `Cli.cs`; `tests/fixtures/content/storylets/base.yaml` (new), `tests/fixtures/content/tuning/missions.yaml`, `content/tuning/missions.yaml`; `tests/Sim.Tests/StoryletContentTests.cs`, `StoryletsModuleTests.cs` (new), `ArchitectureTests.cs`, `tests/SimRunner.Tests/*`.

**Consumes:** `MissionEnded.Result.Flags` (`MissionFlag`), `FlagChange`, `FlagIds` from Task 1.

**Produces:**

```csharp
// Sim.Core.Content
public sealed record FlagRange(string Flag, int Min, int? Max);
// LoyaltyOn is "subject" or "crew" when Loyalty is not 0. Flag.On is "ship", "subject", or "" (the scope the storylet arrived for).
public sealed record StoryletEffect(int Loyalty = 0, string? LoyaltyOn = null, FlagChange? Flag = null);
public sealed record StoryletOption(string Id, string Label, IReadOnlyList<StoryletEffect> Effects);
public sealed record StoryletDef(string Id, int Stakes, IReadOnlyList<FlagRange> Requires, IReadOnlyList<StoryletOption> Options, string? Name = null);
// ContentPack gains: IReadOnlyList<StoryletDef> Storylets { get; init; } = [];  and  int StoryletsPerCycle { get; init; } = 2;

// Sim.Core (contracts, so the bridge and the simulator need not reference the module's internals)
public sealed record AnswerStorylet(string OfferId, string OptionId) : ICommand;
public sealed record AdjustLoyalty(string CrewId, int Delta) : ICommand;            // handled by Sim.Crew, clamped by its tuning
[EventType("LoyaltyChanged", 1)]   public sealed record LoyaltyChanged(string CrewId, int From, int To) : IEvent;
[EventType("FlagChanged", 1)]      public sealed record FlagChanged(string Flag, string? CrewId, int From, int To, string? EncounterId) : IEvent;
[EventType("StoryletOffered", 1)]  public sealed record StoryletOffered(string OfferId, string StoryletId, string? SubjectId, string? EncounterId) : IEvent;
[EventType("StoryletAnswered", 1)] public sealed record StoryletAnswered(string OfferId, string StoryletId, string? SubjectId, string OptionId) : IEvent;
[EventType("StoryletWithdrawn", 1)] public sealed record StoryletWithdrawn(string OfferId, string StoryletId, string? SubjectId) : IEvent;

// Sim.Storylets
public static class StoryletsModule { public const string Name = "storylets"; public static void Register(EventBus bus, ContentPack pack); }
public sealed class StoredFlag { Flag, CrewId (null: ship), Value, SetAtWeek, EncounterId (the last encounter that changed it, or null) }
public sealed class WaitingStorylet { OfferId, StoryletId, SubjectId, EncounterId }
public sealed class StoryletsState { List<StoredFlag> Flags; List<WaitingStorylet> Waiting; }
```

`OfferId` is `"{cycle}:{storyletId}:{subjectId or "ship"}"`.

**Content (design 4.3), file `storylets/*.yaml`:** as the design's example. Loader rules and messages:
- `stakes` 1 to 5; `requires` one to four entries, each a known-shaped flag id with `min` and `max` (`min` defaults to 1, or to 0 when only `max` is given; `max` at least `min`); "A storylet needs one to four requirements."
- Two to four options, ids unique in the storylet, in the approach id's form; labels 1 to 40 characters.
- Effects: `{ loyalty: N, on: subject|crew }` and `{ flag: ID, add|set: N, on: ship|subject }` with `on` optional on a flag. Anything else: "A storylet's effect can change loyalty or a flag; '{key}' is not one of them."
- "This storylet has no subject" for `on: subject`, a `subject` speaker, or a `#subject#` token is not decidable at load (the scope depends on where the flag was set), so instead: a storylet declares `scope: ship` or `scope: crew` (required), the loader checks text and effects against it, and at run time a flag found on the other scope does not offer the storylet.
- "Option '{id}' leaves every requirement true, so this storylet would come back every cycle." Decide it by applying the option's flag changes to the lowest values that satisfy the requirements.
- A storylet's `text` and each option's `text` become lines of the pack, the way an encounter's own text does (`ContentLoader.Narrative.cs`): new beat kinds `storylet` (facts `storylet`; people `subject` when the scope is crew) and `storyletAnswer` (facts `storylet`, `option`; same people). Speakers allowed: the beat's people, `captain`, and `best SKILL_ID`. `#storylet#` gives the storylet's name.
- Tuning: `storylets: { perCycle: 2 }` in `tuning/missions.yaml`, required, 0 or more.

**Rules (design 4.4):**
- On `MissionEnded` (subscriber order after the crew module's): apply each `MissionFlag` to the stored flags (add or set, never below 0, a flag at 0 removed), publishing `FlagChanged`. A flag for a crew member the roster view no longer has is skipped. (Review focus 1)
- On `CrewDied`: remove their flags and withdraw their waiting storylets.
- On `PhaseEntered` with `Aftermath`: first withdraw any waiting storylet whose definition the pack no longer has, or whose subject is gone (Review focus 5). Then find every (storylet, scope) whose requirements all hold on that scope and which is not waiting; order by stakes descending, storylet id, then the subject's roster position; offer while fewer than `StoryletsPerCycle` are waiting. (Review focus 2)
- Guard `AnswerStorylet`: the offer is waiting and the option is the storylet's ("That storylet is not waiting for an answer.", "That is not one of its answers."). Handler: apply the option's effects in order (loyalty through `AdjustLoyalty`, for `crew` to every living member in roster order; flags as above), remove the offer, publish `StoryletAnswered`.
- Guard `AdvancePhase` while the phase is `Aftermath` and anything waits: "A storylet is waiting for an answer."
- `AutoCaptain.NextCommand`: in the aftermath, answer the first waiting storylet with its first option before advancing. `SimRunner cycles` adds "storylets offered" and "answered" per 100 cycles to its summary line.
- Fixture: `STORYLET_01` (scope ship, requires `flag-a` 1 to 1; option-a sets it to 0 with loyalty -1 on crew; option-b sets it to 2) and `STORYLET_02` (scope crew, requires `flag-b` min 1 and `flag-c` max 0; option-a adds -1 to `flag-b`, sets `flag-c` 1; option-b sets `flag-c` 2 with loyalty -2 on subject), in placeholder words.

- [ ] **Step 1:** Tests. `StoryletContentTests`: one per loader message; the fixture loads; a storylet's text is in the pack's lines with its implied criteria. `StoryletsModuleTests`, driving the bus with built packs:

```csharp
[Fact] public void A_missions_flag_is_stored_with_the_week_and_the_encounter()
[Fact] public void Add_and_set_never_go_below_zero_and_a_flag_at_zero_is_gone()
[Fact] public void A_flag_on_someone_who_died_on_that_mission_is_not_stored()                  // focus 1
[Fact] public void A_storylet_whose_requirements_hold_is_offered_when_the_aftermath_begins()
[Fact] public void A_storylet_for_the_ship_ignores_the_same_flag_on_a_crew_member_and_the_reverse()
[Fact] public void At_most_the_cycles_limit_are_offered_highest_stakes_first_and_the_rest_next_cycle() // focus 2
[Fact] public void An_answer_applies_its_effects_in_order_and_the_storylet_does_not_return()
[Fact] public void An_answer_that_moves_the_flag_on_lets_another_storylet_require_the_new_value()
[Fact] public void Loyalty_on_crew_reaches_every_living_member_and_is_clamped()
[Fact] public void A_second_answer_an_unknown_offer_and_an_unknown_option_are_refused()         // focus 3
[Fact] public void The_aftermath_cannot_be_left_while_a_storylet_waits()
[Fact] public void A_storylet_still_waits_in_a_cycle_where_no_team_was_sent()                   // focus 4
[Fact] public void A_waiting_storylet_the_pack_no_longer_has_is_withdrawn()                     // focus 5
[Fact] public void A_dead_subjects_flags_and_waiting_storylets_are_dropped()
```

`SimulationTests`: 50 cycles of the fixture end with nothing waiting and offered equal to answered plus withdrawn. `ArchitectureTests`: `Sim.Storylets` references only `Sim.Core`.
- [ ] **Step 2:** Run them. Expected: compile failures, then failures.
- [ ] **Step 3:** Implement. Register the module in `Game.Build` and its assembly in the event registry. Raise both persistence versions (8 and 6).
- [ ] **Step 4:** `dotnet test`. The simulation's golden hash moves (new events in the log); the mission walkthrough's must not. Re-pin the first with "storylets are offered and answered in the aftermath (1e part one, Task 2)".
- [ ] **Step 5:** `./check.sh`; put the simulator's storylets-per-100-cycles figure for the fixture in the commit message body. Commit: "Add the storylets module: flags stored from missions, storylets offered in the aftermath, and answers with fixed effects".

### Task 3: storylets on the bridge and in the editor

**Files:** `src/Web/Bridge/BridgeView.cs`, `BridgeReader.cs`, `Prose.cs`, `Names.cs`, `src/Web/Endpoints/BridgeEndpoints.cs`, `src/Web/Components/AftermathScreen.razor`, new `StoryletPanel.razor`, `AdvanceForm.razor`, `CallPanel.razor` only if parts are shared, `src/Web/Forms/TextFiles.cs`, `src/Web/Components/Content/ContentIndex.razor` and its view, `src/Web/wwwroot/css/app.css`; `tests/Web.Tests/StoryletBridgeTests.cs` (new), `ParityTests.cs`, `WebCaptain.cs`, `ContentTextFilesTests.cs`, `tests/browser/play.sh`.

**Consumes:** `StoryletsState` (through `state.TryModule`), `AnswerStorylet`, the Task 2 events, the `storylet` and `storyletAnswer` lines.

**Produces:**

```csharp
// Web.Bridge
public sealed record StoryletView(string OfferId, string Title, string? From, IReadOnlyList<ScriptEntryView> Script,
    IReadOnlyList<StoryletOptionView> Options);                    // waiting
public sealed record StoryletOptionView(string Id, string Label);
public sealed record SettledStoryletView(string Title, IReadOnlyList<ScriptEntryView> Said, IReadOnlyList<string> Tags);   // answered this cycle
// AftermathView gains: IReadOnlyList<StoryletView> Waiting, IReadOnlyList<SettledStoryletView> Settled
// Route: POST /legacy/{id}/storylet   fields: offer, option, and the stamp fields every bridge form carries
```

Use the script entry type the call panel already renders in place of `ScriptEntryView` if one exists.

**Behaviour (design 4.6 and 4.7):**
- The aftermath shows a section headed "Matters aboard" above the report whenever anything is waiting or was settled this cycle; it is absent otherwise. One `StoryletPanel` for each, waiting ones first in arrival order.
- A waiting panel: the storylet's name, "From {encounter name} · {flag}" when the flag has an encounter, the script, then one button for each option with its label. A settled panel: the name, the option's text, then tags in the log's figure style: `Loyalty -2 CREW_02`, `Loyalty -1 all crew`, `flag-a 1 to 0`. Settled panels are rebuilt from this cycle's `StoryletAnswered`, `LoyaltyChanged`, and `FlagChanged` events, so a reload shows them.
- The aftermath is shown even when no team was sent, if anything waits. (Review focus 4)
- "Advance" is disabled with the words "A storylet is waiting for an answer." while anything waits; a posted advance is refused by the bus and answers 422 with those words.
- A posted answer the bus refuses answers 422 with the reason beside the panel. (Review focus 3)
- Text is rendered through `Prose`, with the situation a storylet beat implies (the storylet, the subject when there is one, the living fit crew for `best`). Where no line holds, the panel shows the storylet's id and the options' ids, so a storylet with no words is still answerable.
- The parity test's web captain answers storylets as `AutoCaptain` does, and the browser's event log still equals the simulator's.
- The editor: `TextFile.All` gains `new("storylets", "storylets/base.yaml", "Storylets", [...])` with a note that the form comes later and the file is checked on save; a missing file opens empty and is created on the first save. The page lists two kinds of warning under the text, neither blocking a save: "Nothing sets {flag}, which {storylet} requires." and "Nothing reads {flag}, which {encounter} sets."

- [ ] **Step 1:** Tests in `StoryletBridgeTests` (through `TestApp`, on a scratch copy of the fixture, with a seed or a forced state in which `ENCOUNTER_01` ends in a partial):

```csharp
[Fact] public async Task A_waiting_storylet_is_shown_with_where_it_came_from_and_a_button_for_each_option()
[Fact] public async Task The_section_is_absent_when_nothing_waits_and_nothing_was_settled()
[Fact] public async Task Advance_is_disabled_and_a_posted_advance_is_refused_while_one_waits()
[Fact] public async Task Answering_settles_the_panel_with_tags_and_frees_advance()
[Fact] public async Task A_reload_after_answering_still_shows_the_settled_panel()
[Fact] public async Task A_second_answer_and_an_unknown_option_answer_422_and_change_nothing()   // focus 3
[Fact] public async Task A_storylet_waits_on_the_aftermath_of_a_cycle_with_no_team()             // focus 4
[Fact] public async Task A_storylet_with_no_words_shows_its_ids_and_can_be_answered()
```

`ContentTextFilesTests`: the storylets file opens, a refused save shows the loader's reason and writes nothing, and both warnings appear. `ParityTests`: unchanged in what it asserts, passing with storylets in the fixture.
- [ ] **Step 2:** Run them. Expected: FAIL.
- [ ] **Step 3:** Implement. Reuse the call panel's markup classes; add no colours.
- [ ] **Step 4:** In headless Firefox with scripting on, at 400 and 1280 pixels wide: play a cycle of the fixture to a waiting storylet, answer it, reload, advance. Nothing scrolls sideways and nothing scrolls inside a panel. Add the walk to `tests/browser/play.sh` (`bash -n` only).
- [ ] **Step 5:** `./check.sh`; report `ContentSpeedTests` run alone. Commit: "Show waiting storylets in the aftermath with their answers, and open the storylets file in the editor".

### Task 4: the game's two storylets and the record

**Files:** `content/storylets/base.yaml` (new), `content/encounters/base.yaml`, `docs/gdd.md`, `GLOSSARY.md`, `docs/plans/phase-1-outline.md`, `README.md`, `wayfinder/map.md`, `tools/gdd-page/template.html` (version line only).

- [ ] **Step 1:** Write the two storylets of design section 4.8 in the setting the pack already has (read `content/encounters/base.yaml` and `content/crew/starting.yaml` for its voice: clipped, a ship's log, speech by role). Flags: `sensors.quirk` (a stage on the ship: 1 new, 2 let ride, 0 fixed), `shaken` (a count on a crew member), `shaken.heard` (a stage on them: 1 heard, 2 turned away). Each storylet has a script of three to six entries with a report sentence, two options with labels of a few words, and a line of text for each option. "Often a choice between two bad options": neither answer is free.
- [ ] **Step 2:** Set the flags in `content/encounters/base.yaml`: the Debris Shoal's careful approach on partial and on failure (each the tier's default plus the flag), and the Collapsed Gallery's first approach on disaster (the default plus the flag on the actor). Change nothing else in the file.
- [ ] **Step 3:** `dotnet run --project src/SimRunner -- cycles --legacies 20 --cycles 50 --seed 1 --content content`: both storylets are offered at least once across the run (if one never is, say so in the report; do not change difficulty to force it). Play one to its answer in headless Firefox.
- [ ] **Step 4:** The record. GDD: status line to v1.16; 3.5 (an effect can change a flag) and 3.7 (the storylet's YAML as built, in placeholders, noting what part three replaces); 3.8 (every flag is a number; a counter is added to and a stage is set, and one flag is never both); 5.1 (what part one's selection is: requirements, stakes order, the per-cycle limit); 7.1 (`Sim.Storylets` as built); 7.5 (the aftermath's "Matters aboard"); 11.1 (part one done, with the date; part two next); section 15, one new item for the numeric-flag decision and the thin example. Glossary: **Captain storylet** becomes "A storylet that arrives and requires a choice." and **Story flag** gains the counter and stage sentence. Outline, README, map: one line each. Bump the page template's version line to v1.16.
- [ ] **Step 5:** `./check.sh`, then commit: "Give the game two storylets that grow out of its encounters, and record slice 1e's first part".
