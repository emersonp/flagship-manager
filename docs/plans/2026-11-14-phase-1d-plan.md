# Slice 1d: reports — implementation plan

**Goal:** the mission reads as prose. The live log streams one authored line per beat, each with generated tags and a disclosure that holds today's structural line. The aftermath shows an assembled report. The author writes lines, symbols, and pronoun sets in the content editor, previewed against sample beats.

**Architecture:** `Sim.Core` gains the situation a line is chosen for (facts and people) and the catalogue of facts per beat kind. `Sim.Missions` maps a beat to a situation. A new `Sim.Narrative` selects lines by salience and expands them, with no knowledge of missions. `Sim.Content` loads and edits three new kinds (lines, symbols, and pronouns) and the crew's `pronouns` field. `Web` renders prose, tags, and the report, and adds the editor forms and the preview.

**Spec:** `docs/plans/2026-11-14-phase-1d-design.md`. **Design system:** `docs/design/bridge-design.md`.

## Global constraints

The 1c-editor plan's constraints carry over: braces on every body, terse comments that say why, placeholders only (no invented fiction), plain interface words, tokens for every colour, 400 pixels wide with no page scroll, and test-first.

- `src/Sim.*` stays pure. `Sim.Narrative` references only `Sim.Core` (the architecture test picks it up by name). Narrative never draws from an RNG stream: its picks come from a stable hash of the legacy id, mission id, beat index, and what is being picked.
- Text is never stored. Golden hashes move once, in Task 1, for the check's two new members, and never again in this slice.
- `content/` gains placeholder lines that read like today's log. `tests/fixtures/content/` gains its own placeholder lines, symbols, and pronouns, and the tests read only the fixture.
- On Windows, `./check.sh` stops at its web smoke step (`python3` and `setsid`). Each task ends with `dotnet build` (no warnings), `dotnet test`, and the simulator and bench lines of the gate.

## Settled syntax (the design's open points)

- **Criteria** (`when`): `fact: value` for equality, where a set fact (`encounterTags`, `objectiveTags`) means "contains"; `fact: [a, b]` for any of; `fact: { min: 1, max: 3 }` for a whole-number range (either end may be left out). `beat` is required and is a single value. Specificity is the number of criteria, `beat` included.
- **Text**: `#symbol#`; `#role#` gives a person's or thing's display name (`actor`, `helper`, `lead`, `crew`, `encounter`, `skill`, `objective`, and `decision`); `#role.they#`, `.them`, `.their`, `.theirs`, and `.themself` give pronouns; `#role:checks|check#` gives the singular or plural form by the person's pronoun set, for a verb whose subject is that pronoun (`#actor.they# #actor:checks|check#`; after a name, write the singular); `.capitalize` may end any token; `##` is a literal `#`. Nesting is capped at 8.
- **Tags** are generated from the beat, never authored: a check gives its tier and margin, and its momentum and strain changes; an injury gives its outcome; a call or notice gives its stakes; effects give their changes.

## Tasks

### Task 1: the check states its effects

`CheckBeat` gains `int MomentumAfter` and `int StrainAfter`, the state after the tier's effects. Re-pin both golden hashes with a note on why. Tests: a success shows strain relief and momentum gain; a failure shows strain gain; the walkthroughs still hold.

### Task 2: situations and the narrative engine

- `Sim.Core/Narrative/Situation.cs`: `Situation(string Beat, IReadOnlyDictionary<string, FactValue> Facts, IReadOnlyDictionary<string, Person> People, IReadOnlyDictionary<string, string> Words)`, `Person(string Id, string Name, PronounSet Pronouns)`, and `FactValue` (text, number, flag, or set). `Sim.Core/Narrative/NarrativeFacts.cs`: the facts and people of each beat kind, as in the design's table.
- `Sim.Core/Content/NarrativeContent.cs`: `LineDef`, `CriterionDef`, `SymbolDef`, `PronounSet`, with `PronounSet.They` built in.
- `Sim.Narrative/LineSelector.cs`: `Select(lines, situation, use, used, seedKey)`; it returns the winning line or null. `Sim.Narrative/Expander.cs`: `Expand(text, situation, symbols, seedKey)`, which returns parts (name or text).
- Tests: specificity, any-of, ranges, set facts, flags, ties by seed that stay stable, a used line losing to an unused one, every token form, nesting and its cap, and an unknown token.

### Task 3: content and the beat mapping

- The loader reads `lines/`, `symbols/`, and `pronouns/` (all optional) and `pronouns` on crew. It refuses: a missing or unknown `beat`; a fact or person the beat kind lacks; an unknown encounter, skill, objective, decision, or tier value; a range on a fact that is not a number; a missing symbol; a symbol cycle; a token naming a role the beat kind lacks; an unknown pronoun set; and a duplicate id. Ids for symbols and pronoun sets are lower case (`hesitates`, `they`), unlike other ids.
- `ContentPack` gains `Lines`, `Symbols`, and `Pronouns` (optional members, so existing constructions compile).
- `Sim.Missions/BeatSituations.cs`: `Of(MissionBeat beat, MissionFrame frame)`, where the frame holds the objective, the team's crew definitions, the pack's pronoun sets, and names. Its test runs every beat of the fixture missions and checks that the facts and people produced are exactly the catalogue's for that kind.
- Placeholder lines for every beat kind, for both log and report use, in `content/` and in the fixture.

### Task 4: prose on the bridge

- `LogLine` gains `Prose` (parts) and `Tags`. `Text` and `Parts` stay the structural line, so existing tests keep their meaning.
- The mission log shows the prose, then the tags, then a disclosure with the structural line. A line that fell back to the systems line shows no disclosure.
- `AftermathView` gains `Report`: sentences (parts) and decisions. It picks report beats in order: injuries and deaths, calls, notices, and answers, critical and disaster checks, then failures and the result, filled to three with stage beats, and capped at eight.
- A refused call answer is shown in plain words.
- Tests: prose and tags stream; the disclosure holds the structural text; the report's bounds and order; the plain refusal; parity is unchanged.

### Task 5: the editor

- `ContentKind` gains `Line`, `Symbol`, and `Pronoun`, in every place that names kinds: the folders, the reader, the writer, the faithfulness sets, the edits, the editor's kinds, forms, and item page.
- The line form: id, use, beat, criteria rows (fact and value as text: `a`, `a, b`, `1..3`, `1..`, or `..3`), and the text in a text area. The symbol form: id and option rows. The pronoun form: the five forms and `plural`. The crew form gains a pronoun pick.
- The preview on the line form expands the line, as posted, against up to five matching sample beats, and says for each whether it wins or which line beats it. Samples: each objective template, run on four fixed seeds with the pack's starting crew (a team of the template's minimum size from the senior end, and one from the junior end), answering calls as the lead prefers.
- Tests: create, edit, and delete for each new kind; round trips and faithfulness; the preview; a saved line is live at once.

### Task 6: lint, docs, and the browser run

- The lint (on the lines list page): lines no sample beat selects, and beat kinds that fell back to the systems line.
- `tests/browser/edit.sh` gains "write a specific line and see it win" (outside the gate).
- Docs: GDD sections 6, 7.5, and 11.1; the outline; the map; the README; the glossary. Rebuild and republish the GDD page.
