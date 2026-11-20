# Phase 1, slice 1e, part five: the chronicle

**Spec:** `docs/gdd.md` sections 3.8, 3.9, 6, 7.1, 7.4, and 7.5, and section 15, items 42 to 44. **Builds on:** `main` at 2026-11-20 (`79baa88`): slice 1e's first four parts and the calendar.
**Status:** designed 2026-11-20.
**Ends with:** the author plays a legacy for a dozen cycles and opens **Chronicle** from the top bar. Under full-date headings, newest first, the page reads: the ship's founding; every mission, in one plain line each; each call the captain answered, with the ones where the captain overruled the lead set apart; each captain storylet and the option chosen; and a memorial for each crew member lost, with where and how. Notable hides the small entries. After a second death within 120 days, "The names on the wall" arrives, because `chronicle.count('death', 120) >= 2`. The captain holds a service, and that service is in the chronicle as well.

This records what the author decided about the chronicle on 2026-11-20, in a design interview, and designs it. Items marked **(my call)** were proposed by me, not asked for by the author.

## 1. What the author decided (2026-11-20)

- **What goes in:** the legacy's founding, deaths (as memorials), calls answered, captain storylets answered, and every mission's result. Vignettes stay out: they are small and frequent and would bury the captain's real choices. Unattended objectives wait for open question 4.
- **Stored, not projected.** The chronicle keeps its own entries, written when what they record happens, and their **text is rendered once and frozen**. This is the one deliberate exception to slice 1d's "never stored": a chronicle is history, and a later grammar edit must not rewrite how a death was recorded.
- **Written in lines.** Each kind of entry is a situation in `Sim.Narrative`'s pool, so authors write and override chronicle lines as they do report lines. The pack ships a default line for every kind. A storylet option's own `chronicle:` text wins where it is written.
- **A `Sim.Chronicle` module**, with only the chronicle in it. Renown and succession (GDD 7.1) wait for Phases 6 and 8.
- **Storylets can read it:** `chronicle.count(kind, days)` and `x.chronicle.count(kind, days)`, counting entries in the last N **days**. Days, not cycles: what a chronicle read is for (grief, a pattern of overruled leads, a bad year) fades with time in the world, and a 6-week journey is one cycle. `history.count` stays in cycles, because the history is stored per cycle.
- **Old saves are refused**, as the calendar did. The chronicle is not backfilled from the event log, which would be the replay GDD 7.4 rules out.
- **Entry shape:** GDD 3.9's, plus the **day**, minus `hullSuffix` until hulls exist. Each subject's **name is frozen** with the entry.
- **Weight per kind in tuning, overridable:** founding 5, death 5, storylet 3, call 3 (2 when the captain backed the lead), mission 2 when it failed or was abandoned and 1 when it succeeded. A storylet or a call's decision can set its own weight; `weight: 0` leaves no entry.
- **The page:** a Chronicle screen from the top bar, open in any phase, newest first under full-date headings, weight 4 and 5 set apart, memorials marked, a toggle between **All** (the default) and **Notable** (weight 3 and up), no pagination, and no cycle numbers outside dev mode.
- **The worked example:** "The names on the wall", gated on `chronicle.count('death', 120) >= 2`.
- **The simulator** reports entry counts by kind; a golden test pins the frozen texts of a seeded run. No outcome moves; the golden event hash moves once, for the new `CallAnswered` event and `CrewDied`'s new fields (corrected while planning, 2026-11-20).

## 2. Goals, non-goals, anti-goals

**Goals**
- Every death, call, captain storylet, and mission leaves a dated, frozen entry, written once at the moment it happens.
- The player can read the legacy's history on one page, with what matters standing out.
- Authors write the chronicle's words as lines, and a storylet option can name its own.
- Storylets can read the chronicle, by kind and by crew member, over a span of days.

**Non-goals**
- The chronicle screen of Phase 8 (succession, hulls, eras, a search, and Notable as the default).
- Renown and succession in `Sim.Chronicle`.
- Entries for vignettes, unattended objectives, trait changes, or injuries.
- Departments in a read (`dept:`, GDD 3.8's example): no departments yet.
- Editing or deleting an entry, in play or in Dev Play.

**Anti-goals**
- Chronicle text regenerated from today's lines. Once written, an entry's text never changes.
- An entry built by replaying old events.
- Randomness in the chronicle. It draws from no stream; line ties break by a stable hash, as reports do.

## 3. Approaches considered

How entries come to exist.

**A. A module that listens and writes (chosen).** `Sim.Chronicle` subscribes to the events that matter, builds a situation, picks and expands a line, and stores the entry with its frozen text and the event's sequence number. Entries exist the moment the thing happens; the page only reads.

**B. A projection over the event log, rendered on view.** No new state, but the text would follow the current lines (the anti-goal), and every view would replay a mission's beats.

**C. Store facts, render on view, freeze on first view.** Freezing would depend on when the player first looked. Rejected for that.

## 4. Design

### 4.1 Words

- **Chronicle**: unchanged in the glossary: the permanent, curated history of a legacy.
- **Entry**: one item in the chronicle. It has a **kind** (`founding`, `death`, `mission`, `call`, `storylet`), a **weight** from 1 to 5, its **subjects**, and its frozen **text**.
- **Memorial**: unchanged: a `death` entry.
- **Notable**: an entry of weight 3 or more.

### 4.2 An entry in state

```yaml
id: chr_0012                # chr_ and a counter per legacy (my call)
day: 37529                  # the day it happened
cycle: 9
kind: death
weight: 5
subjects:                   # crew ids, with their names as they were (my call: frozen, as the text is)
  - { id: crew_03, name: Okafor }
text: "Okafor fell at the Collapsed Gallery, on Vance's away team, after 8 cycles aboard."
sourceSeq: [412]
```

Entries are held in the chronicle module's state, one JSON row per legacy as other modules are (GDD 7.4), in the order written. The page sorts by day, then `sourceSeq`, newest first. A table of its own, as GDD 7.4 sketches, waits until the list is too long for one row; the module's interface hides which it is **(my call)**.

### 4.3 What each kind records

| Kind | Written on | Subjects | Facts in its situation | Default weight |
|---|---|---|---|---|
| `founding` | `LegacyFounded` | none | the date; the size of the starting crew | 5 |
| `death` | `CrewDied` | the dead | the name and pronouns; the encounter and the approach of the check that killed them; the mission's objective and site; the lead; cycles served (counted from the founding, until crew have a join date) | 5 |
| `mission` | `MissionEnded` | the lead | the objective, the site, the outcome, the lead, the team size, the number who died | 2 if failed or abandoned, 1 if it succeeded |
| `call` | the call's answer | the lead | the decision, the encounter, the answer, the lead's preference, the standing (backed, overruled, or neutral), the mission | 3 if overruled, 2 otherwise |
| `storylet` | `StoryletAnswered` | the cast | the storylet's title, the cast by slot, the option chosen (its label), and for a check option the handler and success or failure | 3 |

- **Default lines** (written in place in both packs; the author rewrites the game's):
  - founding: *"The ship took on her first crew of 8."*
  - death: *"Okafor fell at the Collapsed Gallery, on Vance's away team, after 8 cycles aboard."*
  - mission: *"Vance led four to the Collapsed Gallery; the survey was abandoned. One did not return."*
  - call: *"At the Collapsed Gallery, the captain overruled Vance and pressed on."*
  - storylet: *"Okafor asked for a memorial. The captain held a service."*
- **The founding entry names no ship or captain.** Neither exists in Phase 1. The lines can use them once lineage (Phase 8) and the captain as a crew member arrive.
- **One mission with a death** writes its `death` entry or entries first and its `mission` entry after, as the events fall.
- **A call's entry** is written when the answer is applied, so a call answered and a death in the same encounter keep their true order.

### 4.4 Weight

`content/tuning/chronicle.yaml` **(my call: a tuning file of its own, as the calendar's is)**:

```yaml
weights:
  founding: 5
  death: 5
  storylet: 3
  callOverruled: 3
  callBacked: 2           # backed or neutral
  missionLost: 2          # failed or abandoned
  missionWon: 1
notable: 3                # Notable shows this weight and up
```

- **A storylet** may write `chronicle: { weight: 4 }`, and **an option** may write `chronicle: { weight: 4, text: "..." }`. An option's value wins over its storylet's, and the storylet's over the tuning file's. `weight: 0` writes no entry.
- **A decision** in an encounter may write `chronicle: { weight: n }`, overriding both call weights.
- The loader refuses a weight outside 0 to 5, and `text` anywhere but on a storylet option.

### 4.5 The text

- Each kind is a beat in `NarrativeFacts`, with its facts listed in that one catalogue, as `schedule` is: the loader, the editor, and the lint read it.
- The chronicle module builds the situation, asks the lines engine for the best line, expands it, and stores the result. **The text is plain prose: one sentence or a few, with no speakers.** A script in a chronicle line is refused by the loader **(my call: a memorial reads as a record, not a scene)**.
- **A storylet option's `chronicle.text`** is expanded with the storylet's situation (its cast and the option's facts) and used in place of a line.
- If no line holds, the module falls back to fixed words in code, as the schedule line does, so an entry is never lost. The lint (4.9) means the game's pack never needs the fallback.
- The text is rendered with the pack live **at the moment of writing** and never again.

### 4.6 Reading the chronicle in expressions

| Read | Type | Meaning |
|---|---|---|
| `chronicle.count('kind', n)` | number | Entries of that kind on days within the last `n` days, today included |
| `x.chronicle.count('kind', n)` | number | The same, among entries with `x` as a subject |

- `kind` is a quoted name, checked at load against the five kinds ("No chronicle kind named deaths"). `n` is a whole number constant from 1, checked in the rewrite as `history.count`'s is.
- Entries with `weight: 0` were never written and are not counted.
- `chronicle` stops being refused in `Rewrite.cs`. Its hint moves to `dept:`, which is refused as "dept waits for departments (Phase 5)".
- `x.chronicle` reads a living cast slot. The dead are subjects of their memorials but cannot be cast, so "who died" is `chronicle.count('death', n)`, not a read on the dead.
- **The chronicle module serves `IChronicleView`** (in `Sim.Core`'s `Views.cs`, beside `IHistoryView`); the storylets module implements the language's `IStateSource` over it, as it does over the history.

### 4.7 The page

- **Chronicle** sits in the top bar beside the roster's place, and opens in any phase without changing it **(my call: a link, not a phase; leaving it returns to the screen the phase is on)**.
- **Newest first,** grouped under the **full date** (`Stoneday, 13 Vortun 105`), the place the calendar design kept full dates for. Within a day, by `sourceSeq`, newest first.
- **Weight 4 and 5** are set apart (a stronger rule and type). A **memorial** carries a mark and the name in the entry's head.
- **All / Notable**: All by default. The toggle is a link, so the page stays server-drawn; the choice is not remembered between visits **(my call)**.
- **No pagination** in Phase 1. A dozen cycles make about 50 entries.
- **Dev mode** adds, to each entry, its kind, weight, cycle, and `sourceSeq`.
- The design follows `docs/design/`'s existing system; no new components beyond the entry and the date heading.

### 4.8 The worked example: "The names on the wall"

In the game's pack, written in place for the author to rewrite:

- A storylet with one slot, `voice`: a living crew member, preferring the one with the most `lost_crewmate` in their history (the cast expression orders by it).
- **Requires** `chronicle.count('death', 120) >= 2`, and **cooldown** `{ global: 20 }`.
- **Options:**
  - **Hold a service**: morale up for everyone aboard; `chronicle: { weight: 4, text: "#voice# asked for a memorial. The captain held a service for the fallen." }`.
  - **A quiet word**: morale up for `voice` only.
  - **Not now**: the trait `grieving` on `voice` (a new trait, which expires after 4 weeks; its definition is written in place).
- The test pack gets a short version with the same gate, used by the tests in 4.10.

### 4.9 The editor and the lint

- The **lines page** lists the five chronicle beats, with sample situations to preview against (a fixed sample per kind, built in code, as the schedule's are).
- **The lint fails a pack** with no line for one of the five kinds, so the game's pack always speaks.
- An option's `chronicle.text` is checked for unbound symbols against the storylet's situation, as an option's own text is.
- The **storylet files stay text** in the editor; forms are part six.

### 4.10 The simulator, tests, and saves

- **`SimRunner cycles`** reports the chronicle's entries by kind, and the weight-0 entries skipped.
- **A golden test** runs a fixed seed for a fixed number of cycles and pins every entry's day, kind, weight, subjects, and text. A change to a line shows up here, and only here. The chronicle draws no randomness and writes only its own state, so no outcome moves; the golden event hash moves once, when `CallAnswered` and `CrewDied`'s fields are added (corrected while planning, 2026-11-20).
- **A freezing test:** write an entry, change the line in the pack, reload the legacy: the entry's text is unchanged.
- **Old saves are refused** with the message other format changes have used; the schema version moves.

## 5. Architecture

### 5.1 Modules

- **`Sim.Chronicle`** (new; depends on `Sim.Core` only, as every `Sim.*` module must): its state (the entries and the counter), its subscriptions, and `IChronicleView`. It gets its words through `INarrator`, an interface in `Sim.Core.Narrative` that `Sim.Narrative` implements and `GameHost` passes in at registration (changed while planning, 2026-11-20: the architecture test allows a module no other reference). Registered after the crew, missions, storylets, and traits modules, so it hears each event after the module that owns it has acted **(my call)**. Each entry records the sequence number of the event it was written for, read from the bus's `IBusContext.Sequence` (added in Task 1).
- **`Sim.Missions`** publishes a new contract, `CallAnswered`, when an answer is applied, and its mission facts for the chronicle (objective, site, encounter, approach, lead) travel on the Core contracts in 5.2, so `Sim.Chronicle` never reads a mission beat. `SiteArrived` moves to `Sim.Core` for the same reason (Task 3).
- **`Sim.Crew`**: `CrewDied` carries what killed them (5.2).
- **`Sim.Storylets`**: implements the `chronicle` reads over `IChronicleView`, which it reads from the bus as it reads `ITraitView` (Task 5). (The loader that reads `chronicle:` on storylets and options is `Sim.Content`'s; Task 4.)
- **`Sim.Expressions`**: the two reads (`IStateSource.ChronicleCount`, on a new `chronicle` root and on a slot), the kind check, and the `dept` hint.
- **`Sim.Content`**: the tuning file, `chronicle:` on storylets, options, and decisions, and the refusals in 4.4 and 4.5. The encounter form in `Web` carries a decision's weight.
- **`Web`**: the Chronicle page (`GET /legacy/{id}/chronicle`, and Dev Play's under `/dev`), drawn by `BridgeReader.Chronicle` from `IChronicleView.Entries` without rendering a text again; the top bar link; and the dev details (Task 6).
- **`SimRunner`**: the counts.

### 5.2 Contracts in `Sim.Core`

- `CallAnswered(string MissionId, string DecisionId, string EncounterId, string Answer, string LeadPrefers, string Standing, string LeadId)`: a new event at version 1. Answers and standings travel as words, since `CallAnswer` and `CallStanding` are `Sim.Missions`' types **(my call)**.
- `CrewDied` gains `EncounterId` and `ApproachId`, at version 2.
- `MissionEnded` already carries the objective and the result; the chronicle reads the team, lead, and site from `MissionStarted` and `SiteArrived`, which it keeps until the mission's own entry is written. `SiteArrived` and `SiteObjective` are Core contracts (moved from `Sim.Missions` in Task 3, with the same name and version).
- `DecisionDef.ChronicleWeight`: a decision's `chronicle: { weight: n }`, or null.
- `ChronicleDef(int? Weight, string? Text)`: `StoryletDef.Chronicle` (a weight alone) and `StoryletOption.Chronicle` (a weight, a text, or both), or null (Task 4).
- `INarrator` (in `Sim.Core.Narrative`): `string? Line(Situation situation, string key)`, the chosen line expanded to plain text, or null when no line holds; and `string? Expand(string text, Situation situation, string key)`, an option's own chronicle text expanded the same way, or null when it cannot be (added in Task 4).
- `IChronicleView`: `int Count(string kind, int days)` and `int Count(string crewId, string kind, int days)`, and the entries for the page.
- The five kinds as constants (`ChronicleKinds`), as `HistoryKinds` are.
- `ChronicleTuning(Weights, Notable)`, read from `tuning/chronicle.yaml` and carried on `ContentPack.Chronicle`.
- `IBusContext.Sequence`: the sequence number of the event whose subscribers are running (added in Task 1).

### 5.3 Persistence

- The chronicle module's state is a module row, as every module's is. The schema version moves, so an old save is refused.

### 5.4 Rules that hold

- An entry is written once, at the event, and its text never changes.
- The chronicle draws from no random stream and changes no other module's state.
- Every event that writes an entry writes at most one of its own. The one exception: a mission's entry waits for its deaths (Task 3), so it is written while the last of them is handled, after that death's entry.
- Entries within one day keep the order their events were published in.

## 6. Roadmap and GDD changes (applied when built)

- **GDD 3.8:** the two reads, in days; `dept:` waits.
- **GDD 3.9:** the entry with `day` and frozen names, without `hullSuffix`; kinds and default weights; the tuning file.
- **GDD 6:** chronicle text is the one stored use of the engine, frozen at writing.
- **GDD 7.1:** `Sim.Chronicle` exists with the chronicle only.
- **GDD 7.5:** the Chronicle page.
- **GDD 11:** part five's row marked done when built.
- **GDD 15, item 44:** these decisions.
- **`GLOSSARY.md`:** **Entry** and **Notable**; **Chronicle**'s note that it is read by `chronicle.count`.
- **The outline:** part five's sentence, when built.

## 7. Open, and left to the builder

**For the author, noticed while writing this down:**
- The founding entry has no ship name or captain to name, because neither exists. Should the starter pack name the ship now (a field in `pack.yaml`), or should founding wait for lineage?
- Cycles served counts from the founding. When crew join later (generation, Phase 6), a join date replaces it.

**For the builder:**
- Whether the entry list stays in the module row or moves to a table, if the row grows slow in the simulator's long runs.
- The exact place of the top bar link, and the marks for memorials and weight 4 to 5, within the design system.
- How the cast expression in 4.8 picks the one with the most losses, if a cast slot can't order: a `requires` on a minimum count is an acceptable substitute.
- The wording of refusals, and of the fallback text.
- The game's pack: the default lines and the worked example, written in place for the author to rewrite.

## 8. As built

Kept as each task of the plan lands. Differences from sections 4 and 5 are said here, and section 5 is changed to match.

### Task 1: the module, the narrator, and the founding entry (2026-11-20)

As designed: `Sim.Chronicle` references `Sim.Core` alone, registers last in `Game.Build` (subscriber order 400, after storylets at 300 and before the cycle's next day at 1000), and writes a `founding` entry on `LegacyFounded`: today's day and cycle, no subjects, the starting crew's size as the fact `crew`, and the narrator's text or the fixed "The ship took on her first crew." `ChronicleKinds`, `ChronicleSubject`, `ChronicleEntry`, and `ChronicleTuning` are in `Sim.Core/ChronicleContracts.cs`; `IChronicleView` is in `Views.cs`; `INarrator` is in `Sim.Core.Narrative`, and `Sim.Narrative.Narrator` implements it, passed in by `GameHost`. The schema is 14 and the export format 12; an older one is refused with the message used before.

Changed or settled while building:

- **The bus now says which event is being handled.** It had no way for a subscriber to read its event's sequence number, so `IBusContext` gains `Sequence`: the sequence number of the event whose subscribers are running, or 0 while a top-level command's handler runs. Section 5.1 says so. Nothing else reads it yet.
- **The tuning file** is `tuning/chronicle.yaml`, required, read by the same strict reader as the other tuning files. A weight outside 0 to 5 reads "weights.death must be a whole number from 0 to 5, not '6'."; `notable` outside 1 to 5 the same; a missing key "weights.callBacked is missing."; a stray one "weights.vignette is not a tuning value." `ContentPack.Chronicle` carries it, defaulting to the design's numbers for a pack built in code. The editor lists it as "Chronicle tuning", a text file checked with the whole pack on save.
- **The five beats** are `chronicle_founding`, `chronicle_death`, `chronicle_mission`, `chronicle_call`, and `chronicle_storylet`, named in `NarrativeFacts.Chronicle`. They do not take the mission beats' common facts. As written now (Tasks 3 and 4 may refine them, and will say so here):
  - founding: the number `crew`, said by `#crew#`. **No date**: the page already heads each day with its full date, and `Sim.Core` does not format dates.
  - death: `objective`, `encounter`, `approach` (absent for a collapse), the number `cycles`; people `dead` and `lead`; things `objective`, `encounter`, `site`, `cycles`.
  - mission: `objective`, `outcome`, the numbers `team` and `deaths`; person `lead`; things `objective`, `site`, `team`, `deaths`.
  - call: `objective`, `encounter`, `decision`, `answer`, `prefers`, `standing`; person `lead`; things `objective`, `encounter`, `site`, `decision`.
  - storylet: `storylet`, `option`, `outcome` (for a check option); person `handler`; things `storylet`, `option`. A shared line cannot name a storylet's cast, whose slots each storylet names for itself; an option's own `chronicle.text` can (Task 4).
- **A script in a chronicle line is refused**: "a chronicle line is plain prose: no speaker, no note (>), no report's sentence (=), and not silence (-)." Plain narration over several lines is allowed; the narrator joins its lines with a space.
- **New role names.** `dead`, `site`, `cycles`, `team`, `deaths`, `option`, and `handler` now name people or things in some beat, so a symbol cannot take those ids, and `#handler#` outside a check's outcomes is refused as "names 'handler', which a storylet beat does not have" rather than "names no person, thing, or symbol". Neither pack has such a symbol.
- **The editor's beat glossary** says when each chronicle beat plays and names them "Chronicle: the founding" and so on. Until Task 7 builds samples for them, the lint lists chronicle lines as never chosen, as it does the schedule's.
- The default founding line is in both packs: "The ship took on her first crew of #crew#." in the game's, `CHRONICLE_FOUNDING #crew#.` in the fixture's.

### Task 2: the facts that live in missions (2026-11-20)

As designed: `CallAnswered` (version 1) and `CrewDied` (version 2, with `EncounterId` and `ApproachId`) are in `Sim.Core/MissionContracts.cs`, and `CrewResult` gains `DiedAtEncounter` and `DiedAtApproach`. The answer, the lead's preference, and the standing travel as lower-case words (`bold`, `safe`, `withdraw`; `backed`, `overruled`, `neutral`).

Changed or settled while building:

- **The beats do not change.** `AnswerBeat` does not carry the decision or the encounter, so the missions module reads them from the pending call and the current encounter before the answer advances the mission, and publishes `CallAnswered` right after the answer's `MissionBeat`. Everything else the step does, a death in the same encounter included, comes after it. The golden mission's beat hash does not move.
- **A decision's death has no approach either.** The resolver names an approach only when a check's outcome rolled the injury; a collapse and a decision's effect (an answer or an initiative) name the encounter alone. The living name neither.
- **The golden hash** (`Golden_seed_1_matches_the_accepted_log`) moves from `15E7E2C9…D72C910` to `5B8BEF69…EDA4FB7A`. In SimRunner's 200 x 200, both packs and both answer policies, only the event count moves (by about two `CallAnswered` a mission: the game's pack under first answers from 1,812,185 to 1,899,365 events); no outcome, result count, death, storylet, vignette, morale, or trait figure moves. The CLI test's 2 x 3 run moves from 263 events to 274.

### Task 3: deaths, missions, and calls in the chronicle (2026-11-20)

As designed: a `death` entry on `CrewDied` (weight `death`, the dead as subject), a `call` entry on `CallAnswered` (the lead who made the call as subject, weight `callOverruled` when overruled and `callBacked` otherwise, unless the decision sets `chronicle: { weight: n }`; 0 writes nothing), and a `mission` entry for `MissionEnded` (the starting lead as subject, `missionWon` for a full or partial success and `missionLost` otherwise). Names and pronouns come from the pack's crew definitions; cycles served is the current cycle. Default lines are in both packs, with criteria on `standing`, `answer`, `outcome`, `deaths`, and `dead.isLead`; the fallback words are "A crew member was lost.", "A mission ended.", and "The captain answered a call."

Changed or settled while building:

- **The deaths come after `MissionEnded` on the bus**, not before it. The crew module publishes `CrewDied` while handling `MissionEnded`, and the bus queues it, so the design's "as the events fall" would have put the mission's entry first. The chronicle instead holds the end (`OpenMission.Ended`, `EndedSeq`, and the number the result says died) until it has written that many deaths, then writes the mission's entry after them. A mission with no deaths is written on its end. The entry's `sourceSeq` is the end's and then each death's, and its line's ties break by the end's. Section 5.4's rule now names this one exception.
- **`SiteArrived` and `SiteObjective` moved to `Sim.Core`**, unchanged in name and version, since the chronicle may read no `Sim.Missions` type. The golden hash does not move.
- **What it keeps** is each open mission (its id, objective template, site, team, and starting lead) and also the assignment of the last site arrived at, which a mission started there takes. Both are in the module's row, so a save while a call waits keeps them.
- **The lead.** A death and a mission name the lead the mission started with; a call names the lead who made it, from `CallAnswered`. Each person in a chronicle situation has the stats `id` and `isLead` (the starting lead's); the chronicle knows no `caution`, `discipline`, or `seniority`, so a line testing those never holds.
- **Numbers are said in digits**: `#team#` and `#deaths#` are "3" and "1", `#cycles#` "8 cycles" or "1 cycle". The default mission lines spell "One did not return." in place of `#deaths#` when one died.
- **The abort call** gets an entry like any other, weighed from the tuning file, since no decision defines it. `#decision#` says its id.
- **A decision's weight in content.** The loader refuses a weight outside 0 to 5 ("The decision's chronicle weight must be a whole number from 0 to 5.") and text ("The decision's chronicle has text, which only a storylet option may have."). The writer puts `chronicle: { weight: n }` last in the decision. The encounter form gained an optional **Chronicle weight** field, so a save from the form keeps it.

### Task 4: captain storylets in the chronicle (2026-11-20)

As designed: a `storylet` entry on `StoryletAnswered`, with the cast as its subjects in the storylet's slot order (not the event's), weighed by the option's `chronicle.weight`, else the storylet's, else the tuning file's `storylet`; 0 writes nothing. The text is the option's `chronicle.text`, expanded against the entry's situation, else a line, else the fixed "The captain made a choice." `VignetteAnswered` writes nothing. `ChronicleDef(Weight, Text)` is on `StoryletDef.Chronicle` and `StoryletOption.Chronicle`, as `init` properties, so no existing constructor call changes.

Changed or settled while building:

- **`INarrator` gains `Expand(text, situation, key)`.** The chronicle may not reference `Sim.Narrative`, so it cannot call the expander itself; `Narrator.Line` now picks a line and hands its text to `Expand`. If an option's text cannot be expanded at the moment of writing (a symbol the pack has since lost), the entry takes a line instead, so it is never lost.
- **The situation.** The beat's facts as Task 1 wrote them (`storylet`, `option`, and `outcome` for a check option, from the roll), with `#storylet#` saying the storylet's name (its id when it has none) and `#option#` the option's label. Its people are the cast, by slot, and `handler` for a check option, each with the stats `id` and `isLead` (always false here). A shared line can name only `handler`; an option's own text can name the cast too. A storylet the pack no longer has is still written, under its id, with the event's cast order and the tuning file's weight.
- **The loader.** `chronicle:` is read on a storylet and an option, and refused on a vignette and a vignette's option ("A vignette leaves no chronicle entry, so it has no chronicle."). A storylet's must have a weight and no text ("A storylet's chronicle has no text; an option's may."); an option's must have a weight, a text, or both ("the chronicle needs a weight, a text, or both."). A weight outside 0 to 5 reads "The storylet's chronicle weight must be a whole number from 0 to 5." or, on an option, "Option 'x': the chronicle weight must be…". An option's text is checked as a `chronicle_storylet` line whose people are the storylet's slots, and `handler` on a check option, so `#lead#` reads "option_b.chronicle.text cannot be used: #lead# names 'lead', which a chronicle_storylet beat does not have."; a script in it is refused with the chronicle line's message. It is kept on the option, not added to the pack's lines.
- **No writer or faithfulness change.** The plan named `ContentWriter.cs` and `ContentFaithfulness.cs`, but the editor does not write storylet files (they stay text until part six), and `chronicle` was already an optional key in the faithfulness check from Task 3. Nothing there changed.
- **Default lines** in both packs, with criteria on `outcome` for a check option. The game's: "#storylet#. The captain's word: #option#." and the same with "#handler# saw it through." or "#handler# tried, and it did not take." (for the author to rewrite); the fixture's `CHRONICLE_STORYLET`, `CHRONICLE_STORYLET_SUCCESS`, and `CHRONICLE_STORYLET_FAILURE`.

### Task 5: reading the chronicle in expressions (2026-11-20)

As designed: `chronicle.count('kind', n)` at the root and `x.chronicle.count('kind', n)` on a slot. `IStateSource` gains `ChronicleCount(kind, days)` and `ChronicleCount(crewId, kind, days)`, which the storylets module's `StateSource` reads from `IChronicleView`; the window is in days, today included, from the chronicle's own today. The rewrite checks the kind against `ChronicleKinds` ("No chronicle kind named deaths; the kinds are founding, death, mission, call, storylet.") and `n` as a whole-number constant from 1 ("chronicle.count reads a whole number of days from 1, not 0."), each at its position; anything else in the call reads "The chronicle is read as chronicle.count('death', 120): a kind in quotes and a number of days." `chronicle` is no longer refused, and `dept` is, as "dept waits for departments (Phase 5)." The rule against hidden memory reads the chronicle as unknown without a change to `TraitReading`, which already reads any call but `toward` as unknown; a test pins it.

Changed or settled while building:

- **No upper bound on `n`.** `history.count` stops at `history.cycles` because the history keeps no more; the chronicle keeps every entry, so any whole number from 1 is read.
- **Registered last is no obstacle.** The storylets module reads `IChronicleView` through `context.View` and `bus.View`, which find a view when it is read, not when the module registers, so the chronicle registering after it changes nothing. The storylets module's test harness now registers the chronicle module too, since every read of the state builds a `StateSource` over it.
- **`chronicle` is a fifth root** of the expression's fixed signature, after `storylet`. The error that lists what an expression may read now ends "ship, cycle, storylet, and chronicle"; a crew member's reads end "trait, history, and chronicle".
- **`storylet` and `chronicle` are reserved slot names.** The loader's list of reserved slot names lacked `storylet`, so a slot so named would have failed the expression book's own check with an exception rather than a refusal; both are in the list now, beside `ship` and `cycle`.
- **Dev Play's dry run** reads the chronicle as it stands, not as it stood when the itinerary opened: an answer given since the opening has already written its entry, and is counted. It is the same kind of dev-mode approximation as a trait's clock there; only storylet entries can be written while the itinerary is open.
- **The tests' journey is 30 days**, five of the fixture calendar's six-day weeks: two deaths with 100 days and two ordinary cycles between them pass `chronicle.count('death', 120) >= 2` at the next itinerary; the same with the first cycle a 5-week journey, in the same number of cycles, does not.

### Task 6: the Chronicle page (2026-11-20)

As designed: `GET /legacy/{id}/chronicle` reads the legacy without writing, as the bridge's page does, so it opens in any phase, a call waiting included, and changes nothing; `?show=notable` lists the pack's `notable` weight and up, anything else All. Days are headed by `Dates.Full` (`Dates.FullNarrow` at 40rem or less), newest first. Weight 4 and 5 are set apart; a death carries an "In memory" mark and the subjects' frozen names in its head. All and Notable are a pair of links, not remembered. Play shows no kind, weight, cycle, or day number: the calendar's play-mode guard now reads the Chronicle, both ways, at every step it takes.

Changed or settled while building:

- **Within a day, by the last event an entry records.** The plan said "by `sourceSeq`, newest first", but a mission's entry lists its end's sequence number first and its deaths' after (Task 3), so sorting by the first would put the mission below the deaths it waited for. The page sorts by the largest of an entry's numbers, then by the order written, so it reads in the order the entries were written, which is the true order (5.4).
- **Dev mode is Dev Play.** As the bridge does, the dev details follow the route, not the setting: `/dev/legacy/{id}/chronicle` shows each entry's kind, weight, cycle, source sequence numbers, and id in a dashed line under its text, and its links stay under `/dev`.
- **The top bar link** sits in the ship cell beside "Legacies", as a quiet link of the same kind; on the Chronicle page the same place reads "Bridge", which draws the phase as it stands. `TopBarView` gains the legacy's id for it; a bar drawn without one shows neither.
- **The marks**, within the design system: a set-apart entry has a 3px `ink-2` edge at its left (never azure) and its text in the display face at lg; the memorial's mark is a hairline chip reading "In memory", beside the name at 500. The selected toggle takes the selection fill, as a selected tab does. `bridge-design.md` lists the entry and the date heading.
- **The browser test** (`tests/browser/play.sh`) opens the Chronicle from the top bar after the cycle it plays, checks the founding is the oldest entry and nothing scrolls sideways at 400 and 1280 pixels, follows Notable, and goes back to the itinerary by Bridge.
