# Conventions

How things are named in the content files, the editor, and the text an author writes. The loader enforces the id rules with one shared check, and the editor's hints quote this page. Decided by the author on 2026-11-16.

## Ids

Every id starts with a letter, holds only letters, digits, and underscores, and is at most 40 characters. There are two cases, decided by what kind of thing the id names.

| Case | Used for | Examples |
|---|---|---|
| Upper case: `A` to `Z`, digits, `_` | Anything with its own page in the editor: skills, crew, gear, assignments, objectives, encounters, decisions, lines, storylets | `SKILL_A`, `ENCOUNTER_01`, `STORYLET_01` |
| Lower case: `a` to `z`, digits, `_` | Anything that lives inside an item or is typed into text: approaches, a storylet's options, traits (flags until slice 1e, part three), a trait's level names, symbols, pronoun sets, tags | `careful`, `captain_ask`, `sensors_quirk`, `scanner` |

The split is deliberate: an upper-case id in a file or a log is a reference to another item; a lower-case one belongs to the item it sits in, or is a word of the text.

**One exception: traits.** A trait defined in `content/traits/` will have its own editor page (slice 1e, part six), but every trait id stays lower case: traits are typed into text and requirements far more often than they are opened as pages, and `subject.trait.SHAKEN` reads badly. Decided by the author on 2026-11-17 (GDD section 15, item 40).

## Reserved characters

These never appear in an id, because each means something where ids are read.

| Character | Meaning | Example |
|---|---|---|
| `.` | "of": a property, then a modifier | `actor.caution`, `#actor.they.capitalize#` |
| `-` | Minus, in an expression | `subject.flag.shaken - 1` |
| `#` | Marks a token in text | `#lead#` |
| `:` and `\|` | A verb's two forms inside a token | `#actor:checks\|check#` |

## Traits (flags until slice 1e, part three)

The rules below were written for flags and hold for traits under their new name: `{ trait: shaken, add: 1, on: actor }`, `subject.trait.shaken`. The last rule, counter or stage, is replaced in part three by named levels: a trait whose definition names its levels can only be set, to a named level or 0; one without can be added to and set (GDD section 15, item 40).

- A flag's id is lower case, like every other id inside an item: `shaken`, `sensors_quirk`.
- Where the content already says it is a flag, the id stands alone: `{ flag: shaken, add: 1, on: actor }`, `requires: [{ flag: shaken, min: 1 }]`.
- Anywhere a flag is read beside other properties (text, criteria, and the expression language when it arrives), it is read through `flag`: `subject.flag.shaken`, `ship.flag.sensors_quirk`, `#subject.flag.shaken#`. So a flag can never be mistaken for a built-in property, however many properties there come to be, and no name is forbidden to a flag. Skills and grades are read the same way: `actor.skills.SKILL_A`.
- Related flags share a prefix: `shaken` and `shaken_heard`.
- Every flag is a number. A flag is either a **counter** (only ever added to: "how many times") or a **stage** (only ever set: "where this stands"), never both. If a story needs both, it uses two flags.

## YAML keys

Keys are camel case, as they are today: `gearTag`, `commsBlocked`, `teamSize`, `perCycle`.

## Display names

A name is 1 to 80 characters of ordinary text. The id stays the key in saves, events, and references; screens show the name and fall back to the id.

## Files

Content lives in `content/<kind>/<file>.yaml`, the kind in the plural and lower case: `encounters`, `storylets`, `lines`. The game's own pack is `content/`; the tests' placeholder pack is `tests/fixtures/content/`.
