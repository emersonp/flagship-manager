# Slice 1e, part three (traits that bite): implementation plan

> **For agentic workers:** steps use checkbox (`- [ ]`) syntax for tracking. Do the tasks in order; each ends with `./check.sh` and a commit. Keep the design's section 5 (architecture) current as you go: when the build differs, change section 5 in the same commit, and say so in the design's "As built" section at the end.

**Goal:** story flags become traits, defined in `content/traits/`, owned by a new module `Sim.Traits`, able to sit on a pair of crew, and able to bite on missions (approach requirements, check bonuses, launch rules), with the briefing showing what launching will do; the simulator answers by `first` or `random` and reports fire counts; the game's pack gets the worked example and `avoids`.

**Architecture:** `Sim.Traits` (order 250) owns trait state and every definition rule, serves `ITraitView`, and handles `ChangeTrait` and `SetTrait`. Missions read the view only at launch, into a snapshot on `TeamMember` and `Team`. Storylets read the view and send `ChangeTrait`. Contracts live in `Sim.Core`.

**Spec:** `docs/plans/2026-11-17-phase-1e-part-three-design.md`. Read it with this plan. **Design system:** `docs/design/bridge-design.md`.

## Global constraints

Part one's (`2026-11-16-phase-1e-part-one-plan.md`, "Global constraints") hold. `SchemaVersion` is 10 and `CurrentFormatVersion` is 8 today; Task 1 raises them to 11 and 9, once for the whole part.

## Review focus

1. **The rename changes nothing else.** After Task 1 the fixture's simulation plays the same choices and the same numbers; only event names and the module boundary move. (Task 1)
2. **A trait never goes below 0 or above its max,** a trait with named levels is never added to by anything, and a change that changes nothing publishes nothing. (Tasks 2 and 3)
3. **A death on the mission.** The dead's traits go; a pair whose target died runs `onTargetDied` once, then is dropped; a pair whose holder died is dropped with no rule; pair storylets and vignettes for either are withdrawn. (Task 3)
4. **Launch.** Bonuses are fixed from the traits before the launch rules run; `notSent` fires only when a mission starts; the briefing's preview and the module agree, through one function. (Tasks 4 and 5)
5. **The random policy does not touch the game's streams:** the walkthrough's hash is the same under `first` and `random`. (Task 6)

---

### Task 1: the rename, and `Sim.Traits` carved out

**Files:** `src/Sim.Core/StoryletContracts.cs` (trait contracts move to a new `src/Sim.Core/TraitContracts.cs`), `src/Sim.Core/Views.cs` (`ITraitView`), `src/Sim.Core/Content/StoryletContent.cs`, `MissionContent.cs`, `src/Sim.Core/MissionContracts.cs`; a new project `src/Sim.Traits/` (`TraitsModule.cs`, `TraitsState.cs`, `TraitView.cs`) added to `StarshipSim.slnx` and registered in `src/GameHost/Game.cs`; `src/Sim.Storylets/*`; `src/Sim.Missions/MissionResolver.cs`, `Beats.cs`, `MissionState.cs`; `src/Sim.Content/ContentLoader.Missions.cs`, `ContentLoader.Storylets.cs`, `ContentWriter.cs` if it writes flag effects; `src/Web/Bridge/*`, `src/Web/Endpoints/DevPlayEndpoints.cs`, `src/Web/Forms/TextFiles.cs`, `src/Web/Components/DevPanel.razor`; `src/Sim.Narrative` and `src/Web/Bridge/Prose.cs` where `x.flag.y` facts are built; both packs' `encounters`, `storylets`, `vignettes`, and `lines` YAML; both persistence version files; the tests listed by `grep -rl "Flag" tests --include=*.cs`.

**Produces:**

```csharp
// Sim.Core
public sealed record ChangeTrait(string Trait, string? CrewId, string? TargetId, int Amount, bool Set, string Cause, string? EncounterId = null) : ICommand;
public sealed record SetTrait(string Trait, string? CrewId, string? TargetId, int Value) : ICommand { public const int Most = 99; }
[EventType("TraitChanged", 1)] public sealed record TraitChanged(string Trait, string? CrewId, string? TargetId, int From, int To, string Cause, string? EncounterId) : IEvent;
[EventType("TraitSetByDeveloper", 1)] public sealed record TraitSetByDeveloper(string Trait, string? CrewId, string? TargetId, int Value) : IEvent;
public static class TraitCauses { Mission, Answer, Vignette, Launch, Weekly, Expired, Death, Developer }   // lower-case strings
public interface ITraitView { int Value(...); IReadOnlyList<TraitHeld> Held(string? crewId); string? EncounterId(...); }   // Bonuses and Preview arrive in Task 4
// Sim.Core.Content: FlagRange -> TraitRange, FlagChange -> TraitChange (gains string? Toward = null, used from Task 3),
//   StoryletEffect.Flag -> Trait, FlagIds -> TraitOn, StoryletDef.Flags -> Traits. MissionFlag -> MissionTrait (gains string? TargetId).
// FlagChanged, FlagSetByDeveloper, SetFlag are deleted.
```

- `Sim.Traits` takes over `StoredFlag` (as `StoredTrait`, with `TargetId` and `ChangedAtWeek` in place of `SetAtWeek`), `Change`, `OnSet`, `CheckSet`, the mission's flags on `MissionEnded`, and dropping on `CrewDied`. `Sim.Storylets` keeps offers, answers, vignettes, and the last-cycle record; its `Apply` sends `ChangeTrait` with cause `answer` or `vignette`; "where it came from" reads `ITraitView.EncounterId`.
- YAML key `flag:` becomes `trait:` everywhere; the loader refuses `flag:` with "flag is now trait (GDD section 15, item 40)." Text tokens `x.flag.y` become `x.trait.y`; the narrative's fact names follow.
- Screens and log tags say "trait" where they said "flag".
- Raise `SchemaVersion` to 11 and `CurrentFormatVersion` to 9.

- [ ] **Step 1:** Rename the tests first (they are the spec for "nothing else changed"), then add `TraitsModuleTests` with the storylets module's flag tests moved across, and `Loader_refuses_the_old_flag_key`.
- [ ] **Step 2:** Run them. Expected: compile failures.
- [ ] **Step 3:** Implement. Rename in code; carve the module; convert both packs.
- [ ] **Step 4:** `dotnet test`. The simulation's golden hash moves (event types renamed, a cause added): confirm the choices and outcomes are unchanged by comparing `SimRunner cycles` figures before and after, then re-pin with "flags renamed traits, in their own module (1e part three, Task 1)". The walkthrough's moves only if a fixture mission changes a flag; re-pin with the same reason.
- [ ] **Step 5:** `./check.sh`, then commit: "Rename story flags to traits and give them a module of their own".

### Task 2: definitions

**Files:** `src/Sim.Core/Content/TraitContent.cs` (new), `ContentPack.cs`; `src/Sim.Content/ContentLoader.Traits.cs` (new), `ContentLoader.cs` (load order: traits before encounters, storylets, vignettes), `ContentLoader.Missions.cs`, `ContentLoader.Storylets.cs`; `src/Sim.Traits/*`; `src/Web/ContentKinds.cs` (traits file on the index as text), `src/Web/Forms/TextFiles.cs` (lint); `content/traits/base.yaml` and `tests/fixtures/content/traits/base.yaml` (new); tests beside each.

**Produces:**

```csharp
public sealed record TraitDef(string Id, string? Name, bool Shown, string? On, IReadOnlyDictionary<int, string> Levels, int? Max,
    string? Briefing, TraitLaunch? Launch, TraitMission? Mission, TraitStep? Every, int? ExpiresWeeks, IReadOnlyList<TraitRuleEffect> OnTargetDied);
public sealed record TraitStep(int Weeks, int Add);
public sealed record TraitLaunch(IReadOnlyList<TraitRuleEffect> Sent, IReadOnlyList<TraitRuleEffect> NotSent, IReadOnlyList<TraitRuleEffect> WithTarget);
public sealed record TraitMission(int CheckBonus, bool WithTarget);
public sealed record TraitRuleEffect(string? Trait, int Amount, bool Set, int Loyalty, int Morale, string On);   // Trait null: this trait; On: holder, target, crew
// TraitRange gains nothing: `is: name` resolves to Min = Max = the level's number at load.
```

- Loader: every rule of design 4.3, each with a message naming the trait and key. Level names resolve to numbers everywhere they are used (effects' `set`, requirements' `is`), with the "has no level named" message. An `add` on a trait with levels is refused wherever it is written. A use of a defined trait with an `on` its definition does not allow is refused. Launch rules and `mission` are loaded and checked here but do nothing until Task 4; `withTarget` and `onTargetDied` until Task 3.
- Module: every change clamps to 0 and `max`. On `WeekAdvanced`, for each stored trait in storage order: if its definition has `expires` and `week - ChangedAtWeek >= weeks`, set to 0 (cause `expired`); else if it has `every` and `week - ChangedAtWeek >= weeks`, change by `add` (cause `weekly`), which restarts the clock.
- `SetTrait` refuses a value that is not 0 or a named level on a trait with levels, and a value above `max`.
- Lint: "Nothing sets {trait}" counts launch rules (Task 4 makes them fire, but they are setters from now); new "{trait} is defined and never used".
- Storylet "no hidden memory": unchanged in kind, but with levels resolved and `max` known, an option that sets past `max` is clamped before the check.

- [ ] **Step 1:** Tests. `TraitContentTests`: one per loader message; both packs load. `TraitsModuleTests`: `A_change_is_clamped_to_max`, `A_trait_with_levels_cannot_be_added_to` (focus 2), `Every_steps_down_after_the_weeks_and_restarts_its_clock`, `A_rising_step_stops_at_max`, `Expires_sets_to_zero_after_the_weeks_unchanged`, `A_change_restarts_both_clocks`, `Nothing_is_published_when_nothing_changes`. `TextFilesTests` (or the lint's tests): both findings.
- [ ] **Step 2:** Run them. Expected: FAIL.
- [ ] **Step 3:** Implement. Give the fixture `trait_a` (levels, launch rules, expires), `trait_b` (falling step), `trait_c` (rising step with max). Give the game's pack definitions for `shaken`, `shaken_heard`, `stood_down`, and `sensors_quirk` (levels 1 noticed, 2 let_ride), as in design 4.12.
- [ ] **Step 4:** `dotnet test`. The simulation's hash moves (weekly steps): re-pin with "traits step and expire each week (1e part three, Task 2)".
- [ ] **Step 5:** `./check.sh`, then commit: "Define traits in content, with levels, a cap, a weekly step, and expiry".

### Task 3: pairs

**Files:** `src/Sim.Core/Content/MissionContent.cs`, `StoryletContent.cs`; `src/Sim.Content/ContentLoader.Missions.cs`, `ContentLoader.Storylets.cs`, `ContentLoader.Traits.cs`; `src/Sim.Missions/MissionResolver.cs`; `src/Sim.Traits/*`; `src/Sim.Storylets/StoryletsModule.cs`, `StoryletsState.cs` (waiting offers gain `TargetId`); `src/Sim.Core/StoryletContracts.cs` (offer events gain `string? TargetId = null`, last); `src/Web/Bridge/*` and `StoryletPanel.razor`, `VignettePanel.razor` (heads); fixture content; tests beside each.

- Encounter effects: `toward: lead|actor` with `on: actor|lead` (design 4.5); the resolver emits a `MissionTrait` with `TargetId`, or nothing when actor and lead are one.
- Storylets and vignettes: `scope: pair`; requirements' `of: target`; effects' `on: target`; `target` speaker; `toward: target` on a pair storylet's own effects. Offers carry `TargetId`; offer ids become `{cycle}:{id}:{subject}:{target}` for pairs.
- Deaths (focus 3): on `CrewDied`, the traits module runs each pair's `onTargetDied` where the dead is the target (cause `death`), then drops every trait on or pointing at the dead (cause `death`). The storylets module withdraws offers whose subject or target died.
- Fixture: `trait_d` (`avoids` shape), `trait_e` (`owes` shape), `trait_f` (`looks_up_to` shape) and `trait_g` (the `grieving` they set), one encounter outcome with `toward`, one pair storylet, one pair vignette.

- [ ] **Step 1:** Tests. Content: each pair message. Missions: `Toward_makes_a_pair_from_actor_to_lead`, `No_pair_when_actor_is_lead`. Traits: `A_pair_is_stored_with_its_target`, `The_targets_death_runs_onTargetDied_then_drops_the_pair`, `The_holders_death_drops_the_pair_with_no_rule` (focus 3). Storylets: `A_pair_storylet_arrives_once_for_each_pair`, `Of_target_reads_the_targets_stats_and_last_cycle`, `On_target_changes_the_target`, `A_pair_offer_is_withdrawn_when_either_dies`. Bridge: the head reads "CREW_01 · avoids CREW_03".
- [ ] **Step 2:** Run them. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** `dotnet test`. The simulation's hash moves (pairs in the fixture): re-pin with "pair traits (1e part three, Task 3)".
- [ ] **Step 5:** `./check.sh`, then commit: "Let a trait sit on a pair of crew, made by an encounter and read by storylets and vignettes".

### Task 4: traits on missions

**Files:** `src/Sim.Core/Team.cs`, `Views.cs`; `src/Sim.Core/Content/MissionContent.cs` (`RequirementDef` gains a trait kind); `src/Sim.Content/ContentLoader.Missions.cs`; `src/Sim.Missions/MissionsModule.cs` (snapshot at launch), `MissionResolver.cs` (requirements, bonuses), `Beats.cs` (`CheckBeat` gains `IReadOnlyList<TraitBonus>? TraitBonuses = null`, last); `src/Sim.Traits/*` (launch rules, `Bonuses`, `Preview`); `src/Web/Bridge/BeatLines.cs`; `src/MissionBench/*` where a `TeamMember` is built; tests beside each.

**Produces:**

```csharp
public sealed record TraitBonus(string Trait, int Amount);
public sealed record TeamMember(..., int MoraleBonus = 0, IReadOnlyDictionary<string, int>? Traits = null, IReadOnlyList<TraitBonus>? TraitBonuses = null);
public sealed record Team(IReadOnlyList<TeamMember> Members, string LeadId, IReadOnlyDictionary<string, int>? ShipTraits = null);
// ITraitView gains:
IReadOnlyList<TraitBonus> Bonuses(string crewId, IReadOnlyCollection<string> team);
IReadOnlyList<LaunchPreview> Preview(IReadOnlyList<string> roster);
public sealed record LaunchPreview(string Trait, string HolderId, string? TargetId, string When, IReadOnlyList<PreviewEffect> Effects);   // When: sent, notSent, withTarget
```

- Approach requirement `{ trait, min, max, is, on: ship|lead|anyone }`; the resolver evaluates it from the snapshot. "anyone" reads members standing.
- The missions module builds the snapshot from `ITraitView` when the mission starts, before it publishes `MissionStarted` (focus 4: bonuses from the traits before the launch rules).
- The traits module runs launch rules on `MissionStarted`: holders in roster order, each holder's traits in definition order; `sent`, `notSent` (alive, fit or not, and not on the team), `withTarget`. Cause `launch`. Rule effects send `AdjustLoyalty`/`AdjustMorale`.
- `Preview` is the function the rules run from: the module applies what `Preview` returns for the actual team, so the briefing and the outcome cannot disagree (focus 4).
- `BeatLines`: a check's tags gain each trait bonus ("avoids −1").

- [ ] **Step 1:** Tests. Missions: `A_trait_requirement_on_the_ship_closes_an_approach`, `Anyone_reads_members_standing`, `A_trait_bonus_is_added_to_every_check_and_the_beat_names_it`, `WithTarget_bonus_only_when_both_are_on_the_team`, `Bonuses_are_fixed_before_the_launch_rules_run`. Traits: `Sent_and_notSent_fire_at_launch`, `NotSent_does_not_fire_when_nobody_is_sent`, `WithTarget_fires_when_both_are_sent`, `The_rules_apply_what_Preview_says`. Bench: teams still build.
- [ ] **Step 2:** Run them. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** `dotnet test`. The walkthrough's hash moves (a check's beat gained a field): re-pin with "a check's beat carries trait bonuses (1e part three, Task 4)". The simulation's moves: "launch rules and trait bonuses".
- [ ] **Step 5:** `./check.sh`, then commit: "Let traits bite on missions: approach requirements, check bonuses, and rules at launch".

### Task 5: the screens

**Files:** `src/Web/Bridge/BridgeView.cs`, `BridgeReader.cs`, `DevView.cs`, `Names.cs` (trait names and level names); `src/Web/Components/BriefingScreen.razor`, `Roster.razor`, `DevPanel.razor`, `StoryletPanel.razor`, `VignettePanel.razor`; `src/Web/Endpoints/DevPlayEndpoints.cs`; `src/Web/wwwroot` styles if needed (chips follow `docs/design/` recipes); `tests/Web.Tests/*`.

- Briefing (design 4.7): chips beside picked crew; above Launch, the preview rows from `ITraitView.Preview`, each rendered with its condition as data attributes and shown by the page's existing pick signals when the picks meet it. No blocking, no dialog.
- Roster: a "Traits" cell of shown-trait chips. Mark it in code with a comment and in the component: "Interim until the crew file (1e part five); remove then." The GDD 7.5 already says so.
- Dev Play: every trait, hidden and pairs; set any trait, choosing a target for a pair trait and a level for a trait with levels.
- Panels: pair heads; trait tags say the level's name where there is one ("stood_down promised to broken").

- [ ] **Step 1:** Tests. `BriefingTests`: `Chips_show_for_traits_with_a_briefing_line`, `The_preview_lists_what_launching_would_do_for_each_condition`, `A_pair_chip_needs_both_picked`. `RosterTests`: `Shown_traits_appear_and_hidden_do_not`. `DevPlayTests`: setting a pair and a level; a refused value. The web parity test still holds.
- [ ] **Step 2:** Run them. Expected: FAIL.
- [ ] **Step 3:** Implement. Look at 1280 and 400 pixels, as in part two (headless Edge on a scratch copy of the content), and note what could not be checked.
- [ ] **Step 4:** `dotnet test`; no golden hash moves.
- [ ] **Step 5:** `./check.sh`, then commit: "Show traits in the briefing, with what launching will do, and on the roster until the crew file".

### Task 6: the simulator

**Files:** `src/SimRunner/AutoCaptain.cs`, `Cli.cs`, `CommandLine.cs`, `Simulation.cs`; `tests/Sim.Tests` or `tests/SimRunner.Tests` beside them; the web parity test if `NextCommand`'s signature changes.

- `--answers first|random` (default `first`). `random` hashes (legacy seed, offer id, `sim-captain`) into an index, so it draws from no game stream and needs no stored state.
- Team choice keeps promises (design 4.10): leave out holders whose `Preview` has a `sent` or `withTarget` entry with a cost, unless the objective's smallest team cannot then be fielded; add them back in roster order.
- Report: fire counts per 100 cycles for each storylet, vignette, and option; times each trait was set from 0; "Never fired" for any storylet, vignette, or option at 0. A warning only.

- [ ] **Step 1:** Tests. `Random_answers_are_reproducible`, `The_walkthrough_hash_is_the_same_under_either_policy` (focus 5), `The_captain_keeps_a_promise_when_it_can`, `The_captain_breaks_it_rather_than_send_nobody`, `Never_fired_lists_what_did_not_fire`.
- [ ] **Step 2:** Run them. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** `dotnet test`. The simulation's hash moves only if the default team choice changed (promises): re-pin with "the simulator's captain keeps promises (1e part three, Task 6)".
- [ ] **Step 5:** `./check.sh`, then commit: "Let the simulator's captain answer at random and keep promises, and report what fired".

### Task 7: the game's pack, and the record

**Files:** `content/traits/base.yaml`, `content/storylets/base.yaml`, `content/vignettes/base.yaml`, `content/encounters/base.yaml`; the docs below.

- [ ] **Step 1:** Content, in generated words (design 4.12): the three answers of "Someone shaken"; "You gave your word"; `avoids` with its encounter effect, pair vignette, and "Clear the air"; `sensors_quirk`'s approach requirement. Run `SimRunner cycles` (20 legacies x 50 cycles, seed 1) under `first` and `random`, and read the fire counts. Do not change difficulty to force a storylet to fire; say what did not.
- [ ] **Step 2:** `./check.sh`; play the worked example in the browser through Dev Play (set `shaken`, answer, send them anyway, see the storylet), and the `avoids` pair.
- [ ] **Step 3:** The record.
  - GDD: status line to v1.20. 3.2, 3.5 (`toward`, the trait requirement on approaches), 3.7, and 3.8 move from "designed" to "as built", with the rename carried through their as-built text (`flag` to `trait`, `quality view` to `state view`); 5.1; 7.1 and 7.5 as built; 11.1 (part three done, with the date; part four next).
  - Glossary: drop "Story flag"'s interim sentence to a pointer.
  - Conventions: rewrite "Traits" in the present tense.
  - Design: an "As built" section with differences, the simulator's figures, and what the author should judge in play. Keep section 5 true.
  - Outline, README (line 29: flags become traits), map: one line each.
  - Rebuild and republish the GDD page (`tools/gdd-page/README.md`) to `https://claude.ai/artifact/9CKdbu7bhV7UvriU9jzWHW`.
- [ ] **Step 4:** Commit: "Give the game the stood-down promise and a pair who avoid each other, and record slice 1e's third part".
