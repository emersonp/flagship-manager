# Phase 1, slice 1e, part two: morale, vignettes, and the captain's itinerary

**Spec:** `docs/gdd.md` sections 3.7, 3.8, 5.1, 5.2, and 5.6, and section 15, items 19, 29, 34, and 35. **Builds on:** `main` at 2026-11-17 (`d4625a3`), and slice 1e's first part (`2026-11-16-phase-1e-design.md`).
**Ends with:** the author plays a cycle in the browser. Each crew member's morale moves after the mission and over the week. High or low morale changes their checks. A new screen, the captain's itinerary, opens before the orders. It lists the storylets that must be answered and the vignettes that may be read, in any order, with the captain's time shown.

This records what the author said about part two on 2026-11-17 and designs it. Items marked **(my call)** were proposed, not asked for.

## 1. What the author said (2026-11-17)

- **Morale bites.** Low morale is a small minus on a crew member's checks and high morale a small plus. It is not only shown.
- **What moves morale,** besides vignettes and answers: the mission's result for the team, being hurt, a crewmate's death, a cycle not sent (rest), and a weekly drift back towards a resting value.
- **What brings a vignette:** story flags, a crew member's morale, and a few facts about the last cycle (who was sent, who was hurt, who rested, who lost a crewmate). These are a small fixed set ahead of part three's expression language.
- **The captain's itinerary.** A screen after the aftermath and before the briefing, called "Captain's itinerary". It lists storylets and vignettes, and the player takes them in any order. Every storylet must be answered before the briefing can begin. It shows a count of captain's time, 3, which does nothing yet.

## 2. Goals, non-goals, anti-goals

**Goals**

1. Every crew member has morale, 0 to 100. It starts at a resting value, moves on the events in section 1, and drifts back each week.
2. A crew member whose morale is low takes a minus on every check they make, and one whose morale is high takes a plus. The log says so.
3. A vignette is a short storylet: one to three sentences, no choice or one small choice with a default, and a small effect on morale, loyalty, or a flag.
4. A storylet's or a vignette's requirements can read a flag, morale or loyalty, and four facts about the last cycle.
5. A new phase, the captain's itinerary, holds the storylets (which must be answered) and the vignettes (which may be read or answered). The aftermath no longer holds storylets.
6. The simulator plays whole cycles with morale and vignettes, reports them, and stays reproducible.
7. The game's pack gains five vignettes in generated words. The test fixture gains placeholder ones.

**Non-goals** (each is on the roadmap, GDD 11.1)

- Spending captain's time (slice 1g). Part two only shows the count.
- Insight and bands that hide the figure (Phase 5). The roster shows morale as a word because the word is what changes a check; nothing is hidden yet.
- Salience, casting, the expression language, `ifIgnored`, and cooldowns for storylets (part three). Vignettes get a fixed cooldown here because, unlike a storylet, nothing in their own answers stops them returning.
- Writing vignettes in a form (part five). The vignettes file opens in the editor as a text file, as the storylets file does.
- Morale that makes someone refuse orders or leave (Phase 6).

**Anti-goals**

- A vignette that is a ruling. A vignette's choice is small and free, and leaving it unanswered is always allowed.
- Morale as a second hidden die. The modifier is a fixed step at two thresholds, and the log names it on every check it touches.
- A menu. The itinerary lists what arrived; the player cannot go looking for more.

## 3. Approaches considered

**A. Vignettes as a second kind on the storylet engine (chosen).** One file shape, one loader, one module, one panel. A field says which kind it is, and the loader holds each kind to its own rules. This is what the roadmap asks for ("vignettes on the same engine") and what the GDD's fuller form sketches (`kind: captain | vignette`, section 3.7).

**B. A separate vignettes module.** Cleaner rules for each, but two arrival orders, two sets of requirement code, and two panels for one idea. Rejected.

## 4. Design

### 4.1 Morale

Morale is a stat (GDD 3.8), owned by the crew module, as loyalty is. Tuning in `content/tuning/crew.yaml`, every value required **(my call on every figure; the author tunes them in 1f)**:

```yaml
morale:
  resting: 50          # where a new crew member starts and where it drifts back to
  min: 0
  max: 100
  low: 25              # at or below: low morale
  high: 75             # at or above: high morale
  checkBonus: 1        # minus at low, plus at high, on every check the crew member makes
  drift: 2             # each week, this many points back towards resting
  rested: 3            # a cycle not sent, while fit
  hurt: -4             # a light injury on a mission
  incapacitated: -10
  crewmateDied: -8     # to every living crew member, for each death
  result:              # to each member of the team who came home
    fullSuccess: 6
    partialSuccess: 2
    withdrawal: -2
    loss: -6
    incomplete: -3
```

- **The check.** The roster view gives each team member a `MoraleBonus` (−1, 0, or +1 times `checkBonus`), fixed when the mission launches, as the skill levels are. `Sim.Missions` adds it to every check that member makes and never reads morale itself. A check's beat records the bonus, and the log tags it ("morale +1" or "morale −1"), as it tags gear.
- **The order of a cycle's changes** **(my call)**: at the mission's end, each returning member's result, then each injury, then each death to the living; at the week's end, rest for each fit crew member who was not sent, then drift. Drift moves towards resting and never past it.
- **Commands and events.** `AdjustMorale(CrewId, Delta)` is handled by the crew module, as `AdjustLoyalty` is. Every change publishes `MoraleChanged(CrewId, From, To, Cause)`, where the cause is `mission`, `hurt`, `death`, `rest`, `drift`, or `answer`. A change that changes nothing is not published.
- **Shown.** The roster gains a column "Morale": "Low", "Steady", or "High". The aftermath's crew changes list each member's morale change from the mission, with a tag in the figure style ("Morale −6"). Dev Play's panel shows each crew member's figure.

### 4.2 Requirements a storylet or a vignette can read

`requires` stays a list of one to four entries, all of which must hold. Part one's entries name a flag. Part two adds two kinds:

```yaml
requires:
  - { flag: shaken, min: 1 }            # as in part one
  - { stat: morale, max: 25 }           # a stat: morale or loyalty, with min and max as a flag's
  - { last: hurt }                      # a fact about the last cycle
```

- **Stats** are `morale` and `loyalty`, read on the subject. They need `scope: crew`. `min` and `max` default as a flag's do.
- **Facts about the last cycle**, read on the subject (they need `scope: crew`) **(the four the author picked; the names are my call)**:
  - `sent`: on the last mission and came home.
  - `hurt`: injured or incapacitated on it.
  - `rested`: not sent, and fit when the itinerary opened.
  - `lost_crewmate`: a crew member died on it, and this one is alive.
  - `not: true` turns any of them round: `{ last: sent, not: true }`.
- "The last cycle" is the cycle whose aftermath just ended. The storylets module records the facts from `MissionStarted`, `MissionEnded`, and `CrewDied`, and clears them when the itinerary closes.
- The loader's message for a stat or a fact on a storylet for the ship: "A storylet for the ship has no subject, so it cannot read {what}."

### 4.3 Vignettes

A new file, `content/vignettes/`, on the same shape as a storylet with `kind` left implicit by the folder **(my call: one file for each kind keeps the editor's text pages short, and each file says what its entries are)**:

```yaml
- id: VIGNETTE_01
  name: VIGNETTE_01_NAME
  scope: crew
  requires:
    - { last: rested }
    - { stat: morale, min: 60 }
  text: |-
    subject: VIGNETTE_01_LINE_1
    = VIGNETTE_01_REPORT
  effects: [{ morale: 2, on: subject }]           # a vignette with no choice: these apply when it arrives

- id: VIGNETTE_02
  name: VIGNETTE_02_NAME
  scope: crew
  requires:
    - { last: lost_crewmate }
  text: |-
    > VIGNETTE_02_LINE_1
  default: say_nothing
  options:                                         # one small choice: exactly two options and a default
    - id: say_something
      label: VIGNETTE_02_OPTION_A_LABEL
      effects: [{ morale: 3, on: subject }, { loyalty: 1, on: subject }]
      text: VIGNETTE_02_OPTION_A_TEXT
    - id: say_nothing
      label: VIGNETTE_02_OPTION_B_LABEL
      effects: []
      text: VIGNETTE_02_OPTION_B_TEXT
```

- **A vignette has no stakes** and no "no hidden memory" rule. What stops it returning is a cooldown: it does not arrive for the same scope (the ship, or the same crew member) within `vignettes.cooldown` cycles of the last time (tuning, 6) **(my call)**.
- **No choice, or one.** Either one or more `effects`, applied when it arrives (a vignette that changes nothing is cut, GDD 5.2), or exactly two `options` and a `default` naming one of them. Not both. An option's effects may be empty: saying nothing is allowed to change nothing. The loader's messages: "A vignette has effects or two options, not both.", "A vignette with no choice needs at least one effect.", "A vignette's choice is two options.", "A vignette with options needs a default, and it must be one of them."
- **Effects** are a storylet's (loyalty and flags) and morale: `{ morale: N, on: subject|crew }`. Storylets may use `morale` too, so the loader's "not one of them" list gains it for both.
- **Text** is one to three script entries **(my call: GDD 5.2's "one to three sentences")**, with the same speakers as a storylet. The loader refuses more: "A vignette's text is one to three entries."
- **Arrival.** When the itinerary opens, after the storylets: every vignette whose requirements hold for a scope, not in its cooldown for that scope, is due. Up to `vignettes.perCycle` arrive (tuning, 3) **(my call: GDD 5.1 says five, for a crew of hundreds; eight crew need fewer)**, ordered by the cycles since that vignette last arrived for any scope (never first), then by id, then by the subject's place on the roster. Nothing draws randomness. The rest are dropped; a vignette is not a debt.
- **Leaving.** When the itinerary closes, each unanswered vignette with options takes its default, published as `VignetteAnswered` with `Defaulted: true`, and the settled panels show "Left: {default's label}".

### 4.4 The captain's itinerary

A new phase, `Itinerary`, the first of a cycle: itinerary, orders, briefing, resolution, aftermath **(my call: the week has turned when it opens, so healing, rest, and drift have happened and the vignettes speak of the ship as it now is; and it is where slice 1g's captain's time will be spent. The author's words were "after the aftermath and before the briefing", which this keeps)**.

```
Captain's itinerary                                    Captain's time  3
Week 14. Two matters need an answer before the orders.     Not spent yet

  Storylets                                                <- must be answered
  ┌──────────────────────────────────────────────────────────────┐
  │ A quirk in the sensors              From Debris Shoal · sensors_quirk │
  │ ...script...                                                   │
  │ [ Recalibrate tonight ]   [ Fly on it ]                        │
  └──────────────────────────────────────────────────────────────┘

  Vignettes                                                <- may be read or answered
  ┌──────────────────────────────────────────────────────────────┐
  │ VIGNETTE_02_NAME                              CREW_03 · lost a crewmate │
  │ ...one to three lines...                                        │
  │ [ Say something ]  [ Say nothing ]           Left alone: Say nothing │
  └──────────────────────────────────────────────────────────────┘
  ┌──────────────────────────────────────────────────────────────┐
  │ VIGNETTE_01_NAME                                         CREW_05 │
  │ ...                                                Morale +2 CREW_05 │
  └──────────────────────────────────────────────────────────────┘

  [ To orders ]   A storylet is waiting for an answer.
```

- **Storylets move here.** They are offered when the itinerary opens, not when the aftermath begins, with part one's rules otherwise unchanged (at most `storylets.perCycle`, highest stakes first). The aftermath loses its "Matters aboard" section. The guard that held the aftermath now holds the itinerary, with the same words.
- **Any order.** Every panel can be answered while any other waits. A settled panel stays for the rest of the itinerary, as in part one.
- **The head of a vignette** says who it is about and, when a fact brought it, the fact in words ("lost a crewmate", "back from the mission", "hurt on the mission", "rested").
- **Captain's time** is a readout in the head: "Captain's time 3", with the note "Not spent yet" **(the author: it does nothing yet)**. Its figure is a tuning value, `captainsTime.perCycle: 3` in `tuning/missions.yaml`, so slice 1g starts from it.
- **An empty itinerary** says "Nothing needs the captain this week." and still shows the captain's time and the button. The first cycle of a legacy opens on it.
- **The button** is "To orders". It is disabled with the waiting sentence while a storylet waits; leaving applies the vignettes' defaults.
- **Saved games.** The phase enum gains a value, so the store's schema version and the export's format version rise by one, and old saves are refused, as now.

### 4.5 The simulator

The simulator's captain answers each storylet with its first option, as now, and leaves every vignette to its default. `SimRunner cycles` adds, per 100 cycles, vignettes arrived and answered by default, and at the end of the run the share of living crew at low, steady, and high morale (GDD 10's "morale distribution"). Both golden hashes move: the walkthrough's because a check's beat carries the morale bonus, and the simulation's because of new events. Each test says why.

### 4.6 The editor

- `content/vignettes/base.yaml` appears on the editor's index as a text file, checked by the loader on save, as the storylets file is.
- The storylets lint learns the new requirement kinds: "Nothing sets {flag}" is not said of a stat or a fact.
- The crew tuning page, if the tuning file is edited as text today, gains nothing; the file's new `morale` block is checked on save.

### 4.7 The game's pack

Five vignettes in generated words (GDD section 15, item 37, which allowed generated words for slice 1e's storylets), one for each trigger the author picked, in the voice of the pack's storylets:

1. **Rested** and morale not low: someone back from leave, a line in passing; morale +2. No choice.
2. **Hurt** on the last mission: the medical bay, a short exchange; a choice, visit or leave them be (default), with a little morale and loyalty on the visit.
3. **Lost a crewmate:** someone at the dead crewmate's station; a choice, say something or say nothing (default).
4. **Low morale** and sent last cycle: a short word overheard in a corridor; loyalty −1. No choice. This is the signal that someone needs attention.
5. **A flag:** `sensors_quirk` at 2 (let ride): the forward array reads a ghost on the night watch; morale −1 to everyone. No choice.

The test fixture gains three placeholder vignettes: one with effects, one with a choice, and one on a ship flag.

## 5. Left to the builder

- Names of the new state, events, and the itinerary's view types, following part one's.
- Whether the itinerary's panels share the storylet panel's component or extend it.
- How a fact's words in a vignette's head are kept (a fixed table beside the fact names is enough).
- The wording of the five vignettes within section 4.7.
