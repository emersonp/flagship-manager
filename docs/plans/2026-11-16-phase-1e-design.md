# Phase 1, slice 1e, part one: a storylet from a mission

**Spec:** `docs/gdd.md` sections 3.7, 3.8, 5.1, and 5.2, and section 15, items 32 to 37. **Builds on:** `main` at 2026-11-16 (`888ffc2`).
**Ends with:** the author plays a mission in the browser, something that happened on it leaves a story flag on the ship or on a crew member, and in the aftermath a storylet arrives because of that flag: a short script, two to four answers, and a visible consequence.

This records what the author said about slice 1e on 2026-11-15 and 2026-11-16, and designs its first part. Items marked **(my call)** were proposed, not asked for.

## 1. Why

The game has one layer today: missions. Nothing happens aboard, so nothing a mission does outlasts its report except injuries and experience. Slice 1e is the second layer. The author's picture of it:

- Storylets come to the captain; they are not shopped for. None to two a cycle.
- A storylet is often a choice between two bad options.
- Many storylets grow out of missions: an encounter leaves a quality on the ship or the crew, and a storylet fires because it is there. "A quirk in the sensors."
- Vignettes are mostly flavour, with a small +1 or -1.
- Captain's time is a different thing: a menu of good things to do. It is not part of 1e.

The author expects to change a lot once it can be played, as with encounters. So the first part is the thinnest thing that can be played and judged.

## 2. Goals, non-goals, anti-goals

**Goals of part one**

1. A mission can leave a story flag: on the ship, on the crew member who acted, on the lead, or on the whole team.
2. A storylet that requires a flag arrives in the aftermath, at most two a cycle.
3. A storylet is a script and two to four answers with authored labels. An answer's effects are fixed.
4. The player sees what a storylet came from and what their answer did.
5. The simulator still plays whole cycles and its figures are reproducible.
6. Encounters with flags stay editable in the browser editor.
7. The game's pack gains two storylets in generated words, so the thread can be played.

**Non-goals of part one** (each is on the GDD's roadmap, section 11.1)

- Vignettes and morale (part two).
- The expression language, casting beyond the flag's own subject, salience scoring, cooldowns, `ifIgnored`, flags that change a mission, and an answer settled by a check (part three; since 2026-11-17, flags that change a mission are part three and the rest part four, and there is no `ifIgnored`).
- The crew file and the chronicle list (part four; part five since 2026-11-17).
- Forms for writing storylets in the editor (part five; part six since 2026-11-17). In part one a storylet is written in its YAML file, which the editor shows as a text file.
- Captain's time (slice 1g).

**Anti-goals**

- A menu. Nothing in part one lets the player go looking for a storylet.
- An answer whose consequence is hidden or random. In part one what an answer does is certain and shown after it is chosen.
- A storylet engine built for cases nobody has played yet.

## 3. Approaches considered

**A. One thin thread first, then widen (chosen by the author).** One flag from one encounter, one storylet, two answers, one visible effect, in the browser; then vignettes and morale, the full engine, the crew file and chronicle, and the editor. The author plays a real storylet soonest. Some of the engine is reshaped as it widens.

**B. All the rules first, then all the screens,** as slice 1c was built. The engine is designed once and measured on the simulator before any screen. Nothing playable until late. The author asked that everything B would have built be on the roadmap; it is (parts two to five).

## 4. Design

### 4.1 Vocabulary

The GDD already has the words. A **story flag** is a named number stored for the legacy, with a scope: nobody (the ship) or one or more crew. A **quality** is anything content can test, a story flag included. This design says "flag" for a story flag. The glossary's **Captain storylet** ("requires a choice and costs captain's time") changes to "a storylet that arrives and requires a choice"; the cost is gone (GDD section 15, item 35).

### 4.2 Content: setting a flag

An effect, in an approach's outcome or a decision's answer, can change a flag:

```yaml
outcomes:
  partial: [{ strain: 1 }, { flag: flag_a, add: 1, on: ship }]
decision:
  bold: [{ progress: 1 }, { flag: flag_b, set: 1, on: lead }]
```

- **Every flag is a number** (the author, 2026-11-16, after Fallen London: a mark today is a count tomorrow, and "has this happened twice?" must not need a rewrite). A flag nobody has set is 0. `add: N` changes it by N, which may be negative; `set: N` makes it N. A flag never goes below 0, and a flag at 0 is not stored.
- `on` is `ship`, `actor` (the one who made the check; outcomes only), `lead`, or `team` (the flag on each member standing). It is required.
- A flag id follows `docs/conventions.md`: lower case, letters, digits, and underscores, starting with a letter, at most 40 characters (`sensors_quirk`). Where it is read beside other properties it is read through `flag`, as `subject.flag.shaken` (part three).
- An effect may change at most one flag; an outcome or an answer may hold several effects.

### 4.3 Content: a storylet

A new kind, `content/storylets/`:

```yaml
- id: STORYLET_01
  name: STORYLET_01_NAME
  stakes: 2                       # 1 to 5; orders arrivals, highest first
  scope: crew                     # ship or crew: whose flags it reads, and whether it has a subject
  requires:                       # every entry must hold; part three replaces this with the expression language
    - { flag: flag_a, min: 1, max: 1 }
    - { flag: flag_b, max: 0 }
  text: |-
    subject: STORYLET_01_LINE_1
    captain: #captain_ask#
    = STORYLET_01_REPORT
  options:
    - id: option_a
      label: STORYLET_01_OPTION_A_LABEL
      effects: [{ loyalty: -2, on: subject }, { flag: flag_a, set: 0 }]
      text: STORYLET_01_OPTION_A_TEXT
    - id: option_b
      label: STORYLET_01_OPTION_B_LABEL
      effects: [{ flag: flag_a, set: 2 }]           # the same flag, moved on: known and let ride
      text: STORYLET_01_OPTION_B_TEXT
```

- **Requires** is a list of one to four entries, and all must hold. Each names a flag and a range: `min` (1 when left out, or 0 when only `max` is given) and `max` (no limit when left out). "Has it happened twice" is `min: 2`; "not yet" is `max: 0`. The list is what lets the author keep a count in one flag and a stage in another, as Fallen London's authors do: a counter is only ever added to, a stage is only ever set, and one flag is never both.
- **Scope.** A storylet says `scope: ship` or `scope: crew`. Its requirements are all read on that scope: the ship's flags, or one crew member's. The loader needs this to check the text and the effects, since it cannot know at load where a flag will be set. A flag found on the other scope does not offer the storylet.
- **The subject.** A storylet for crew arrives for one crew member, its `subject`. A storylet for the ship has none, and the loader refuses a text or an effect that names one. A storylet whose requirements hold for several crew arrives once for each, up to the cycle's limit.
- **Text** is a script, as an encounter's is: speech, notes, narration, and a report sentence. Speakers are `subject`, `captain`, and `best SKILL_ID` among the living, fit crew. An option's `text` is what the log says once it is chosen.
- **Options:** two to four, each with a label of 1 to 40 characters.
- **Effects in part one:** `loyalty` (a whole number, `on: subject` or `on: crew` for everyone living), and `flag` with `add` or `set`, `on: ship` or `on: subject`; `on` may be left out, and then means the scope the storylet arrived for. Nothing else. The loader refuses the rest by name, so part two can add `morale` without ambiguity.
- **A flag nothing reads** is a loader warning in the editor's lint, not an error **(my call: the author will set flags before writing the storylets that read them)**.

### 4.4 Rules

- **From mission to flag.** The resolver records each flag an effect sets, with the crew it lands on, and the mission's result carries the list. `Sim.Missions` does not know what a flag is for. A new module, `Sim.Storylets`, subscribes to the mission's end and stores the flags with the week. A flag set on someone who then died on that mission is dropped.
- **Arrival.** When the aftermath begins, `Sim.Storylets` finds every storylet whose requirement holds and which is not already waiting for that scope. It offers at most `storylets.perCycle` of them (a new tuning value, 2), highest stakes first, then by id, then by the subject's roster order. The rest wait for a later cycle; nothing is lost.
- **No hidden memory.** A storylet is offered whenever its requirement holds and it is not already waiting. What stops it coming back is its own answers: each must move the flag out of the storylet's range, to 0 (dealt with) or onward (to 2, "known and let ride", which another storylet may require). This is Fallen London's way, and it means the flags alone say what will happen next. The loader refuses a storylet with an option that leaves all of its own requirements true, since it would arrive again every cycle.
- **Answering.** A new command answers one waiting storylet with one of its options. Its effects apply at once. Loyalty changes go to the crew module as a command, so `Sim.Storylets` never touches crew state.
- **The cycle waits.** The aftermath cannot be left while a storylet waits, as a mission cannot be left with a call waiting **(my call: ignoring a storylet, with its own consequence, is part three; until then every storylet is answered)**.
- **A subject who is gone.** A storylet whose subject has died is withdrawn, and the flags on them are dropped.
- **Determinism.** Nothing here draws randomness. Text is regenerated on view and stored nowhere, as mission text is.
- **Saved games.** The store's schema version and the export format version each rise by one. Old saves are refused, as now.

### 4.5 The simulator

The simulator's captain answers each waiting storylet with its first option. `SimRunner cycles` reports storylets offered and answered per 100 cycles. The golden hashes move once, because the fixture gains a flag and a storylet; the test says why. Policies that choose between options, fire counts by storylet, and dead-content alarms are part three.

### 4.6 The bridge

```
Aftermath

  Partial success                                  <- as today
  Outcome of OBJECTIVE_NAME, team of 3 led by CREW_01

  Matters aboard                                   <- new, above the report while anything waits
  ┌──────────────────────────────────────────────────────────────┐
  │ STORYLET_01_NAME                    From ENCOUNTER_01 · flag_a │
  │                                                                │
  │ CREW_02   STORYLET_01_LINE_1                                   │
  │ Captain   ...                                                  │
  │                                                                │
  │ [ OPTION_A_LABEL ]   [ OPTION_B_LABEL ]                        │
  └──────────────────────────────────────────────────────────────┘

  Report ...                                       <- as today
  Crew changes ...
```

- One panel for each waiting storylet, in arrival order, built from the call panel's parts: the script, then the answers as buttons with their authored labels.
- The panel's head says where it came from: the encounter that set the flag, and the flag.
- Once answered, the panel stays for the rest of the aftermath in a settled form: the option's text, then tags for what changed ("Loyalty -2 CREW_02", "flag_a 1 to 0"), in the figure style the log's tags use.
- "Advance" is disabled with a reason while any storylet waits.
- In the mission log, the beat whose effect set a flag gains a tag ("flag_a +1, ship").
- A cycle on which no team was sent can still have storylets waiting from an earlier one.

### 4.7 The editor

- **Encounters.** The effect rows of a decision's answers, and each outcome's fields on an approach's tab, gain "Flag", "Add or set", "Amount", and "On". Without this the encounters file could not be saved faithfully from the form once any encounter sets a flag.
- **Storylets.** `content/storylets/base.yaml` appears on the editor's index as a text file, validated on save by the loader like the tuning file. The storylet form is part five (part six since 2026-11-17).

### 4.8 The game's pack

Two storylets in generated words (GDD section 15, item 37), on encounters that exist:

1. **A quirk in the sensors.** Set on the ship when the Debris Shoal's careful approach ends in a partial or a failure. The flag is on the ship, so the storylet has no subject. The dilemma: hold the off-duty watch back to recalibrate now (loyalty falls a little for everyone, and the flag clears), or fly on it (the flag moves to 2: known and let ride).
2. **Someone shaken.** A counter, `shaken`, rises by 1 on the actor when the Collapsed Gallery ends in a disaster. They ask for a word with the captain. Make the time (their `shaken` falls by 1, a stage flag records that they were heard, and everyone else's loyalty dips a little: the captain is seen to have favourites) or tell them to carry on (their loyalty falls, and the stage flag records that they were turned away). The storylet requires `shaken` at 1 or more and the stage flag at 0, so each crew member asks once. An earlier draft had them ask to be kept off the next away team; that needs a flag that changes the briefing and one that moves when a mission launches, which are part three (the author, 2026-11-16: go thin).

Both consequences are thin in part one: loyalty has no effect on play yet, and no rule reads a flag. That is accepted for the first thread; part three makes flags bite on missions. The test fixture gets one placeholder flag and one placeholder storylet.

## 5. Parts of slice 1e after this one

In the order of the GDD's progress table: vignettes and morale; the storylet engine in full; the crew file and the chronicle list; writing storylets in the editor. Captain's time is slice 1g. Each gets its own design when it is next.

## 6. Left to the builder

- The names of the module's state, commands, and events, following the crew and missions modules.
- How a flag's "from" (the encounter that set it) is kept for the panel's head.
- Whether the storylet panel shares a component with the call panel or copies its parts.
- The lint's wording for a flag nothing reads and a storylet whose flag nothing sets.
- The wording of the two generated storylets, within section 4.8's outlines.
