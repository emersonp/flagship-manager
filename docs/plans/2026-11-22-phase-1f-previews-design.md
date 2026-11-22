# Phase 1, slice 1f-previews: previews on test ships

**Spec:** `docs/gdd.md` sections 6 and 7.4, and section 15, items 52 and 54. **Builds on:** `main` at 2026-11-22 (`b4086fc`): slice 1e, part six (test ships and the storylet previews cast on them) and slice 1f, part one (`LaunchTeam`, `TestShipLegacy`, and `BenchTeams`).
**Status:** designed 2026-11-22; to be built from `2026-11-22-phase-1f-previews-plan.md`.
**Ends with:** the author opens an encounter in the editor, picks USS Quality Assurance in What plays here, and writes a line for a critical on its second approach. The preview shows that line in a real run on the ship, with "Seed 212, Okafor leads, with Reyes", and says "wins in 9 of 61 runs that met this encounter". They press Next sample and see it with another team (names here are illustrative). The line form and the lines list's lint read the same kind of runs, and `NarrativeSamples` is gone.

This records what the author decided on 2026-11-22, in a design interview, and designs it. Items marked **(my call)** were proposed by me, not asked for by the author.

## 1. What the author decided (2026-11-22)

- **All three 1d surfaces move to test ships:** the encounter and objective forms' What plays here, the line form's preview, and the lint on the lines list. `NarrativeSamples` is deleted. `ChronicleSamples` stays: it is the chronicle module's own, and the storylet previews already use it.
- **Real runs, searched.** A preview shows a run on the ship where its beat holds, found among seeds 1 to N, with how often it holds and wins. No beat's facts are forced. A line that **can never hold** at the item (today's static check) is told apart from one that **did not happen** in N runs on this ship.
- **The bench's way of running.** The ship is founded in memory (`TestShipLegacy.Found`), the team built with `LaunchTeam.Build` on the crew and traits modules' own views, and the mission run on `MissionResolver`. No command is sent, nothing is published or stored.
- **The preview stops at the mission's beats,** through the result. No aftermath, no storylets or vignettes after the launch, and no chronicle row on the panels. So the ship's history and arrivals, which 1f part one left unset on a founded ship, are still not needed.
- **The form as posted is run.** The encounter or objective is built into a candidate pack, as the storylet form's is, because an edit to an approach, a difficulty, or a decision changes the mission and not only its words. When the candidate does not load, the stored item is run in its place, greyed, and the panel says why.
- **No new line facts.** A person's facts stay `id`, `caution`, `discipline`, `seniority`, and `isLead`, so a ship's traits, morale, and injuries change who goes and how the dice fall, but not which lines may hold. Whether lines should read them is a new open question (GDD 16, question 8).
- **The ship select of the storylet forms,** on the encounter, objective, and line forms: the first ship by default, kept in the URL, and "The starting crew" when there are no test ships.
- **Teams are picked for each seed:** the objective's minimum size from the ship's fit crew in roster order, the first member moving one place each seed, and the first member leads.
- **An encounter runs under every objective that can reach it,** by its stages' draw tags or by a chain from an encounter that can, the objectives taken in turn across the seeds. Its frequency counts only the runs that met it. An encounter no objective can reach is a fault: in its panel, in a new lint on the encounters list, and in a test on the game's pack.
- **Calls are answered by turns:** the option at `(seed + calls so far) mod options`, as `NarrativeSamples` answers them now, so every answer turns up. The note under a frequency says so.
- **One pool of runs per post and ship,** seeds 1 to N, cached, shared by every preview on the form. N starts at 500; the plan measures it against the time a keystroke can afford and lowers it if it must, saying so.
- **Next sample is one counter for the panel;** each preview shows its k-th matching run, and each preview names its run.
- **The line form** shows up to five beats on the chosen ship where the line holds, each with where it came from and whether the line wins, and counts the runs it holds and wins in.
- **The lines list's lint** runs every objective on every test ship over the same seeds, cached per pack. A line is never chosen only if no ship chooses it; a beat is without a line if it meets none on any ship. The chronicle check stays on `ChronicleSamples`, failing as now.
- **A ship with too few fit crew** for an objective skips that objective, and the panel says so.
- **No mock-up.** The visible change is the reused ship select, run labels, and frequencies; the plan's last task screenshots the panels for the author's approval.
- **No link from a preview to the Tuning page's replay** (section 7).

## 2. Goals, non-goals, anti-goals

**Goals**
- What the author reads in a preview is a beat that happens: a real team from a real ship state, in a mission the resolver ran.
- The author can see how often a line plays, not only how it reads.
- Encounters, objectives, lines, storylets, and vignettes are all previewed on the same test ships.

**Non-goals**
- Line criteria on traits, morale, or injuries (GDD 16, question 8).
- What follows a mission: the aftermath, launch-rule changes shown as such, storylets and vignettes arriving, the chronicle's mission entry on the panels.
- Choosing the team or pinning one; the Tuning page benches a chosen team.
- A new call policy on the bench.

**Anti-goals**
- Forcing a fact to make a beat appear. A beat that does not happen in N runs says so.
- A second way to build a team. The previews use `LaunchTeam.Build`, as the launch and the bench do.
- Writing to a legacy, the store, or the bus, or drawing randomness from any legacy.
- A keystroke that waits on runs it could have shared: every preview on a post reads one pool.

## 3. Approaches considered

1. **Real runs, searched (chosen).** Run the mission on the ship over seeds 1 to N and take the runs where the beat holds. It is the only one that answers "how often", and it removes the synthetic samples outright.
2. **A hybrid:** real team and ship, beat facts forced to the criteria. Every line would always have a sample, but the sample would be one play never produces, which is the fault of 1d's samples this slice exists to remove.
3. **Real runs, one seed each:** cheapest, but a rare beat would mostly show nothing, and nothing would say how rare it is.

## 4. Design

### 4.1 The runs

`ShipRuns` (new, `src/Web/Forms/ShipRuns.cs`) is the pool. `ShipRuns.Of(pack, ship, objectives, n)` gives, for one ship and a list of objectives:

- **The ship,** founded with `TestShipLegacy.Found` on the candidate pack, once.
- **For each seed 1 to n,** the objective at `(seed - 1) mod objectives` that the ship can crew; the team of that objective's minimum size from the ship's fit crew in roster order, starting at member `(seed - 1) mod fit`, wrapping, the first member leading; the team built with `LaunchTeam.Build`; `MissionResolver.Start` and `Advance` on the seed, each call answered with option `(seed + calls so far) mod options`; and the beats turned into situations by `BeatSituations.Of` with a `BeatFrame` whose people are the ship's crew, named and with pronouns from the pack. A check is also its attempt, as `NarrativeSamples` makes it now.
- **Each situation** keeps its run: the seed, the objective, the team and lead, the beat's place in the run, and the key its text is picked by (`ship|{ship}|{objective}|{seed}|{beat}`).
- **Objectives the ship cannot crew** are listed with the sentence the panel shows: "USS Quality Control has 2 fit crew; Waystation survey needs 3."

The pool is cached on the candidate pack and the ship id (a `ConditionalWeakTable` keyed on the pack, as `NarrativeSamples.Of` is now), so every preview on one post reads the same runs, and a post that changes only words reuses the pool. **A post that changes only an owned text box builds a new candidate pack, so it would miss the cache (my call: the plan's first task measures it, and if a words-only post is too slow, the cache key becomes the pack's mission content, which owned texts do not change).**

**N** starts at 500. The plan's first task times one pool on the game's pack and the fixture's, and sets N to what a post can afford (the budget is the one the storylet previews keep); the design's "As built" records it.

### 4.2 Which objectives an item runs under

- **An objective** runs alone.
- **An encounter** runs under every objective that can reach it: an objective with a stage whose `drawTags` the encounter's tags all hold (and the encounter is not a chain target), or one that can reach an encounter whose `chain` is this one. `Reach.Objectives(pack, encounterId)` (new, in `src/Web/Forms/`) answers it from content alone, with no runs. The frequency's base is the runs that met the encounter, not all N.
- **An encounter no objective reaches** shows a fault in place of every preview: "No objective can draw this encounter: no stage's draw tags are all among its tags, and nothing chains to it." **The encounters list gains a lint** above it, as the storylets list has, listing each unreachable encounter as a fault. **A test** holds every encounter in the game's pack reachable.
- **A line** (on its own form, and in the lint) runs under every objective, since a shared line may hold anywhere.

### 4.3 What plays here on the encounter and objective forms

The panel keeps its groups, slots, fallbacks, lines, and chips. What changes:

- **A ship select** above it, the storylet forms' `ShipSelect`, its choice in the URL (`ship=`). With no test ships, "The starting crew": a ship with no changes, so everyone fit and aboard at the starting morale and loyalty.
- **Each preview** takes, from the pool, the situations of its beat at this item (the encounter or objective fact) that its criteria hold for, in seed order, and shows the k-th, wrapping, where k is the sample number. It is rendered by `Prose.Render` with the pool's key, as now.
- **Its note** gains the run and the counts: "Seed 212, Okafor leads, with Reyes. Wins in 9 of 61 runs that met this encounter; holds in 14." For an objective, "of 500 runs". The words for who wins stay as today's ("This line wins in this sample." and the rest), with "sample" now meaning this run.
- **When no run holds,** the preview says "Doesn't happen in 500 runs on USS Quality Assurance." (or "in 61 runs that met this encounter"), in place of a sample. Today's static check runs first and keeps its own words, so **can never hold** and **didn't happen** read apart.
- **The cast line** at the top names the run behind the first preview shown, and below it a line on the pool: "500 runs on USS Quality Assurance, calls answered in turn." Objectives skipped for too few fit crew are listed under it.
- **A placeholder** (the line that plays in a blank box) is chosen in the same run as its preview.

### 4.4 The line form

`LinePreview.Of` reads the pool for the chosen ship, under every objective. It shows up to five situations where the line holds, each with its place ("Waystation survey, seed 14, Okafor leads, beat 6") and whether the line wins there or which line beats it, and above them the counts: "Holds in 37 of 500 runs on USS Quality Assurance; wins in 22." With none, "Doesn't happen in 500 runs on USS Quality Assurance." The form gains the ship select.

### 4.5 The lint on the lines list

`LineLint.Of(pack)` reads the pools of every test ship, under every objective (with no ships, the starting crew's), and keeps its findings: shared lines never chosen on any ship, beat kinds that met no line on any ship, of those the ones the report shows, and the chronicle beats from `ChronicleSamples` that meet no line, which still fail. Its basis line says what it ran: "From 500 runs of every objective on each of 2 test ships, calls answered in turn." It is cached per pack. Its cost is ships × N runs; the plan measures it with the pool.

### 4.6 What goes

`NarrativeSamples` and `SampleBeat`, `Plays.Sample` and `Plays.Cast` (the synthetic situation and the starting crew rotated through its roles), and `Plays.FallbackText`'s synthetic situation, which takes the first run of the pool instead. Their tests move to the pool.

### 4.7 Tests

- **The pool** on the fixture's test ship: every team is built by `LaunchTeam.Build` (an unfit or away member never goes; a member's injury, morale bonus, and trait bonuses are in the starting team, compared as JSON with the launch's, as part one's Task 4 test compares them); the same seed gives the same run; the calls are answered in turn; an objective the ship cannot crew is skipped with its sentence; building a pool leaves the store's legacies and their events unchanged.
- **Reach:** on the fixture, an encounter reached by draw tags, one reached only by a chain, and one reached by nothing. On the game's pack, every encounter is reachable.
- **The panel:** an encounter line for a rare tier shows a real run with the counts; a line that can never hold says so in its own words; a line that does not occur in N runs says that instead; Next sample moves each preview to its next run; an unsaved change to an approach's difficulty moves the counts (the candidate is run); a candidate that does not load runs the stored item, greyed.
- **The line form and the lint** agree with the pool on the fixture: what the line form counts, the lint sees.
- **The encounters list's lint** names an unreachable encounter in the fixture as a fault.
- The existing previews' and lint's tests are kept where they test words and routes, and moved to the pool where they read a sample.

## 5. Architecture

### 5.1 Modules

- **`Web`** gains `ShipRuns` and `Reach` (`src/Web/Forms/`), the encounters list's lint, and the ship select on three more forms. `Plays` (its mission part), `LinePreview`, and `LineLint` read the pool. `NarrativeSamples` goes. It founds ships with part one's `TestShipLegacy.Found` and builds teams with `LaunchTeam.Build`, as `BenchTeams` does; `ShipRuns` does not go through `BenchTeams`, whose picks come from the author **(my call: the pool shares `Found` and `Build`, the two calls that matter, and needs none of `BenchTeams`' refusals)**.
- **`Sim.*`, `MissionBench`, and `SimRunner`** are unchanged.

### 5.2 Rules that hold

- Developer mode only, as every editor page.
- No event, state, or golden changes; play is untouched.
- A preview never writes to a legacy, the store, or the bus, and draws no randomness from any legacy: its streams are the run's seed.
- 1h-seams moves `TestShipLegacy` onto the modules' own entry points; the pool calls `Found` and nothing below it, so that move does not reach here.

## 6. Roadmap and GDD changes

Applied with this design:

- **GDD 11.1:** the 1f-previews row names this design.
- **GDD section 15, item 54** records these decisions.
- **GDD section 16, question 8:** whether line criteria should read a person's traits, morale, and injuries.
- **The outline's** paragraph names this design.
- The GDD's status line moves to v1.32, and the page is rebuilt and republished.

Applied when it is built: GDD 7.4 (the editor's previews, which 1d's paragraph describes as against fixed sample missions on the starting crew), 6 where it names the samples, 11.1's row Done with a pointer to "As built", the outline's paragraph, and `GLOSSARY.md` if it names a sample.

## 7. Open, and left to the builder

- **A link from a preview to the Tuning page's replay of its run.** Left out on 2026-11-22. The link itself is cheap (`TuningQuery` holds a test ship's team, the objective, and the seed, and `BenchReplay.Page` builds it), but the replay would not be the same run: the previews answer calls in turn and the bench has no such policy, so it needs a `rotate` policy across `MissionBench`, the command line, the Tuning page, and the bench goldens, and the replay runs saved content where the preview runs the form as posted. About a task and a half.
- How the counts read in a narrow panel.
- Whether a fixture ship is needed with too few fit crew for some objective, or the fixture's existing ship already covers it.
- Whether the cast line's run and the first preview's run are worth showing twice, once the panel is seen.
