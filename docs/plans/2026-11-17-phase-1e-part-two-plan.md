# Slice 1e, part two (morale, vignettes, and the captain's itinerary): implementation plan

> **For agentic workers:** steps use checkbox (`- [ ]`) syntax for tracking. Do the tasks in order; each ends with `./check.sh` and a commit.

**Goal:** crew have morale that moves and changes their checks; vignettes arrive on flags, stats, and facts about the last cycle; a new first phase of the cycle, the captain's itinerary, holds the storylets and the vignettes.

**Architecture:** morale is crew state, changed by the crew module on mission and week events and by an `AdjustMorale` command; missions see it only as a `MoraleBonus` on `TeamMember`. Vignettes are a second kind in `Sim.Storylets`, sharing the requirement and effect code. `CyclePhase` gains `Itinerary` as its first value; the storylets module offers on entering it and holds `AdvancePhase` there. The bridge draws the itinerary from stored state.

**Spec:** `docs/plans/2026-11-17-phase-1e-part-two-design.md`. Read it with this plan. **Design system:** `docs/design/bridge-design.md`.

## Global constraints

Part one's (`2026-11-16-phase-1e-part-one-plan.md`, "Global constraints") hold, with these figures: `SchemaVersion` is 8 and `CurrentFormatVersion` is 6 today.

## Review focus

1. **A death on the mission.** The dead get no morale change; the living each get `crewmateDied` once per death; `lost_crewmate` holds for each of the living and not for the dead. (Tasks 1 and 2)
2. **Drift never crosses resting,** and a member above resting who rests drifts down after resting up, in that order. (Task 1)
3. **A vignette in its cooldown** does not arrive, for that scope only; another crew member may get it the same cycle. (Task 3)
4. **Leaving the itinerary** applies each unanswered vignette's default exactly once, and an answered one's never. A reload between answering and leaving changes nothing. (Tasks 3 and 4)
5. **The first cycle** opens on an empty itinerary, and an old save is refused. (Tasks 3 and 4)

---

### Task 1: morale

**Files:** `src/Sim.Core/Content/CrewContent.cs`, `src/Sim.Content/ContentLoader.Crew.cs`, both packs' `tuning/crew.yaml`; `src/Sim.Core/Team.cs`, `src/Sim.Core/StoryletContracts.cs` (or a new `CrewContracts.cs`); `src/Sim.Crew/CrewState.cs`, `CrewModule.cs`, `RosterView.cs`; `src/Sim.Missions/Beats.cs`, `MissionResolver.cs`; `src/Web/Bridge/BeatLines.cs`; `src/MissionBench/*` where a `TeamMember` is built; both persistence version files; tests beside each.

**Produces:**

```csharp
// Sim.Core.Content: CrewTuning gains MoraleResting, MoraleMin, MoraleMax, MoraleLow, MoraleHigh, MoraleCheckBonus, MoraleDrift,
//   MoraleRested, MoraleHurt, MoraleIncapacitated, MoraleCrewmateDied, and IReadOnlyDictionary<MissionOutcome, int> MoraleResult.
// Sim.Core
public sealed record TeamMember(..., int MoraleBonus = 0);      // last, so existing builders keep compiling
public sealed record AdjustMorale(string CrewId, int Delta) : ICommand;
[EventType("MoraleChanged", 1)] public sealed record MoraleChanged(string CrewId, int From, int To, string Cause) : IEvent;
public static class MoraleCauses { Mission, Hurt, Death, Rest, Drift, Answer }   // lower-case strings
// CheckBeat gains, last: int MoraleBonus = 0
```

- Loader: `morale.*` required; `min <= low < high <= max`, `min <= resting <= max`, `drift >= 0`, `checkBonus >= 0`; each `result` key is a `MissionOutcome` in camel case and all five are present. Messages name the key.
- `CrewMember.Morale`, set to `resting` at founding. `RosterView.Member` sets `MoraleBonus` to `-checkBonus` at or below `low`, `+checkBonus` at or above `high`, else 0.
- The resolver adds the actor's `MoraleBonus` wherever it adds the gear bonus to a check's total, and records it on the `CheckBeat`. The lead's choice of approach (by skill threshold) does not use it.
- On `MissionEnded`, after the existing work: for each member who came home, `result[outcome]`; for each injured, `hurt` per light injury, or `incapacitated`; then for each `CrewDied` the mission caused, `crewmateDied` to every living member. Track who was sent in `CrewState` (`LastSentCycle` on the member) so rest can be decided.
- On `WeekAdvanced`, before healing: `rested` to each fit member whose `LastSentCycle` is not the cycle just ended; then drift by `drift` towards `resting`, never past it.
- Every change goes through one helper that clamps to min and max and publishes `MoraleChanged` only when the value moved.
- `BeatLines`: a check's tags gain `morale +1` / `morale −1` when its bonus is not 0.

- [ ] **Step 1:** Tests. `CrewContentTests`: one per loader message; the fixture loads. `CrewModuleTests`: `Morale_starts_at_resting`, `A_mission_result_moves_morale_for_those_who_came_home`, `An_injury_and_an_incapacitation_lower_morale`, `A_death_lowers_every_living_members_morale_and_not_the_deads` (focus 1), `A_member_not_sent_rests_and_one_sent_does_not`, `Drift_moves_towards_resting_and_never_past_it` (focus 2), `Morale_is_clamped`, `AdjustMorale_is_refused_for_the_dead`, `The_roster_view_gives_the_bonus_at_the_thresholds`. `MissionRulesTests`: `High_morale_adds_to_every_check_the_member_makes_and_the_beat_says_so`, `Morale_does_not_change_the_leads_choice_of_approach`. `BeatLinesTests`: both tags.
- [ ] **Step 2:** Run them. Expected: compile failures, then failures.
- [ ] **Step 3:** Implement. Raise both persistence versions (9 and 7).
- [ ] **Step 4:** `dotnet test`. The walkthrough's hash moves (a check's beat gained a field; every member starts at resting, so no total changes): confirm that, then re-pin with "a check's beat carries the morale bonus (1e part two, Task 1)". The simulation's moves (new events): re-pin with "morale changes after missions and each week (1e part two, Task 1)".
- [ ] **Step 5:** `./check.sh`, then commit: "Give each crew member morale that moves with missions, rest, and the week, and let it change their checks".

### Task 2: richer requirements, and morale as an effect

**Files:** `src/Sim.Core/Content/StoryletContent.cs`, `src/Sim.Content/ContentLoader.Storylets.cs`; `src/Sim.Core/Views.cs` (`IRosterView` gains `Morale(id)` and `Loyalty(id)`, or one `Stats(id)`); `src/Sim.Crew/RosterView.cs`; `src/Sim.Storylets/StoryletsModule.cs`, `StoryletsState.cs`; the storylets lint in `src/Web/Forms/TextFiles.cs` (or wherever it lives); tests beside each.

**Produces:**

```csharp
// Sim.Core.Content: a requirement is one of three kinds.
public abstract record Requirement;
public sealed record FlagRange(string Flag, int Min, int? Max) : Requirement;        // as now
public sealed record StatRange(string Stat, int Min, int? Max) : Requirement;        // "morale" or "loyalty"
public sealed record LastCycle(string Fact, bool Not) : Requirement;                 // sent, hurt, rested, lost_crewmate
// StoryletDef.Requires becomes IReadOnlyList<Requirement>.
// StoryletEffect gains int Morale = 0, string? MoraleOn = null ("subject" or "crew").
// Sim.Storylets: StoryletsState gains LastCycle { Cycle, Sent, Hurt, Died } (crew ids), recorded at the mission's end.
```

- Loader: `{ stat: morale|loyalty, min, max }`, `{ last: FACT, not: true }`; a stat or a fact on a ship storylet: "A storylet for the ship has no subject, so it cannot read {stat or fact}."; an unknown stat or fact names the allowed ones. The "no hidden memory" rule ignores stat and fact requirements (only a storylet's own flags can be moved by its answers; a storylet whose requirements are only stats and facts is refused: "A storylet needs at least one flag among its requirements, so its answers can stop it returning."). `{ morale: N, on: subject|crew }` is an effect for both kinds.
- Module: requirements evaluated through one `Holds(requirement, subjectId, context)`; `rested` is a fit living member not in `Sent`; `lost_crewmate` is any living member when `Died` is not empty. Morale effects go through `AdjustMorale` with cause `answer`.

- [ ] **Step 1:** Tests. `StoryletContentTests`: each new message; the three kinds load. `StoryletsModuleTests`: `A_stat_requirement_reads_the_subjects_morale`, `Each_last_cycle_fact_holds_for_the_right_crew` (focus 1), `Not_turns_a_fact_round`, `A_morale_effect_reaches_the_crew_module`.
- [ ] **Step 2:** Run them. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** `dotnet test`; no golden hash moves.
- [ ] **Step 5:** `./check.sh`, then commit: "Let a storylet require a crew member's morale or loyalty or a fact about the last cycle, and change morale".

### Task 3: the itinerary phase and vignettes, headless

**Files:** `src/Sim.Core/LegacyState.cs` (`CyclePhase`), `src/Sim.Cycle/CycleModule.cs`; `src/Sim.Core/Content/StoryletContent.cs`, `ContentPack.cs`, `MissionTuning.cs`; `src/Sim.Content/ContentLoader.Storylets.cs`, `ContentLoader.cs`, `ContentFiles.cs`, `ContentLoader.Tuning.cs`; both packs' `tuning/missions.yaml`; `tests/fixtures/content/vignettes/base.yaml` (new); `src/Sim.Core/StoryletContracts.cs`; `src/Sim.Storylets/*`; `src/SimRunner/AutoCaptain.cs`, `Simulation.cs`, `Cli.cs`; every test and caller that assumes `Orders` is a cycle's first phase; both persistence version files.

**Produces:**

```csharp
public enum CyclePhase { Itinerary, Orders, Briefing, Resolution, Aftermath, Advance }
// Sim.Core.Content
public sealed record VignetteDef(string Id, string Scope, IReadOnlyList<Requirement> Requires, IReadOnlyList<StoryletEffect> Effects,
    IReadOnlyList<StoryletOption> Options, string? Default, string? Name = null);
// ContentPack gains Vignettes, VignettesPerCycle (3), VignetteCooldown (6), CaptainsTimePerCycle (3).
// Sim.Core
public sealed record AnswerVignette(string OfferId, string OptionId) : ICommand;
[EventType("VignetteArrived", 1)]  public sealed record VignetteArrived(string OfferId, string VignetteId, string? SubjectId, string? Fact) : IEvent;
[EventType("VignetteAnswered", 1)] public sealed record VignetteAnswered(string OfferId, string VignetteId, string? SubjectId, string OptionId, bool Defaulted) : IEvent;
// Sim.Storylets: StoryletsState gains Vignettes (waiting, with options) and LastArrived { VignetteId, SubjectId, Cycle }.
```

- Cycle: a new legacy is founded into `Itinerary` of cycle 1; `Advance` goes on to `Itinerary` of the next cycle. `AdvancePhase` from `Aftermath` still passes through `Advance` (the web sends two, as now).
- Storylets are offered on entering `Itinerary` (the stale-offer withdrawal moves with them), and the waiting guard holds `Itinerary`. The aftermath no longer offers.
- Vignettes: loader rules and messages as design 4.3; tuning `vignettes: { perCycle: 3, cooldown: 6 }` and `captainsTime: { perCycle: 3 }` in `tuning/missions.yaml`, required. On entering `Itinerary`, after the storylets: arrival as design 4.3. A vignette with effects applies them and is recorded as answered at once (no waiting entry). One with options waits. `AnswerVignette` guards as `AnswerStorylet` does. On `AdvancePhase` out of `Itinerary` (a subscriber to `PhaseEntered` `Orders`, run before anything else reads the state), each waiting vignette takes its default with `Defaulted: true`. The last-cycle facts are cleared then.
- `AutoCaptain`: answers storylets as now, never vignettes. `SimRunner cycles` adds "vignettes arrived", "left to default" per 100 cycles, and the morale shares at the run's end.
- Fixture: `VIGNETTE_01` (crew, `last: rested`, effects morale +2 on subject), `VIGNETTE_02` (crew, `last: lost_crewmate`, two options, default the second), `VIGNETTE_03` (ship, `flag_a` 2, effects morale −1 on crew).

- [ ] **Step 1:** Tests. `CycleModuleTests`: `A_legacy_opens_on_the_itinerary_of_cycle_one`, `Advance_leads_to_the_next_itinerary`. `StoryletContentTests` (or new `VignetteContentTests`): each loader message. `StoryletsModuleTests`: part one's offering tests move to the itinerary; `A_vignette_with_effects_applies_them_when_it_arrives`, `A_vignette_in_its_cooldown_does_not_arrive_for_that_scope_only` (focus 3), `At_most_the_cycles_limit_of_vignettes_arrive_least_recent_first`, `Leaving_the_itinerary_applies_each_unanswered_default_once` (focus 4), `An_answered_vignette_keeps_its_answer`, `Vignettes_do_not_hold_the_itinerary`. `SimulationTests`: 50 cycles end with nothing waiting; vignettes arrive. `SqliteLegacyStoreTests` / export tests: an old version is refused (focus 5).
- [ ] **Step 2:** Run them. Expected: compile failures, then failures.
- [ ] **Step 3:** Implement. Raise both persistence versions (10 and 8).
- [ ] **Step 4:** `dotnet test`. The simulation's hash moves: re-pin with "the cycle opens on the captain's itinerary, where storylets and vignettes arrive (1e part two, Task 3)". The walkthrough's must not.
- [ ] **Step 5:** `./check.sh`; put the simulator's vignette and morale figures for the fixture in the commit body. Commit: "Open each cycle on the captain's itinerary, and add vignettes that arrive there on flags, stats, and the last cycle".

### Task 4: the itinerary on the bridge, morale on the roster, vignettes in the editor

**Files:** `src/Web/Bridge/BridgeView.cs`, `BridgeReader.cs`, `Prose.cs`, `Names.cs`, `DevView.cs`; `src/Web/Endpoints/BridgeEndpoints.cs`, `DevPlayEndpoints.cs` if it posts into a phase; `src/Web/Components/MainScreen.razor`, new `ItineraryScreen.razor`, `StoryletPanel.razor`, `AftermathScreen.razor`, `Roster.razor`, `TopBar.razor` (the phase steps), `DevPanel.razor`; `src/Web/Forms/TextFiles.cs`; `src/Web/wwwroot/css/app.css`; `tests/Web.Tests/StoryletBridgeTests.cs` (moves to the itinerary), new `ItineraryTests.cs`, `ParityTests.cs`, `WebCaptain.cs`, `ContentTextFilesTests.cs`, `DevPlayTests.cs`, `BridgeReaderTests.cs`; `tests/browser/play.sh`.

**Produces:**

```csharp
public sealed record ItineraryView(int CaptainsTime, IReadOnlyList<StoryletView> Waiting, IReadOnlyList<SettledStoryletView> Settled,
    IReadOnlyList<VignetteView> Vignettes);
public sealed record VignetteView(string OfferId, string Title, string? About, IReadOnlyList<ScriptEntryView> Script,
    IReadOnlyList<StoryletOptionView> Options, string? DefaultLabel, IReadOnlyList<string>? Tags, IReadOnlyList<ScriptEntryView>? Said);
// BridgeView gains Itinerary; AftermathView loses Waiting and Settled.
// Route: POST /legacy/{id}/vignette   fields: offer, option, and the stamp fields.
```

- The itinerary screen as design 4.4: head "Captain's itinerary" with the week and a sentence on what waits; a readout "Captain's time" with its figure and the note "Not spent yet"; a section "Storylets" and a section "Vignettes", each absent when empty; "Nothing needs the captain this week." when both are; the button "To orders", blocked with the waiting sentence while a storylet waits.
- A vignette panel: name; the subject and the fact in words; the script; either its tags (no choice), or two buttons and "Left alone: {default label}", or, once answered, the option's text and tags. Answered through the new route; a refusal answers 422 beside the panel.
- The aftermath loses "Matters aboard"; its crew changes gain each member's morale change from the mission ("Morale −6").
- The roster gains "Morale" ("Low", "Steady", "High") before "Loyalty". Dev Play's panel gains a table of each crew member's morale figure.
- The phase steps in the top bar gain "Itinerary" first.
- The editor's index gains the vignettes file as a text file, checked on save.
- The web captain answers storylets as `AutoCaptain` does and leaves vignettes; the parity test still holds.

- [ ] **Step 1:** Tests. `ItineraryTests` (through `TestApp`, on a scratch copy of the fixture): `A_new_legacy_opens_on_an_empty_itinerary_with_captains_time` (focus 5), `A_waiting_storylet_is_shown_and_blocks_to_orders`, `Storylets_and_vignettes_can_be_answered_in_any_order`, `A_vignette_with_effects_shows_its_tags`, `Leaving_shows_the_defaults_in_the_next_cycles_log_and_applies_them_once` (focus 4), `A_reload_after_answering_a_vignette_shows_it_settled`, `An_answer_to_a_settled_vignette_answers_422`. `BridgeReaderTests`: the roster's morale word at each threshold; the aftermath's morale changes. `ContentTextFilesTests`: the vignettes file opens and a refused save shows the loader's reason. Move part one's storylet bridge tests to the itinerary.
- [ ] **Step 2:** Run them. Expected: FAIL.
- [ ] **Step 3:** Implement. Reuse the storylet panel's classes; add no colours.
- [ ] **Step 4:** In headless Firefox at 400 and 1280 pixels wide: play a cycle to an itinerary with a storylet and a vignette, answer both in reverse order, reload, go to orders. Nothing scrolls sideways or inside a panel. Add the walk to `tests/browser/play.sh` (`bash -n` only).
- [ ] **Step 5:** `./check.sh`; report `ContentSpeedTests` run alone. Commit: "Show the captain's itinerary with storylets, vignettes, and the captain's time, and morale on the roster".

### Task 5: the game's five vignettes and the record

**Files:** `content/vignettes/base.yaml` (new), `docs/gdd/03-data-model.md`, `05-storylets.md`, `07-architecture.md`, `11-roadmap.md`, `15-decisions.md`, `docs/gdd.md` (status line), `GLOSSARY.md`, `docs/plans/phase-1-outline.md`, `README.md`, `wayfinder/map.md`, `tools/gdd-page/template.html` (version line only).

- [ ] **Step 1:** Write the five vignettes of design section 4.7, in the voice of `content/storylets/base.yaml`.
- [ ] **Step 2:** `dotnet run --project src/SimRunner -- cycles --legacies 20 --cycles 50 --seed 1 --content content`: every vignette arrives at least once (if one never does, say so; do not change difficulty to force it); report the morale shares.
- [ ] **Step 3:** The record. GDD: status line to v1.18; 3.5's crew sample gains morale as built; 3.7 (vignettes as built, the new requirement kinds); 5.1 (the itinerary replaces the aftermath as where storylets arrive; vignette arrival and cooldown); 5.2 (a vignette's shape as built); 7.1 (`Sim.Crew` owns morale; `Sim.Storylets` owns vignettes); 7.5 (the itinerary screen); 11.1 (part two done, with the date; part three next); section 15, one new item recording the author's 2026-11-17 answers. Glossary: **Morale**, **Captain's itinerary**, and **Vignette** as built. Outline, README, map: one line each. Bump the page template's version line, rebuild and republish the GDD page (`tools/gdd-page/README.md`).
- [ ] **Step 4:** `./check.sh`, then commit: "Give the game five vignettes, and record slice 1e's second part".
