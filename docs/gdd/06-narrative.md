# 6. Narrative report generation

A chapter of the [Starship Sim GDD](../gdd.md). Section numbers are permanent ids; cross-references such as "section 4.3" find their chapter through the index.

- **Input**: beats. A beat is `{ kind, actors, encounterId, variant, approach, tier, tags, context }`. The `approach` is the id of one of the encounter's own approaches, a string, so a shared line cannot test for "careful" or "fast" as such; an encounter's own lines may test its own approach ids. The log's roll detail names the approach by its name.
- **Engine**: a Tracery-style grammar, expanded with a seed derived from the legacy, mission, and beat index. The same input always gives the same text.
- **Selection by salience**: every line carries criteria over its beat and the qualities of the people in it: encounter, variant, approach, tier, tags, and context, and later personality, traits, and relationships. Of the lines whose criteria all hold, the one with the most criteria wins; ties break by the seed and the repetition memory. This is the rule-matching approach of Elan Ruskin's dialogue system for Left 4 Dead (section 5). The fixed fallback chain first proposed here (encounter + variant + tier, then encounter + tier, then tags + tier, then tier alone) is a special case of it. The author can start with a handful of generic lines and add specific ones where they matter, and a specific line never needs a change to the generic ones.
- **Modifiers**: name, rank, pronoun, and species forms; personality-keyed word choices (`#actor.manner#`); relationship-aware inserts when two actors with a strong edge share a beat.
- **Assembly**: a report is a header (mission, teams, result), three to eight beat sentences, the decisions (calls and initiatives, marked differently), and a plain table of consequences.
- **Repetition control**: the engine keeps a short memory per legacy of recently used expansions and avoids them.
- **One engine, three uses**: mission reports, vignette text, and chronicle entries.
- **Lint**: a content check expands every symbol against generated beats and fails on missing symbols, unbound variables, and unreachable lines.

Register: reports read as a ship's log (clipped, factual, attributed), which sits comfortably beside tables. See risk 2.

Built in slice 1d (`Sim.Narrative`; design and plan in `docs/plans/2026-11-14-phase-1d-*.md`):

- **Situations.** Each beat becomes a situation: a flat set of facts (the encounter and its tags, the stage number, momentum, strain, and the beat's own facts, such as a check's tier and margin) and the people in it by role (`actor`, `helper`, `lead`, `crew`), with their stats as facts (`actor.caution`). `Sim.Core` lists the facts of every beat kind; the loader, the editor, and the mapping in `Sim.Missions` all read that one catalogue. `Sim.Narrative` knows nothing of missions, so vignettes and the chronicle can build situations of their own.
- **Lines** have a beat, criteria, and text. A criterion is a value, any of several values, or a range (`actor.caution: { min: 1 }`). The most specific line whose criteria all hold wins; a line already used in the mission loses to an equally specific fresh one; ties break by a stable hash of the legacy seed, the mission, and the beat.
- **Where lines live** (revised after slice 1d, on the author's note that an encounter's narrative belongs in the encounter). There is one pool and one rule of selection, but two places to write. A line about one encounter or one objective is written inside that item: as `text` for its own beats (an encounter's arrival, each approach by tier, a decision's call, initiative, and the effects of each choice; an objective's result by outcome), or as an entry in its `lines` for any other beat, with criteria of its own. The item is an implied criterion, so its own words outrank a shared line that tests the same things. The shared lines (`content/lines/`) hold only what belongs to no one item (injuries, the lead, the captain's answers, words keyed to personality) and the general fallbacks.
- **Scripts** (slice 1d authoring, `docs/plans/2026-11-15-phase-1d-authoring-design.md`). A line's text is one sentence or a script, one entry to a line: `lead: words` is speech, `> words` a system note, anything else narration, and a last line `= sentence` is what the report says. A speaker is one of the beat's people, `captain`, or `best SKILL_ID`: whoever still standing is best at that skill, the lead aside. An entry whose speaker is not there is left out and the rest plays. A text of `-` alone says nothing. An approach's `attempt` text is said before its check's result is shown. A call's script is shown once, as the log's newest beat under the waiting call, and the decision's `labels` name the call's answer buttons.
- **The log and the report.** There is no separate report line. A one-sentence line is its own report sentence, so it is written in the past tense, as a ship's log is. A script gives the report its `=` sentence; one that gives none leaves the report to the shared line for that beat.
- **Text**: `#symbol#`, names by role (`#actor#`, `#encounter#`), pronouns (`#actor.they#`, `.them`, `.their`, `.theirs`, `.themself`), a verb after a pronoun (`#actor.they# #actor:checks|check#`), `.capitalize`, and `##` for a `#`. **Symbols** (`content/symbols/`) give one of their options; **pronoun sets** (`content/pronouns/`) ship as she, he, and they, and a crew member names one (they by default).
- **Never stored, never random.** Text is regenerated on every view from the beats and the current pack, so a grammar edit shows at once, even in old missions. It draws from no RNG stream: the event log is identical with or without lines.
- **Fallback.** A beat no authored line holds for shows the structural systems line, so a pack with no lines still plays. The loader refuses criteria and text that can never hold for their beat kind.
- **Tags.** Every log line ends with generated figures for what its beat changed (a check's tier and margin, and its momentum and strain changes, from two members the check beat gained in this slice). Lines never author them, so every line cites what changed (risk 2).
- **The report** in the aftermath: three to eight of the log's sentences, in order, chosen from the beats that mattered (injuries and deaths, calls, initiatives, answers, the collapse, and the result always; then failures and lead changes; then arrivals at encounters, the stage beats, to make three), and each call and initiative with its answer.
- **Not yet**: encounter variants and assignment contexts as criteria, relationship-aware inserts, species forms, repetition memory across missions, and the expression language as a criterion (slice 1e).

## 6.1 Writing lines

A guide for the author. The rules above say what the engine does; this says how to use it.

**What a line is.** A mission is a string of moments: the team reaches a place, someone attempts something, someone gets hurt, the lead calls in, the mission ends. Each moment is a beat. A line is a sentence written for one kind of beat, with a note on when it fits. Every time the game shows a beat, it looks through the lines, finds the ones that fit, and picks the best.

A line is shown in two places, and written once. **The live log**, which the captain watches as the mission plays, shows a line for every beat. **The aftermath report**, the short account afterwards, has one sentence for each beat that mattered. A one-sentence line gives both the same sentence, so write it in the past tense, as a ship's log does: "CREW_02 was lightly injured at ENCOUNTER_03." A line can also be a script, several lines of speech and notes, which says in its last line what the report should say (see "Scripts" below).

**Where to write a line.** Ask what the line is about.

- **About one encounter:** write it in that encounter. Its form reads from the top: the definitions (tags, the approaches as a table, the decision), then a tab for each approach, then **What plays here**. An approach's tab holds its words before the check and for each tier, and beside each tier's words that tier's effects, shown in words as the default until the author changes them. **What plays here** lists every other beat the encounter can produce in the order a mission meets them: the team arriving, effects, injuries, a change of lead, its decision (the lead calling it in, the lead deciding alone, each choice taking effect, and the call's button labels), and the rarer ones. A box you leave blank shows, in grey, the line that plays there now; write over it to give the encounter its own words. A beat with no box shows what plays there and a **Write one for here** button, which starts a line of the encounter's own from that sentence, to rewrite and to narrow with criteria.
- **About one objective:** write it in that objective, whose panel has a box for each way a mission can end and the same lines of its own for every other beat.
- **About neither:** write it on the Lines page. These are the shared lines: what any encounter falls back on, and what belongs to none of them, such as an injury, the lead taking over, the captain's answer, or a cautious person failing anywhere.

So "CREW_02 was injured" is a shared line, "CREW_02 was gravely injured" is a shared line with the criterion `severity: severe`, and the death that can only happen at ENCOUNTER_07 is written in ENCOUNTER_07. Nothing about an encounter has to be searched for anywhere else.

```yaml
# encounters/base.yaml
- id: ENCOUNTER_07
  tags: [field, hazard]
  text:
    arrive: "#lead# brought the team to #encounter#."
  approaches:
    - id: APPROACH_A
      name: APPROACH_A_NAME
      skill: SKILL_A
      difficulty: 5
      text:
        success: "#actor# took #encounter# slowly, and it held."
        disaster: "#actor# took #encounter# slowly, and it gave way under #actor.them#."
    - { id: APPROACH_B, name: APPROACH_B_NAME, skill: SKILL_C, difficulty: 6 }
  decision:
    id: DECISION_PROMPT_7
    stakes: 3
    bold: [{ progress: 1 }, { injuryRoll: severe }]
    safe: []
    text:
      call: "#lead# called in from #encounter#: #decision#."
      bold: "The team pressed on through #encounter#."
  lines:
    - when: { beat: injury, outcome: dead }
      text: "#crew# did not come back from #encounter#."

# lines/base.yaml: the shared lines
- id: LINE_INJURY_LIGHT
  when: { beat: injury, outcome: lightInjury }
  text: "#crew# was lightly injured at #encounter#."
- id: LINE_INJURY_GRAVE
  when: { beat: injury, severity: severe, outcome: [incapacitated, dead] }
  text: "#crew# was gravely injured at #encounter#."
```

**How the game picks a line.** Take a check where CREW_03 attempts a technical skill at a hazardous encounter and fails badly. The game asks two questions. First, which lines are allowed? A line qualifies only if it is for this kind of beat and every one of its criteria is true right now; one false criterion and it is out, and a line with no criteria fits every beat of its kind. Second, of those, which is most specific? The line with the most criteria wins. So with three check lines, one with no criteria, one for failures, and one for failures at hazards, a failure at a hazard gets the third, a failure anywhere else the second, and a success the first. The general lines never change to make room: the specific one simply wins wherever it applies.

Words written inside an encounter or an objective are chosen the same way, and where they sit counts as criteria. A box for an approach failing counts as three (this encounter, that approach, failure); a line of the encounter's own counts the encounter as one, plus whatever it adds. That is why an encounter's own words beat the shared ones without anyone arranging it. A shared line can still win by being more specific: one that tests four things beats the encounter's box for that check.

Two tie-breakers sit underneath. **Freshness:** between equally specific lines, one not yet used in this mission wins, so five checks do not read the same five times. **A fixed coin toss:** if it is still a tie, the game picks, but always the same way for the same beat, so a reload shows the same words while another mission may get the other line. If no line fits, the log shows the structural line, numbers and all, so nothing is ever blank; the report leaves that beat out.

**Writing criteria.** A criterion is a fact and a value, one row each, on the line form and in an item's lines of its own alike. A line written inside an item shows the item as its first criterion, greyed out and fixed: it always holds there, and it counts. Fill the blank row to add a criterion; clear a row to drop it. A value is written one of three ways:

- One value: `failure`.
- Any of several: `failure, disaster`, true when the fact is either.
- A range, for numbers: `1..3`, `1..` (one or more), or `..0` (zero or less).

Some facts are lists, such as an encounter's tags, and for those the criterion asks whether the list holds the value: `encounterTags` with `hazard` means the encounter is tagged hazard, and `hazard, social` means it has either. Yes-or-no facts take `true` or `false`.

**The facts to test.** Every beat has the objective and its tags (`objective`, `objectiveTags`), the encounter in play and its tags (`encounter`, `encounterTags`), the `stage` number (how many encounters the team has reached, this one included: it counts encounters, not the objective's stages), the team's `momentum` and `strain`, `commsBlocked`, `teamStanding` (how many of the team can still act; on an injury beat, counted after the injury), and the `lead`. Each kind of beat adds its own:

| Beat | When it happens | Its own facts | People |
|---|---|---|---|
| stage | The team arrives at the next encounter | `slot`, the label of the stage that drew it, such as approach or extraction (`chain` for a follow-on encounter) | lead |
| attempt | The moment before a check's result is known | `approach`, `skill`, `helped`, `supervised`, `mentoring` | actor, helper (when there is one), lead |
| check | Someone attempts something | `approach` (the approach's id), `skill`, `tier` (critical, success, partial, failure, or disaster), `margin`, `helped`, `supervised`, `mentoring` | actor, helper (when there is one), lead |
| injury | Someone rolls against injury | `severity` (light or severe), `outcome` (unharmed, lightInjury, incapacitated, or dead) | crew, lead |
| call | The lead asks the captain | `decision`, `stakes` (1 to 5), `prefers` (bold, safe, or withdraw) | lead |
| initiative | The lead decided alone | `decision`, `stakes`, `chose`, `blocked` (comms were down) | lead |
| answer | The captain replies | `decision`, `answer`, `standing` (backed, overruled, or neutral) | lead |
| effects | A decision takes effect | `decision`, `chose` (bold, safe, or withdraw), `progress`, `momentumChange`, `strainChange`, `injuryRoll`, `withdraw` | lead |
| lead | Someone takes over as lead | | crew (the new lead), lead |
| skip, chain, collapse | An encounter is skipped on momentum; a disaster leads to a follow-on encounter; the team collapses | | lead |
| result | The mission ends | `outcome` (fullSuccess, partialSuccess, incomplete, withdrawal, or loss) | lead |

Every person in a beat also has stats to test: `id`, `caution`, `discipline`, `seniority`, and `isLead`, written as `actor.caution` or `lead.discipline`. That is how lines reflect personality: `actor.caution` with `1..` means the person acting leans cautious, so the line can have them hesitate, while a reckless actor gets a line where they charge in. The form suggests exactly the chosen beat's facts, and the game refuses a fact the beat does not have.

**Writing the text.** The text is an ordinary sentence with placeholders between `#` marks.

- **Names.** `#actor#`, `#helper#`, `#lead#`, and `#crew#` give a person's name; `#encounter#`, `#objective#`, `#skill#`, and `#decision#` give the display name of that thing. Only names the beat has are allowed, so a stage line cannot mention `#actor#`, because no one is acting.
- **Pronouns.** `#actor.they#`, `#actor.them#`, `#actor.their#`, `#actor.theirs#`, and `#actor.themself#` give that person's pronouns from their set: for someone set to she, `#actor.their# kit` becomes "her kit".
- **A verb after a pronoun.** "She checks", but "they check". Write `#actor.they# #actor:checks|check#`, singular first, and the game picks. Use it only straight after a pronoun; after a name, write the verb as usual (`#actor# checks`), because a name is always singular.
- **Capitals.** `.capitalize` on any placeholder raises its first letter, for a pronoun opening a sentence: `#actor.they.capitalize# steadies the line.`
- **A literal #** is written `##`.

**Scripts: more than one sentence.** A text can be a short script, one entry to a line:

```yaml
text:
  call: |-
    lead: Captain, we're here, and it is worse than the survey said.
    best SKILL_MEDICINE: I'm not detecting any life signs.
    captain: #captain_ask#
    lead: We go in, or we wait for the storm to pass. Your call.
    > Signal strength falling.
    = #lead# called in from #encounter#.
```

- **Speech** starts with who is speaking and a colon. The speaker is one of the beat's people (`lead`, and for a check `actor` and `helper`, for an injury `crew`), `captain`, or `best` and a skill id, which is whoever still standing is best at that skill, not counting the lead. If nobody fits (no helper, nobody left with the skill), that entry is left out and the rest of the script plays.
- **A system note** starts with `>`: the ship's instruments talking.
- **Narration** is any other line.
- **The report's sentence** is a last line starting with `=`. Without one, the report uses the shared line for that beat.
- **Saying nothing:** a text of `-` alone lets the beat pass in silence, where a blank box would fall back to a shared line.

Where scripts show: an approach's **Before the check** box is said before the result, so "I'm going to try to climb it" comes first and the outcome after. The text of a decision's call is shown once, as the log's newest beat, just under the call and its answers; the decision's button labels replace Bold, Safe, and Withdraw. The log reads newest first, and the newest beat's entries arrive one after another.

The cliff, as three small pieces in one encounter: an approach's Before the check box is `actor: Captain, there's a sheer face here. I'm going to try to climb it.`; its Disaster box is `> Carrier signal lost.`; and a line of the encounter's own for the injury beat, with `outcome: dead`, is `> No life signs detected.` A second line for the attempt beat with `teamStanding: 1` gives the last survivor different words.

**Symbols: variety without more lines.** A symbol is a named list of options. A symbol `hesitates` with three options drops one of them wherever a line says `#hesitates#`. One check line with two or three symbols reads differently from beat to beat without a dozen near-identical lines. Options may hold placeholders and other symbols. A symbol's id is lower case, because it is typed in text, and its choice follows the same fixed coin toss: varied across beats, steady for any one beat.

**The numbers take care of themselves.** No line needs figures. Every log line ends with small tags for what changed (the check's tier and margin, a momentum or strain change, an injury outcome), and the full roll is one click away under "Roll". Sentences can stay purely narrative.

**What the report uses.** In mission order: always injuries that hurt someone, deaths, calls, initiatives, the captain's answers, a collapse, critical and disastrous checks, and the result; then failed checks and changes of lead while there is room, up to eight sentences; then arrivals at encounters (stage beats) to make at least three. For each it uses the line's report sentence: the sentence itself for a one-sentence line, the `=` line for a script. Plain successes and partials never reach the report.

**A sensible way to work.**

1. Keep the shared lines few. One general line for each beat kind is enough to keep the structural text out of the log, and the placeholder lines do this already; replace their words with yours.
2. Write each encounter in the encounter, in **What plays here**. Fill in the boxes that matter (the arrival, the tiers worth telling apart, the call) and leave the rest blank: a blank box falls back to the line shown in grey. The lists of encounters and objectives say how many boxes each has written.
3. Add a shared line only for something that is true across encounters: a grave injury, a death, a cautious lead calling in over something small.
4. Read the previews. In an encounter or an objective, every box and line shows what the log would say, a moment after you stop typing and before anything is saved, and says which line that is. The samples are built for the item from the starting crew; **Next sample** turns to other people, which catches a wrong pronoun. A line with criteria is shown against a sample made to satisfy them, and one that can never hold at this item says so. The chips above each group insert the speakers, names, and symbols that beat allows. On the Lines page, **Preview** runs a shared line against the sample missions and says beat by beat whether it wins or which line beats it.
5. Check the **lint** on the Lines page now and then. It lists shared lines that never won in the samples, beats that met no line at all, and which of those the report wanted. The samples are a small fixed set of missions, so a rare situation (a supervised check, a skip on momentum) may never come up, and a line flagged as never chosen may simply be waiting for its moment.

**Worth knowing.**

- Edits are live. A saved line shows on the next page, even in missions already played, because text is rebuilt from the beats on every view.
- Lines never change what happens. They describe beats; they cannot touch a roll, an injury, or a decision.
- The game refuses a line that can never work: a fact the beat lacks, a value that cannot occur (an unknown encounter, a misspelt tier), a name the beat does not have, or a symbol that loops back on itself. The reason is shown and nothing is written.
- The abort call, when strain runs high and the lead asks whether to pull out, is a call with `decision` set to `DECISION_ABORT`. A shared call line with that criterion gives it its own words.
