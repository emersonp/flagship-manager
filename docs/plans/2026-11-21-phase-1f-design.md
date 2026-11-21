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

- **`MissionBench`** gains `BenchTally`, fed one run at a time and readable at any point, which `Bench.Run` is now a loop over; cancellation and progress on `Bench.Run`; the figures needed for the noise test (each mean's sum of squares); an every-objective run over the same options (`Bench.Objectives`); and the mission's last state on each trace. It stays free of the web and of any legacy.
- **`Sim.Content`** gains reading a tuning file's leaves with their key paths and comments, and replacing scalars in place in the text (the tuning files only; the list kinds keep the writer).
- **`Sim.Crew` and `Sim.Traits`**: the launch's team building is reachable as one function, if it is not already, so the bench and the launch share it.
- **`Web`** gains the Tuning page, the draft singleton, the form, the streamed runs, replay through `Sim.Narrative`, the entry links, and the editor index's link.
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
