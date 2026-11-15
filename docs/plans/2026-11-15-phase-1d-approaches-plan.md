# Authored approaches, "initiative", and the briefing's crew card: implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** an encounter holds one to four approaches the author defines, the lead picks among the available ones by skill, every outcome has a defined effect, "notice" becomes "initiative", and the briefing's away team card never scrolls inside itself.

**Architecture:** the change is made in four moves that each leave the gate green. The word is renamed first. The content takes its new shape under the *old* rules, so the golden hashes prove the conversion changed nothing. Then the rules change (availability, choice by skill, authored outcomes, no risk tolerance) and the hashes are re-pinned once, on purpose. The encounter form is laid out last, over field names that were final from the shape task.

**Tech stack:** .NET (see `global.json`), xUnit, Blazor static components (`.razor`) with Datastar 1.0.5 (`docs/research/datastar-1.0.5.md`), YAML content, SQLite store.

**Spec:** `docs/plans/2026-11-15-phase-1d-approaches-design.md`. Read it with this plan. **Design system:** `docs/design/bridge-design.md`.

## Global constraints

- Braces on every body. Comments are terse and say why. Plain interface words. Tokens for every colour. Every page works at 400 pixels wide with no page scroll.
- Test first: each task's tests are written and seen to fail before its code.
- `src/Sim.*` stays pure (the architecture test enforces it). Narrative draws no randomness.
- `tests/fixtures/content/` holds placeholders only (`APPROACH_A_NAME` in the style already there). `content/` gains no new fiction: converted approaches are named plainly "Careful" and "Fast" for the author to rename.
- The vocabulary follows `GLOSSARY.md`. After Task 1 the word "notice" names nothing in the game. It remains correct for the editor's page alerts (`TextFileAlert`, `FormFlow`, `KindPage`, `KindList`, `FormShell`, `ItemFormView`, `TextFiles`, the content endpoints): leave those.
- A task that changes what the store holds raises `SchemaVersion` in `src/Persistence/SqliteLegacyStore.cs` by one. The store's existing refusal of a database from another build is the "old saves start fresh" of the design: the author deletes `starship-sim.db`. No conversion is written.
- Golden hashes (`tests/Sim.Tests/MissionWalkthroughTests.cs`, `tests/SimRunner.Tests/SimulationTests.cs`) move only where a task says so, with a comment saying why.
- `AGENTS.md` and `global.json` have the author's uncommitted changes. Never stage them: every commit names its paths. If `docs/personal_todo.md` changes, commit it alone, as `AGENTS.md` says.
- Each task ends with `./check.sh` green and one commit, on a branch (not `main`).

## Review focus

Conditions the design implies and a player or the author will meet. Each has a test in the task named.

1. **The lead has no level in any approach's skill, or equal levels.** The first listed available approach is taken. (Task 4)
2. **The lead changes mid-mission.** The next check is chosen by the new lead's skills, after their injuries. (Task 4)
3. **An outcome written as "nothing happens".** `outcomes: { partial: [] }` means no effect, not the default; it survives load, write, and a save from the form. (Tasks 4 and 5)
4. **The author removes or renames an approach that the encounter's own lines test.** The save is refused with a problem on that line; nothing throws. (Task 3)
5. **A posted form with five approaches, none, or two with one id.** Refused with a problem on the form, whatever the page's buttons allow. (Task 3)

---

### Task 1: "notice" becomes "initiative"

**Files:** every game-term use found by `grep -rIn -i notice src tests content tools GLOSSARY.md README.md wayfinder docs/gdd.md docs/plans/phase-1-outline.md` (skip `obj/`, the font licences, and the editor alerts listed above). Earlier plans and designs in `docs/plans/` are history and stay as written.

**Produces (names later tasks use):**
- `InitiativeBeat(string DecisionId, int Stakes, double Threshold, string LeadId, CallAnswer Chose, bool Blocked)`, JSON discriminator `"initiative"`.
- `MissionResult(MissionOutcome Outcome, int Progress, int Need, int Calls, int Initiatives, IReadOnlyList<CrewResult> Crew)`; `MissionState.Initiatives`; `BenchFigures.Initiatives`.
- The beat kind and the decision text key are `initiative` (`NarrativeFacts.DecisionTexts = ["call", "initiative", "bold", "safe"]`); the shared lines `LINE_NOTICE` become `LINE_INITIATIVE` in both packs.
- Interface words: "Initiatives per mission" (bench), "The lead decides alone" stays as the editor's label for the box.

- [ ] **Step 1:** Rename in tests first (`MissionRulesTests`, `MissionWalkthroughTests`, `NarrativeContentTests`, `ContentFaithfulnessTests`, `ContentEditsTests`, `BeatLinesTests`, `ProseTests`, `PhaseBoundTests`, `ContentFormsTests`, `ContentTextFilesTests`, `tests/browser/edit.sh`) and add to `NarrativeContentTests`:

```csharp
[Fact]
public void A_decision_text_keyed_notice_is_refused()
{
    // The old key must fail loudly, not be dropped: an author's words would vanish otherwise.
    ContentResult result = TestContent.LoadWithEncounterDecisionText("notice", "TEXT");
    Assert.Contains(result.Errors, e => e.Message.Contains("notice"));
}
```

Use the helper the neighbouring tests use to load a pack with one changed text; add `LoadWithEncounterDecisionText` beside it only if none fits.

- [ ] **Step 2:** `dotnet build --nologo -v quiet`. Expected: it fails to compile on the renamed members.
- [ ] **Step 3:** Rename in `src/`, both packs' YAML, and `GLOSSARY.md` (entry **Initiative**: "A decision the team lead made alone and reported afterwards. _Avoid_: Notice, FYI, report-after"; fix the **Team lead** and **Decision** entries). Rename GDD section 4.4 to "Decisions: call or initiative" and each use in sections 2.2, 6, 6.1, 9, 10, and 11. Raise `SchemaVersion` to 4.
- [ ] **Step 4:** `./check.sh`. If a golden hash moved because the fingerprint includes the beat's kind word, re-pin it with the comment "the notice beat is named initiative". Expected: green.
- [ ] **Step 5:** `grep -rIn -i notice src tests content GLOSSARY.md README.md wayfinder --exclude-dir=obj --exclude-dir=bin` shows only the editor alerts, the licences, the glossary's _Avoid_, and the test from Step 1.
- [ ] **Step 6:** Commit: "Call a decision the lead makes alone an initiative, not a notice".

### Task 2: the away team card does not scroll inside itself

**Files:** `src/Web/Components/BriefingScreen.razor:46-82`, `src/Web/wwwroot/css/app.css` (`.tw` at 186, `.pick` at 226-230 and 382-389), `tests/Web.Tests/BriefingTests.cs`, `tests/browser/play.sh`.

- [ ] **Step 1: See it.** Run the game (`./run.sh`), open a briefing with the full starting roster, and look at the away team card at 400, 768, 1280, and 1920 pixels wide, using the browser tooling `tests/browser/play.sh` uses. Write down in the commit message which widths scroll and in which direction. If the scroll is vertical or comes from somewhere other than `.tw`, fix that cause instead and keep the test below.
- [ ] **Step 2:** Add to `BriefingTests`:

```csharp
[Fact]
public async Task The_away_team_table_has_no_scrolling_wrapper()
{
    string html = await Briefing();   // the helper the other tests in this file use to fetch a briefing
    string card = Section(html, "h-crew");
    Assert.DoesNotContain("class=\"tw\"", card);
    Assert.Contains("class=\"pick pick--crew\"", card);
}
```

- [ ] **Step 3:** Run it. Expected: FAIL (the wrapper is there).
- [ ] **Step 4:** Remove the `.tw` wrapper from the away team table and give the table the class `pick pick--crew`. Each skill cell gets `data-label="@SkillName(skill)"`. In `app.css`: at widths where the columns fit, `.pick--crew` is a table with `table-layout: auto` and tightened cell padding; below the breakpoint found in Step 1, each row is a grid of three areas: name and status on the first line, the shown skills on the second as "label value" pairs from `data-label` (the header row is visually hidden, not removed), and the Team and Lead boxes at the right across both lines with their 44 pixel targets kept. Drop the sticky-column rules at 382-389 for `.pick--crew` only; the objective table keeps `.tw` and its rules.
- [ ] **Step 5:** Repeat Step 1's four widths. Expected: no scrollbar on the card and none on the page; every skill, the Team box, and the Lead box are visible for every crew member. Add the 400 pixel case to `tests/browser/play.sh` as an assertion that the card's `scrollWidth` equals its `clientWidth`.
- [ ] **Step 6:** `./check.sh`, then commit: "Fit the away team table to its card at every width".

### Task 3: approaches as an authored list, under today's rules

The content, the models, and the editor's fields take their final shape. The resolver keeps today's rule on the first two approaches, so nothing a player sees changes and **no golden hash moves**. That is this task's proof.

**Files:**
- Modify: `src/Sim.Core/Content/MissionContent.cs`, `src/Sim.Core/Narrative/NarrativeFacts.cs`, `src/Sim.Content/ContentLoader.Missions.cs`, `ContentLoader.Narrative.cs:298-302`, `ContentModels.cs:55,81-82`, `ContentReader.cs:108-161`, `ContentWriter.cs:202-320`, `ContentFaithfulness.cs`, `src/Sim.Missions/MissionTypes.cs`, `Beats.cs:28`, `BeatSituations.cs:146`, `MissionResolver.cs:201-246`, `src/MissionBench/Bench.cs`, `BenchFigures.cs`, `BenchReport.cs`, `src/Web/ContentKinds.cs:82-138`, `Web/Bridge/BeatLines.cs:228`, `BridgeReader.cs:122`, `Names.cs`, `Web/Forms/MissionKinds.cs`, `FormChoicesReader.cs:16`, `Plays.cs`, `Web/Components/Content/EncounterForm.razor`.
- Convert: `content/encounters/base.yaml`, `tests/fixtures/content/encounters/base.yaml`.
- Test: `MissionContentTests`, `ContentWriterTests`, `ContentFaithfulnessTests`, `ContentEditsTests`, `NarrativeContentTests`, `ScriptTests`, `MissionRulesTests`, `BenchTests`, `ContentMissionFormsTests`, `BeatLinesTests`, `tests/browser/edit.sh`.

**Produces:**

```csharp
// Sim.Core.Content
public sealed record ApproachDef(string Id, string Skill, int Difficulty, string? GearTag, string? Name = null);
public sealed record EncounterDef(string Id, IReadOnlyList<string> Tags, bool CommsBlocked, string? Chain,
    IReadOnlyList<ApproachDef> Approaches, DecisionDef? Decision, string? Name = null);

// Sim.Content
public sealed record ApproachModel(string Id, string? Name, string Skill, int Difficulty, string? GearTag,
    IReadOnlyDictionary<string, string>? Text = null);
// EncounterModel: Careful and Fast are replaced by IReadOnlyList<ApproachModel> Approaches, in the same position.

// Sim.Missions: the Approach enum is deleted. CheckBeat.Approach is a string, the approach's id.
// Web.Bridge.Names
public string Approach(string encounterId, string approachId);   // the name, or the id when it has none

// Web.Forms.EncounterKind
public static readonly RowSet Approaches;   // prefix "approaches", Qualified, Noun "approach"
// Fields: "id", "name", "skill", "difficulty", "gearTag", then "text.<key>" for each of NarrativeFacts.ApproachTexts.
// Posted names are approaches[0].skill, approaches[0].text.attempt, and so on.
```

- `NarrativeFacts.Approaches` is deleted. The `approach` fact on `attempt` and `check` gets a new `FactDomain.Approach`: its allowed values are the owning encounter's approach ids.
- YAML shape: the design's section 4.1, without `requires` and `outcomes` (Task 4). An approach with no words stays on one line: `- { id: careful, name: Careful, skill: SKILL_A, difficulty: 5 }`.

**Rules pinned here:**
- An approach id: lowercase letters, digits, and hyphens, 1 to 24 characters, unique within its encounter. A name follows `CheckName` like every other name.
- An encounter has 1 to 4 approaches. Loader messages: "An encounter needs at least one approach.", "An encounter may have at most 4 approaches.", "Approach id '{id}' is used twice.", and the existing skill and difficulty messages with the approach's id where "careful" or "fast" stood.
- A line that tests `approach` must be an encounter's own, and each value must be one of that encounter's approach ids: "tests approach '{value}', which {encounter id} does not have." A shared line that tests `approach`: "Only an encounter's own lines may test approach."
- **Interim rule, removed in Task 4:** in `MissionResolver.Check`, `caution - risk >= 0` takes `Approaches[0]`, otherwise `Approaches[Math.Min(1, Approaches.Count - 1)]`; the fast-success momentum goes to a success at index 1. Mark both lines `// Interim until Task 4 of the approaches plan.`
- Bench: `EncounterFigures(string EncounterId, int Checks, IReadOnlyList<int> ByPosition, IReadOnlyDictionary<Tier, int> Tiers, int Deaths, int Incapacitated)`, `ByPosition` always four long. The report's "careful %" column becomes "1st %", "2nd %", "3rd %", "4th %".
- The log's roll detail ends with the approach's name and a full stop for every check (`" {name}."`), in place of `" Fast approach."` on some.
- The index's "Careful" and "Fast" columns become one, "Approaches": each as `{name or id}: {skill} at {difficulty}`, joined by "; ".

- [ ] **Step 1:** Convert both encounter files by hand: `careful` then `fast`, ids kept, `name: Careful` and `name: Fast` in `content/`, `name: APPROACH_A_NAME` and `APPROACH_B_NAME` in the fixture. Words move with their approach unchanged.
- [ ] **Step 2:** Write the tests. In `MissionContentTests`:

```csharp
[Fact] public void An_encounter_with_no_approaches_is_refused()        // message contains "at least one approach"
[Fact] public void An_encounter_with_five_approaches_is_refused()      // "at most 4"
[Fact] public void Two_approaches_with_one_id_are_refused()            // "used twice"
[Fact] public void An_approach_id_with_a_capital_or_a_space_is_refused()
[Fact] public void Approaches_load_in_the_order_written()
// Assert.Equal(["careful", "fast"], pack.Encounters.Single(e => e.Id == "ENCOUNTER_01").Approaches.Select(a => a.Id));
```

In `NarrativeContentTests`: `An_own_line_testing_an_approach_the_encounter_lacks_is_refused`, `A_shared_line_testing_approach_is_refused`. In `ContentWriterTests` and `ContentFaithfulnessTests`: an encounter with three approaches, one of them with words, writes and reads back equal. In `ContentMissionFormsTests` (Review focus 4 and 5):

```csharp
[Fact] public async Task Posting_five_approaches_is_refused_with_a_problem()
[Fact] public async Task Posting_no_approach_is_refused_with_a_problem()
[Fact] public async Task Posting_two_approaches_with_one_id_is_refused_with_a_problem()
[Fact] public async Task Removing_an_approach_an_own_line_tests_is_refused_on_that_line()
// Each: status 200 with the form redrawn, the problem's text present, and the encounter file unchanged on disk.
[Fact] public async Task Adding_an_approach_row_redraws_the_form_with_one_more_and_saves_nothing()
```

Update the existing tests' field names (`careful.skill` becomes `approaches[0].skill`).
- [ ] **Step 3:** `dotnet build`. Expected: compile failures across the files listed.
- [ ] **Step 4:** Implement to the interfaces above. `FormRows` field names now contain a dot (`text.attempt`); if its pattern refuses that, widen the pattern and add a `FormRows` test for a dotted field. `EncounterForm.razor` draws each approach as a plain fieldset from the rows, with Add and Remove (`RowButton`), Add hidden at four and Remove at one; layout is Task 5's. `Plays.cs` builds one group for each approach row, titled with its name (or id), and `Plays.Written` counts every approach.
- [ ] **Step 5:** `dotnet test --nologo -v quiet`. Expected: all pass, and both golden tests pass **without edits**. If a golden hash moved only because `CheckBeat.Approach` now serialises as `"careful"` where the enum wrote `"Careful"` or a number, re-pin with that comment and confirm by hand, on one walkthrough, that every beat's other fields are identical before and after.
- [ ] **Step 6:** Raise `SchemaVersion` to 5. `./check.sh`. Expected: green, with the bench's figures the same as on `main` apart from the new column heads.
- [ ] **Step 7:** Commit: "Hold an encounter's approaches as a list the author writes".

### Task 4: availability, the lead's choice, authored outcomes, and no risk tolerance

**Files:**
- Modify: `src/Sim.Core/Content/MissionContent.cs`, `MissionTuning.cs:26`, `src/Sim.Content/ContentLoader.Missions.cs`, `ContentLoader.Tuning.cs:49`, `ContentModels.cs`, `ContentReader.cs`, `ContentWriter.cs`, `src/Sim.Missions/MissionResolver.cs`, `MissionTypes.cs:36-37`, `MissionsModule.cs:94`, `Beats.cs`, `src/SimRunner/Cli.cs`, `AutoCaptain.cs`, `src/MissionBench/*`, `src/Web/Endpoints/BridgeEndpoints.cs`, `Web/Bridge/BeatLines.cs`, `Web/Components/BriefingScreen.razor:182-187`, `Web/Forms/MissionKinds.cs`, both `tuning/missions.yaml`, both `encounters/base.yaml`.
- Test: `MissionRulesTests`, `MissionWalkthroughTests`, `MissionContentTests`, `ContentWriterTests`, `ContentFaithfulnessTests`, `MissionsModuleTests`, `ScriptTests`, `CliTests`, `SimulationTests`, `BenchTests`, `BriefingTests`, `ResolutionTests`, `PhaseBoundTests`, `WebCaptain.cs`, `ContentMissionFormsTests`.

**Produces:**

```csharp
// Sim.Core.Content
// Exactly one of Skill and Gear is set. Skill with Level: someone standing has that level after injury. Gear: the loadout carries the tag.
public sealed record RequirementDef(string? Skill, int Level, string? Gear);

public sealed record ApproachDef(string Id, string Skill, int Difficulty, string? GearTag,
    IReadOnlyList<RequirementDef> Requires, IReadOnlyDictionary<string, EffectDef> Outcomes, string? Name = null);
// Outcomes is keyed by the tier words of NarrativeFacts.Tiers; a tier absent from it takes the default.

// Sim.Content
public sealed record RequirementModel(string? Skill, int? Level, string? Gear);
// ApproachModel gains IReadOnlyList<RequirementModel> Requires and
// IReadOnlyDictionary<string, IReadOnlyList<EffectModel>> Outcomes, both after GearTag.

// Sim.Missions
public sealed record Parameters(int Priority = 0, int Autonomy = 0, bool DevelopJuniors = false);
// CheckBeat gains int Offered after Approach: how many approaches were available.

// MissionResolver, internal static so MissionRulesTests can call them:
internal static EffectDef DefaultOutcome(MissionTuning tuning, Tier tier);
internal static IReadOnlyList<ApproachDef> Available(EncounterDef encounter, IReadOnlyList<MemberState> standing, IReadOnlyCollection<string> gearTags);
internal static ApproachDef Choose(IReadOnlyList<ApproachDef> available, MemberState lead);

// Web.Forms.EncounterKind.Approaches gains, after "gearTag":
//   "r0.what", "r0.level", "r1.what", "r1.level", "r2.what", "r2.level"
//     what is a skill id, or "gear:<tag>", or empty; level is used only with a skill.
//   The form edits three requirements. The author will not write a fourth in a file until the form supports it,
//     so the form does nothing special for more than three.
//   for each tier T of NarrativeFacts.Tiers: "out.T.own", "out.T.progress", "out.T.momentum", "out.T.strain", "out.T.injury"
//     own is "true" when the tier is written; the other four are then its one effect, a blank meaning 0 or no roll.
```

**Rules pinned here:**
- `DefaultOutcome`, from the tuning file and nothing else: critical `Progress 1, Strain -reliefOnSuccess, Momentum +momentum.critical`; success `Progress 1, Strain -reliefOnSuccess`; partial `Strain +strain.partial`; failure `Strain +strain.failure, InjuryRoll Light`; disaster `Strain +strain.disaster, InjuryRoll Severe`.
- A check applies its tier's outcome: momentum and strain are added and clamped as today, progress is added, and an injury roll falls on the actor **after** the check's beat, where today's rolls fall. `withdraw` is refused in an outcome: "An outcome cannot withdraw the team."
- The chain on a disaster is unchanged and does not depend on the outcome.
- `Available`: every requirement holds. `Choose`: the highest `lead.Skills[approach.Skill] - lead.Injury`, floored at 0; the earliest in the list wins a tie.
- Loader: an approach may have any number of requirements; the data has no limit. A requirement names exactly one of `skill` and `gear`; a skill requirement's level is 1 or more and its skill is known; outcome keys are tier words; "At least one approach must have no requirements." when every approach has one.
- `compounding.momentum.fastSuccess` and `MomentumFastSuccess` are deleted. In both packs every approach with id `fast` gains `outcomes: { critical: [{ progress: 1 }, { strain: -1 }, { momentum: 2 }], success: [{ progress: 1 }, { strain: -1 }, { momentum: 1 }] }`, written from the tuning values then in force (relief 1, critical 1, fast 1).
- Risk tolerance is removed from `Parameters`, the launch command's range check, the briefing's `Parameters` array, the launch endpoint's binding, the command line's options and its summary sentence, `AutoCaptain`, the bench's grid, and the log line in `BeatLines` that names it. A posted `risk` field is ignored, not refused.
- Bench: `EncounterFigures` gains `int Narrowed` after `ByPosition` (checks where `Offered` was less than the encounter's approaches); the report adds a "narrowed %" column.
- Remove both `// Interim` lines from Task 3.

- [ ] **Step 1:** Write the rule tests in `MissionRulesTests`, building encounters in code:

```csharp
[Fact] public void The_lead_takes_the_approach_they_are_best_at()
// lead skills { SKILL_A: 2, SKILL_C: 5 }, approaches [a: SKILL_A, c: SKILL_C]  =>  check.Approach == "c"
[Fact] public void Equal_or_absent_skill_takes_the_first_listed()                 // Review focus 1
// lead skills {} => "a";  lead { SKILL_A: 3, SKILL_C: 3 } => "a"
[Fact] public void The_leads_injury_counts_against_every_skill_alike_so_the_choice_holds()
[Fact] public void A_new_lead_chooses_by_their_own_skills()                       // Review focus 2
// first lead best at SKILL_A is incapacitated at encounter 1; the next check's Approach is the new lead's best
[Fact] public void An_approach_is_unavailable_below_its_skill_level()
// requires [{ SKILL_C, 4 }], team's best SKILL_C is 3, lead best at SKILL_C  =>  Approach == "a", Offered == 1
[Fact] public void A_gear_requirement_holds_when_the_loadout_carries_the_tag()
[Fact] public void The_best_at_the_chosen_skill_makes_the_check_not_the_lead()
[Fact] public void An_unwritten_outcome_is_the_default_for_every_tier()
// for each tier: Assert.Equal(expected per the list above, MissionResolver.DefaultOutcome(tuning, tier))
[Fact] public void A_written_outcome_replaces_the_default_whole()
// success: [{ momentum: 2 }]  =>  after a success Progress is unchanged, Strain unchanged, Momentum +2
[Fact] public void An_empty_outcome_does_nothing()                                // Review focus 3
// failure: []  =>  no strain, and no InjuryBeat follows the check
[Fact] public void A_disaster_still_chains_when_its_outcome_is_written()
```

In `MissionContentTests`: one test for each loader message above, and `An_empty_outcome_loads_as_no_effect_not_as_absent` (`Outcomes.ContainsKey("partial")` and it equals `new EffectDef()`). In `ContentWriterTests` and `ContentFaithfulnessTests`: requirements (five on one approach) and outcomes, including an empty one, read back equal. In `MissionRulesTests`: `An_approach_with_five_requirements_needs_all_five`. In `BriefingTests`: the page has no `name="risk"`. In `ContentMissionFormsTests`: a save with `approaches[0].out.partial.own=true` and the four fields blank writes `partial: []`; with `own` absent it writes no `partial`.
- [ ] **Step 2:** Run them. Expected: compile failures, then failures.
- [ ] **Step 3:** Implement. Convert both packs' `fast` approaches and both tuning files.
- [ ] **Step 4:** `dotnet test`. The two golden tests fail: the lead now chooses by skill. Read one walkthrough's beats and confirm each check's approach is the lead's better skill and each tier's effects match `DefaultOutcome` or the written outcome. Then re-pin both with the comment "approaches are chosen by the lead's skill and outcomes are authored (approaches plan, Task 4)".
- [ ] **Step 5:** Raise `SchemaVersion` to 6. Run `./check.sh`, then `dotnet run --project src/SimRunner -- bench --objective OBJECTIVE_TEMPLATE_1 --runs 2000 --team sweep --content tests/fixtures/content` on this branch and on `main`, and put the before and after of losses, deaths, calls, and initiatives per mission in the commit message. This is the new baseline; do not tune to match the old one.
- [ ] **Step 6:** Commit: "Let the lead choose an approach by skill, with thresholds and authored outcomes; drop risk tolerance".

### Task 5: the encounter form

**Files:** `src/Web/Components/Content/EncounterForm.razor`, new `ApproachTable.razor` and `ApproachTabs.razor` beside it, `PlaysPanel.razor`, `src/Web/Forms/Plays.cs`, `src/Web/wwwroot/css/app.css`, `src/Web/wwwroot/js/editor.js` only if Datastar cannot carry the tabs; `tests/Web.Tests/ContentMissionFormsTests.cs`, `ComponentTests.cs`, `tests/browser/edit.sh`.

**Consumes:** `EncounterKind.Approaches` and its field names (Tasks 3 and 4); `Plays` groups keyed by approach id.

**Layout, top to bottom (design 4.5):** tags, comms, chain; the approaches table; the decision; the approach tabs; "What plays here".

- `ApproachTable`: one row for each approach with Name, Id, Skill, Difficulty, Gear tag, and "Available when" (the three `rN.what` selects, each with its level beside it when a skill is chosen; an unused slot shows as "Add a condition"). Remove on each row, "Add an approach" under the table. Below 720 pixels each row stacks as a labelled block; the table never scrolls sideways.
- `ApproachTabs`: a `role="tablist"` with one `role="tab"` button for each approach, labelled with its name, or its id while the name is blank; one `role="tabpanel"` for each. The shown tab is a local Datastar signal (`_tab`, the row's index), so switching posts nothing. Every panel stays in the form; hidden panels use the `hidden` attribute through `data-attr:hidden`, never removal, so one save sends them all. Left and Right move between tabs and Home and End go to the ends, per the ARIA tabs pattern.
- A panel holds, for "Before the check" and then each tier: the text box with its preview (the `Plays` slot for that approach, drawn by the parts `PlaysPanel` already uses), and for a tier its effects line. With `out.T.own` unticked the line reads "Default: {the default in words}" greyed, with a "Change" tick box; ticked, it shows the four fields. The default's words come from `MissionResolver.DefaultOutcome` through one function in `Plays.cs`: `public static string EffectWords(EffectDef effect)` ("+1 progress, -1 strain, +1 momentum", "light injury roll", and "Nothing" for the empty effect).
- A tab whose panel holds a field with an error shows "(problems)" after its label and gets `aria-invalid="true"`; after a refused save the first such tab is the one shown.
- `PlaysPanel` no longer draws the approach groups. Its "Checks, either approach" group is retitled "Checks, any approach".

- [ ] **Step 1:** Tests in `ContentMissionFormsTests`:

```csharp
[Fact] public async Task The_form_has_a_tab_and_a_panel_for_each_approach_before_what_plays_here()
// two role="tab" in document order before the heading "What plays here"; each aria-controls an existing role="tabpanel"
[Fact] public async Task Every_panels_fields_are_in_the_form_whichever_tab_shows()
// approaches[1].text.failure is present in the html though tab 0 is shown
[Fact] public async Task An_unwritten_outcome_shows_the_default_in_words()
// contains "Default: +1 progress, -1 strain" for success
[Fact] public async Task A_refused_save_marks_and_shows_the_tab_with_the_problem()
// post approaches[1].out.failure.strain=x  =>  tab 1 has aria-invalid="true" and the signal's initial value is 1
[Fact] public async Task What_plays_here_holds_no_approach_rows()
```

and `Plays.EffectWords` cases in the test file that covers `Plays`.
- [ ] **Step 2:** Run them. Expected: FAIL.
- [ ] **Step 3:** Build the two components and the styles. No new colours: tabs use the tokens the `.seg` control uses.
- [ ] **Step 4:** In a browser, with `tests/browser/edit.sh`'s tooling, on `ENCOUNTER_01` in a scratch copy of the fixture: add a third approach, name it, write its attempt and one outcome, switch tabs with the keyboard, save, reload, and confirm the file and the form agree. Check 400 and 1280 pixels wide. Add this walk to `edit.sh`.
- [ ] **Step 5:** `./check.sh`, then commit: "Lay out the encounter form as definitions, a tab for each approach, then what plays here".

### Task 6: the record

**Files:** `docs/gdd.md`, `GLOSSARY.md`, `docs/plans/phase-1-outline.md`, `README.md`, `wayfinder/map.md`, `tools/gdd-page` output if the repository keeps a built page.

- [ ] **Step 1:** GDD: raise the status line to v1.15 with a clause naming this review. Rewrite 3.5 (the encounter's YAML, in placeholders) and 4.2 step 1 (how the approach is chosen), 4.3 (the fast bullet becomes "an approach may write its own outcomes; unwritten ones take the tuning file's"), 4.4 (no risk in the parameters), 6 and 6.1 (the `approach` fact and where an approach's words are written), 9 (the combinatorics line: "about 3 approaches" is now authored), 10.1 (the bench's columns), and add the row "1d-review. Authored approaches" to 11.1 as done with the date. Add item 24 to section 15 recording the author's decisions from the design's sections 2 to 4, and in section 16 replace question 4's "an approach" wording only if it no longer reads true. The two "Unplaced" roadmap rows are already there: leave them.
- [ ] **Step 2:** Glossary: **Approach** becomes "One of the ways an encounter offers to attempt its check, written by the author. The team lead chooses among those available, by skill."; **Operational parameters** lists priority and how readily to call in; add **Outcome**: "What a tier of an approach does to the mission: progress, momentum, strain, or an injury roll." Check **Tier** still reads true.
- [ ] **Step 3:** Outline, README, and map: one line each where they describe encounters, the briefing's parameters, or the bench.
- [ ] **Step 4:** `./check.sh`, then commit: "Record authored approaches and the 1d review in the GDD, glossary, outline, README, and map".
