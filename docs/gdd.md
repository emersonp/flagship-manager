# Starship Sim: Game Design Document v1

Status: draft v1.4, 2026-11-13, revised after the author's review (section 15) and the mission prototype (section 4.7). Vocabulary follows `GLOSSARY.md`. Creative content is placeholder throughout (`SPECIES_A`, `OBJECTIVE_TEMPLATE_1`, `SKILL_A`); numbers are starting points for the balance simulator, not commitments.

## 1. Summary

The player holds the **chair** of a ship. Each **cycle** they choose missions, pick **away teams**, and then live with the consequences aboard. One player's whole game is a **legacy**. There is no ending and no game over: hulls are lost, captains retire, crew die or leave, and the **chronicle** keeps growing.

The central tension is one decision made over and over: send the people most likely to succeed, or the people who most need the experience.

### Settled decisions

| Area | Decision |
|---|---|
| Structure | Assignment, then objectives at a site, then one mission per away team |
| Stack | C# backend, Postgres (SQLite locally), Datastar front end |
| Resolver | Step-wise, resumable, seeded, serialisable |
| State | Current-state tables plus append-only event log |
| Qualities | Typed model behind a read-only quality view |
| Scale | Every crew member simulated identically, up to roughly 500 |
| Permanence | Permadeath, single autosave, no game over |
| Online | Isolated legacies per player; shared universe later |
| Time | Cycles of variable length, one week by default; crew and captains age and turn over |
| Lineage | One name; every new hull takes the next suffix |

## 2. Core loop

### 2.1 Three levels

- An **assignment** is what the ship has been sent to do. It lasts one to about four cycles, and there is usually only one at a time. Some are **mandates**.
- Each cycle of an assignment presents a **site** with one to four **objectives**: the things an away team could do there. Most sites offer one, so the usual cycle is one team on one mission. A site with several is the frequent exception, and is where the opportunity-cost choice lives.
- A **mission** is one away team sent on one objective.

The player often has no choice of assignment. The choice is in how to carry it out: which objectives to attempt, and with whom. An objective nobody is sent on still resolves, **unattended**, and usually badly. Sending two or three teams means attempting several objectives at once with thinner teams, which is the same dilemma as team selection: one strong team on one objective, or weaker teams on more of them.

What happens at one site can change the objectives offered at the next.

### 2.2 One cycle

One cycle has five strict phases. The **duty roster** sits outside them and can be edited at any time.

1. **Orders.** The inbox shows the current site and its objectives. Between assignments it shows the assignments on offer, or the mandate. Declining a mandate needs enough Admiralty standing, and costs some of it.
2. **Briefing.** The player chooses which objectives to attempt and builds one away team for each, up to three. For each team they name the lead, choose a loadout from requisition, and set **operational parameters**.
3. **Resolution.** Missions advance encounter by encounter on one interleaved timeline. **Calls** pause a mission until answered; **notices** arrive as they happen. Unattended objectives resolve at the end.
4. **Aftermath.** Reports, injuries, and growth land. Storylets fire. The player spends **captain's time**. Department heads resolve what falls below their threshold and file a **digest**.
5. **Advance.** Unattended storylines progress, time moves on by the cycle's length, offers expire, and the event log is committed.

Crew who are away are not cast in shipboard storylets that cycle.

Target length for a routine cycle is five to ten minutes. A site worked by three teams is allowed to take longer; that is the game's set piece.

### 2.3 Cycle length

A cycle is one decision point, not a fixed span. Most last one week. **Transit** between assignments is a single longer cycle of several weeks with no missions: training, healing, ageing, and storylines run for the whole span, and the inbox shows one combined digest. Transit has its own, different choices in place of missions, and a larger fixed budget of captain's time, so it is when a captain catches up with the crew. The player also sets the pace, within what the hull can do: a faster transit takes fewer weeks, and costs the crew: less training and recovery time, and lower morale. Hull wear is added when dry dock exists.

Everything time-based (ageing, healing, training, cooldowns, and expiries) is measured in weeks, never in cycles, so a long transit and a run of busy weeks are handled by the same rules.

## 3. Data model

Two kinds of data, kept strictly apart:

- **Content** (author-written, in git, YAML): templates, encounters, gear, traits, storylets, and grammars. Immutable at runtime.
- **State** (per legacy, in the database): crew, relationships, missions in flight, flags, and the event log.

Every content item carries `tags`. Tags are the glue that makes content combine (section 9).

### 3.1 Crew member (state)

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
insight: rough                # hidden | rough | clear; see 5.6
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
morale: 62                    # 0-100
loyalty: 55                   # 0-100; captains get a standing bonus
ambitions: [{ kind: reach_rank, target: RANK_4, byWeek: 600 }]
renown: 12
awayMissions: 9               # gates promotion
injuries: [{ kind: INJURY_A, healsAtWeek: 188 }]
portrait: { face: f03, hair: h11, hairTint: "#3a2b1f", uniform: u_dept_a, features: [sp_a_ridge] }
isCaptain: false
```

The four personality axes are systems vocabulary, not fiction. `caution` and `discipline` drive calls and delegation; the other two drive storylets. The author may rename or add axes.

### 3.2 Trait (content)

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

### 3.3 Relationship (state)

```yaml
from: crew_0042
to: crew_0017
kind: rival                   # friend | rival | romance | grudge | mentor
strength: 34                  # 0-100
sinceWeek: 161
lastEventId: evt_88213
```

Directed, and sparse: an edge exists only once two people have history. A grudge can be one-sided. With 500 crew this stays in the low thousands of edges, not 250,000.

### 3.4 Assignment and objective templates (content)

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
stages:
  - slot: approach
    draw: { tags: [travel, planetary], count: 1 }
  - slot: complication
    draw: { tags: [hazard], count: [1, 2] }
  - slot: objective
    draw: { tags: [science, objective], count: 1 }
  - slot: extraction
    draw: { tags: [travel], count: 1, skipIf: "mission.momentum >= 3" }
rewards: { shipRenown: 2, admiraltyStanding: 1 }
```

An objective template does not list encounters. It lists slots that draw from the encounter pool by tag, so every new encounter is automatically available to every template whose tags match.

### 3.5 Encounter (content)

```yaml
id: ENCOUNTER_07
tags: [hazard, planetary]
variants: [VARIANT_A, VARIANT_B, VARIANT_C, VARIANT_D]
approaches:
  - id: careful
    skill: SKILL_A
    difficulty: 5
    gearTags: [scanner]
  - id: fast
    skill: SKILL_C
    difficulty: 7
    onSuccess: { momentum: 1 }
decision:                     # optional
  stakes: 3
  prompt: DECISION_PROMPT_1
  options:
    - id: press_on
      leadPrefers: "lead.caution <= -1"
      effects: [{ strain: 1 }, { objectiveBonus: 1 }]
    - id: withdraw
      leadPrefers: "lead.caution >= 1"
      effects: [{ abortStage: true }]
outcomes:
  critical: [{ momentum: 2 }]
  success:  [{ momentum: 1 }]
  partial:  [{ strain: 1 }]
  failure:  [{ strain: 2 }, { injuryRoll: light }]
  disaster: [{ strain: 3 }, { injuryRoll: severe }, { chain: ENCOUNTER_12 }]
```

### 3.6 Gear (content)

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

### 3.7 Storylet (content)

```yaml
id: STORYLET_031
kind: captain                 # captain | vignette
stakes: 3
cost: 2                       # captain's time; vignettes cost 0
tags: [rivalry, department]
cast:
  a: "crew.status == 'active'"
  b: "rel(a, b).kind == 'rival' and rel(a, b).strength >= 50 and a.departmentId == b.departmentId"
requires: "not flag('STORYLET_031.done', a, b)"
cooldown: { perCast: 40, global: 5 }
text: STORYLET_031_TEXT       # grammar symbol in the content pack
options:
  - id: OPTION_A
    requires: "captain.empathy >= 1"
    effects: [{ relAdjust: [a, b, -20] }, { morale: [a, 5] }]
  - id: OPTION_B
    effects: [{ transfer: [b, otherDepartment] }, { morale: [b, -10] }]
ifIgnored:
  afterWeeks: 3
  effects: [{ relAdjust: [a, b, 15] }, { setFlag: 'rivalry.escalated' }]
  then: STORYLET_032
```

### 3.8 Quality

Not stored. A quality is anything the expression language can read: `a.skills.SKILL_A`, `rel(a, b).strength`, `ship.renown`, `chronicle.count('death', dept: a.departmentId, within: 20)`, or `flag('x')`. Only story flags are stored generically:

```yaml
legacyId: legacy_01
key: rivalry.escalated
scope: [crew_0042, crew_0017]   # empty = legacy-wide
value: 1
setAtWeek: 164
```

### 3.9 Event log entry and chronicle entry (state)

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

## 4. Mission resolution

### 4.1 Mission state

A mission in flight is a small serialisable record: the drawn encounter list, a cursor, the team's **momentum** and **strain**, objective progress, injuries so far, and the tuning and encounter definitions it started with. Dice are keyed by seed and encounter, so no stream position is stored; beats are returned by each step, not kept in the state.

### 4.2 The check

For each encounter:

1. **Pick the approach.** The team lead chooses from the approaches the team's gear allows. Operational parameters and the lead's personality weight the choice (a cautious lead under "low risk" picks `careful`). In the resolver the lead goes careful when caution minus risk tolerance is zero or more.
2. **Pick the actor.** The crew member with the best relevant skill acts, unless the parameters say "develop juniors". Then the lowest-skilled eligible member acts under the best member's supervision, but only when the check is within reach: an expected margin of +2 or better on an average roll, at the supervised score. Otherwise the best acts as usual.
3. **Compute the score.** `actor skill + assist + gear + traits + momentum − strain − injuries`. The assist is half the second-best skill, rounded down. Under "develop juniors" the senior carries the junior: the supervised check is made at the score the best-qualified member would have had, minus a penalty of 1, and minus 1 more for every four levels between senior and junior. Supervision therefore always costs something, on any team.
4. **Roll.** Score plus the check dice (2d6 today) against `difficulty + 7`. The margin gives a tier: critical (+5 or more), success (0 to +4), partial (−1 to −3), failure (−4 to −7), or disaster (−8 or less).
5. **Apply the tier's effects** and record a beat.

Two dice give a bell curve, so skill differences matter more than luck and the simulator's results are stable. The dice are expected to change: every roll goes through one seam (section 4.7).

### 4.3 Compounding

Momentum and strain are the "dungeon" memory. Both carry between encounters and both are capped at 5.

- Momentum can skip a stage (`skipIf`), and adds to checks.
- Strain subtracts from checks. A success or critical removes one strain.
- At strain 4 the abort decision is always a call, whatever the lead is like, unless comms are blocked. At strain 5 the mission collapses: one member takes a severe injury roll and the rest take light rolls.
- The collapse is checked after the encounter's decision, so a call at the strain cap can still pull the team out by withdrawing. This is the order the prototype played in. Checking first would cost weak teams under a lead who calls (an average lead with a rookie team: 21% lost becomes 28%); it is one condition in the resolver.
- The fast approach is authored at +1 difficulty over the careful one, and earns momentum on success.
- Disasters can chain a follow-on encounter that was not in the original draw.

A mission ends in one of five results: full success, partial success, incomplete, withdrawal, or loss. Incomplete means the team played every stage and achieved too little; withdrawal means only that someone chose to pull out. Withdrawing when the lead preferred the safe option does not count as overruling them. Death comes only from a failed severe injury roll: on a disaster, on a bold option that risks one, or in a collapse.

### 4.4 Decisions: call or notice

When an encounter has a decision:

```
askThreshold = 3 − (lead.caution / 2) − (lead.discipline / 4) + parameters.autonomy
if decision.stakes >= askThreshold  → call (mission waits for the captain)
else                                → notice (lead picks the option their personality prefers)
```

- The player names each team's lead, defaulting to the highest rank. Choosing the lead is choosing what you will be asked.
- `parameters.autonomy` runs from −1 ("check in often") to +1 ("use your judgement"). This is the player's lever on how many calls they get.
- A cautious, disciplined lead calls about most things. A reckless one mostly sends notices, some of which the captain would not have chosen. Lead and autonomy weigh about equally: on the prototype's bench a cautious lead makes 3.1, 2.5, or 1.2 calls per mission across the three autonomy settings, and a reckless one 0.8, 0.2, or 0.2.
- Some encounters set `commsBlocked`, forcing a notice whatever the lead is like. Who you sent matters most then.
- Overruling a lead's stated preference on a call costs a little of their loyalty; backing them raises it.

Expected volume: most missions produce zero to two calls. A site worked by three teams produces more in total, by design.

### 4.5 Growth and harm

- **Experience** goes to the actor of each check, and half to the assist. It is scaled by how close the check was: a check won or survived at a margin within ±3 earns the most; a trivial check earns almost none.
- **Mentoring**: a junior actor with an assist four or more levels above them earns a bonus.
- **Shipboard training** (from the duty roster) earns experience every week at roughly a fifth of the mission rate, and cannot raise a skill past a cap set by rank.
- **Promotion** requires a minimum `awayMissions` count per rank.
- **The captain** may join an away team, at a cost in captain's time that cycle. It is the usual way a captain dies in the line of duty.
- **Injury rolls** are light or severe. A severe roll, when failed, is permanent injury or death.

This makes the safest team the worst one for development, which is the point.

### 4.6 The resolver contract

The rest of the game knows only this:

```
Start(objective, encounter pool, team, loadout, parameters, tuning, seed) → MissionState
Advance(MissionState, reply?) → { state, beats[], pending: Decision | null, result: MissionResult | null }
```

`Advance` runs until it needs a captain's answer or the mission ends. A reply names the call it answers; a reply to another call, or when none is waiting, is refused. `MissionResult` carries outcome, injuries, experience by skill, and resource changes. The stored mission state carries a format version and is read strictly: an unknown or missing member is an error. Everything in and out is plain data. The resolver knows nothing about assignments, sites, or other teams; the cycle orchestrator runs several missions side by side and interleaves their beats.

A tactical grid resolver would implement the same two operations. `pending` would then be "the player's turn on the grid" rather than a call, and the consumer would not change.

### 4.7 Tuning

The numbers in this section were set on the throwaway prototype's bench (2,000 missions per briefing) and are starting values, to be tuned in play.

- Every number the resolver uses (tier margins, strain and momentum effects, the reach margin, assist values, call-rule weights, experience by margin, and injury targets) lives in one tuning file in the content pack, not in code.
- In developer mode the file reloads without a restart, and a tuning page edits it and re-runs the mission bench on the same seeds, showing before and after side by side.
- A running mission keeps the tuning it started with, so a legacy stays deterministic.
- Known and accepted: a reckless lead still loses about 13% of missions at about 3.7 deaths per 100 crew-missions. That is the price of sending them.
- Built in Phase 1 (slices 1a and 1b): the tuning file is `content/tuning/missions.yaml`, and `SimRunner bench --objective ID --compare FILE` runs the same seeds under the pack's tuning and under `FILE`. On the benchmark crew (10,000 missions per briefing) the C# resolver gives calls per mission of 3.1, 2.5, and 1.3 for a cautious lead across the three autonomy settings, 2.5, 1.3, and 0.6 for an average one, and 0.7, 0.2, and 0.2 for a reckless one. A reckless lead loses about 17% of missions at about 3.6 deaths per 100 crew-missions (the prototype gave 13% and 3.7: skips now belong to a stage, not an encounter, and the dice differ).
- **The dice seam** (slice 1c): the tuning file holds `check.dice` and `injury.dice`, each `{ count, sides }`, both 2 and 6 today. The resolver rolls only through one dice type, and the reach rule uses its average, so another number and size of dice is a file edit plus a bench. A different mechanic (a pool, advantage, or exploding dice) replaces that one type; the golden values are then re-pinned and the shape tests say whether the game kept its shape.
- Measured again after slice 1c on the bench's generated mixed team (10,000 missions per briefing): calls per mission are unchanged at 3.1, 2.5, and 1.3 for a cautious lead across the three autonomy settings, 2.5, 1.3, and 0.6 for an average one, and 0.7, 0.2, and 0.2 for a reckless one. A reckless lead of that team loses 15.7% of missions at 3.5 deaths per 100 crew-missions. That is a different team from the fixed benchmark crew behind the 17% above; slice 1c changed neither figure.
- "Develop juniors" under the carried rule, same team: full success 78.2% against 85.2% for best qualified. The junior's experience goes from 2.0 to 5.2 per mission and the senior's from 12.0 to 10.1. On a veteran team it costs about three points (95.4% against 98.3%); rookies never qualify for supervision, so it changes nothing for them.
- Whole cycles (slice 1c, `SimRunner cycles`, 20 legacies of 50 cycles): the simulator's captain always sends the strongest fit team and backs the lead. Of 1,000 missions, 882 were full successes, 110 partial, 5 incomplete, 1 withdrawn, and 2 lost, with no deaths and a highest skill level of 7. Over 10,000 missions that captain loses about 4 crew. That is far below the alarm band in section 10, as it should be for the safest policy; the simulator's other policies come with slice 1e.

## 5. Storylet system

### 5.1 Selection each cycle

1. **Index.** Storylets are indexed by the tags and quality kinds they test, so only plausible candidates are evaluated.
2. **Cast.** For each candidate, find crew bindings that satisfy the cast slots. Bindings are sampled, not enumerated, with a hard cap per storylet.
3. **Score salience.** `stakes × recency of the underlying change × how much the player has invested in the cast (time spent, missions sent on) − repetition penalty`.
4. **Route.**
   - Vignettes: the top few by salience go to the inbox (a per-cycle budget, starting at five). The rest are dropped or folded into the digest.
   - Captain storylets with stakes below the relevant department head's threshold: the head resolves them by personality, and the result goes in the digest.
   - Captain storylets above it: the inbox, with a cost and an expiry.

The head's threshold uses the same formula as the away-team call rule. One rule, two places.

### 5.2 Captain storylets and vignettes

| | Captain storylet | Vignette |
|---|---|---|
| Choice | Yes, two to four options | None |
| Cost | One to three captain's time | Free |
| Length | A short paragraph plus options | One to three sentences |
| Purpose | A ruling with lasting effects | A signal about who needs attention |
| Mechanical effect | Always | Always, but small (a relationship or morale nudge, or a flag) |

A vignette always changes something or reveals something true about the state. If it does neither, it is cut.

### 5.3 Storylines

A storyline is storylets chained by story flags. Every captain storylet that reaches the inbox has an `ifIgnored` clause: after its expiry, effects apply and the next storylet in the chain becomes eligible. Ignoring is a choice with an outcome, and not always the worst one.

### 5.4 Captain's time

- Budget per cycle: three to six, set by the captain's traits.
- Spent on inbox captain storylets and on **discretionary actions**: player-initiated storylets such as a ready-room talk with a chosen crew member, a promotion review, or a social evening. These are ordinary storylets whose cast the player picks.
- Does not carry over.
- Transit cycles get a larger fixed budget. It does not scale with the number of weeks.
- The inbox should usually hold more than the budget can cover. If the simulator shows the budget routinely covering everything, the economy is broken.

### 5.5 Delegation

Department heads fill the duty roster for their department, resolve low-stakes storylets, and nominate people for missions. Each does it in character: an ambitious head hoards the good assignments, and a low-empathy head lets morale problems run. The digest reports what they did, in one line each, and the player can open any line to see the detail or reverse a roster choice.

A department that looks quiet may be well run or badly reported. Appointing heads is therefore one of the weightiest captain storylets.

### 5.6 Insight and the org chart

The org chart is always visible: who reports to whom, by department and post. What the player knows about each person is not.

- Personality, morale, and relationship values are shown as bands in words, never as numbers.
- Each crew member has an insight level: hidden, rough, or clear. It controls how many bands are shown and how fine they are.
- The captain's direct reports are always clear.
- For everyone else, insight comes from the people responsible for them. Department heads and trainers give insight into skills and potential. Counsellors give insight into personality, morale, and relationships. The better they are at it, the clearer the picture and the sooner it arrives.
- Vignettes and missions raise insight for the crew they feature.
- Insight is never wrong at first, only vague: poor staff mean wide bands. Later, traits can distort it, both in the person observed (someone hiding something) and in the observer (a counsellor biased against `SPECIES_A` reads them badly).

Staffing the insight roles well is therefore how a captain of 500 sees the ship at all. A vignette is often the first sign of something the bands have not yet shown.

## 6. Narrative report generation

- **Input**: beats. A beat is `{ kind, actors, encounterId, variant, approach, tier, tags, context }`.
- **Engine**: a Tracery-style grammar, expanded with a seed derived from the legacy, mission, and beat index. The same input always gives the same text.
- **Selection**: for each beat the engine looks for the most specific matching symbol, falling back in order: encounter + variant + tier, then encounter + tier, then tags + tier, then tier alone. The author can start with a handful of generic lines and add specific ones where it matters.
- **Modifiers**: name, rank, pronoun, and species forms; personality-keyed word choices (`#actor.manner#`); relationship-aware inserts when two actors with a strong edge share a beat.
- **Assembly**: a report is a header (mission, teams, result), three to eight beat sentences, the decisions (calls and notices, marked differently), and a plain table of consequences.
- **Repetition control**: the engine keeps a short memory per legacy of recently used expansions and avoids them.
- **One engine, three uses**: mission reports, vignette text, and chronicle entries.
- **Lint**: a content check expands every symbol against generated beats and fails on missing symbols, unbound variables, and unreachable lines.

Register: reports read as a ship's log (clipped, factual, attributed), which sits comfortably beside tables. See risk 2.

## 7. Architecture

### 7.1 Modules

| Module | Responsibility | Depends on |
|---|---|---|
| `Sim.Core` | Domain types, event bus, seeded RNG, expression language, quality view | Nothing |
| `Sim.Content` | Load and validate content packs | Core |
| `Sim.Missions` | Assignment, site, and objective generation; the default resolver | Core |
| `Sim.Storylets` | Selection, casting, salience, and effects | Core |
| `Sim.Crew` | Growth, morale, relationships, careers, and roster | Core |
| `Sim.Chronicle` | Renown, succession, and chronicle projection | Core |
| `Sim.Narrative` | Grammar engine | Core |
| `Sim.Cycle` | Phase state machine; the only orchestrator | Core |
| `Persistence` | Database, event log, and legacy load and save | Core |
| `Web` | ASP.NET endpoints and Datastar views | All, through commands and queries |
| `SimRunner` | Headless balance simulator | All except Web and (optionally) Persistence |

Rules that keep agents from rewriting old code:

- Feature modules never reference each other. They react to events and read through the quality view.
- Everything under `Sim.*` is pure: no database, no clock, and no ambient randomness. State in, commands and events out.
- A new mechanic is a new module that subscribes to existing events and publishes new ones. Existing modules change only to react to the new events, if they need to.

### 7.2 Event bus

- In-process, synchronous, and ordered. One player action runs as one database transaction.
- **Commands** ask for a change (`AssignTeam`, `AnswerCall`, `ApplyEffect`). Exactly one handler each.
- **Events** state what happened (`EncounterResolved`, `CrewInjured`, `RelationshipChanged`, `CycleAdvanced`). Any number of subscribers. Every event is appended to the log.
- Events are versioned and additive. A field is never removed or repurposed; a changed meaning is a new event type.
- Handlers run in a declared order, so a replay with the same seed gives the same result.
- Storylet and encounter effects are data that compiles to commands. Adding an effect type means adding a command, not editing the storylet engine.

### 7.3 Determinism

Each random draw comes from a named stream: `hash(legacySeed, cycle, streamName)`. Adding a new mechanic that draws from its own stream does not disturb the draws of existing ones, so golden-seed tests survive new features.

### 7.4 Save format

- Postgres in production, SQLite allowed locally, behind one persistence interface.
- Every row is keyed by `legacyId`. This is the online isolation boundary, and the future seam for the shared universe.
- Current-state tables for crew, relationships, flags, missions in flight (resolver state as JSON), and the inbox.
- `event_log` (append-only, JSON payload) and `chronicle`.
- Each legacy pins the content pack version it was created under and records each upgrade.
- Single autosave: state commits after every action. There is no load.
- **Developer mode** (off for normal play): snapshot, restore, and export a legacy to a JSON file. Needed for bug reports and to feed real legacies into the simulator. Without it, ironman plus early bugs would destroy the author's own games.

Why not full event sourcing: rebuilding state from the log requires every old event to replay correctly forever. Over years of changing mechanics that is a standing tax. State tables plus the log give the chronicle, storylets that reference history, and debugging, without the tax.

### 7.5 Front end

Server-rendered HTML with Datastar. The server holds all state; the browser holds none. Resolution streams beats over server-sent events, and a call arrives as a fragment containing a form. Three screens carry the game: the inbox, the crew file, and the assignment screen. The duty roster, chronicle, and ship screen follow.

Built in slice 1c (`src/Web`, run with `./run.sh`):

- **The page is the state.** Every screen is drawn from the stored legacy, so a reload at any moment, even with a call waiting, shows where things stand. Nothing the game needs lives only in the browser.
- **One request, one command, one commit.** Each request locks the legacy, loads it, sends its commands, commits, and renders. A refusal is shown beside the control that caused it and stores nothing.
- **Datastar 1.0.5, self-hosted,** with the stream events written by hand; pages and stream fragments are the same server-rendered components. The game needs no network.
- **Briefing is one form,** sent whole at launch. Sending nobody just moves the phase on.
- **Beats are paced by the server.** A launch or an answer resolves the mission to its next call or its end and commits; the response then sends the log one beat longer each time, with a pause that is a setting. The roster, the meters, the call, and the outcome hold back until the log reaches them, so nothing gives the ending away. A dropped connection loses nothing.
- **The screens so far:** the list of legacies, orders, briefing, resolution, and aftermath, with the top bar (week, cycle, and the five phases) and the roster beside each. The inbox and the crew file come with slices 1d and 1e.
- **Beat lines are plain and structural** until the narrative report (slice 1d) replaces them: `CREW_02 checks SKILL_B at ENCOUNTER_01, helped by CREW_05: 6 + 3 against 15, failure (−6).`
- **A legacy played through the browser has exactly the simulator's event log** for the same seed and choices; a test holds the two together.
- **The look** is recorded in `docs/design/` (the mock-up, the tokens, and the component recipes): a blue-slate ground, one azure signal, a serif for headings over a technical grotesk, monospaced figures, and the mission log as one numbered trail that runs into the call. Screens are in `docs/screens/1c/`.

## 8. Engine recommendation

Stay with C# and Datastar.

| | C# + Datastar | Godot |
|---|---|---|
| Inbox, tables, and forms | Native strength | Laborious; UI toolkit is weaker than HTML |
| Online, accounts, and persistence | Native strength | Needs a backend anyway |
| Headless simulator | Same code as the game | Possible, with more friction |
| Agent-built iterations | Plain C# with fast tests | Scene files are harder for agents to edit safely |
| Paper-doll portraits | Layered images composited in code | Better tooling (cutout and shaders) |
| Tactical grid | Weak fit for Datastar | Strong fit |

Seams for later migration:

- `Sim.*` has no web or database dependencies. It can be hosted in Godot's .NET runtime as it stands, or ported module by module to Rust behind the same data contracts.
- The resolver contract is plain data in and out. A tactical grid can be a separate client (Godot web export, or canvas) that plays the grid and returns a `MissionResult`.
- Portraits are a spec (`{ face, hair, tints, … }`) rendered by a replaceable component.

## 9. Combinatorics

How content multiplies, using the example sizes of 10 objective templates, 20 encounters, and 4 variants:

| Layer | Mechanism | Factor |
|---|---|---|
| Mission skeletons | 10 objective templates × ordered draws of about 4 encounters from about 8 tag-eligible × 4 variants each | about 10 × 1,680 × 256 ≈ 4 million |
| Context | 4 contexts reskin text and shift difficulty | × 4 |
| Assignment | Each assignment draws sites, and each site offers 2 to 4 objectives from the pool | The same objective is a different decision depending on what it competes with |
| Team | Lead personality, actor skills, traits, and relationships inside the team | Effectively unbounded |
| Outcome paths | 5 tiers per encounter with momentum and strain carried forward | 5⁴ = 625 per skeleton |

That headline number flatters the design. Players do not perceive skeletons; they perceive situations. The useful measure is distinct beats:

`20 encounters × 4 variants × about 3 approaches × 5 tiers × about 3 lead dispositions (call, notice in line with the captain, notice against) ≈ 3,600 distinct beats from 80 authored encounter variants.`

Each addition's yield:

- One new assignment template: a new frame for every objective sharing its tags.
- One new objective template: offered under every matching assignment, beside different rivals each time.
- One new encounter: available to every objective template sharing its tags (typically three to five), in four variants.
- One new trait: modifies every check and storylet whose tags it names.
- One new gear item: opens an approach on every encounter carrying its gear tag.
- One new storylet: castable on every crew pair or group meeting its requirements. A rivalry storylet on a 40-crew hull with about 15 rival edges has 15 casts; with 500 crew, hundreds.
- One new grammar line at the tag level: appears across every encounter with that tag.

The combination space is not the constraint. Authored text is (risk 4).

## 10. Balance simulator

Built in Phase 0 and run on every change.

- **What it is**: `SimRunner`, a console program that plays legacies with no UI, using the same `Sim.*` code as the game.
- **Policies** stand in for the player. Start with four: always send the best, always develop juniors, random, and a mixed policy that sends the best on mandates and juniors otherwise. Policies also choose objectives, split teams, answer calls, and spend captain's time by simple rules.
- **A run**: N legacies × M cycles per policy, from fixed seeds. The default is 200 legacies × 500 cycles.
- **Metrics per run**:
  - Mission result distribution, by objective template, by encounter, and by context.
  - How often a site offers only one objective, and how unattended objectives turn out.
  - Deaths and severe injuries per 100 crew-missions.
  - Skill growth curves by policy.
  - Calls and notices per mission, and per cycle.
  - Captain's time demanded against budget.
  - Morale distribution, departures, and poaching rate.
  - Usage and win-rate lift of each gear item, trait, and approach.
  - Storylet fire counts, including storylets that never fire.
  - Hull losses and captain successions per 100 cycles.
- **Alarms** (a target band per metric, checked in CI):
  - Too easy: full-success rate above the band for any policy.
  - Too deadly: death rate above the band, or legacies collapsing to an empty roster.
  - Dominant: one policy beats every other on both renown and survival; or one gear item, trait, or approach is chosen in over 70% of eligible cases with a large lift.
  - Dead content: an encounter, option, or storylet with zero occurrences.
  - Drift: any metric moving more than a set percentage from the last accepted baseline.
- **The key balance assertion**: "always best" and "always develop" must each lose to the mixed policy over 500 cycles. If either pure policy wins, the central trade-off is gone.
- **Golden seeds**: a few fixed legacies whose full event logs are compared across builds, to catch unintended changes in behaviour.
- **Output**: a JSON file and a one-page HTML summary per run, with the baseline alongside.

### 10.1 Mission bench

A second simulator mode that answers one question: is this mission balanced? It runs one mission many times (10,000 by default) and never runs cycles.

- **Where**: the `SimRunner` command line, and a developer-mode page reachable from the legacy selection screen and from any objective inside a legacy. Both call the same code.
- **Inputs**:
  - The mission: an objective template (encounters redrawn each run), or one fixed drawn mission (same encounters each run). The first tests the template; the second tests a specific offer.
  - The team, from one of three sources: a real team from a legacy, generated teams at a named strength (rookie, mixed, or veteran), or a sweep across all three.
  - Loadout and operational parameters, or a sweep across them.
  - A policy to answer calls, and the run count and base seed.
- **Isolation**: a bench run works on an in-memory copy. It never writes to a legacy, its event log, or its chronicle.
- **Output**:
  - Result distribution (full success, partial, incomplete, withdrawal, or loss).
  - Deaths and severe injuries per run, and which encounter caused them.
  - Calls and notices per run.
  - Experience earned per team member.
  - Per-encounter tier distribution, and how often each approach was chosen.
  - Momentum and strain at each stage, to show where runs are won or lost.
  - The same alarms as the full simulator, applied to this one mission.
  - With a sweep: the results side by side, so the gap between rookie and veteran teams is visible.
- **Reproducing a run**: every run is identified by its seed. Any single run, such as the worst one, can be replayed to read its full report.
- **Speed**: 10,000 runs of one mission is a few seconds of pure computation, so the page can show results as they arrive.

## 11. Roadmap

Each phase ends with something playable, a simulator run, and a short list of what the author learnt by playing. Art and online are separate tracks that can start whenever convenient.

| Phase | Adds | Playable result |
|---|---|---|
| 0. Skeleton | Core, bus, event log, content loader and validator, RNG streams, `SimRunner` running an empty cycle | Nothing to play; a green simulator run |
| 1. Thinnest game | 8 crew, 1 skill set, 1 single-cycle assignment template, 2 objective templates, 1 team, 6 encounters, checks with tiers, momentum and strain, 1 decision with the call rule, text report; mission bench (command line, then developer page) with the tuning file and tuning page; morale; 5 vignettes and 2 captain storylets; captain's time; inbox and crew file; chronicle list | A full cycle of both layers in the browser |
| 2. Growth and loss | Experience, mentoring, injuries, death, memorials, several objectives at some sites, unattended objectives, mandates | The central trade-off exists |
| 3. Who you send | Personality-driven calls and notices, operational parameters, gear and requisition | Team choice changes what you are asked |
| 4. Life aboard | Relationships, storylines with `ifIgnored`, vignette budget, discretionary actions | Ignored problems get worse |
| 5. Roster | Departments, org chart, duty roster, shipboard training, department heads, digest, insight | Football Manager's weekday |
| 6. Careers | Ageing, ranks and promotion, ambitions, poaching, personal and ship renown, Admiralty standing | Rosters turn over |
| 7. Split teams and long assignments | Two to three missions at once on an interleaved timeline; assignments of several cycles with carried state; transit cycles | One strong team or two thin ones |
| 8. Succession | Hull loss and succession, captain succession and pool, chronicle screen | A legacy survives its first disaster |
| 9. Scale | Larger hulls up to roughly 500, salience tuning, casting performance | Management by exception |
| 10. Families | Relatives of crew (siblings, cousins, and proteges) can arrive at any time; children join the fleet after real ageing | Names recur in the chronicle |
| Track: online | Accounts, hosted Postgres, one legacy per player | Play from anywhere |
| Track: portraits | Paper-doll spec, compositor, first part set | Faces in crew files |
| Later | Dry dock, diplomacy, tactical grid, shared universe | |

Phase 1 uses the real architecture at the smallest size. Nothing in it is thrown away.

### 11.1 Progress

What has been built, slice by slice. A phase is cut into slices when it is planned, so later phases have one row until then. Every slice ends with `./check.sh` green and a review. Detail for Phase 1's slices is in `docs/plans/phase-1-outline.md`.

| Phase | Slice | What it adds | Status |
|---|---|---|---|
| 0. Skeleton | Whole phase | Core, event bus, event log, SQLite store, content loader, RNG streams, and an empty cycle in `SimRunner` | Done 2026-11-13 |
| 1. Thinnest game | 1a. Mission resolver | Checks with tiers, momentum and strain, decisions with the call rule, and the tuning file | Done 2026-11-14 |
| 1. Thinnest game | 1b. Mission bench | One mission run thousands of times from the command line, with before and after comparison | Done 2026-11-14 |
| 1. Thinnest game | 1c, part one. A whole cycle, headless | Crew that grow, are hurt, heal, and die; missions on the event bus; module state stored; the simulator plays real cycles | Done 2026-11-14 |
| 1. Thinnest game | 1c, part two. The cycle in the browser | Legacy selection, orders, briefing, resolution with paced beats and calls, aftermath, and the visual design | In progress |
| 1. Thinnest game | 1c-editor. Content editor | Developer-mode pages that write the content files, validated on save | Next |
| 1. Thinnest game | 1d. Reports | The grammar engine and the narrative report built from beats | Later |
| 1. Thinnest game | 1e. Life aboard | Storylets, morale, captain's time, the inbox and crew file, and the chronicle list | Later |
| 1. Thinnest game | 1f. Tuning in play | The mission bench and the tuning page in the browser | Later |
| 1. Thinnest game | Playtest | Three legacies of 20 to 30 cycles against the checklist below | Later |
| 2. Growth and loss | Not yet cut | Mentoring, memorials, several objectives at some sites, and mandates (experience, injuries, death, and unattended objectives arrived early, in 1c) | Not started |
| 3. Who you send | Not yet cut | Gear and requisition (personality-driven calls and operational parameters arrived early, in 1a) | Not started |
| 4. Life aboard | Not yet cut | Relationships, storylines, the vignette budget, and discretionary actions | Not started |
| 5. Roster | Not yet cut | Departments, org chart, duty roster, training, department heads, digest, and insight | Not started |
| 6. Careers | Not yet cut | Ageing, ranks and promotion, ambitions, poaching, renown, and Admiralty standing | Not started |
| 7. Split teams and long assignments | Not yet cut | Several missions at once, assignments of several cycles, and transit | Not started |
| 8. Succession | Not yet cut | Hull loss and succession, captain succession, and the chronicle screen | Not started |
| 9. Scale | Not yet cut | Larger hulls, salience tuning, and casting performance | Not started |
| 10. Families | Not yet cut | Relatives and children of crew | Not started |
| Track: online | Not yet cut | Accounts, hosted database, and one legacy per player | Not started |
| Track: portraits | Not yet cut | Paper-doll spec, compositor, and the first part set | Not started |

Before Phase 2 starts, the Phase 1 playtest should show all of these:

1. The central trade-off bites: the author has hesitated between sending the best crew and developing a junior.
2. A loss landed: someone died or a mission was lost, and it mattered.
3. Calls feel right: zero to two per mission, and who was sent changed what was asked.
4. The numbers have been tuned in play at least once, including the dice.
5. Both layers feed each other: a storylet changed who was sent, and a mission outcome reached the inbox.
6. Authoring is sustainable: an encounter or a storylet takes minutes to add.
7. The author wants to play one more cycle.

The throwaway mission prototype (risk 1) was built and played on 2026-11-13. The author's verdict: it does not feel passive. Its balance findings are in section 4.

## 12. Art

- **Portraits**: each part is a transparent PNG drawn on a shared canvas with fixed anchor points, in greyscale where it will be tinted. Layer order is fixed: back hair, body and uniform, head, species features, face, front hair, and rank marks. Tints are applied per layer in code. A portrait is fully described by its spec, so it is reproducible and cacheable.
- **Consistency rules**: one brush, one line weight, a fixed palette for skin, hair, and uniform tints, and one light direction. A contact sheet of random portraits is generated on every part added, to catch parts that clash.
- **Coverage**: a small set multiplies fast. Six faces, eight hairstyles, four species feature sets, and three uniforms give 576 silhouettes before tinting.
- **Interface**: dark only. The palette, type, and component recipes are in `docs/design/bridge-design.md`; new screens follow them.
- **Icons and ship screen**: flat, single-weight, and monochrome with one accent colour. The ship screen is a static cutaway with department status overlaid as data.
- **Ageing**: optional overlay layers (lines, greying tint) keyed to age, so veterans visibly age.

## 13. Pushback

Points where the brief was changed or should be watched:

1. **Calls per mission.** "Two to five per mission" across three teams would stall the loop. Now: calls scale with mission size, and the player has an autonomy lever.
2. **Ironman while building.** Single autosave and a game under construction do not mix. Developer mode exists from Phase 0.
3. **500 fully simulated crew.** Accepted, and cheap to compute. The cost moves to authoring and to salience; both are flagged as risks, and scale is deliberately the last phase.
4. **"Sometimes only one mission is possible."** Withdrawn after review. The author's point was that the choice is in how an assignment is carried out, which led to the assignment, site, and objective structure in section 2. What should be rare is a site with only one objective, and the simulator reports how often that happens.
5. **Vignettes "should be many".** Many are generated, few are shown. An inbox of thirty vignettes is noise.
6. **Online in one to three months.** Achievable because the server is authoritative from the start, but it should not be allowed to jump ahead of Phase 2. A hosted game without the central trade-off is not worth hosting.
7. **Datastar and a future tactical grid.** Datastar is the wrong tool for a grid. The plan is a separate client behind the resolver contract, not stretching hypermedia to do it.

## 14. Risks

| # | Risk | Mitigation |
|---|---|---|
| 1 | The mission layer feels passive | Throwaway prototype first. Calls are real choices with costs. The autonomy lever gives control over involvement. Resolution streams beat by beat, so the player watches a story unfold, not a result appear. Preparation choices (approach-opening gear, parameters) are visibly credited in the report. |
| 2 | Tone clash between spreadsheets and prose | Log-style register. Every prose line cites what changed (a small tag such as "morale −5"). Every number links to the beats that caused it. |
| 3 | Vignette fatigue and repetition | Salience budget, cooldowns, expansion memory, and the dead-content and over-fire counts in the simulator. |
| 4 | Authoring load: one writer, 500 crew | Fallback chain in the grammar so generic lines cover everything from day one. Tag-level lines. The simulator reports which lines are seen most, so writing effort goes where the player looks. |
| 5 | Balance drift as mechanics pile up | Simulator in CI with baselines and the pure-policy assertion. |
| 6 | Agents eroding module boundaries | The dependency rules in 7.1 enforced by an architecture test. Events additive only. Golden seeds. |
| 7 | Death feels arbitrary | Death needs a failed severe roll, which needs a failure or disaster, which strain and the abort rule give warning of. The report names the chain of causes. |
| 8 | Delegation hides the game from the player | Digest lines are one click from full detail. Salience promotes anything touching crew the player has invested in. |
| 9 | Expression language grows into a programming language | Keep it to boolean and arithmetic expressions over the quality view, with no loops or assignment. New capability comes from new quality functions, not new syntax. |
| 10 | Save compatibility over years | State tables migrate normally. Events are never replayed to load. Content version pinned per legacy. |

## 15. Decisions from review

Answered by the author on 2026-11-13.

1. **Cycle length.** One week by default, and variable: see 2.3.
2. **Declining a mandate.** Allowed only above an Admiralty standing threshold, and it costs standing. The cost grows on repeat.
3. **Team lead.** The player names the lead, defaulting to the highest rank.
4. **Visibility.** Bands in words, gated by insight and the org chart: see 5.6.
5. **A new hull after a loss.** The player picks a core of survivors to carry over. The rest are reassigned.
6. **Class upgrades.** Several routes, all rare: Admiralty offers the player may decline, and storylines.
7. **Families.** Relationships now. Later (Phase 10), time stays honest: children join only after real ageing, and other relatives (siblings, cousins, and proteges) can arrive at any time.
8. **Portraits.** Composited on the server into a cached image.
9. **Content format.** YAML with a schema.
10. **Simulator alarm bands.** Start at 1 to 3 deaths per 100 crew-missions and 40 to 60 cycles to mid-skill, then adjust from play.
11. **The captain on away missions.** Sometimes, at a cost in captain's time.
12. **The unit of a player's game** is a **legacy**.
13. **Transit.** One long cycle with a combined digest, its own choices, and a player-set pace bounded by the hull: see 2.3.
14. **Insight accuracy.** Only vague at first. Distortion comes later through traits of the observed and of the observer: see 5.6.
15. **Unattended objectives.** Set per objective by the author, defaulting to a roll against difficulty with no team.
16. **Captain's time in transit.** A larger fixed budget, not scaled by weeks.
17. **The cost of a faster transit.** The crew pays: less training and recovery time, and lower morale. Hull wear is added when dry dock exists.

## 16. Open questions

None at present. New ones are added here as they arise.
