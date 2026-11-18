# 3. Data model

A chapter of the [Starship Sim GDD](../gdd.md). Section numbers are permanent ids; cross-references such as "section 4.3" find their chapter through the index.

Two kinds of data, kept strictly apart:

- **Content** (author-written, in git, YAML): templates, encounters, gear, traits, storylets, and grammars. Immutable at runtime.
- **State** (per legacy, in the database): crew, relationships, missions in flight, flags, and the event log.

Every content item carries `tags`. Tags are the glue that makes content combine (section 9).

## 3.1 Crew member (state)

```yaml
id: crew_0042
legacyId: legacy_01
name: CREW_NAME
speciesId: SPECIES_A
age: 31                       # years; advances with weeks elapsed
rank: RANK_2
status: active                # active | away | injured | departed | dead
departmentId: DEPT_A
post: POST_A
reportsTo: crew_0007           # the org chart
insight: rough                # hidden | rough | clear | keen; see 5.6
parents: []                   # crew ids, for later generations
trainingFocus: SKILL_B        # null = none
skills:                       # level 0-10, xp toward next
  SKILL_A: { level: 6, xp: 40 }
  SKILL_B: { level: 2, xp: 10 }
personality:                  # each axis -2..+2
  caution: 2
  discipline: 1
  empathy: -1
  ambition: 0
traits: [TRAIT_A, TRAIT_B]
morale: 62                    # 0-100; built in 1e part two: resting 50, a minus on checks at 25 or below and a plus at 75 or above
loyalty: 55                   # 0-100; captains get a standing bonus
ambitions: [{ kind: reach_rank, target: RANK_4, byWeek: 600 }]
renown: 12
awayMissions: 9               # gates promotion
injuries: [{ kind: INJURY_A, healsAtWeek: 188 }]
portrait: { face: f03, hair: h11, hairTint: "#3a2b1f", uniform: u_dept_a, features: [sp_a_ridge] }
isCaptain: false
```

The four personality axes are systems vocabulary, not fiction. `caution` and `discipline` drive calls and delegation; the other two drive storylets. The author may rename or add axes.

## 3.2 Trait (content)

```yaml
id: TRAIT_A
tags: [social, steady]
modifiers:
  - when: "check.tags has 'negotiation'"
    effect: { checkBonus: 1 }
  - when: "team.any(other => rel(self, other).kind == 'rival')"
    effect: { strainPerEncounter: 1 }
excludes: [TRAIT_C]
```

Traits never contain bespoke code. They are a list of conditional modifiers over the same expression language storylets use.

**Designed for slice 1e, part three** (section 15, item 40; not yet built): traits and story flags become one thing. A trait is a whole number of 0 or more, stored generically on the ship, a crew member, or a pair of crew; ids are lower case (`trait_a`, `shaken`). A trait that has rules has a definition in `content/traits/`:

```yaml
- id: trait_a
  shown: true                           # false: a marker, never on the roster or the crew file
  on: crew                              # ship | crew | pair
  levels: { 1: level_a, 2: level_b }    # named values; a trait with levels can only be set
  max: 2
  briefing: TRAIT_A_BRIEFING            # the chip beside the holder in the briefing
  launch:                               # when a mission starts: sent, notSent, withTarget (pairs)
    sent: [{ set: level_b }, { loyalty: -2 }]
    notSent: [{ set: 0 }]
  mission: { checkBonus: -1 }           # on every check the holder makes; withTarget: true for pairs
  expires: { weeks: 6 }                 # back to 0 after this many weeks unchanged
- id: trait_b
  on: pair
  max: 5
  every: { weeks: 4, add: -1 }          # a signed weekly step; not on a trait with levels; rising needs max
  onTargetDied: [{ set: 0 }]            # pairs only
```

The conditional modifiers above wait for the expression language (part four); part three's definitions have one modifier, the check bonus.

## 3.3 Relationship (state)

```yaml
from: crew_0042
to: crew_0017
kind: rival                   # friend | rival | romance | grudge | mentor
strength: 34                  # 0-100
sinceWeek: 161
lastEventId: evt_88213
```

Directed, and sparse: an edge exists only once two people have history. A grudge can be one-sided. With 500 crew this stays in the low thousands of edges, not 250,000.

## 3.4 Assignment and objective templates (content)

```yaml
id: ASSIGNMENT_TEMPLATE_1
tags: [survey, deep_space]
mandateEligible: true
sites:
  count: [1, 3]               # one site per cycle
  draw: { tags: [planetary] }
  weeks: 1                    # length of each cycle
objectives:
  offer: { 1: 6, 2: 3, 3: 1 } # per site: how many objectives, by weight. One is the rule
  draw: { tags: [science, rescue, repair] }
carry: [SITE_FLAG_A]          # flags an earlier site can set and a later one reads
rewards: { shipRenown: 4, admiraltyStanding: 2 }
context: [CONTEXT_A, CONTEXT_B]   # themes that reskin text and shift difficulty
```

An assignment template does not list its objectives. It draws them by tag, so the same objective can turn up under many assignments, set against different rivals for the player's attention.

```yaml
id: OBJECTIVE_TEMPLATE_1
type: survey                  # one of ~10 objective types
tags: [planetary, science]
teamSize: [2, 4]
baseStakes: 2                 # 1-5
ifUnattended:
  check: { difficulty: 6 }    # rolled with no team; omit for a fixed result
  onFail: [{ shipRenown: -1 }, { chronicle: UNATTENDED_TEXT_1 }]
                              # the author sets this per objective; the default is the roll
stages:                       # in order; each draws encounters with every listed tag
  - slot: approach
    draw: { tags: [travel, planetary] }
    count: 1
  - slot: complication
    draw: { tags: [hazard] }
    count: [1, 2]             # how many: rolled from 1 to 2 when the mission is drawn
  - slot: objective
    draw: { tags: [science, objective] }
    count: 1
  - slot: extraction
    draw: { tags: [travel] }
    count: 1
    skippable: true           # momentum may skip what this stage draws (section 4.3)
rewards: { shipRenown: 2, admiraltyStanding: 1 }
text:                         # the objective's own words for how it ends, by outcome (section 6)
  fullSuccess: OBJECTIVE_TEMPLATE_1_SUCCESS_TEXT
  loss: OBJECTIVE_TEMPLATE_1_LOSS_TEXT
lines:                        # any other line that holds only on missions for this objective
  - when: { beat: collapse }
    text: OBJECTIVE_TEMPLATE_1_COLLAPSE_TEXT
```

An objective template does not list encounters. It lists stages that draw from the encounter pool by tag, so every new encounter is automatically available to every template whose tags match.

A stage is a draw rule, not a pool of its own. Every stage draws from the one encounter pool, an encounter is drawn at most once per mission, and an encounter that another chains to is never drawn. The loader refuses an objective whose stages, each drawing its most in order, could run out of matching encounters, so a stage always finds its count. Once drawn, the mission is a single list of encounters, played one at a time, and each encounter carries its stage's `slot` and `skippable` mark. The slot is a free label: it changes nothing in play, but lines can test it (section 6) and the bench reports it.

## 3.5 Encounter (content)

```yaml
id: ENCOUNTER_07
tags: [hazard, planetary]
variants: [VARIANT_A, VARIANT_B, VARIANT_C, VARIANT_D]
text:
  arrive: ENCOUNTER_07_ARRIVE_TEXT        # the encounter's own words, written here and nowhere else (section 6)
approaches:                               # one to four, in the order the author writes them
  - id: APPROACH_A
    name: APPROACH_A_NAME                 # shown in the editor, the log's roll detail, and the bench
    skill: SKILL_A
    difficulty: 5
    gearTag: scanner                      # optional: gear with this tag adds its bonus
    requires:                             # optional: every entry must hold for the approach to be available
      - { skill: SKILL_A, level: 4 }      # someone standing on the team has this level, after injury
      - { gear: scanner }                 # the loadout carries gear with this tag
    outcomes:                             # optional: a tier written here replaces that tier's default whole
      success: [{ progress: 1 }, { strain: -1 }, { momentum: 1 }]
      partial: [{ strain: 1 }, { flag: flag_a, add: 1, on: ship }]   # the default's strain, and a story flag (3.8)
    text:                                 # what is said before this check, and what the log says after, by tier
      attempt: ENCOUNTER_07_APPROACH_A_ATTEMPT_TEXT
      success: ENCOUNTER_07_APPROACH_A_SUCCESS_TEXT
      disaster: ENCOUNTER_07_APPROACH_A_DISASTER_TEXT
  - id: APPROACH_B
    name: APPROACH_B_NAME
    skill: SKILL_C
    difficulty: 7
decision:                     # optional
  stakes: 3
  prompt: DECISION_PROMPT_1
  text: { call: DECISION_PROMPT_1_CALL_SCRIPT }    # what the team says on the call: a script (section 6)
  labels: { bold: DECISION_PROMPT_1_BOLD_LABEL }   # what the call's answer buttons say
  options:
    - id: press_on
      leadPrefers: "lead.caution <= -1"
      effects: [{ strain: 1 }, { objectiveBonus: 1 }]
    - id: withdraw
      leadPrefers: "lead.caution >= 1"
      effects: [{ abortStage: true }]
lines:                                    # any other line that holds only here, with criteria of its own
  - when: { beat: injury, outcome: dead }
    text: ENCOUNTER_07_DEATH_TEXT
```

An encounter's approaches are the ways through that suit the situation, named and tuned by the author: one to four, usually two. An approach's `id` is unique within the encounter and is the key in saved beats and in lines. It may carry any number of `requires`, though the editor's form edits three. The loader refuses an encounter in which every approach has a `requires`, so one is always available. A tier an approach does not write takes the tuning file's default for that tier: critical and success add 1 progress and remove 1 strain, critical adds momentum, partial adds strain, failure adds strain and a light injury roll, and disaster adds more strain and a severe one. An injury roll in an outcome falls on the actor. A disaster chains the encounter's follow-on whatever its approach's disaster outcome says.

An effect can also change a story flag (slice 1e): `{ flag: flag_a, add: 1, on: ship }` or `{ flag: flag_b, set: 1, on: lead }`, in an approach's outcome or a decision's answer. `add` changes the flag by a whole number, which may be negative; `set` makes it a number of 0 or more. `on` is required: `ship`, `actor` (the one who made the check; outcomes only), `lead`, or `team` (each member standing). An effect changes at most one flag; an outcome or answer may hold several. The mission's result carries every flag it changed, and the storylets module stores them when the mission ends; a flag set on someone who died on that mission is dropped. Because a written tier replaces the default whole, a tier that only adds a flag writes the default's entries beside it. The mission log tags the beat that changed a flag: "flag_a +1, ship" for an add, "flag_b = 1, CREW_02" for a set.

The fixture's placeholder form, which the tests use, is one line to an approach: `{ id: careful, name: APPROACH_A_NAME, skill: SKILL_A, difficulty: 5, gearTag: scanner }`, and a second whose `outcomes` write the critical and success tiers with the momentum the old fast approach earned.

An encounter carries its own narrative. Its `text` gives the words for its own beats (arriving, each approach before its check and by tier, and its decision, whose `labels` name the call's answer buttons), and its `lines` are ordinary lines (section 6) that hold only at this encounter. Nothing about an encounter is written in the shared lines; those hold what belongs to no encounter, and the fallbacks.

## 3.6 Gear (content)

```yaml
id: GEAR_ITEM_1
tags: [scanner]
cost: 2                       # requisition
slots: 1
modifiers:
  - when: "approach.gearTags has 'scanner'"
    effect: { checkBonus: 2 }
  - when: "encounter.tags has 'hazard'"
    effect: { injurySeverity: -1 }
```

Gear, like traits, is only modifiers. Gear opens approaches (through `gearTags`) as well as improving them.

## 3.7 Storylet (content)

As built in slice 1e, part one (`content/storylets/`):

```yaml
- id: STORYLET_01
  name: STORYLET_01_NAME          # optional display name
  stakes: 2                       # 1 to 5; orders arrivals, highest first (5.1)
  scope: crew                     # ship or crew: whose flags it reads, and whether it has a subject
  requires:                       # one to four entries, all must hold; part three replaces this with the expression language
    - { flag: flag_b, min: 1 }
    - { flag: flag_c, max: 0 }
  text: |-                        # a script (section 6): speech, notes, narration, and a report sentence
    subject: STORYLET_01_LINE_1
    captain: STORYLET_01_LINE_2
    = #subject# STORYLET_01_REPORT
  options:                        # two to four
    - id: option_a
      label: STORYLET_01_OPTION_A_LABEL                     # the answer's button, 1 to 40 characters
      effects: [{ flag: flag_b, add: -1 }, { flag: flag_c, set: 1 }]
      text: STORYLET_01_OPTION_A_TEXT                       # what the log says once it is chosen
    - id: option_b
      label: STORYLET_01_OPTION_B_LABEL
      effects: [{ flag: flag_c, set: 2 }, { loyalty: -2, on: subject }]
      text: "#subject# STORYLET_01_OPTION_B_TEXT"
```

- **Requires.** Each entry names a flag and a range: `min` is 1 when left out, or 0 when only `max` is given; `max` has no limit when left out. "Has it happened twice" is `min: 2`; "not yet" is `max: 0`. The loader refuses requirements no value can satisfy. Since part two an entry may instead read the subject's morale or loyalty (`{ stat: morale, max: 25 }`, with `min` and `max` as a flag's) or a fact about the cycle just ended (`{ last: sent }`, `hurt`, `rested`, or `lost_crewmate`; `not: true` turns it round); a storylet for the ship can read neither, and every storylet still needs a flag among its requirements, since only flags can be moved by its own answers.
- **Scope.** A storylet for the ship reads the ship's flags and has no subject; its text and effects may not name one. A storylet for the crew reads one crew member's flags and arrives once for each crew member whose flags hold, as its `subject`.
- **Text.** Speakers are `subject` (crew scope only), `captain`, and `best SKILL_ID` among the living, fit crew. An option's `text` is what the log says once it is chosen.
- **Effects.** `loyalty` (a whole number, `on: subject` or `on: crew` for everyone living), `morale` (the same, since part two), and `flag` with `add` or `set`, `on: ship` or `on: subject`, or no `on` for the scope the storylet arrived for. Nothing else; the loader refuses the rest by name.
- **No hidden memory.** Each option must move the storylet's own flags out of its range, to 0 (dealt with) or onward (to 2, "known and let ride", which another storylet may require). The loader refuses an option that leaves every requirement true, since the storylet would come back every cycle.

**Vignettes, as built in slice 1e, part two,** are a second kind on the same shape, in `content/vignettes/`:

```yaml
- id: VIGNETTE_02
  scope: crew
  requires:
    - { last: lost_crewmate }
  text: "subject: VIGNETTE_02_LINE_1"     # one to three entries
  default: option_b                       # taken when the itinerary closes unanswered
  options:                                # one small choice: exactly two
    - { id: option_a, label: VIGNETTE_02_OPTION_A_LABEL, effects: [{ morale: 3, on: subject }], text: VIGNETTE_02_OPTION_A_TEXT }
    - { id: option_b, label: VIGNETTE_02_OPTION_B_LABEL, effects: [], text: VIGNETTE_02_OPTION_B_TEXT }
- id: VIGNETTE_01
  scope: crew
  requires:
    - { last: rested }
  text: "subject: VIGNETTE_01_LINE_1"
  effects: [{ morale: 2, on: subject }]   # no choice: these apply when it arrives
```

A vignette has no stakes and needs no flag. It has `effects` (at least one) or two `options` and a `default`, never both. Nothing in its own answers stops it returning, so a cooldown does: it does not arrive again for the same scope within `vignettes.cooldown` cycles (tuning, 6). Its words are lines of the storylet beats.

Part three (designed 2026-11-17 in `docs/plans/2026-11-17-phase-1e-part-three-design.md`, not yet built) renames flags to traits (`{ trait: shaken, min: 1 }`), adds `is:` for a trait's named levels, and adds `scope: pair`, with `target` as a speaker, as an `on:` for effects, and as `of: target` on stat and last-cycle requirements. Part four (renumbered on 2026-11-17; this paragraph said part three until then) replaces `requires` with the expression language over the state view, and adds casting from the crew in place of the one subject, salience in place of the fixed order, storylet cooldowns, and an option settled by a check. There is no `ifIgnored`: storylets are always answered (section 15, item 40). The fuller form it is designed towards, as sketched before slice 1e (`cost` is gone: section 15, item 35; `ifIgnored` is gone: item 40):

```yaml
id: STORYLET_031
kind: captain                 # captain | vignette
stakes: 3
tags: [rivalry, department]
cast:
  a: "crew.status == 'active'"
  b: "rel(a, b).kind == 'rival' and rel(a, b).strength >= 50 and a.departmentId == b.departmentId"
requires: "a.flag.rivalry_done == 0"
cooldown: { perCast: 40, global: 5 }
text: STORYLET_031_TEXT
options:
  - id: option_a
    requires: "captain.empathy >= 1"
    effects: [{ relAdjust: [a, b, -20] }, { morale: [a, 5] }]
  - id: option_b
    effects: [{ transfer: [b, otherDepartment] }, { morale: [b, -10] }]
ifIgnored:
  afterWeeks: 3
  effects: [{ relAdjust: [a, b, 15] }, { flag: rivalry_escalated, set: 1, on: ship }]
  then: STORYLET_032
```

## 3.8 Quality

The game is a hybrid: part stats model, part quality-based narrative (section 5).

- **Stats** are typed, numeric, and simulated: skills and their grades (3.10), personality axes, morale, loyalty, renown, and standing. They are the Football Manager half. The rules compute with them, the player sees them in bands, and only the systems that own them change them (content changes them through effect commands).
- **Story flags** are open-ended: named by content, numeric, and stored generically. They are the Fallen London half, and content invents new ones with no code change. **Every flag is a number** (section 15, item 38). A flag nobody has set is 0, a flag never goes below 0, and a flag at 0 is not stored. A flag is either a **counter**, only ever added to ("how many times": `owes_the_captain: 3`), or a **stage**, only ever set ("where this stands": `rivalry_escalated: 1`), never both; a story that needs both uses two flags, which share a prefix (`shaken` and `shaken_heard`). A flag's id is lower case (`docs/conventions.md`), and wherever it is read beside other properties it is read through `flag`: `subject.flag.shaken`, `ship.flag.sensors_quirk`.
- **Traits and tags** stay typed content (3.2). Testing for one (`a.has('TRAIT_A')`) reads like any other quality.

A **quality** is anything the expression language can read through the read-only quality view, stat or flag alike: `a.skills.SKILL_A`, `a.grade.ATTRIBUTE_A`, `rel(a, b).strength`, `ship.renown`, `chronicle.count('death', dept: a.departmentId, within: 20)`, or `a.flag.x`. Only story flags are stored generically. As built in slice 1e, part one, a flag is on the ship or on one crew member, held in the storylets module's state:

```yaml
flag: flag_a
crewId: null                    # null: the ship
value: 1
setAtWeek: 164
encounterId: ENCOUNTER_01       # the encounter whose effect last changed it, or null for a storylet's answer
```

A flag on several crew at once (`scope: [crew_0042, crew_0017]`, for a relationship) waits for part three.

**Designed for slice 1e, part three** (section 15, item 40; not yet built): a quality is a **stat** or a **trait**, and the read-only view of both is the **state view**. Story flags are renamed traits and move to a module of their own, `Sim.Traits`; they keep everything above, read as `subject.trait.shaken`. Decision 38's counter-or-stage rule gives way to named levels: a trait that names its levels can only be set. A trait may sit on a **pair**: held by one crew member (`crewId`) and pointing at another (`targetId`), made by an encounter's effect with `toward` (`{ trait: avoids, add: 1, on: actor, toward: lead }`). A pair trait is not a relationship (3.3): whether relationships become pair traits with levels is Phase 4's question.

## 3.9 Event log entry and chronicle entry (state)

```yaml
# event log: everything, technical, append-only
seq: 88213
legacyId: legacy_01
cycle: 61
type: EncounterResolved
version: 1
payload: { missionId: m_211, encounterId: ENCOUNTER_07, tier: failure, actors: [crew_0042] }

# chronicle: curated, player-facing
id: chr_0412
cycle: 61
kind: death                   # death | promotion | hull_lost | succession | feat | ...
weight: 5                     # 1-5; drives what the chronicle screen shows first
subjects: [crew_0017]
hullSuffix: A
text: "…rendered once, then frozen…"
sourceSeq: [88213, 88214]
```

Chronicle text is rendered once and stored, so later grammar edits never rewrite history.

## 3.10 Skills, departments, and attributes (content)

Phase 1 has one small flat set of skills, which is enough for prototyping. In the long run skills are finer, and each one is grouped:

```yaml
id: SKILL_A
name: SKILL_A_NAME
department: DEPT_A            # a department, such as command or engineering
attribute: ATTRIBUTE_A        # a broad capacity, such as fitness or presence
```

- Every skill belongs to a department, an attribute, or both; most belong to one of each. Departments and attributes are content, named by the author.
- A crew member's **grade** in a department or attribute is the plain mean of the levels of the skills under it. It is computed, never stored, and is a stat like any other (3.8).
- A grade has a shape as well as a height. Being moderately good at every skill under an attribute and being average with one exceptional skill can give the same grade, and the difference is not normally obvious. Good insight shows the shape (5.6).
- Checks always name a skill, never a department or attribute grade. This is looked at again with the bench when Phase 5 builds grades (section 15, item 28).
- Built with Phase 5 (departments and insight). Until then a skill has neither field; the loader starts requiring at least one when grades are built.
