# 3. Data model

A chapter of the [Flagship: Ready Room GDD](../gdd.md). Section numbers are permanent ids; cross-references such as "section 4.3" find their chapter through the index.

Two kinds of data, kept strictly apart:

- **Content** (author-written, in git, YAML): templates, encounters, gear, traits, storylets, and grammars. Immutable at runtime.
- **State** (per legacy, in the database): crew, relationships, missions in flight, traits, and the event log.

Every content item carries `tags`. Tags are the glue that makes content combine (section 9).

## 3.1 Crew member (state)

```yaml
id: crew_0042
legacyId: legacy_01
name: CREW_NAME
speciesId: SPECIES_A
age: 31                       # years of 360 days; advances with the calendar (see below)
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
ambitions: [{ kind: reach_rank, target: RANK_4, byDay: 41100 }]
renown: 12
awayMissions: 9               # gates promotion
injuries: [{ kind: INJURY_A, healsAtDay: 37494 }]   # a day number; due a whole number of weeks after the injury
portrait: { face: f03, hair: h11, hairTint: "#3a2b1f", uniform: u_dept_a, features: [sp_a_ridge] }
isCaptain: false
```

**Days and ages** (slice 1e-calendar): every field that falls due holds a **day number**, counted from the 1st of the first month of year 1 (13 Vortun 105 is day 37482). As built, a light injury's `healsAtDay` and an incapacitated member's `returnsAtDay`. A year is the calendar's days in a year (360). No crew member has an age in code or content yet, since ageing is Phase 6; when one does, a member written with `age: 31` gets a birth date rolled from the legacy's seed within the year that makes them 31 on the legacy's start day, and ages on its anniversaries.

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

**As built in slice 1e, part three** (2026-11-17; section 15, item 40): traits and story flags are one thing. A trait is a whole number of 0 or more, stored generically on the ship, a crew member, or a pair of crew; ids are lower case (`trait_a`, `shaken`). A trait that has rules has a definition in `content/traits/`:

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
  expires: { weeks: 6 }                 # back to 0 on the day this many weeks (6 days each) after its last change
- id: trait_b
  on: pair
  max: 5
  every: { weeks: 4, add: -1 }          # a signed step every 4 weeks from its last change; not on a trait with levels; rising needs max
  onTargetDied: [{ set: 0 }]            # pairs only
```

The conditional modifiers above wait for the expression language (part four); part three's definitions have one modifier, the check bonus. Every key is optional but `id`. A level's name may be written wherever its number would be (`{ trait: stood_down, set: broken }`, a requirement's `is: broken`); a trait with levels is never added to, by content, a rule, or Dev Play. Every change is clamped to 0 and `max` (or the highest level). A rule's effect is `{ set }` or `{ add }` on its own trait, `{ trait, set | add }` on another, or `{ loyalty }` or `{ morale }`, with `on: holder` (the default), `target` (a pair's), or `crew` (loyalty and morale only). The loader checks every use of a trait in an encounter, storylet, or vignette against its definition: where it may sit, its levels, and no `add` to a trait with levels. The game's pack defines `sensors_quirk`, `shaken`, `shaken_heard`, `stood_down`, `avoids`, `came_back` (slice 1e, part four), and `grieving` (part five).

Since slice 1e-calendar (2026-11-20) a trait's expiry and step count in days from its last change (`changedAtDay`, section 3.8), and happen on the day they fall due, which may be in the middle of a cycle; an expiry due before a step cancels it. The trait also keeps the cycle of its last change (`changedAtCycle`), for `x.trait.t.cyclesSince`. Definitions still write `weeks:`.

## 3.3 Relationship (state)

```yaml
from: crew_0042
to: crew_0017
kind: rival                   # friend | rival | romance | grudge | mentor
strength: 34                  # 0-100
sinceDay: 37170
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
rewards: { shipRenown: 4, admiraltyStanding: 2 }   # not built; designed with Phase 6 (section 15, item 49)
context: [CONTEXT_A, CONTEXT_B]   # themes that reskin text and shift difficulty
```

An assignment template does not list its objectives. It draws them by tag, so the same objective can turn up under many assignments, set against different rivals for the player's attention.

```yaml
id: OBJECTIVE_TEMPLATE_1
tags: [planetary, science]
teamSize: [2, 4]
ifUnattended: { difficulty: 6 }   # rolled with no team; it ends as a mission would, with the objective's own text (and, from Phase 6, rewards) for that outcome
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
rewards: { shipRenown: 2, admiraltyStanding: 1 }   # not built; from Phase 6, effects per outcome beside text (section 15, item 49)
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
      - { trait: trait_a, max: 1, on: ship }   # a trait at launch (1e part three): on the ship, the lead, or anyone standing
    outcomes:                             # optional: a tier written here replaces that tier's default whole
      success: [{ progress: 1 }, { strain: -1 }, { momentum: 1 }]
      partial: [{ strain: 1 }, { trait: trait_a, add: 1, on: ship }]   # the default's strain, and a trait (3.8)
      disaster: [{ strain: 3 }, { injuryRoll: severe }, { trait: trait_g, add: 1, on: actor, toward: lead }]   # a pair (3.8)
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
      effects: [{ withdraw: true }]   # ends the mission; there is no ending a stage early (section 15, item 51)
lines:                                    # any other line that holds only here, with criteria of its own
  - when: { beat: injury, outcome: dead }
    text: ENCOUNTER_07_DEATH_TEXT
```

An encounter's approaches are the ways through that suit the situation, named and tuned by the author: one to four, usually two. An approach's `id` is unique within the encounter and is the key in saved beats and in lines. It may carry any number of `requires`, though the editor's form edits three. The loader refuses an encounter in which every approach has a `requires`, so one is always available. A tier an approach does not write takes the tuning file's default for that tier: critical and success add 1 progress and remove 1 strain, critical adds momentum, partial adds strain, failure adds strain and a light injury roll, and disaster adds more strain and a severe one. An injury roll in an outcome falls on the actor. A disaster chains the encounter's follow-on whatever its approach's disaster outcome says.

An effect can also change a trait (a story flag in slice 1e's first two parts): `{ trait: trait_a, add: 1, on: ship }` or `{ trait: trait_b, set: 1, on: lead }`, in an approach's outcome or a decision's answer. `add` changes the trait by a whole number, which may be negative; `set` makes it a number of 0 or more, or one of its levels by name. `on` is required: `ship`, `actor` (the one who made the check; outcomes only), `lead`, or `team` (each member standing). An outcome's effect with `toward: lead` or `toward: actor`, beside `on: actor` or `on: lead`, makes a **pair** (3.8): the one it lands on holds it toward the other, and when actor and lead are one, no pair is made (slice 1e, part three). An effect changes at most one trait; an outcome or answer may hold several. The mission's result carries every trait it changed, and the traits module applies them when the mission ends; a trait set on someone who died on that mission, or a pair toward them, is dropped. Because a written tier replaces the default whole, a tier that only adds a trait writes the default's entries beside it. The mission log tags the beat that changed a trait: "trait_a +1, ship" for an add, "trait_b = 1, CREW_02" for a set.

An approach's `requires` may read a trait (slice 1e, part three): `{ trait, min, max }` or `{ trait, is }`, `on: ship`, `lead` (the lead as the mission now stands), or `anyone` (someone standing). It reads the snapshot fixed when the team launched, never the traits as they change. The game's Debris Shoal closes its careful approach once `sensors_quirk` has been let ride. A trait can also give a **check bonus** to its holder (a definition's `mission`), added to the score of every check they make and tagged on the beat ("avoids −1"); it too is fixed at launch, before the launch rules run.

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

As built in slice 1e, part four (`content/storylets/`; `docs/plans/2026-11-18-phase-1e-part-four-design.md`), on part one's shape and in part three's words (flags are traits):

```yaml
- id: STORYLET_01
  name: STORYLET_01_NAME          # optional display name
  stakes: 3                       # 1 to 5; weighs most in salience (5.1)
  cast:                           # optional; none: a storylet for the ship. Up to three slots, cast in order
    subject: "subject.history.count('lost_mission', 2) > 0 && subject.trait.trait_a == 0"
    target: "target.aboard && target.history.count('lost_mission', 2) == 0"
  requires: "ship.trait.trait_b == 0"     # optional; read once every slot is bound
  cooldown: { cast: 8, global: 2 }        # optional, in cycles; both default to 0
  text: |-                        # a script (section 6): speech, notes, narration, and a report sentence
    subject: STORYLET_01_LINE_1
    target: STORYLET_01_LINE_2
    = #subject# STORYLET_01_REPORT
  options:                        # two to four
    - id: option_a
      label: STORYLET_01_OPTION_A_LABEL                    # the answer's button, 1 to 40 characters
      check: { skill: SKILL_D, difficulty: 4 }             # settled by a check, with a handler the captain names
      success: { effects: [{ trait: trait_a, set: level_a }, { morale: 3, on: subject }], text: "#handler# STORYLET_01_SUCCESS" }
      failure: { effects: [{ trait: trait_a, set: level_b }, { trait: trait_c, add: 2, on: subject, toward: target }], text: STORYLET_01_FAILURE }
    - id: option_b
      label: STORYLET_01_OPTION_B_LABEL
      effects: [{ trait: trait_a, set: level_b }]
      text: STORYLET_01_OPTION_B_TEXT                      # what the log says once it is chosen
    - id: option_c
      label: STORYLET_01_OPTION_C_LABEL
      requires: "subject.loyalty >= 60"                    # not shown while it is false
      effects: [{ trait: trait_a, set: level_a }, { loyalty: -1, on: subject }]
      text: STORYLET_01_OPTION_C_TEXT
```

- **Cast.** Each slot is a lower-case name (`docs/conventions.md`) with an expression (3.8) that may read the ship, the cycle, its own slot, and the slots before it, never a later one. The slots are cast from the living crew in a seeded order, drawn from the stream `cast:<id>:<cycle>`, searched depth first. Every expression read is a try. The search stops at the first full binding that holds and is out of its cast cooldown, or when `storylets.castTries` (tuning, 100) are spent ("out of tries", which Dev Play and the simulator tell apart from "no cast"). No crew member fills two slots. A storylet with no `cast` is for the ship, and its text and effects name no slot. A storylet arrives at most once a cycle and keeps its cast while it waits. It is not cast again while it waits, and it is withdrawn if anyone in its cast dies. Part three's `scope` is gone, and the loader refuses it: "scope was replaced by cast in part four; a storylet for crew casts subject."
- **Requires.** One expression, read once every slot is bound. Whether a condition goes on a slot or in `requires` is the author's choice; both count towards specificity (5.1). The loader refuses part one's list form, as it refuses `flag:`.
- **Must read a trait.** Some expression across `cast` and `requires` must read a trait, since only traits are moved by its answers. With `cooldown.cast` above 0 the cooldown meets the rule instead. A trait read in an expression must be defined in `content/traits/` ("No trait named nosuch").
- **Cooldown.** With `cast: N`, the same binding (the same crew in the same slots) does not arrive again within N cycles of its last arrival. With `global: M`, the storylet does not arrive at all within M cycles of its last arrival. Both count from the arrival.
- **Text.** Speakers are any slot, `captain`, and `best SKILL_ID` among the living, fit crew. An option's `text` is what the log says once it is chosen.
- **Effects.** `loyalty` and `morale` take a whole number and `on:` a slot, or `crew` for everyone living. `trait` takes `add` or `set` (a level by name) and `on:` a slot or `ship`; with no `on`, it applies to the first slot, or to the ship when there is no cast. `toward:` names another slot, for a pair trait. Nothing else is allowed, and the loader refuses the rest by name.
- **An option's `requires`** is an expression. While it is false, the option is not shown and is refused if sent. One option must have neither `requires` nor `check`, so a storylet can always be answered.
- **An option settled by a check** has `check: { skill, difficulty }` with `success` and `failure`, each with `effects` and `text`, and no effects or text of its own. Choosing it asks the captain to name a **handler**: anyone living, fit, aboard, and not in the cast. Each is listed with their skill and a word for their chance (long shot, even, likely, or sure). The roll is the handler's skill, plus their morale modifier and their trait check bonuses, plus the check dice (4.2), against `difficulty + 7`, drawn from `storylet-check:<offer>`. A margin of 0 or more succeeds. `handler` is a slot only inside `success` and `failure`. With no one fit to send, the option is disabled with "No one fit to send."
- **No hidden memory.** When `cooldown.cast` is 0, every option, and each outcome of a check, must move a trait its expressions read out of the range they allow. The loader works this out: it takes the lowest trait values that let every expression hold, applies the option's trait changes, and refuses an option that leaves them all true. A clause on a stat or the history counts as "may still hold". With `cooldown.cast` above 0, a storylet may come back by design.

**Vignettes** are a second kind on the same shape, in `content/vignettes/`, on the same engine (cast, expressions, and salience), as built in part two and rewritten in part four:

```yaml
- id: VIGNETTE_02
  cast:
    subject: "subject.history.last('lost_crewmate')"
  text: "subject: VIGNETTE_02_LINE_1"     # one to three entries
  default: option_b                       # taken when the itinerary closes unanswered
  options:                                # one small choice: exactly two
    - { id: option_a, label: VIGNETTE_02_OPTION_A_LABEL, effects: [{ morale: 3, on: subject }], text: VIGNETTE_02_OPTION_A_TEXT }
    - { id: option_b, label: VIGNETTE_02_OPTION_B_LABEL, effects: [], text: VIGNETTE_02_OPTION_B_TEXT }
- id: VIGNETTE_01
  cast:
    subject: "subject.history.last('rested')"
  text: "subject: VIGNETTE_01_LINE_1"
  effects: [{ morale: 2, on: subject }]   # no choice: these apply when it arrives
```

A vignette has no stakes and needs no trait. It has either `effects` (at least one) or two `options` and a `default`, never both, and its options take neither `requires` nor `check`. Nothing in its own answers stops it returning, so a cooldown does: a vignette with no `cooldown` takes `{ cast: vignettes.cooldown }` (tuning, 6). Its words are lines of the storylet beats.

How it got here. Part one (2026-11-16) built storylets with a list of requirements and fixed answers. Part two (2026-11-17) added vignettes and requirements on morale, loyalty, and the last cycle. Part three (2026-11-17) renamed flags to traits and added `scope: pair`. Part four (2026-11-19) replaced the scope with `cast` and the list with expressions, and added salience, cooldowns, an option's `requires`, and checks. Of the fuller form sketched before slice 1e, what is not built is tags (for 5.1's index), relationships (`rel(a, b)`, Phase 4), and the captain's own traits on an option (`captain.empathy`). There is no `ifIgnored`, since storylets are always answered (section 15, item 40), and no `cost` (item 35).

## 3.8 Quality

The game is a hybrid: part stats model, part quality-based narrative (section 5).

- **Stats** are typed, numeric, and simulated: skills and their grades (3.10), personality axes, morale, loyalty, renown, and standing. They are the Football Manager half. The rules compute with them, the player sees them in bands, and only the systems that own them change them (content changes them through effect commands).
- **Traits** (story flags until slice 1e, part three; section 15, item 40) are open-ended: named by content, numeric, and stored generically. They are the Fallen London half, and content invents new ones with no code change. **Every trait is a number** (section 15, item 38). A trait nobody has set is 0, a trait never goes below 0 or above its cap, and a trait at 0 is not stored. A trait whose definition names its levels can only be set; one without can be added to and set (item 40, replacing item 38's counter-or-stage rule). Related traits share a prefix (`shaken` and `shaken_heard`). A trait's id is lower case (`docs/conventions.md`), and wherever it is read beside other properties it is read through `trait`: `subject.trait.shaken`, `ship.trait.sensors_quirk`.
- **Tags** stay typed content. A crew member's founding personality traits (3.2) are traits like any other once the crew files name them (slice 1h or crew generation).

A **quality** is a stat or a trait: anything the expression language can read through the read-only **state view** (the "quality view" until 2026-11-17), stat or trait alike: `a.skills.SKILL_A`, `a.grade.ATTRIBUTE_A`, `rel(a, b).strength`, `ship.renown`, `chronicle.count('death', 120)`, or `a.trait.x`. Only traits are stored generically. As built in slice 1e, part three, a trait is on the ship, on one crew member, or on a **pair**, held in the traits module's state:

```yaml
trait: avoids
crewId: crew_0042               # null: the ship
targetId: crew_0017             # a pair's target: the crew member it points at; null for a trait on one
value: 1
changedAtDay: 37494             # the day it last changed: its step and expiry count from here, in days (slice 1e-calendar)
changedAtCycle: 3               # the cycle it last changed in, for x.trait.t.cyclesSince
encounterId: ENCOUNTER_01       # the encounter whose effect last changed it, or null
```

A pair is held by one crew member (the **holder**) and points at another (the **target**), made by an encounter's effect with `toward` (`{ trait: avoids, add: 1, on: actor, toward: lead }`) or a pair storylet's `toward: target`. When the target dies, the definition's `onTargetDied` runs once and the pair goes; when the holder dies, it goes with no rule. Every change publishes `TraitChanged` with its cause (mission, answer, vignette, launch, weekly, expired, death, or developer). A pair trait is not a relationship (3.3): whether relationships become pair traits with levels is Phase 4's question.

**The state view in expressions**, as built in slice 1e, part four. An expression is written with C#'s operators (`&&`, `||`, `!`, `==`, `!=`, `<`, `<=`, `>`, `>=`, `+`, `-`, `*`, and parentheses) over whole numbers and quoted names, and is true or false. It runs on DynamicExpresso, and the loader checks it when the pack loads; every error names the storylet, the key, and the character ("STORYLET_02: cast.subject, at character 45: No trait named nosuch."). A slot is read as a crew member:

| Read | Type | Meaning |
|---|---|---|
| `x.morale`, `x.loyalty` | number | Stats |
| `x.skills.SKILL_A` | number | A skill's level; the skill id is checked |
| `x.fit` | true or false | Not injured or incapacitated |
| `x.aboard` | true or false | Alive and not on a mission; in Phase 1, true whenever the itinerary is open |
| `x.trait.t` | number | The trait's value on them; the trait must be defined, and on crew |
| `x.trait.t.toward(y)` | number | A pair trait `x` holds toward `y`; only a trait defined on a pair, and always with `toward` |
| `x.trait.t.cyclesSince` | number, or unset | Cycles since the trait last changed on `x`; unset when it is not held. Not yet on a pair trait |
| `x.trait.t.daysSince` | number, or unset | Days since it last changed; unset when it is not held. Not yet on a pair trait |
| `x.trait.t == 'level'` | true or false | A level by name; the name is checked |
| `x.history.count('kind', n)` | number | How many of the last `n` cycles `kind` happened to them in |
| `x.history.last('kind')` | true or false | It happened to them in the last cycle |
| `x == y`, `x != y` | true or false | The same crew member |
| `ship.trait.t` | number | A ship trait; `ship.trait.t.cyclesSince` and `.daysSince` as a crew member's |
| `cycle.number` | number | The cycle now starting |
| `storylet.s.cyclesSince` | number, or unset | Cycles since storylet `s` last arrived, anywhere on the ship; unset if it never has. `s` must be a storylet, not a vignette |
| `storylet.s.daysSince` | number, or unset | Days since it last arrived |
| `chronicle.count('kind', n)` | number | How many chronicle entries (3.9) of `kind` were written in the last `n` days, today included (slice 1e, part five) |
| `x.chronicle.count('kind', n)` | number | The same, counting only the entries with `x` among their subjects |

The history is each living crew member's last `history.cycles` (tuning, 20) cycles, kept by the storylets module and served to other modules as `IHistoryView`. Its kinds are `sent`, `hurt`, `rested`, `lost_crewmate` (someone died on the cycle's mission and they are alive, sent or not), `lost_teammate` (someone died on their mission and they came home), and `lost_mission` (they were on a mission whose objective failed or was abandoned). `n` is 1 to `history.cycles`. Anything not in the table is refused by name, for example "rel() waits for relationships (Phase 4)", "grade waits for 1h", and "dept waits for departments (Phase 5)". An expression can reach nothing but the view: no reflection, type names, or static methods, no division, and no side effects.

The four readings of time (slice 1e-calendar) count whole cycles and whole days back from the itinerary now open, to the cycle and day the storylet arrived on (arriving and being answered fall on the same cycle and day) or the trait last changed. **Unset** means it never happened, or the trait is at 0: any `<`, `<=`, `>`, `>=`, or `==` with an unset reading is false, through `+`, `-`, and `*` too, so `storylet.a.daysSince >= 40` and `storylet.a.daysSince < 40` are both false before A has arrived, and "A has arrived" is `storylet.a.cyclesSince >= 0`. `!=` follows C#: an unset reading is not 40, so `!=` is true. They turn on time, not on a trait's value, so the rule against hidden memory (3.7) reads them as unknown, and a storylet that reads only them still needs a trait or a cast cooldown to stop it returning. A duration an effect sets stays in weeks.

**The chronicle in expressions** (slice 1e, part five) is read in days, not cycles: an entry counts while its day is within `n` days of today, today included, so a 35-day journey ages every entry by 35 days. `kind` is one of the five kinds in 3.9, checked when the pack loads ("No chronicle kind named deaths; the kinds are founding, death, mission, call, storylet."), and `n` is a whole number from 1, with no upper bound, since the chronicle keeps every entry. A chronicle department filter (`dept:`) waits for departments (Phase 5). The rule against hidden memory (3.7) reads the chronicle as unknown, as it reads time, so a storylet that reads only the chronicle still needs a trait or a cast cooldown.

## 3.9 Event log entry and chronicle entry (state)

```yaml
# event log: everything, technical, append-only
seq: 88213
legacyId: legacy_01
cycle: 61
type: EncounterResolved
version: 1
payload: { missionId: m_211, encounterId: ENCOUNTER_07, tier: failure, actors: [crew_0042] }

# chronicle: curated, player-facing (as built in slice 1e, part five)
id: chr_0012                  # chr_ and a counter per legacy
day: 37529                    # the day it happened
cycle: 9
kind: death                   # founding | death | mission | call | storylet; later: promotion | hull_lost | succession | ...
weight: 5                     # 1-5; drives what the Chronicle page shows first
subjects:                     # crew ids, with their names as they were when it was written
  - { id: crew_0017, name: Okafor }
text: "Okafor fell at the Collapsed Gallery, on Vance's away team, after 8 cycles aboard."
sourceSeq: [412]              # the events it records; a mission's lists its end, then its deaths
```

The chronicle module (`Sim.Chronicle`, 7.1) writes one entry, once, when what it records happens: the legacy's founding, each death, each call answered, each captain storylet answered (not a vignette), and each mission's end. A mission's deaths are written before its own entry, and a call answered before a death in the same encounter. Entries live in the module's state, one row per legacy, in the order written; a table of their own waits until the list grows too long for one row. `hullSuffix` waits for hulls (Phase 8).

**Chronicle text is rendered once and stored**, with the pack live at that moment, so a later edit to a line never rewrites history (section 6), and each subject's name is frozen with it. The words are the best line for the kind's beat (`chronicle_founding`, `chronicle_death`, and so on), plain prose with no speakers, or for a storylet option its own `chronicle.text`; if no line holds, fixed words in code, so an entry is never lost.

**Weights** come from `content/tuning/chronicle.yaml`:

| Kind | Default weight |
|---|---|
| `founding` | 5 |
| `death` | 5 |
| `storylet` | 3; a storylet's `chronicle: { weight: n }` overrides it, and an option's `chronicle: { weight: n, text: "..." }` overrides both |
| `call` | 3 if overruled (`callOverruled`), 2 if backed or neutral (`callBacked`); a decision's `chronicle: { weight: n }` overrides both |
| `mission` | 2 if failed or abandoned (`missionLost`), 1 if it succeeded (`missionWon`) |

`notable` (3) is the weight from which the Chronicle page's Notable view shows an entry (7.5). A weight of 0 writes no entry: it is not stored, counted, or shown. The loader refuses a weight outside 0 to 5, and `text` anywhere but on a storylet option.

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
