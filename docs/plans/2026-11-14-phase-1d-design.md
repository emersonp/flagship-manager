# Phase 1, slice 1d: reports

**Spec:** `docs/gdd.md` section 6, section 5 (the three narrative structures), and risk 2; `docs/plans/phase-1-outline.md`, "Small things left in the browser part" and "Carried forward from the content editor". **Builds on:** `main` at 2026-11-14 (slice 1c-editor, then the GDD v1.9 notes).
**Ends with:** the author plays a cycle and reads the mission as prose: the live log streams one line per beat with small tags for what changed, any line opens to show its roll, and the aftermath shows the assembled report. The author writes a new line in the content editor, sees it previewed against sample beats, saves, and sees it win over the generic line in the running game with no restart.

## 1. Decisions

From the author (2026-11-14):

1. **Prose in both places.** The live log streams one prose line per beat, and the aftermath shows the assembled report: a header, three to eight chosen sentences, the calls and notices, and the consequences table.
2. **Tags and detail.** Each prose line ends with small tags for what changed (`strain +2`, `failure −6`). The full roll (score, dice, target, and margin, as in today's log) opens from the line on request.
3. **Lines are written in the editor.** 1d adds the grammar to `/dev/content`: criteria pickers, a text area with help for symbols, and a preview against sample beats. The systems ship placeholder lines only.
4. **Pronouns now.** Crew gain an optional pronoun set; species forms wait for crew generation.

Made here, each cheap to reverse:

5. **Salience selection, not a fallback chain** (GDD section 6, after the author's note 20). Every line carries criteria. Of the lines whose criteria all hold for a beat, the one with the most criteria wins, and ties break by seed. A specific line never needs a change to a generic one.
6. **Text is regenerated on view, never stored.** A log line or report is a pure function of the beats (already in the event log as `MissionBeat`), the current pack, and a seed derived from the legacy seed, mission id, and beat index. Grammar edits therefore show at once in old missions too. Only chronicle text (slice 1e) is frozen.
7. **Narrative draws from no simulation stream.** Its seed is derived, not drawn, so the event log, golden hashes, and the browser–simulator parity test are unaffected by any grammar change.
8. **`Sim.Narrative` knows nothing of missions.** It takes a situation: a flat set of facts (key to value) and named people (id, name, pronoun set, and their stats). `Sim.Core` holds the situation type; `Sim.Missions` maps a beat to a situation; `Sim.Narrative` selects and expands lines. Vignettes (1e) and the chronicle reuse the engine by building situations of their own. The architecture test enforces the rule that feature modules never reference each other.
9. **A built-in systems line is the last fallback.** If no authored line matches, the engine shows today's structural line for that beat, so a pack with no grammar still plays and nothing ever renders blank. The lint reports every beat kind that fell back to it.
10. **Criteria are plain matches, not the expression language** (which arrives in 1e). A criterion is a fact compared with a value, any of a list, or a range. In 1e the expression language becomes one more criterion kind; it replaces nothing.
11. **The check states its own effects.** `CheckBeat` gains `momentumAfter` and `strainAfter`, so its tags need no copy of the resolver's rules ("a check's own effects have no beat", from the browser part's small things). The members are additive with no default for new events; the golden hashes are re-pinned once, on purpose.
12. **Repetition is avoided within one log or report.** A line already used in this mission loses to an equally specific one not yet used. Memory across missions waits until per-legacy narrative state is needed (1e).

## 2. Content

Two new kinds and one new field. Ids are the keys, and names are optional, as everywhere.

```yaml
# lines/base.yaml
- id: LINE_CHECK_FAILURE_HAZARD
  use: log                         # log | report
  when:
    beat: check
    tier: [failure, disaster]      # any of
    encounterTags: hazard          # the set contains this
    actor.caution: { min: 1 }      # a range
  text: "#actor# #hesitates#, and #actor.their# #skill# check at #encounter# goes wrong."

# symbols/base.yaml
- id: hesitates
  options: [LINE_PLACEHOLDER_A, LINE_PLACEHOLDER_B]   # one is picked by seed; options may use #symbols#

# crew/starting.yaml (new optional field; "they" when absent)
- id: CREW_01
  pronouns: she

# pronouns/base.yaml (English pronoun sets are grammar, not fiction, so the defaults ship)
- id: they
  subject: they
  object: them
  possessive: their
  possessivePronoun: theirs
  reflexive: themself
  plural: true                     # verb agreement
```

**Facts a line can test**, by beat kind (all also have `beat`, `use`, `objective`, `objectiveTags`, `encounter`, `encounterTags`, `stage`, `momentum`, `strain`, and `commsBlocked`):

| Beat | Facts | People |
|---|---|---|
| stage, skip, chain | slot | lead |
| check | approach, skill, tier, margin, supervised, helped, mentoring | actor, helper, lead |
| injury | severity, outcome | crew, lead |
| lead change | | crew (the new lead) |
| call, notice, answer | decision, stakes, chose or answer, standing (backed or overruled), blocked | lead |
| collapse, result | outcome | lead |

Each person also exposes `id`, `caution`, `discipline`, `seniority`, and `isLead` (for example `actor.caution`).

**Text**: `#symbol#` expands a symbol; `#actor#`, `#encounter#`, `#skill#`, `#objective#`, and `#decision#` give display names; `#actor.they#`, `.them`, `.their`, `.theirs`, and `.themself` give pronouns, with a two-form verb choice keyed to the set's `plural` (exact syntax in the plan); `.capitalize` applies to any expansion. Names stay name parts, so they are encoded as plain text everywhere, as today.

**The loader refuses**: a criterion on a fact the beat kind does not have; a value that cannot occur (an unknown encounter, skill, or tier); a missing symbol or person; a symbol cycle; a duplicate id; an unknown pronoun set. **The lint reports** (not refuses): a line no sample beat ever selects (shadowed or unreachable), and beat kinds that fell back to the systems line.

**The abort call gets its words here**: the reserved `DECISION_ABORT` is phrased by lines with `decision: DECISION_ABORT`, which closes "the abort call needs a name source".

## 3. Screens

- **Live log** (resolution): each beat is one prose line, then its tags in the mono figure style (tier and margin, momentum and strain changes, injury outcome). A native disclosure on the line shows the structural line, which is today's text. Calls and notices keep their distinct marks. Pacing, streaming, and hold-back are unchanged.
- **Aftermath report**: the header (objective, result, team, and lead); three to eight sentences from `use: report` lines, chosen in order from the beats that mattered (injuries and deaths, calls and notices, critical and disaster checks, then failures and the result, filled to three with stage openers); the calls and notices listed with their answers; and the existing consequences table beneath.
- **Plainer refusals**: a refused call answer says what happened in words ("That call has already been answered."), not the bus's message with resolver ids.
- **Editor, Lines**: the list shows each line's beat, the number of criteria, use, and the start of its text. The form has the id, use, criteria rows (fact picked from the beat kind's facts, then a comparison and a value picked from what exists), and the text in a text area. The preview shows the line expanded against each sample beat it matches (up to five), and for each one whether it wins or which line beats it.
- **Editor, Symbols and Pronouns**: a list and a small form each. The crew form gains the pronoun pick.
- **Sample beats** come from a fixed set of bench missions on the current pack (fixed seeds, sweep teams), so the preview and the lint see the same beats every time.

## 4. Tests

- **Selection:** the most specific matching line wins; ties break by seed and stay stable across runs; a used line loses to an unused equal one; the systems line is the fallback; every criterion kind (value, list, and range) on stats and on facts.
- **Expansion:** symbols, nesting, names as name parts, pronouns and verb agreement for singular and plural sets, `.capitalize`, and a depth cap.
- **Loader and lint:** each refusal above in the loader's words; the lint's unreachable and fallback reports on the fixture pack.
- **Purity:** the event log of a fixture legacy is byte-identical with and without a grammar; changing a line changes text and nothing else; the golden hashes move once for the check's new members and never again in this slice.
- **Report assembly:** sentence count bounds; the order follows the beats; injuries, deaths, and calls always make the report.
- **Web:** the log streams prose with tags, and the disclosure holds the structural text; the aftermath report; a refused answer in plain words; the editor's create, edit, delete, and preview for lines, symbols, and pronouns; a saved line is live at once.
- **In a browser** (scripted, outside the gate): `tests/browser/edit.sh` gains "write a specific line, play until its encounter, and see it win".
- The tests use the placeholder pack in `tests/fixtures/content/`, which gains its own placeholder lines, symbols, and pronouns.

## 5. Not in this slice

- Encounter variants and assignment contexts as criteria. Variants change the resolver's draw and move golden hashes; they come with the content that needs them.
- Relationship-aware inserts (relationships are Phase 4) and species forms (crew generation).
- Repetition memory across missions, and chronicle text (1e).
- The expression language as a criterion (1e).
- `SimRunner report --seed`, which prints a mission's report headless for the bench's "replay the worst run". It is small, and goes in this slice only if the plan finds room.

## 6. Settled in the plan

The syntax for verb agreement and for criteria, and generated tags: see `2026-11-14-phase-1d-plan.md`, "Settled syntax".
