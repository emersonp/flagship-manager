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

- **`Sim.Chronicle`** (new; depends on `Sim.Core` only, as every `Sim.*` module must): its state (the entries and the counter), its subscriptions, and `IChronicleView`. It gets its words through `INarrator`, an interface in `Sim.Core.Narrative` that `Sim.Narrative` implements and `GameHost` passes in at registration (changed while planning, 2026-11-20: the architecture test allows a module no other reference). Registered after the crew, missions, storylets, and traits modules, so it hears each event after the module that owns it has acted **(my call)**.
- **`Sim.Missions`** publishes a new contract, `CallAnswered`, when an answer is applied, and its mission facts for the chronicle (objective, site, encounter, approach, lead) travel on the Core contracts in 5.2, so `Sim.Chronicle` never reads a mission beat.
- **`Sim.Crew`**: `CrewDied` carries what killed them (5.2).
- **`Sim.Storylets`**: implements the `chronicle` reads over `IChronicleView`; the loader reads `chronicle:` on storylets and options.
- **`Sim.Expressions`**: the two reads, the kind check, and the `dept:` hint.
- **`Sim.Content`**: the tuning file, `chronicle:` on storylets, options, and decisions, and the refusals in 4.4 and 4.5.
- **`Web`**: the Chronicle page, the top bar link, and the dev details.
- **`SimRunner`**: the counts.

### 5.2 Contracts in `Sim.Core`

- `CallAnswered(string MissionId, string DecisionId, string EncounterId, string Answer, string LeadPrefers, string Standing, string LeadId)`: a new event at version 1. Answers and standings travel as words, since `CallAnswer` and `CallStanding` are `Sim.Missions`' types **(my call)**.
- `CrewDied` gains `EncounterId` and `ApproachId`, at version 2.
- `MissionEnded` already carries the objective and the result; the chronicle reads the team, lead, and site from `MissionStarted` and `SiteArrived`, which it keeps until the mission ends.
- `INarrator` (in `Sim.Core.Narrative`): `string? Line(Situation situation, string key)`, the chosen line expanded to plain text, or null when no line holds.
- `IChronicleView`: `int Count(string kind, int days)` and `int Count(string crewId, string kind, int days)`, and the entries for the page.
- The five kinds as constants (`ChronicleKinds`), as `HistoryKinds` are.

### 5.3 Persistence

- The chronicle module's state is a module row, as every module's is. The schema version moves, so an old save is refused.

### 5.4 Rules that hold

- An entry is written once, at the event, and its text never changes.
- The chronicle draws from no random stream and changes no other module's state.
- Every event that writes an entry writes at most one.
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
