# Phase 1, slice 1f: tuning in play

**Spec:** `docs/gdd.md` sections 4.7, 10, and 10.1, and section 15, item 46. **Builds on:** `main` at 2026-11-21 (`fdda9e3`): slice 1e's first six parts and the calendar.
**Status:** part one designed 2026-11-21; part two sketched, to be designed before it is built.
**Ends with (part one):** the author opens Tuning from an objective in Orders, sweeps rookie, mixed, and veteran teams, and sees rookies lose about a third of their missions. They change one number in the draft, press Run, and see rookie loss fall while veterans barely move, saved and draft side by side on the same seeds. They replay the worst rookie run as the report play would show, press Save, and the legacy's next mission uses the new number. Nothing was typed into a YAML file, and nothing left the game.

This records what the author decided on 2026-11-21, in a design interview, and designs it. Items marked **(my call)** were proposed by me, not asked for by the author.

## 1. The split: two parts

1f is cut in two. **Each part has its own plan; this design covers part one in full and sketches part two**, which gets its own design just before it is built, once part one has been used.

| Part | Adds | Measures | Tunes | Status |
|---|---|---|---|---|
| **1f, part one. The mission bench in play** | The Tuning page: a draft of the tuning file beside the mission bench, saved and draft side by side on the same seeds; teams from a legacy or a test ship; an every-objective summary; replay as the report | One mission, many runs | The resolver's numbers in `tuning/missions.yaml`, and `bench.teams` | Designed here |
| **1f, part two. Legacy mode** | A Legacy tab on the same page: whole legacies (`SimRunner cycles`) on the same seeds under saved and draft tuning; every tuning file in the form; alarms | Whole legacies over many cycles, storylets and vignettes firing as in play | Every tuning file | Sketched (section 7) |

Why two modes: the mission bench answers "is this encounter fair to this team", fast and in detail. Only whole legacies can show what happens between missions: storylets and vignettes moving morale and traits, growth, healing, drift, and deaths compounding. The author asked whether 1f should wait for a bench that captures every mechanical effect; the answer is that mission mode captures every effect a mission *starts with* (once real teams carry them, section 4.5), and legacy mode captures the rest.

## 2. What the author decided (2026-11-21)

- **Two modes, two parts:** mission mode now, legacy mode next (section 1).
- **The order stays 1f, 1g, 1h.** 1h (skills, attributes, and gear) reshapes what a character is, so 1f keeps the part that 1h would change thin: generated teams stay exactly as they are, and **starting conditions (morale, traits, ship traits, injuries) come only from real teams**, from a legacy or a test ship. No controls for generated conditions. **`TeamGenerator` and the `bench.teams` block belong to 1h**, which updates them.
- **Edits are a draft held by the page, saved to `content/`.** Run benches the saved tuning (before) against the draft (after) on the same seeds; Save writes the file through the editor's validated path; git is the undo. No override file. This settles the outline's open decision for 1f.
- **The draft lives in the server's memory,** one per tuning file, shared by every tab, cleared by Save or Discard, and lost on restart; the page says so. Drafts in the browser wait for the online track.
- **A form generated from the schema, saved in place.** Each number is a labelled field grouped by its YAML section, with the file's own comment as its hint; dice are count and sides steppers. Save replaces values inside the existing text, so comments and layout survive. The text view stays.
- **Numbers the bench cannot see are read-only in part one:** `storylets`, `salience`, `vignettes`, `captainsTime`, and `history`, labelled "Measured in Legacy mode (part two); edit as text for now".
- **One developer page, "Tuning",** with mode tabs (Mission now, Legacy in part two) sharing the draft.
- **Entry points,** developer mode only: the editor index, legacy selection, Dev Play, and "Bench this" on each objective in Orders and the briefing.
- **Real teams** from a legacy's roster or a test ship's crew, each member built by the launch's own code. The legacy is read, never written.
- **Every input the command line has,** runs defaulting to 10,000.
- **An every-objective summary,** 1,000 runs a cell.
- **Run on demand,** streamed with progress, cancelled by any change.
- **Figures as tables** with a change column: no good or bad colour; changes within the noise greyed.
- **Replay** of the worst run or any seed, as the narrated report.
- **No alarms in part one;** they are decided with legacy mode.
- **The worked example** is the rookie loss rate.
- **The split is shown on the GDD page:** 11.1's 1f row becomes two rows, and the page is republished.

## 3. Goals, non-goals, anti-goals

**Goals**
- Every number the resolver reads can be changed and measured without leaving the game or opening a YAML file.
- Before and after are always the same seeds, so a change in the figures is the change in the number.
- A team as it stands in a legacy, with its morale and traits, can be benched as easily as a generated one.
- Any run in the figures can be read as play would show it.

**Non-goals**
- Whole legacies in the browser (part two).
- Forms for `crew.yaml`, `calendar.yaml`, and `chronicle.yaml` (part two).
- Alarms and target bands (part two).
- Generated teams with conditions, and any change to `TeamGenerator` (1h).
- Saving a bench run as a baseline to compare later; before is always the saved file.

**Anti-goals**
- A second store of tuning. The YAML in `content/` is the only tuning; the draft is a view that is saved into it or thrown away.
- A bench that disagrees with play: a real team benched must be the team the launch would send, and a run under the saved tuning must equal `SimRunner bench` on the same inputs.
- A save that loses the file's comments.
- Writing to a legacy, its event log, or its chronicle (GDD 10.1, isolation).

## 4. Design

### 4.1 The page

`/dev/tuning`, developer mode only, in the editor's frame **(my call)**. Two columns on a wide screen, stacked on a narrow one:

- **The draft** (section 4.2): the tuning form, changed fields marked, with Save and Discard at its head and a line saying when the draft began and that a restart loses it.
- **The bench** (sections 4.4 to 4.8): tabs **Mission** (part one) and **Legacy** (part two, absent until then). Mission has two views: **One mission** (the default) and **Every objective**. Inputs above, Run, then the figures.

The page's state (inputs, view, the last figures) is in the URL's query where it is small enough to share, so a "Bench this" link and a reload land on the same inputs **(my call)**. Seeds stay out of signals as 64-bit numbers (outline, carried forward to the browser part): the seed travels as text.

### 4.2 The draft and the form

**The fields** are the file's scalar leaves, in file order, read from the file's own YAML tree: each leaf is a field named by its key, grouped under its section's heading (`check`, `compounding`, `develop`, `calls`, `experience`, `injury`, `result`, `bench`), with the trailing comment on its line as its hint and a comment line above a section as the section's note. A `{ count, sides }` map is one dice field of two steppers. **The loader is the judge** of each value, as everywhere in the editor: its errors are shown beside the field they name. Reading the fields from the file, not from a hand-written list, is how "generated from the schema" is met: a number added in 1g or 1h appears in the form when it appears in the file **(my call)**.

**Read-only sections:** `storylets`, `salience`, `vignettes`, `captainsTime`, and `history` show their values and hints, greyed, under the label above, with a link to the file's text view.

**The draft** is held per tuning file in a singleton beside the content host **(my call)**: the file's text as the draft began (with its hash), and the changed values by key path. Every tab sees the same draft. A changed field shows the saved value beside it. **Discard** throws the draft away. A draft whose file has changed on disk since it began says so at its head and offers Discard or Save anyway, which is refused (section 4.3).

**Running on the draft** builds a `MissionTuning` from the file's text with the draft's values put in, through `ContentLoader.LoadTuningText`. If the draft does not load, Run is refused and the errors are shown beside their fields; the saved column alone can still run.

### 4.3 Save

- Save replaces each changed value **in place in the file's text**: the scalar at that key path, and nothing else. Comments, spacing, key order, and flow maps are unchanged **(my call on the mechanism; the author decided the outcome)**.
- The new text goes through `ContentEditor.Save`, so the whole pack is loaded with it and a refusal is shown beside the field it names, as for any form.
- **Save is refused** if the file's hash is no longer the one the draft began from (an edit in the text view, by hand, or a save from another draft), and the page shows the keys whose saved values changed. The author discards, or copies their values over by hand.
- After a save, the live pack is swapped as for any edit. **A mission in flight keeps the tuning it started with** (GDD 4.7); every mission that starts later uses the new numbers. The pack's version follows the editor's rule for a save.

### 4.4 Inputs

As the command line's `bench` (`SimRunner/Cli.cs`), one control each:

- **Objective**, and **Draw**: redrawn each run (tests the template) or fixed (tests one offer).
- **Runs** (10,000; 1,000 in Every objective) and **Seed** (1).
- **Team**: Generated (rookie, mixed, veteran, or sweep), Legacy, or Test ship (section 4.5).
- **Lead style** (cautious, average, reckless): generated teams only, since a real lead has their own.
- **Autonomy, priority, develop juniors**: the operational parameters.
- **Gear**, by tag.
- **Call policy**: back the lead, bold, safe, or withdraw.

### 4.5 Real teams

- **Legacy:** choose a legacy, then members from its roster and a lead. **Test ship:** choose a ship from `content/testships/`, then members and a lead.
- Each member is built by **the code the launch uses**: `RosterView`'s `TeamMember` (skills less injuries, morale bonus) and the traits module's launch view (traits, trait bonuses, ship traits, launch rules, stand-downs). A member the launch would stand down is shown so, with the reason, and cannot be picked, as in the briefing. The bench must not grow its own copy of this; if the launch's path is not reachable from outside a command, it is lifted out so both call it.
- A legacy is read through the read that does not create (outline, carried forward to the browser part), under its lock only while it is read. Nothing is sent to the bus. A test ship is read as the editor's previews read it.
- The same team is used for every run, so the draft and the saved columns differ only in the tuning.

### 4.6 Entry points

All in developer mode only:

- **The editor index:** Mission tuning links to the Tuning page; its text view stays, with the hint changed from "comes later" to a link.
- **Legacy selection:** a "Tuning" link beside the developer links.
- **Dev Play:** a "Tuning" link on the legacy's panel, arriving with that legacy as the team source.
- **"Bench this"** on each objective in Orders and the briefing: arrives with that objective and that legacy, and the team as picked so far (members and lead), or the legacy with nobody chosen.

### 4.7 Running

- Nothing runs by itself. **Run** starts the run on a background task; the figures stream into the page through Datastar as they are computed, with a progress line ("4,000 of 10,000"). The saved and draft columns run interleaved, so a half-finished run is still a fair comparison **(my call)**.
- **Any change to the draft or the inputs cancels a run in progress**, and so does leaving the page. One run per page at a time.
- With no draft, one column. With a draft, saved, draft, and change. A sweep with a draft gives six columns, grouped by strength (section 4.8).
- `Bench.Run` gains a cancellation and a progress callback; the command line's output does not change.

### 4.8 Figures

**One mission:** `BenchReport`'s summary rows (results, deaths and incapacitations per 100 crew-missions, calls and initiatives per mission, experience per member, and reaching, momentum, and strain at each encounter), then a table per encounter (checks, approach position, narrowed %, tiers, deaths). Drawn as HTML tables from `BenchFigures`, not from the text tables.

**Every objective:** one row per objective; columns rookie, mixed, and veteran, each saved and draft when there is a draft; cells full success %, loss %, deaths per 100, and calls per mission. A row opens that objective in One mission with the same inputs.

**The change column** has no good or bad colour, because whether more loss is good depends on what is being tuned. **A change within the noise is greyed:** smaller than twice the standard error of the difference between two independent samples of that size (a share as a proportion, a rate per crew-mission as a count, a mean from its runs' own spread) **(my call on the test; the author decided to grey)**. The rule is conservative, since same seeds make the true noise smaller, and is said once under the table.

### 4.9 Replay

- The figures name **the worst run's seed** (already kept as `BenchFigures.WorstSeed`), as a link, under the saved and the draft tuning each. A seed box replays any run.
- A replay shows **the narrated report**: the mission log in prose with tags, through `Sim.Narrative` as play draws it, with the rolls on request, and the aftermath's result. Under the saved or the draft tuning, chosen beside the seed.
- A replay is `Bench.Replay` on the same inputs and seed, so it is exactly that run of the figures.

### 4.10 The worked example: rookie loss

On the game's own pack, rookies lose about 34% of missions (GDD 4.7, after the 1d review). The author opens Tuning from an objective in Orders, sweeps the strengths, changes one number they choose, runs, and sees rookie loss fall while veterans barely move. They replay the worst rookie run under the draft, save, and the legacy's next mission uses the new number. The number chosen and the figures before and after are recorded in GDD 4.7, as the bench's other figures are.

### 4.11 Tests

- **Equivalence:** the page's run under the saved tuning gives the same `BenchFigures` as `SimRunner bench` on the same inputs and seed, for a generated team and a sweep.
- **Real teams:** a team built from a legacy equals the team the launch sends for the same picks, morale bonus, traits, trait bonuses, and ship traits included; a stood-down member cannot be picked. The legacy's event log and stored state are byte for byte the same after a bench run.
- **Save in place:** changing every editable value and saving changes only those scalars; the rest of `missions.yaml`, comments included, is unchanged, and the loader reads the new values back. A save over a file changed on disk is refused and names the keys.
- **The draft:** a draft that does not load refuses Run and puts each error beside its field; Discard restores the saved values; two tabs see one draft.
- **Running:** a change to the draft cancels a run; progress arrives before the end; a half run's columns cover the same seeds.
- **Noise:** a known pair of shares either side of the threshold greys one change and not the other.
- **Web tests through the real app:** open Tuning from "Bench this", change a number, run, see three columns, save, and see the next mission's state carry the new tuning.
- **No golden moves:** nothing here changes play.

## 5. Architecture

### 5.1 Modules

- **`MissionBench`** gains `BenchTally`, fed one run at a time and readable at any point, which `Bench.Run` is now a loop over; cancellation and progress on `Bench.Run`; the figures needed for the noise test (each mean's sum of squares); an every-objective run over the same options (`Bench.Objectives`); the mission's last state on each trace; and `BenchNoise`, the noise rule (section 4.8) and the one list of which rows are shares, rates, and means. It stays free of the web and of any legacy.
- **`Sim.Content`** gains reading a tuning file's leaves with their key paths and comments, and replacing scalars in place in the text (the tuning files only; the list kinds keep the writer).
- **`Sim.Missions`** gains `LaunchTeam`: the launch's team building (`Build`, from the roster and trait views) and its checks on a member (`Refusal`), on the picks as a whole (`PicksRefusal`: listed twice, a member refused, the lead not picked), and on a team's size (`SizeRefusal`), lifted out of `MissionsModule` so the bench and the launch share them. `Sim.Crew` and `Sim.Traits` are unchanged: their views were already reachable from outside a command; only the assembling of the team was inside one.
- **`Web`** gains a test ship founded as a legacy in memory (`TestShipLegacy`), so a test ship's team is built through `LaunchTeam` on the crew and traits modules' own views, the bench's real teams from either (`BenchTeams`, in `src/Web/Tuning/`), the Tuning page, the draft singleton, the form, the streamed runs, replay through `Sim.Narrative`, the entry links, and the editor index's link.
- **`SimRunner`** is unchanged in behaviour.

### 5.2 Rules that hold

- The YAML in `content/` is the only stored tuning; the loader judges every save.
- A bench run never writes to a legacy, its event log, its chronicle, or the bus.
- Developer mode only, as every editor page; never in a hosted game (outline, carried forward from the content editor).
- No event, state, or golden changes.

## 6. Roadmap and GDD changes

Applied with this design:

- **GDD 11.1:** the row "1f. Tuning in play" becomes two: **1f, part one. The mission bench in play** (Next) and **1f, part two. Legacy mode** (Later).
- **GDD section 15, item 46** records these decisions.
- **The outline:** the 1f row is split, the paragraph after the table names this design, and the "Decisions each slice plan must make first" entry for 1f is answered.
- The GDD's status line moves to v1.28, and the page is rebuilt and republished.

Applied when part one is built: GDD 4.7's tuning paragraph (the page as built, and the worked example's figures), GDD 10.1's "Where" (the page and its entry points), GDD 7.5's developer pages, and 11.1's row "Done" with a pointer to "As built".

## 7. Part two, sketched: legacy mode

Fixed now, because part one builds the ground it stands on:

- **A Legacy tab on the Tuning page,** sharing the draft.
- **`SimRunner cycles` on the same seeds** under saved and draft tuning, side by side, through the same streamed, cancellable runs.
- **Every tuning file in the form,** `crew.yaml`, `calendar.yaml`, and `chronicle.yaml` included, and the sections read-only in part one become editable. The draft becomes one per file, saved together.
- **Storylets and vignettes fire as in play,** so their effects on morale and traits are in the figures.
- **Alarms** (GDD 10's target bands) are decided here.

Open for its own design:

- Run size, and how long a run may take in the browser.
- Which figures to show: results, deaths per 100 crew-missions, morale and loyalty over time, skill growth curves, storylet and vignette fire counts and never fired, losses on the budget.
- The simulator captain's policies (GDD 10's four), and whether the page can choose one.
- Where bands live, and whether an alarm blocks a save or only warns.

## 8. Open, and left to the builder

- The look of the page: from `docs/design/`'s recipes, mocked up first in `docs/screens/1f/`.
- How a long objective list reads in Every objective on a narrow screen.
- Whether the read-only sections fold away by default.
- Whether "Bench this" also appears in the aftermath, for the mission just played (the same team, the same objective).

**Next after part one:** slice 1f-previews (GDD section 15, item 52) moves the encounter, objective, and line previews onto test ships. It starts a mission on a test ship's crew through the same launch team building as section 4.5, so part one should leave that function callable with a test ship's state, not only a legacy's.

## 9. As built

### Task 1: the bench as a tally, with cancellation, progress, and spread

- `BenchTally` (new, `src/MissionBench/BenchTally.cs`) holds the loop body that was in `Bench.Run`; `Figures()` copies its counts, so a snapshot read part way does not move as more runs are added. `Bench.Run` is a loop of `Replay` and `Add`. Its figures are unchanged, field for field: `tests/MissionBench.Tests/golden-figures.txt` pins them on the fixture and the game's pack (generated teams of each strength, a real team, fixed draw, gear, every call policy but one), captured before the change. Since Task 14 saves a number into `content/tuning/missions.yaml`, its game-pack lines will move then, as any golden on the game's pack does.
- **The worst seed** is the run with the most harm, the lowest seed among equals. Fed in seed order that is today's rule (the first run with the most harm); it also makes the seed the same in whatever order a tally is fed **(my call, reading the plan's "whatever order")**.
- **Cancellation** throws `OperationCanceledException`, checked before the first run and every 100 runs (`Bench.ProgressEvery`); progress reports the runs done at each 100 and at the end. A token cancelled before the run starts runs nothing.
- **The spread**: `BenchFigures` gains `CallsSquares`, `InitiativesSquares` (as `long`), and `ExperienceSquaresByMember`; `StageFigures` gains `MomentumSquares` and `StrainSquares`. Each is a sum of squares, one term a run. The command line's printed text is unchanged (`tests/SimRunner.Tests/golden-bench.txt` pins a single team, a sweep, `--compare`, and `--replay`, captured before the change). **`--out`'s JSON gains these fields**, since it serializes `BenchFigures` whole; it is a file, not printed output, and nothing reads it back.
- **Every objective**: `Bench.Objectives` returns `ObjectiveCell`s in the pack's order, one per strength asked for, or one per objective for a real team, refused in `MissionsModule`'s words ("A team of 4 is outside the 2 to 3 this objective takes.") when its size does not fit. Progress there is the runs done across every cell. The sentence is written out again rather than shared with `MissionsModule`; Task 4 is where the launch's checks are lifted out.
- **Generated teams follow `pack.Bench`**, as they did; a test now says so, since the draft column will pass `pack with { Bench = draftBench }` (Task 10).
- `MissionTrace` gains `State`, the mission's last `MissionState`.

### Task 2: the noise rule

- `BenchNoise` (new, `src/MissionBench/BenchNoise.cs`) is design 4.8's test: `Within(kind, a, b)` is true when the change is under twice the standard error of the difference, each column's variance a share's `p(1-p)/n`, a rate's `count/n²`, or a mean's sample variance over `n`. A column of no runs is never within. `BenchNoise.Rule` is the sentence said once under each table.
- **Which rows are which** is one list, `BenchNoise.KindOf(BenchRow)`, so Task 10's tables ask it rather than decide in a component: results and reached are shares of runs; deaths and incapacitations rates of crew-missions; calls, initiatives, experience, momentum, and strain means; an encounter's tier, position, and narrowed shares of its checks, and its deaths and incapacitations rates of crew-missions. The encounter's incapacitations are not in the plan's list; they are rates, as the summary's are.
- **Two edges (my call):** a change of exactly nothing is within the noise, even where neither column has any spread (0 deaths against 0), so an unchanged row is greyed rather than shown as a change; and a mean over a single run has no sample variance, so it is taken as 0.

### Task 3: a tuning file's fields, and saving them in place

- `TuningFields` (new, `src/Sim.Content/TuningFields.cs`) reads a tuning file's fields from its own text: YamlDotNet's parser for the structure and each scalar's span, and its scanner, with comments kept, for where each comment starts (so a `#` inside a quoted scalar is not one). Fields are the scalar leaves in file order, each with the loader's dotted path (`check.tiers.critical`); a `{ count, sides }` map is one Dice field and a two-item sequence one Range field, whose path is the map's or the list's (`check.dice`, `bench.teams.rookie.skill`; the loader's own are `.count` and `.sides`, `.0` and `.1`). A section is a top-level key; a top-level key that is itself a leaf (as `chronicle.yaml`'s `notable`) is a section of one field named by the key. A list of any other length is refused, as is an anchor or alias.
- **The comment rules:**
  - A field's hint is the comment trailing its key's line, after any comment lines just above it.
  - A comment with two or more spaces after its `#` continues the comment on the line above, and joins that comment's owner: `boldPriorityWeight`'s comment completes `boldCautionWeight`'s hint, and `boldPriorityWeight` has none of its own; `supervisorGapDivisor`'s completes `supervisorPenalty`'s. Joined lines are separated by a space.
  - A section's note is the comment on its key's line, after any comment lines just above it (`salience`'s "1e part four: see ...").
  - The file's leading comment (its comment lines before the first line that is not one) is nobody's.
  - **A nested map's key comment is the hint of the first field under it (my call):** `tiers:`'s "the lowest margin for each tier; below failure is a disaster" is `check.tiers.critical`'s hint. The plan names hints for leaves and notes for sections, and a nested key is neither; the form will show this hint at the head of the group it explains, and dropping it would lose the one explanation of the tiers.
  - A comment on its own line that is neither above a key nor a continuation is nobody's.
- `Replace` splices each new value over its scalar's span, last to first. Nothing else in the text changes, so a wider value pushes the rest of its line to the right: the trailing comment stays on its line, a column further along, and is not realigned. **It refuses** a path that is not a field, a wrong count of values for a dice or range, an empty value or one with a control character (a line break), and any value that does not read back as written once spliced in (a `#`, a `: `, a bracket): `Replace` reads its own result again and checks every path it changed, so it cannot change the file's shape. It does not judge the value as a number; the loader does that (Task 7).
- **Tests** (`tests/Sim.Tests/TuningFieldsTests.cs`): the fields of the game's `missions.yaml` are exactly the paths the loader reads, gathered by loading a file with none of them and taking each path it reports missing, so no test hook was needed. Replacing every field with a marker of a different width and putting the old values back over the markers gives the file character for character. Every field changed to one more than its value (which keeps every order the loader checks) is read back by `LoadTuningText`. A wider value keeps its comment on its line; dice and ranges round-trip in their flow style; a file with Windows line endings is replaced in place.

### Task 4: the launch's team, as one function

- `LaunchTeam` (new, `src/Sim.Missions/LaunchTeam.cs`) holds what `StartMission` did to build the team and what `AssignTeam`'s check did to a member: `Build(roster, traits, crewIds, leadId)` gives each member from `IRosterView.Member` (skills less injuries, morale bonus) with their own traits and this team's trait bonuses from `ITraitView`, and the ship's traits; `Refusal(roster, crewId)` is "not on the roster" or "not fit", in the check's words. `MissionsModule` calls both, and `Own` moved with `Build`. It lives in `Sim.Missions`, not `Sim.Crew` or `Sim.Traits` as section 5.1 first put it, because the views were already reachable; only the assembling was inside a command. It reads views and nothing else, so a test ship founded in memory (Task 5, and slice 1f-previews) calls it the same way.
- **The size check is shared too (my call):** `LaunchTeam.SizeRefusal(objective, size)` is the "A team of 4 is outside the 2 to 3 this objective takes." sentence, called by `AssignTeam`'s check and by `Bench.Objectives`, which Task 1 had written out again. `MissionBench` already references `Sim.Missions`, so no reference was added.
- **The ship's traits are now read before `MissionStarted` is published,** where `StartMission` read them just after. The launch rules that run on it change crew traits, morale, and loyalty, never the ship's (`TraitsModule.Apply` only ever names a crew member), so the team is the same; every golden hash and chronicle is unchanged.
- **One word changed, in an exception, not a refusal:** a member the roster no longer has at the launch threw "... is no longer available for mission m_2_1."; `Build` has no mission id, so it says "... is no longer available." Nothing reaches it in play, since the check refuses such a member first.
- **Tests** (`tests/Sim.Tests/LaunchTeamTests.cs`): on a legacy with an injured member holding a crew trait with a check bonus, a low-morale member holding a pair toward the first (a bonus with its target on the team), and a ship trait, `Build` at the briefing shows each condition and equals, as JSON, the resolver's starting team and ship traits after the launch. The test passes against `MissionsModule` as it was before the move, too. `Refusal` names an unknown and an unfit member; `SizeRefusal` refuses either side of the range and passes its ends.

### Task 5: a test ship as a legacy in memory

- `TestShipLegacy.Found(live, ship)` (new, `src/Web/Forms/TestShipLegacy.cs`, beside `TestShipCapture`) sends `FoundLegacy` on a fresh `LegacyState` (seed 1, an empty legacy id) on the live pack's game, drops the events, and never stores it. It then sets the day and cycle to the ship's, each listed member's morale, loyalty, and skill levels (experience left at 0), and status, and replaces the traits module's list with the ship's, each `ChangedAtDay` and `ChangedAtCycle` counted back from the ship's today, with no encounter. The `IRosterView` and `ITraitView` are then the modules' own, through `game.Bus.View<T>(state)`, so neither the trait bonuses nor the launch previews are copied. Section 5.1's `Web` line says so.
- **Away has no state of its own in play (my call):** the crew module knows Active, Recovering, and Dead, and play reads "aboard" as on the roster. A member the ship lists as not fit or not aboard is `Recovering`, with no day they return on (the ship gives none), so the launch refuses them as unfit; a dead one is `Dead`, off the roster. This is how the editor's storylet previews already turn a ship's crew into play's (`Plays.Storylets`'s `Crew`). So on a founded ship, `IRosterView.IsFit` is the ship's fit and aboard together, and `Roster` is its living crew, away members included.
- **Left unset:** history, the chronicle, and storylet and vignette arrivals are as founding leaves them, not the ship's, and `StartDay` stays the calendar's start; no light injuries, and no day a recovering member returns. Nothing the launch reads uses them. 1f-previews, which plays storylets after a launch on a test ship, will need history and arrivals if anything it runs reads them; it sets them then.
- **Tests** (`tests/Web.Tests/TestShipLegacyTests.cs`): on every test ship of the game's pack and the fixture's, the founded legacy's roster, each member's morale, loyalty, morale bonus, every skill, and fitness (as fit and aboard), every trait's value on the ship, each member, and each pair (held or not), and each held trait's value and when, agree with `TestShipSource`. The fixture's unfit, away, and dead members are read so. Founding every ship leaves the store with one legacy and its events unchanged.

### Task 6: the bench's real teams

- `BenchTeams` (new, `src/Web/Tuning/BenchTeams.cs`, registered as a singleton) gives a source's candidates and builds its team. `Legacy(id)` reads the legacy with `LegacySession.Read`, which holds the lock only while it loads, and lists its roster in roster order, each member with `LaunchTeam.Refusal`, named by the live pack's names. `TestShip(id)` loads the ships as the editor's previews do (`ContentLoader.LoadTestShips` on the editor's files and the live pack), founds the ship with `TestShipLegacy.Found`, and lists the same. `Build(kind, id, picks, lead)` reads or founds the source again and builds with `LaunchTeam.Build` on the crew and traits modules' own views, once for a run. Nothing is sent, published, or stored.
- **The picks are checked by the launch's own code (my call):** `LaunchTeam.PicksRefusal(roster, crewIds, leadId)` (new) is `AssignTeam`'s check after the size, lifted out as Task 4 lifted the rest: a member listed twice, a member `Refusal` refuses, and a lead not on the team, in that order and in its words. `MissionsModule` calls it; no words changed, and no golden moved. The bench adds only what the launch has no words for: "Nobody is picked." for an empty team (the launch's size check covers it there, and the bench's team has no objective until it runs; `Bench.Objectives` checks the size), and "No legacy '...'." or "No test ship '...'." for a source that is gone.
- **"`LockCount` is back to 0"** is read as "no lock added and none held": `LockCount` counts the locks kept, one for each stored legacy, so it is never 0 while the legacy exists. The test checks that the count is unchanged after `Legacy`, `Build`, and an unknown id, and that a run takes the legacy's lock at once afterwards.
- **Tests** (`tests/Web.Tests/BenchTeamsTests.cs`): on a fixture legacy at the briefing with an injured member holding a crew trait, a low-morale member holding a pair toward the first (a check bonus with its target on the team), an unfit member, and a ship trait, the built team shows each condition and equals, as JSON, the resolver's starting team and ship traits once the same `AssignTeam` is launched. The unfit member is listed with the refusal, and every launch refusal comes back in its words. The game's ship's grieving member carries `grieving` (and the pair `avoids` toward CREW_06, and the ship's `sensors_quirk`), on a `BenchTeams` over the game's pack that only reads `content/`; the fixture's ship's unfit and away members are refused alike. After `Legacy` and `Build`, `LegacyExport.Export` of the legacy (its stored record and its event log) is the same string.

### Task 7: the draft

- `TuningDrafts` (new, `src/Web/Tuning/TuningDrafts.cs`, a singleton registered beside `ContentHost`) holds one draft per tuning file under a lock: the file's text and SHA-256 hash as the draft began, when it began (`GameServices.Clock`), and the changed values by the loader's path, a dice or range field's two values together as `TuningFields` names them. Only `tuning/missions.yaml` can hold a draft in part one (`TuningDrafts.Files`); part two adds the rest. Every tab sees the one draft, and a restart loses it.
- **`Set` returns the draft, or null when the change empties it (my call):** setting a field back to its saved value takes the path out, and the last one out ends the draft, so there is no draft to return. `Set` refuses a path that is not a field of the file and the wrong count of values for one; the values themselves are left to `Load`, as the loader judges them. `Changed` is raised with the file whenever a draft's values change, it is discarded, or it is saved.
- **Where each error goes** (`TuningDrafts.Place`, which the page also uses for a save's refusal): a message that begins with a field's path, followed by a space or a value's index (`bench.teams.rookie.skill.0 must not be above ...` goes to the range `bench.teams.rookie.skill`, `check.dice.count ...` to the dice `check.dice`), goes to that field. **One message cannot be placed by a field's path today:** `check.tiers must fall from critical to failure.` names the group, not a field, so it goes to the group's first field, `check.tiers.critical` (where Task 3 put `tiers:`'s comment too). `ContentError` is unchanged. A message with no path is one of the rest; one in another file (the whole pack is loaded on a save) is one of the rest, named by its file. Two errors on one field are joined.
- **A value that cannot be put in plain** (a `#`, a `: `, a line break) is refused by `TuningFields.Replace`; `Load` then tries each value alone and puts each refusal beside its field, with no tuning.
- **Stale and its keys:** a draft is stale when the file on disk no longer hashes to its base; the changed keys are the fields whose values differ between the base text and the disk's, both read with `TuningFields.Read`, in the base's order (and any field only the disk has). An edit to a comment alone makes a draft stale with no keys. A stale save is refused with one error saying the file changed and one per key ("check.gearBonus has changed in the file since this draft began."), which `Place` puts beside each field; the draft is kept, to be discarded or copied over by hand. The hash is checked again inside `ContentEditor.Save`'s change, under its lock, so a write that gets in between is refused too.
- **A save** replaces the values in the text on disk with `TuningFields.Replace` and hands it to `ContentEditor.Save`, which loads the whole pack, writes the one file, and swaps the live pack. A saved draft is cleared; a value set while the save ran stays, as a draft on the text just written (my call). A mission in flight keeps its tuning, since `MissionState` carries it.
- **Tests** (`tests/Web.Tests/TuningDraftsTests.cs`, on a scratch copy of the fixture): a draft that does not load gives each error beside its field (a whole-number check, a range check, a range field's order, the tiers' order on `check.tiers.critical`, a value with a `#`) and no tuning; setting back and discarding leave no draft, with `Changed` raised each time; two lookups of the singleton see one draft; a comment edit on disk makes the draft stale with no keys, a value edit names `check.gearBonus` and `injury.dice`, and the save is refused with the file untouched; a save changes the file only at the changed spans, the live pack has the new numbers, and a draft the pack refuses comes back with the loader's words beside the field; a mission waiting on a call when `check.target` is saved from 7 to 9 answers its calls to the end on 7, and the next cycle's mission starts on 9.
