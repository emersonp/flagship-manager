# Phase 1, slice 1d review: authored approaches, "initiative", and the briefing's crew card

**Spec:** `docs/gdd.md` sections 4.2 to 4.4 and 6. **Builds on:** `main` at 2026-11-15 (`c45bda9`).
**Ends with:** the author opens an encounter, defines one to four approaches of their own, writes each approach's outcomes and lines on its own tab, and the lead picks among them by skill. "Notice" is gone from the game's vocabulary. The briefing's away team card never scrolls inside itself.

This records the author's review of slice 1d on 2026-11-15. The author approved this design the same day. Items marked **(my call)** were proposed, not asked for, and were approved with the rest.

## 1. Why

The author reviewed the encounter prototype and found its structure sound and its form wrong. Careful and fast are the engine's ideas, not the author's: an encounter should offer the ways through that suit the situation, named and tuned by the author. "Notice" does not say what happens. A card that scrolls inside itself is unpleasant to use.

## 2. Goals, non-goals, anti-goals

**Goals**

1. An encounter's approaches are authored: one to four, usually two, each with its own name.
2. An approach can be unavailable to a team that does not meet its thresholds.
3. The lead chooses among the available approaches by what they are best at.
4. Every outcome of every approach has a defined effect, with today's numbers as the default.
5. The encounter form reads top to bottom: definitions, then a tab for each approach, then the generic lines.
6. "Notice" is renamed everywhere.
7. The briefing's away team card shows the whole roster without scrolling inside the card.

**Non-goals**

- Choosing by personality trait. Crew have only caution and discipline today; the author expects trait comparison once there are more. Nothing is built for it now.
- A new job for risk tolerance. It is removed, and returns with a fuller skill list.
- Author-defined answers to a decision. "Bold" and "safe" stay as they are.
- Converting saved games.

**Anti-goals**

- A shared vocabulary of approaches that makes every encounter feel the same.
- A change to the default numbers. A team that meets only default outcomes must play exactly as it does today.

## 3. Approaches considered

**A. Each encounter owns its approaches (chosen).** The encounter holds an ordered list; every approach is written for that situation. More to fill in per encounter, and nothing is shared. It matches the form the author described: definitions at the top of the encounter, a tab of lines for each.

**B. A shared library of approaches.** A new content kind defines approaches once and encounters pick from it. Less typing and more consistency, but an approach cannot belong to its situation, and it adds a page. Not built now. The author wants a shared library later, beside an encounter's own approaches, and will design it once more is known; it is on the GDD's roadmap as unplaced work, with the overhaul of skills, attributes, and gear.

## 4. Design

### 4.1 Content

An encounter's `approaches` becomes an ordered list of one to four:

```yaml
approaches:
  - id: chart                  # unique within the encounter; the key in saved beats and in lines
    name: Chart the drift      # shown in the editor, the log's roll detail, and the bench
    skill: SKILL_A
    difficulty: 5
    gearTag: scanner           # optional, as today
    requires:                  # optional; every entry must hold for the approach to be available
      - { skill: SKILL_A, level: 4 }   # someone standing on the team has this level, after injury
      - { gear: scanner }              # the loadout carries gear with this tag
    outcomes:                  # optional; a tier written here replaces that tier's default whole
      success: [{ progress: 1 }, { strain: -1 }, { momentum: 1 }]
    text:                      # attempt and the five tiers, as today
      attempt: "..."
```

- An effect is the decision's `EffectDef` (momentum, strain, progress, injury roll). An injury roll in an outcome falls on the actor.
- An approach may have any number of requirements. The form edits three, and does nothing special for more: the author will not write a fourth until the form supports it (the author, 2026-11-15).
- The loader refuses an encounter in which every approach has a `requires`, so some approach is always available **(my call)**.
- The game's pack and the tests' fixture are converted. Each encounter's `careful` and `fast` become its first and second approach, keeping their ids as `careful` and `fast` and gaining names. Each converted `fast` gets an explicit success and critical outcome that adds the momentum fast earned by rule, so the converted content plays as before.

### 4.2 Rules

- **Availability.** An approach is available when all of its `requires` hold for the team as it stands at that encounter.
- **Choice.** Among the available approaches the lead takes the one whose skill is highest *for the lead*, after injury **(my call: the lead's own skill, not the team's best, so naming the lead changes how the team works)**. A tie goes to the approach listed first. The best-qualified member still makes the check, and "develop juniors" is unchanged.
- **Outcomes.** A tier's effects are the approach's if it wrote that tier, and the default otherwise. The defaults are today's numbers, read from `tuning/missions.yaml`: critical and success add 1 progress and remove 1 strain, critical adds 1 momentum, partial adds 1 strain, failure adds 2 strain and a light injury roll, disaster adds 3 strain and a severe injury roll. `momentum.fastSuccess` leaves the tuning file.
- **Chains** stay the encounter's: a disaster chains the follow-on whatever the approach's disaster outcome says.
- **Risk tolerance** leaves `Parameters`, the briefing, the launch command's checks, the command line, the automatic captain, and the bench.
- **Beats** record the approach's id as a string. The `Approach` enum goes.
- **Saved games.** A legacy saved before this change is not converted. The store's version rises and an old save starts fresh **(my call)**.

### 4.3 "Notice" becomes "initiative" (my call on the word)

A decision becomes a call, or the lead acts on their own initiative. Renamed in the glossary (with "notice" under *Avoid*), the GDD, the beat kind and its record, `MissionResult`, the content key under a decision's `text`, the lines' `beat` fact, the bridge's log and report, the editor's labels, the bench, and the tests.

### 4.4 Narrative

- The `approach` fact holds the approach's id. A shared line that tests `approach: careful` or `approach: fast` is rewritten or removed; an encounter's own lines may test its own approach ids.
- The log's roll detail names the approach by its name in place of "Fast approach."

### 4.5 The encounter form

```
Tags            [field] [hazard]
Comms blocked   [ ]
Chains to       [Encounter chain v]

Approaches                                              [Add an approach]
  Name               Skill        Difficulty  Gear tag   Available when
  [Chart the drift]  [Survey v]   [5]         [scanner]  [Survey 4+] [scanner] [+]   [Remove]
  [Run it blind   ]  [Fieldcraft] [6]         [None   ]  always                      [Remove]

Decision  [x] This encounter has a decision
  ...as today...

[ Chart the drift ] [ Run it blind ]                  <- one tab for each approach
  Attempt    [ text box, preview beside it                     ]
  Critical   [ text box ]   Effects: +1 progress, -1 strain, +1 momentum (default)  [Change]
  Success    [ text box ]   Effects: ...
  Partial    ...
  Failure    ...
  Disaster   ...

What plays here                                       <- everything that is not an approach's
  Arrive, the decision's texts, and the encounter's own lines, as today
```

- Definitions are a table at the top: one row for each approach, at most four. "Add an approach" disappears at four and "Remove" at one.
- Each approach's tab holds its attempt and its five outcomes. An outcome's effects sit beside its words **(my call: the author asked for definitions at the top and lines in tabs; five outcomes for four approaches would bury the top, and an outcome's rule belongs next to its sentence)**. An outcome left alone shows the default greyed, as a blank text box shows its fallback.
- Every tab's fields are in the page whichever tab is showing, so one save sends them all. A tab whose fields have problems is marked on its label.
- "What plays here" loses the approach rows and keeps the rest.

### 4.6 The briefing's away team card

The crew table sits in a wrapper that scrolls sideways when the table is wider than the card. The card must show every row and column without a scrollbar of its own at any width the page supports. The first step of the work is to reproduce the scroll in a browser at the author's window size, since the cause was read from the stylesheet and not seen. Removing the wrapper's scroll is not enough if the page then scrolls sideways: the table has to fit, by tightening columns at wide widths and by restacking each crew member's skills under their name at narrow ones.

### 4.7 The bench

"Careful share" becomes the share of checks taken by each approach position (first listed, second, and so on), and the share of encounters where an approach was unavailable. The risk axis leaves the bench's grid. The figures are expected to move because the lead now chooses by skill; the report records the new baseline.

## 5. Order of work

1. The rename and the briefing card. Independent of the rest and small.
2. Content shape, loader, writer, rules, beats, narrative facts, bench, and the converted packs. The game's own checks pass before the form is touched.
3. The encounter form.
4. The GDD, glossary, outline, and README.

## 6. Left to the builder

- How an approach's id is made in the form (typed, or made from the name once and then fixed, as a decision's id is).
- Whether approaches can be reordered in the form, or only added at the end.
- The tabs' mechanics in Datastar, and their keyboard behaviour.
- How "Available when" entries are edited within the row machinery the form already has.
- Whether a negative strain in an effect needs a change to the effect checks.
