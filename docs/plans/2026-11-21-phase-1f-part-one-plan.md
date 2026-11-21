# Slice 1f, part one (the mission bench in play): implementation plan

> **For agentic workers:** steps use checkbox (`- [ ]`) syntax for tracking. Do the tasks in order; each ends with `./check.sh` and a commit. Keep the design's section 5 (architecture) current as you go: when the build differs, change section 5 in the same commit, and say so in a new "As built" section (section 9) of the design.

**Goal:** the author opens Tuning from an objective in Orders, sweeps rookie, mixed, and veteran teams, and sees rookies lose about a third of their missions. They change one number in the draft, press Run, and see rookie loss fall while veterans barely move, saved and draft side by side on the same seeds. They replay the worst rookie run as the report play would show, press Save, and the legacy's next mission uses the new number. Nothing is typed into a YAML file, and nothing leaves the game.

**Architecture:** `MissionBench` gains a tally that a run feeds one trace at a time, so a run can be cancelled, report progress, and be interleaved with another; the figures gain the sums of squares the noise test needs; and a small `BenchNoise` decides what is greyed. `Sim.Content` gains `TuningFields`: a tuning file's scalar leaves with their key paths and comments, and a replace of scalars in place in the text. `Sim.Missions` gains `LaunchTeam`, the launch's team building lifted out of `MissionsModule.StartMission` so the bench calls the same code. `Web` gains a test ship founded as an in-memory legacy (so its views are the modules' own), the bench's real teams, the draft singleton, the Tuning page with its form, streamed runs, figures, every-objective view, and replay through the bridge's own report, and the entry links.

**Spec:** `docs/plans/2026-11-21-phase-1f-design.md`, part one (sections 1 to 6 and 8). Read it with this plan. **Design system:** `docs/design/bridge-design.md`, and the editor's existing forms (`src/Web/Components/Content/`) for their recipes.

Tasks 1 to 4 are the ground below the web (bench, noise, tuning fields, launch team). Tasks 5 to 7 are the server's model of a bench (test ship state, real teams, the draft). Tasks 8 to 13 are the page. Task 14 is the worked example and the record.

## Global constraints

Part one of 1e's (`2026-11-16-phase-1e-part-one-plan.md`, "Global constraints") hold, with these changes:

- **Nothing here changes play.** `SchemaVersion` and `CurrentFormatVersion` stay as they are; no event, state shape, or module behaviour changes. No golden hash or golden chronicle moves, in any task. Task 4 moves code out of `MissionsModule` and must leave every golden where it is; that is its test. Task 14 saves a changed number into `content/tuning/missions.yaml`, which will move goldens that run on the game's pack. If it does, record the before and after in that commit, as for any content change.
- **The bench agrees with play and with the command line.** A run under the saved tuning gives the same `BenchFigures` as `SimRunner bench` on the same inputs and seed. A real team benched is the `Team` the launch would build for the same picks. Neither the bench nor the web grows a second copy of the launch's team building or of the resolver loop.
- **The command line's output does not change.** `SimRunner bench` prints byte for byte what it prints today, for every flag combination its tests cover.
- **A bench never writes to a legacy.** No command is sent, nothing is published on a legacy's bus, nothing is stored. A legacy is read through `LegacySession.Read`, under its lock only while read.
- **The YAML in `content/` is the only stored tuning.** The draft is in memory, one per tuning file (only `missions` in part one), and is saved into the file or thrown away. No override file, no browser storage.
- **The loader judges a save,** through `ContentEditor.Save`: the whole pack is built, the loader runs, then the one file is written. A save never drops a comment, reorders a key, or respells a value it did not change.
- **Developer mode only.** Every route is under the `dev` group, as the editor's are, and absent in a hosted game.
- **Every `Sim.*` project still references `Sim.Core` alone** (plus `Sim.Expressions` for its users). `MissionBench` stays free of the web and of any legacy. The architecture tests are not loosened.
- **Seeds travel as text** in URLs, forms, and signals (64-bit numbers do not survive JavaScript).
- **No hover, and steppers and selects for known sets.** The tablet waits for the online track, but nothing here works against it.
- `content/` changes only in Task 14 (the worked example's one number), and the fixture not at all.

## Review focus

1. **Equivalence.** The page's saved column equals `SimRunner bench` on the same inputs, for a generated team and a sweep; a half-finished interleaved run covers the same seeds in both columns. (Tasks 1 and 10)
2. **One launch.** The bench's real team is the launch's team: morale bonus, injuries, traits, trait bonuses, and ship traits included; a member the launch would refuse cannot be picked. (Tasks 4 to 6)
3. **Save in place loses nothing.** Every editable value changed and saved changes only those scalars; comments, spacing, key order, and flow maps are untouched; a stale draft is refused with the keys named. (Tasks 3 and 7)
4. **Isolation.** A legacy's stored record and event log are byte for byte the same after a bench and a replay. (Tasks 6 and 12)
5. **Greying is conservative and said.** The noise rule is the one in design 4.8, tested at its threshold, and stated once under each table. (Task 2)

---

### Task 1: the bench as a tally, with cancellation, progress, and spread

**Files:** `src/MissionBench/Bench.cs`, `src/MissionBench/BenchTally.cs` (new), `src/MissionBench/BenchFigures.cs`, `src/MissionBench/BenchOptions.cs`, `tests/MissionBench.Tests/BenchTests.cs`, `tests/SimRunner.Tests/` (the bench output tests).

**Produces** (shape):

```csharp
// Fed one trace at a time; Figures() can be read at any point and is the figures of the runs added so far.
public sealed class BenchTally(ContentPack pack)
{
    public int Runs { get; }
    public void Add(MissionTrace trace);
    public BenchFigures Figures();
}
public static class Bench
{
    public static BenchFigures Run(ContentPack pack, MissionTuning tuning, BenchOptions options,
        IProgress<int>? progress = null, CancellationToken cancellation = default);
    public static MissionTrace Replay(ContentPack pack, MissionTuning tuning, BenchOptions options, ulong seed);
    // Every objective in the pack, each strength asked for, options.Runs runs a cell (design 4.8, "Every objective").
    public static IReadOnlyList<ObjectiveCell> Objectives(ContentPack pack, MissionTuning tuning, BenchOptions options,
        IReadOnlyList<TeamStrength> strengths, IProgress<int>? progress = null, CancellationToken cancellation = default);
}
public sealed record ObjectiveCell(string ObjectiveId, TeamStrength? Strength, BenchFigures? Figures, string? Refused);
// BenchFigures gains, for the noise test: CallsSquares, InitiativesSquares, ExperienceSquaresByMember,
// and StageFigures gains MomentumSquares and StrainSquares (sums of squares over the runs that reached the stage).
// MissionTrace gains State: the mission's last MissionState, for replay's report (Task 12).
```

- [x] **Step 1: the tally.** Move `Run`'s loop body into `BenchTally.Add`, and its final record into `Figures()`. `Run` becomes a loop of `Replay` and `Add`, checking `cancellation` and reporting `progress` every 100 runs and at the end. `WorstSeed` keeps today's rule (the first run with the most harm), so it is the same seed whatever order the tally is fed in seed order.
- [x] **Step 2: the spread.** Add the sums of squares above, each per run (calls and initiatives per run, experience per member per run, momentum and strain per stage reached). The command line ignores them.
- [x] **Step 3: generated teams follow the pack's `Bench`.** They already do (`TeamGenerator.Generate(pack.Bench, ...)`). Write it down in a test, because the page's draft column passes `pack with { Tuning = draft, Bench = draftBench }` (Task 10), which is how an edit to `bench.teams` is measured **(my call: the design lists `bench.teams` as tuned in part one but does not say which column's teams the draft uses)**.
- [x] **Step 4: every objective.** `Objectives` runs each objective in `pack.Objectives` order, for each strength (a real team: one cell per objective, `Strength` null). A real team whose size is outside the objective's `TeamSizeMin` to `TeamSizeMax` gets a cell with `Refused` ("a team of 4 is outside the 2 to 3 this objective takes", the words `MissionsModule`'s check uses) and no figures.
- [x] **Step 5: the trace's state.** `Replay` keeps the last `MissionStep.State` on the trace.
- [x] **Step 6: tests.** `Run` gives the same figures as before the change on the game's pack and the fixture (compare against figures captured before Step 1, every field). A tally fed the same traces in two halves gives the same figures as one fed them at once. Cancellation stops within 100 runs. Progress arrives before the end. `SimRunner bench`'s output is unchanged: if `SimRunner.Tests` has no test comparing the full text for a single team, a sweep, `--compare`, and `--replay`, add one first, from the output before this task.
- [x] **Step 7:** `./check.sh`; commit "Run the bench through a tally, cancellable and with progress".

### Task 2: the noise rule

**Files:** `src/MissionBench/BenchNoise.cs` (new), `tests/MissionBench.Tests/BenchNoiseTests.cs` (new).

**Produces:**

```csharp
public enum FigureKind { Share, Rate, Mean }
public static class BenchNoise
{
    // True when |b - a| is under twice the standard error of the difference of two independent samples (design 4.8).
    // Share: successes of n. Rate: a count over n crew-missions, as Poisson. Mean: sum and sum of squares over n runs.
    public static bool Within(FigureKind kind, (double Sum, double Squares, int N) a, (double Sum, double Squares, int N) b);
    public const string Rule = "Greyed: a change smaller than twice its standard error, as if the two columns were independent samples; the same seeds make the true noise smaller.";
}
```

- [x] **Step 1:** Implement the three standard errors: a share's `p(1-p)/n` per column, a rate's `count/n²`, a mean's sample variance over `n`. A column with `n` of 0 is never within the noise (nothing to compare).
- [x] **Step 2: which rows are which.** Results % and reached % are shares of runs. Deaths and incapacitations per 100 crew-missions are rates. Calls, initiatives, experience, momentum, and strain are means. In the encounter tables, tier %, position %, and narrowed % are shares of checks; deaths are rates of crew-missions. Put this list in one place (`BenchNoise` or the figures' row list in Task 10), not in the components.
- [x] **Step 3: tests.** A known pair of shares either side of the threshold (for example 34.0% against 33.0% and 35.5% at 10,000 runs each: compute the threshold in the test from the formula, and pick one pair under and one over) greys one and not the other. The same for a rate and a mean. Zero runs is not within.
- [x] **Step 4:** `./check.sh`; commit "Decide which bench changes are within the noise".

### Task 3: a tuning file's fields, and saving them in place

**Files:** `src/Sim.Content/TuningFields.cs` (new), `tests/Sim.Tests/TuningFieldsTests.cs` (new).

**Produces:**

```csharp
public enum TuningFieldKind { Number, Dice, Range }   // Dice: a { count, sides } map. Range: a two-number sequence, as bench skill: [1, 3].
public sealed record TuningField(string Path, string Section, string Key, TuningFieldKind Kind, IReadOnlyList<string> Values, string? Hint, int Line);
public sealed record TuningSection(string Name, string? Note, IReadOnlyList<TuningField> Fields);
public static class TuningFields
{
    public static IReadOnlyList<TuningSection> Read(string text);
    // Replaces each path's scalar(s) in place; throws for a path that is not a field of the text.
    public static string Replace(string text, IReadOnlyDictionary<string, IReadOnlyList<string>> values);
}
```

- [x] **Step 1: reading.** Walk the text with YamlDotNet's parser (events with marks), not the deserializer, so each scalar's start and end are known. Fields are the scalar leaves in file order, with their dotted key paths as the loader names them (`check.tiers.critical`, `bench.teams.rookie.skill`). A section is a top-level key; nested maps stay one section, their keys prefixed (`tiers.critical` under `check`). A `{ count, sides }` map is one Dice field; a sequence of two scalars is one Range field. The hint is the comment that trails the line (a continuation comment, as on `boldPriorityWeight`'s line, joins the field above whose comment it continues: detect it by its leading spaces after `#`). A comment on the line of a section key, or on lines above it, is the section's note (`salience`'s "1e part four: see ..."). The file's leading comment is not a field's.
- [x] **Step 2: replacing.** For each changed path, splice the new text over that scalar's span, from the last in the file to the first, so earlier spans stay valid. Nothing else in the text moves. A value is written as typed after the loader's check (Task 7); `Replace` does no formatting of its own beyond keeping the scalar plain.
- [x] **Step 3: tests.** `Read` on the game's `missions.yaml` gives every key `LoadTuning` reads, and no other (compare with the paths `TuningReader` reads, gathered by a test hook or by listing them). Changing every field to a different valid value and saving changes only those spans: the text outside them is equal character for character, and `LoadTuningText` reads the new values back. A changed value of different width (`7` to `12`) keeps the trailing comment on its line. Dice and ranges round-trip. A path that is not a field throws.
- [x] **Step 4:** `./check.sh`; commit "Read a tuning file's fields and replace their values in place".

### Task 4: the launch's team, as one function

**Files:** `src/Sim.Missions/LaunchTeam.cs` (new), `src/Sim.Missions/MissionsModule.cs`, `tests/Sim.Tests/LaunchTeamTests.cs` (new).

**Produces:**

```csharp
public static class LaunchTeam
{
    // The team the launch sends: each member from the roster view (skills less injuries, morale bonus), with their own traits
    // and this team's trait bonuses from the trait view, and the ship's traits.
    public static Team Build(IRosterView roster, ITraitView traits, IReadOnlyList<string> crewIds, string leadId);
    // Why the launch would refuse this member, or null: not on the roster, or not fit (the AssignTeam check's words).
    public static string? Refusal(IRosterView roster, string crewId);
}
```

- [x] **Step 1:** Move the member-building lines and `Own` out of `StartMission` into `Build`, and the roster and fitness checks of the `AssignTeam` validation into `Refusal`. `MissionsModule` calls both. The words of each refusal are unchanged.
- [x] **Step 2: tests.** Every golden passes untouched (the test of this task). `Build` on a legacy with an injured, a low-morale, and a trait-holding member gives what `StartMission` gave before the move (capture a `MissionStarted` legacy's resolver team from the event log's first state and compare). `Refusal` names an unfit and an unknown member.
- [x] **Step 3:** `./check.sh`; commit "Lift the launch's team building into LaunchTeam".

### Task 5: a test ship as a legacy in memory

**Files:** `src/Web/Forms/TestShipLegacy.cs` (new, beside `TestShipCapture.cs`), `tests/Web.Tests/TestShipLegacyTests.cs` (new).

A test ship has no `IRosterView` or `ITraitView` of its own, and writing them would copy the crew and traits modules' logic (trait bonuses, launch previews). Instead, found a legacy in memory on the live pack's `Game`, as `LegacySession.CreateLegacy` does but never stored, then set its crew and traits state from the ship. The views are then the modules' own, through `game.Bus.View<T>(state)`, the way `TestShipCapture` reads a legacy **(my call on the mechanism; design 4.5 asks only that a test ship's team go through the launch's code)**. Slice 1f-previews starts missions on test ships through this.

- [x] **Step 1: founding.** `TestShipLegacy.Found(LivePack live, TestShipDef ship) : LegacyState` sends `FoundLegacy` on a fresh `LegacyState` with a fixed seed, in memory. Then, for each listed crew member: morale, loyalty, skills (level only), fit (false: `Incapacitated`), aboard (false: away), and alive (false: dead). Each trait becomes a `StoredTrait`, its `ChangedAtDay` and `ChangedAtCycle` counted back from the ship's `Date` and `Cycle`, and the legacy's day and cycle are the ship's. Leave history, chronicle, and storylet arrivals unset: nothing the launch reads uses them. Say so in a comment and in "As built".
- [x] **Step 2: tests.** For every crew member, trait, and pair on the game's and the fixture's test ships, the founded legacy's `IRosterView` and `ITraitView` agree with `TestShipSource` (morale, loyalty, skill, fit, aboard, trait values). Founding writes nothing to the store (the store's legacy count is unchanged).
- [x] **Step 3:** `./check.sh`; commit "Found a test ship as an in-memory legacy, read through the modules' own views".

### Task 6: the bench's real teams

**Files:** `src/Web/Tuning/BenchTeams.cs` (new; `src/Web/Tuning/` is the page's folder from here on), `tests/Web.Tests/BenchTeamsTests.cs` (new).

**Produces:**

```csharp
public sealed record BenchCandidate(string Id, string Name, string? Refused);
public sealed record BenchSource(string Kind, string Id, string Name, IReadOnlyList<BenchCandidate> Candidates);  // Kind: "legacy" or "testship"
public sealed class BenchTeams(LegacySession sessions, ContentHost host, ContentEditor editor)
{
    public Task<BenchSource?> Legacy(Guid legacyId, CancellationToken cancellation);
    public BenchSource? TestShip(string shipId);
    // Null with the refusal when a pick is refused, the lead is not picked, or the team is empty.
    public Task<(Team? Team, string? Refused)> Build(string kind, string id, IReadOnlyList<string> crewIds, string leadId, CancellationToken cancellation);
}
```

- [x] **Step 1: a legacy.** Read it with `LegacySession.Read` (which holds the lock only while it loads), take `IRosterView` and `ITraitView` from the live game's bus on its state, list the roster in roster order with each member's `LaunchTeam.Refusal`, and build with `LaunchTeam.Build`. The team is built once per run, not per seed.
- [x] **Step 2: a test ship.** Load the ships as the editor's previews do (`ContentLoader.LoadTestShips` on the editor's files and the live pack), found it with Task 5, and the same as Step 1.
- [x] **Step 3: tests.** A team built from a legacy equals the team `StartMission` builds for the same picks on that legacy (advance a test legacy to the launch with the same `AssignTeam`, and compare with the resolver's starting team), injuries, morale bonus, traits, trait bonuses, and ship traits included. A member who is not fit is listed with the refusal and cannot be built. A team from a test ship's grieving member carries the trait. After `Legacy` and `Build`, the legacy's stored record and its event log are byte for byte the same, and `LegacySession.LockCount` is back to 0.
- [x] **Step 4:** `./check.sh`; commit "Build a bench team from a legacy or a test ship through the launch's code".

### Task 7: the draft

**Files:** `src/Web/Tuning/TuningDrafts.cs` (new), `src/Web/Program.cs` (register it as a singleton beside `ContentHost`), `tests/Web.Tests/TuningDraftsTests.cs` (new).

**Produces:**

```csharp
public sealed record TuningDraft(string File, string BaseText, string BaseHash, DateTimeOffset Began, IReadOnlyDictionary<string, IReadOnlyList<string>> Values);
public sealed record DraftLoad(MissionTuning? Tuning, BenchTuning? Bench, IReadOnlyDictionary<string, string> FieldErrors, IReadOnlyList<string> OtherErrors);
public sealed class TuningDrafts(ContentEditor editor, GameServices services)
{
    public TuningDraft? Current(string file);
    public TuningDraft Set(string file, string path, IReadOnlyList<string> values);    // begins a draft from the file on disk if there is none
    public void Discard(string file);
    public bool Stale(string file, out IReadOnlyList<string> changedKeys);           // the file's hash differs from BaseHash
    public DraftLoad Load(string file);                                               // the base text with the values put in, through LoadTuningText
    public Task<SaveResult> Save(string file, CancellationToken cancellation);
    public event Action<string>? Changed;                                             // runs subscribe to cancel themselves (Task 10)
}
```

- [x] **Step 1: holding.** One draft per tuning file, under a lock; `Set` with the saved value removes that path from the draft, and a draft with no values is gone. `Began` comes from `GameServices.Clock`. Every tab sees the one draft, and a restart loses it.
- [x] **Step 2: loading.** `Load` puts the values into the base text with `TuningFields.Replace` and calls `ContentLoader.LoadTuningText`. Each error whose message begins with a field's path (the `TuningReader` and `CheckRanges` messages do: "check.assistDivisor must be ...") goes to that field; the rest are `OtherErrors`. If any message cannot be placed today, place it by its path and say so in "As built"; do not change `ContentError`.
- [x] **Step 3: saving.** Refused when `Stale`: the changed keys are those whose value in the file on disk differs from the base text's (read both with `TuningFields.Read`). Otherwise `ContentEditor.Save` with the file's text replaced by `Replace(diskText, values)`, so the whole pack is loaded with it and a refusal comes back with the editor's errors, placed as in Step 2. A saved draft is cleared. The pack swaps as for any edit; a mission in flight keeps its tuning (GDD 4.7) because its state carries it.
- [x] **Step 4: tests.** A draft that does not load gives each error beside its field and no tuning. Discard leaves no draft. Two lookups see one draft. An edit to the file on disk after a draft began makes it stale, and Save is refused naming the keys whose saved values changed. A save writes only the changed spans, and the live pack's `Tuning` has the new number. A mission in flight in a test legacy finishes on its old tuning; the next mission starts on the new.
- [x] **Step 5:** `./check.sh`; commit "Hold a tuning draft in memory and save it in place".

### Task 8: the mockup

**Files:** `docs/screens/1f/tuning.html` (new), `docs/screens/1f/README.md` (new), following `docs/screens/1e-6/`'s form.

- [x] **Step 1:** Mock up the page from `docs/design/bridge-design.md`'s recipes, wide and narrow: the draft column (sections, a changed field showing its saved value, a dice field, a range field, a read-only section greyed with its label and link, Save and Discard with the "began at ... lost on restart" line, the stale banner), and the bench column (Mission tab, One mission and Every objective views, inputs, Run, the progress line, a summary table with saved, draft, and change columns with one change greyed and the rule under it, an encounter table, the worst seed links, a replay). Decide here the open items in design 8: how Every objective reads on a narrow screen, and whether the read-only sections fold away by default. Write the choices in the README.
- [x] **Step 2:** Show the author before Task 9 and take their changes. Commit "Mock up the Tuning page".

### Task 9: the page and the draft form

**Files:** `src/Web/Endpoints/TuningEndpoints.cs` (new), `src/Web/Program.cs`, `src/Web/Components/Tuning/TuningPage.razor`, `TuningMain.razor`, `DraftForm.razor`, `DraftField.razor` (new), `src/Web/Tuning/TuningQuery.cs` (new), `src/Web/wwwroot/css/` (the page's styles, from the mockup), `tests/Web.Tests/TuningPageTests.cs` (new).

Routes, all in the `dev` group: `GET /dev/tuning`, `POST /dev/tuning/draft` (one field), `POST /dev/tuning/discard`, `POST /dev/tuning/save`.

- [x] **Step 1: the shell.** In the editor's frame (`ContentShell`), two columns wide and stacked narrow, from the mockup. `TuningQuery` reads and writes the page's inputs and view to the query (objective, draw, runs, seed, team kind and source and picks and lead, lead style, operational parameters, gear, policy, view), seed as text; a reload and a link land on the same inputs.
- [x] **Step 2: the form.** From `TuningFields.Read` on the draft's base text (or the file, with no draft): each section a heading with its note, each Number a `Field` with its hint, each Dice and Range two `Stepper`s. A changed field shows the saved value beside it. `storylets`, `salience`, `vignettes`, `captainsTime`, and `history` are read-only, greyed, under "Measured in Legacy mode (part two); edit as text for now", linking to `/dev/content/files/missions`. The read-only list is one constant beside the page, so part two removes it.
- [x] **Step 3: changing a field** posts the field through Datastar, which calls `TuningDrafts.Set` and patches the form's head and that field (its saved value, and its error from `Load`). Save and Discard patch the form and show the editor's refusal beside its field, or the stale banner with the keys and its Discard.
- [x] **Step 4: tests.** The page lists every field of the game's `missions.yaml` in file order with its hint, and the read-only sections as read-only. Posting a field shows the saved value beside it, and a bad value shows the loader's error beside it. Two requests see one draft. Save writes the file in place; a stale draft's save is refused naming the keys. The routes are absent outside developer mode.
- [x] **Step 5:** `./check.sh`; commit "Add the Tuning page and its draft form".

### Task 10: inputs, running, and the figures for one mission

**Files:** `src/Web/Tuning/TuningRuns.cs` (new), `src/Web/Tuning/FigureRows.cs` (new), `src/Web/Components/Tuning/BenchInputs.razor`, `BenchFigures.razor` (or a name that does not clash with the record), `EncounterTable.razor`, `RunProgress.razor` (new), `TuningEndpoints.cs`, `tests/Web.Tests/TuningRunsTests.cs` (new).

Route: `POST /dev/tuning/run`, a Datastar stream.

- [x] **Step 1: the inputs** (design 4.4), one control each, from the mockup: objective (select), draw (redrawn or fixed), runs (default 10,000) and seed (default 1, as text), team (Generated: rookie, mixed, veteran, or sweep; Legacy; Test ship), lead style (generated only), autonomy, priority, develop juniors, gear by tag, and call policy. For Legacy and Test ship, the source select, then member checkboxes from `BenchTeams` with each refusal shown and the box disabled, and a lead select from the picked. Changing an input updates the query and cancels the page's run.
- [x] **Step 2: running.** `TuningRuns` keeps one run per page (a page id in a signal), a `CancellationTokenSource` each. A new run, a change of input, leaving the page (the stream's `RequestAborted`), and `TuningDrafts.Changed` each cancel it. The run builds the team once (real teams) and, with a draft, runs the saved and draft columns interleaved: seed by seed, one `Replay` and `BenchTally.Add` on each, so a half run's columns cover the same seeds. Under the saved column, `pack.Tuning` and `pack.Bench`; under the draft, `pack with { Tuning = draft, Bench = draftBench }` (Task 1, Step 3). A sweep is three strengths, each saved and draft. If the draft does not load, Run is refused with the errors beside their fields and offers "Run saved only".
- [x] **Step 3: streaming.** Every 500 runs of each column, or every quarter second, whichever is later, patch the figures and the progress line ("4,000 of 10,000"). The run is on a background task fed through a channel to the response, as `Sse` streams elsewhere; a slow client does not slow the bench beyond one pending patch (drop older patches, keep the latest).
- [x] **Step 4: the figures** (design 4.8). `FigureRows` is the summary's row list as `BenchReport.Summary` has it (labels, values, formats), with each row's `FigureKind` from Task 2, computed from `BenchFigures`; `BenchReport` is not changed, so the two lists are kept equal by a test, not by sharing (sharing would change the command line's code for the page's sake; **my call**). HTML tables: one column with no draft; saved, draft, and change with one; a sweep with a draft grouped by strength. The change column has no good or bad colour; a change within the noise is greyed, and `BenchNoise.Rule` is said once under the table. Then a table per encounter (checks, approach position %, narrowed %, tiers %, deaths), each with its change column.
- [x] **Step 5: tests.** The page's saved column equals `Bench.Run` (and so `SimRunner bench`) on the same inputs, for a generated team and for a sweep (compare `BenchFigures`, not text). A half run's tallies have the same run count and seeds. A draft change cancels a running run; so does a new run on the same page. Progress arrives before the end. A refused real team shows its refusal and does not run. `FigureRows`' labels and values equal `BenchReport.Summary`'s rows for the same figures.
- [x] **Step 6:** `./check.sh`; commit "Run the bench from the Tuning page, saved and draft on the same seeds".

### Task 11: every objective

**Files:** `src/Web/Components/Tuning/EveryObjective.razor` (new), `TuningRuns.cs`, `TuningQuery.cs`, `tests/Web.Tests/TuningRunsTests.cs`.

- [ ] **Step 1:** The view's runs default to 1,000. One row per objective, columns rookie, mixed, and veteran (or the real team alone), each saved and draft when there is a draft; cells full success %, loss %, deaths per 100, and calls per mission, greyed as in Task 10. Run through `Bench.Objectives`, interleaved saved and draft as in Task 10, a row patched as each finishes. A refused cell shows its refusal.
- [ ] **Step 2:** A row opens One mission for that objective with the same inputs (a link from `TuningQuery`).
- [ ] **Step 3: tests.** Each cell equals `Bench.Run` for that objective and strength at 1,000 runs. A real team too large for an objective shows the refusal there. A row's link carries the inputs.
- [ ] **Step 4:** `./check.sh`; commit "Add the every-objective view to the Tuning page".

### Task 12: replay as the report

**Files:** `src/Web/Tuning/BenchReplay.cs` (new), `src/Web/Bridge/BridgeReader.cs` (lift the log's lines and prose into a function both call), `src/Web/Components/Tuning/ReplayPanel.razor` (new), `TuningEndpoints.cs` (`POST /dev/tuning/replay`), `tests/Web.Tests/BenchReplayTests.cs` (new).

- [ ] **Step 1: the links** (design 4.9). Under each column, its `WorstSeed` as a link, and a seed box with a saved or draft choice beside it.
- [ ] **Step 2: the report.** `BenchReplay` calls `Bench.Replay` on the run's inputs and the seed, under the chosen tuning, then renders the mission as play does: the beats wrapped as `MissionBeat`s, `Prose` over them with the trace's final state and the bench's seed in place of the legacy's, and the bridge's `Lines` (lifted from `BridgeReader` so both call it, not copied), shown with `MissionLog` with the rolls on request (its `Dev` detail), and the result's outcome and each member's status as the aftermath shows them. Names come from the live pack's `Names`.
- [ ] **Step 3: tests.** A replay's beats equal the run's `Replay` beats at that seed, so it is that run of the figures. The worst seed under the draft gives the worst harm among the draft's runs. The report's prose equals the bridge's for the same beats (feed both one test mission). Nothing is written to any legacy.
- [ ] **Step 4:** `./check.sh`; commit "Replay a bench run as the narrated report".

### Task 13: the entry points

**Files:** `src/Web/Components/Content/ContentIndex.razor`, `src/Web/Forms/TextFiles.cs`, `src/Web/Components/LegaciesPage.razor`, `src/Web/Components/DevPanel.razor`, `src/Web/Components/OrdersScreen.razor`, `src/Web/Components/BriefingScreen.razor`, `src/Web/Bridge/EditorLinks.cs`, `tests/Web.Tests/TuningEntryTests.cs` (new).

All shown in developer mode only, as the editor links are.

- [ ] **Step 1: the editor index.** Mission tuning links to `/dev/tuning`, and its text view stays. `TextFiles`' `Tuning` hint for `missions` changes from "comes later" to a link to the Tuning page ("Edit these numbers and re-run the bench on the Tuning page."). `crew.yaml`'s hint says it waits for Legacy mode (part two).
- [ ] **Step 2: legacy selection** gets a "Tuning" link beside the developer links. **Dev Play's** legacy panel gets a "Tuning" link arriving with that legacy as the team source.
- [ ] **Step 3: "Bench this"** on each objective in Orders and the briefing: that objective, that legacy as the source, and the team as picked so far (members and lead, read from the briefing's signals into the link), or nobody chosen. Build the URL in `EditorLinks` with `TuningQuery`.
- [ ] **Step 4: Bench this in the aftermath** (design 8, open). Add it if the mockup's review in Task 8 asked for it; otherwise leave it, and say which in "As built".
- [ ] **Step 5: tests.** Each link is present in developer mode and absent outside it, and lands on the page with the inputs it names. A "Bench this" with picks lands with those members ticked and that lead.
- [ ] **Step 6:** `./check.sh`; commit "Link the Tuning page from the editor, legacy selection, Dev Play, and Orders".

### Task 14: the worked example, and the record

**Files:** `content/tuning/missions.yaml` (one number, saved by the page only), `tests/Web.Tests/TuningWorkedExampleTests.cs` (new), `tests/browser/` (a script for the page, beside `edit.sh`), the GDD chapters in design section 6, `docs/gdd/11-roadmap.md`, `docs/plans/phase-1-outline.md`, `docs/gdd.md`, `GLOSSARY.md`, and the design's "As built".

- [ ] **Step 1: the web test through the real app.** Open Tuning from "Bench this", change a number, run, see three columns, save, and see the next mission's starting state carry the new tuning. On the fixture, so it does not depend on the game's numbers.
- [ ] **Step 2: the example, in the browser** (design 4.10). Run the app and drive the page in a browser session: from an objective in Orders on a test legacy, sweep the strengths, and read the rookie loss (about 34%). Choose one number, change it in the draft, run, and read rookie loss fall while veterans barely move. Replay the worst rookie run under the draft. Save. On the legacy, launch the next mission and check it used the new number (Dev Play's resolution detail, or its event log's mission state). The number and its value are generated choices under the author's standing allowance for this slice; record which number and why, for the author to judge. Note every awkward moment as it happens.
- [ ] **Step 3: the goldens.** The save will move goldens that run on the game's pack. Record each before and after in the commit, and nothing else may move.
- [ ] **Step 4: the GDD** (design 6, "Applied when part one is built"). 4.7's tuning paragraph: the page as built, and the example's number and figures before and after. 10.1's "Where": the page and its entry points. 7.5's developer pages: Tuning. 11.1's row for part one: "Done", with a pointer to "As built". Bump the status line and name the slice. Rebuild and republish the GDD page (`tools/gdd-page/README.md`).
- [ ] **Step 5: glossary and progress.** `GLOSSARY.md` gains **Draft** (of a tuning file) and **Within the noise**. The outline's paragraph says "Done", with the date and a pointer to "As built". The next row (1f-previews) becomes "Next".
- [ ] **Step 6: As built.** The design's section 9, task by task: what changed and why, including Task 1's draft-column teams, Task 3's comment rules, Task 5's mechanism and what it leaves unset, Task 7's error placement, Task 10's row list kept by a test, Task 13's aftermath choice, and the mockup's decisions. Then the awkward moments from Step 2, and what's left for the author to judge (the number chosen and its value).
- [ ] **Step 7:** `./check.sh`, including the browser tests; commit "Tune rookie loss on the Tuning page, and record part one".
