# Phase 1, slice 1e, part six: writing storylets in the editor

**Spec:** `docs/gdd.md` sections 3.7, 3.8, 5.1, 6, and 7.5, and section 15, items 22, 23, 40, 41, and 45. **Builds on:** `main` at 2026-11-20 (`d22f765`): slice 1e's first five parts and the calendar.
**Status:** designed 2026-11-20; being built (see "As built", section 8).
**Ends with:** the author opens the editor, taps **New storylet**, and writes one from start to finish in the form: a name, a cast slot built from rows ("subject · trait grieving · is at least · 1"), a script with chips for the speakers, three options with their words and effects. As they type, "What plays here" shows the storylet as it would read on **USS Quality Assurance**, cast for real, with "Next sample" rotating whoever fits. They save; the loader accepts it; in Dev Play on a legacy where someone is grieving, it arrives. Nothing was typed into a YAML file.

This records what the author decided on 2026-11-20, in a design interview, and designs it. Items marked **(my call)** were proposed by me, not asked for by the author.

## 1. What the author decided (2026-11-20)

- **Scope: storylets, vignettes, and traits,** built in that order, in one plan of three stages. The trait forms also finish the encounter form's leftover job: writing a new trait condition on an approach.
- **Notes, not comments.** The writer keeps only a file's leading comment and drops every inner comment; the faithfulness check compares YAML trees and cannot see comments, so saving from a form would silently drop the design reasoning the storylet and trait files carry (26 and 9 comment lines). Storylets, vignettes, traits, and encounters gain a `notes:` field, plain text, shown in the form as "Author's notes" and never in play. The existing inner comments move into it once, by hand. The leading comment stays.
- **The form follows the encounter form:** definitions, a tab for each option, then "What plays here".
- **Storylets and vignettes stay two kinds,** two files and two lists, sharing one form part (slice 1d authoring, decision 10: "one reusable part, for every later storylet kind").
- **The text view stays.** Each of the three files still opens as text, with part four's expression errors at line and column. An item the form cannot keep sends the author there.
- **Expressions are a hybrid:** a builder of rows for the common shapes, the language as text for the rest. Writing storylets is first class: the author wants it as easy as playing.
- **Test ships.** The previews need state with traits, history, a chronicle, and time behind it, so the time has come for the "USS Quality Assurance": a ship described in content, against which the editor casts for real.
- **The tablet waits for hosting.** The author means to write storylets on a tablet, but reaching the editor from one, touch-first forms, and drafts kept in the browser all wait until the game is served at a URL. The roadmap's "Track: online" row is replaced and widened to carry them (section 6). This part only avoids working against that: nothing needs hover, and the builder offers selects and steppers wherever a value comes from a known set.
- **The worked example** is a new storylet that picks up `grieving` ("does nothing on its own yet; it marks them for a later storylet"), written entirely in the browser.

Accepted as recommended: the trait form's "Where it's used" panel, a lint on the storylets and vignettes lists, the lists' columns, "Save as test ship" in Dev Play, and how new items and options get their ids.

## 2. Goals, non-goals, anti-goals

**Goals**
- Every storylet, vignette, and trait in the pack can be opened, edited, and saved in a form without loss.
- A new storylet can be written, previewed, and saved without the keyboard except for its words.
- The previews show the real casting on a real-looking ship, and say why when no one fits.
- The design reasoning in the files survives the first save from a form.

**Non-goals**
- A form for test ships (they are written as text, or captured from play).
- Moving the encounter and line previews onto test ships (section 7, open question 6).
- Reaching the editor from another device, a login, touch-first redesign, and drafts in the browser (the online track).
- A clause builder for `||`, arithmetic, or grouped `!`.
- Redesigning the encounter, objective, and line forms.

**Anti-goals**
- A second stored form of an expression. The YAML holds the string the loader reads; the builder is a view of it.
- A preview that claims a storylet can arrive when the real casting would refuse it.
- A save that drops anything the author wrote, comments included.

## 3. Approaches considered

**Expressions.**
- **A. Text boxes with a live check and chips.** Exact and cheap; on a tablet, and for an author who does not want to remember `history.count('lost_mission', 2)`, a wall.
- **B. A clause builder only.** Easy for common shapes, but `||`, `.toward(...)` and `count(...)` turn it into a second language.
- **C. Hybrid (chosen).** Rows when the expression is clauses joined by `&&` in shapes the builder knows; text otherwise. Every expression in today's pack fits in rows.

**What the previews cast against.**
- **A. The starting crew with slots filled at will,** ignoring the conditions. Shows how words read; says nothing about whether anyone fits.
- **B. A sample built to satisfy the conditions.** A solver, and it cannot handle `||` or ranges in general.
- **C. A ship described in content (chosen).** It implements `IStateSource`, so the real casting runs on it and `Casting.Failed` says which clause held for no one. "Preview anyway" keeps A's use when nothing fits.

**Comments in the files.**
- **A. A writer that keeps comments.** Fragile: a comment's anchor moves when items are reordered or edited.
- **B. Accept the loss; git keeps the history.** The reasoning would leave the file it explains.
- **C. A `notes:` field (chosen).** Moves with its item, is edited in the form, and reads in the file as the comment did.

## 4. Design

### 4.1 Notes

`notes:` is an optional string on a storylet, a vignette, a trait, and an encounter (the encounter file has two inner comments and gets the field for consistency). The writer writes it as a block scalar after `name`. The loader reads and ignores it; nothing in play shows it. The form shows it as a multi-line box, "Author's notes", at the foot of the definitions.

The migration is one task: each inner comment becomes its item's `notes`, worded as it stands. A comment that sits on an option or an effect (for example "Everyone's loyalty dips but the subject's") goes into its item's notes, prefixed with the option's id. A comment that explains the file as a whole joins the leading comment. After the migration the faithfulness check gains one rule **(my call)**: a list file with an inner comment is lossy, so the editor refuses to rewrite it and says why, instead of dropping the comment. That closes the hole for every kind, not only these.

### 4.2 The editor's new kinds

Four kinds join the editor's index: **Storylets** (`storylets`), **Vignettes** (`vignettes`), **Traits** (`traits`), and **Test ships** (`testships`, text only). The storylets, vignettes, and traits files stop appearing as text files on the index; each list page has "Edit as text", which opens the file in today's text view.

A file the form cannot keep (an unknown key, an inner comment, a value the models cannot hold) still lists its items, marked as today, and their forms say so and link to the text view.

**List columns:**

| Kind | Columns |
|---|---|
| Storylets | Stakes, Cast (slot names), Requires (shortened), Options (count), Cooldown, Casts on (test ships it can be cast on, of how many) |
| Vignettes | Cast, Choice (Yes or No), Casts on |
| Traits | On, Levels or max, Shown, Used by (count) |
| Test ships | Date, Crew, Traits held |

**New items.** "New storylet" suggests the next free id in the pack's pattern (`STORYLET_09`); the author may change it before the first save. A new option's id is made from its label as it is typed ("Hold a service" becomes `hold_service`), can be edited, and locks once saved, because saves, events, and the chronicle refer to option ids. A trait's id follows the same rule from its name, lower case (`docs/conventions.md`).

### 4.3 The storylet form

Top to bottom:

1. **Definitions.** Id, name, stakes (a stepper, 1 to 5), `requires` (the expression widget, 4.5), the cast (up to three slots, each a name and an expression widget; add and remove), cooldown (cast and global, steppers, blank for none), notes.
2. **A tab for each option,** in order, with add, remove, and move. Each holds:
   - id and label;
   - `requires` (the expression widget, with the storylet's slots in scope);
   - **Settled by:** "Its effects" or "A check". Effects are rows (4.4). A check has a skill (a select of the pack's skills), a difficulty (a stepper), and two panels, Success and Failure, each with effect rows;
   - its words: the option's text, or the success and failure texts, as boxes with chips;
   - **Chronicle:** a weight select (blank for the tuning file's, 0 for none, 1 to 5) and an optional text, on the option or on each outcome where the loader allows it.
3. **What plays here.** The storylet's opening script; then, for each option in order, its text, or its success and failure texts; then the chronicle line each would write (the option's own text, or the shared `chronicle_storylet` line, greyed). Each row is a box with a live preview, as on an encounter: a blank box shows what plays there now, greyed. A text sits both in its tab and here, as an approach's does (slice 1d approaches).

**Chips** insert, at the caret: the script speakers the storylet allows (`captain:`, each slot as `subject:`, `best SKILL_ID:` for each skill), the tokens (`#subject#` and the other slots, `#handler#` inside a check's outcomes only), and the pack's symbols. The script syntax is slice 1d's: `speaker: words`, `> note`, narration, and a last line `= sentence`.

### 4.4 Effect rows

One row is one effect, with selects for its parts:

| What | Amount | On | Toward |
|---|---|---|---|
| Morale, Loyalty | a stepper, signed | a slot, `crew`, or `handler` (in a check's outcome) | |
| Trait (a select of defined traits) | Add a stepper, or Set to a level (by name where the trait has names, or a number) | a slot, `crew`, `handler`, or the ship, as the trait's `on` allows | a slot, for a pair trait |

Rows are added and removed freely; there is no fixed count. The rows write exactly the flow maps the loader reads today (`{ trait: avoids, add: 2, on: subject, toward: target }`). A vignette with no options has the same rows on the vignette itself.

### 4.5 The expression widget

The YAML stores the expression string, as now. The widget is a view of it.

- **Rows** when the expression is clauses joined at the top level by `&&`, each in one of the ten shapes below. Each row is a sentence of selects and steppers; the row's text is shown under it, small, so the author learns the language by reading it.
- **Text** otherwise, with a one-line reason ("uses `||`; edit as text"), checked as typed, with the loader's error placed at its character, and chips for paths: the slots, traits with their level names, history kinds, chronicle kinds, and storylet ids.
- **A switch, Rows / Text,** always shown. Going to Text shows the string the rows make. Going back to Rows works whenever the text parses into rows, and otherwise says why.

The shapes (*who* is a slot, or the ship where it applies):

1. *who* is aboard, is fit, or is not (`subject.aboard`, `!subject.fit`)
2. *who*'s trait *T* is, is at least, or is at most a *level*, by name where the trait has names (`subject.trait.shaken >= 1`, `ship.trait.sensors_quirk == 'let_ride'`)
3. *who*'s pair trait *T* toward *another slot*, compared with *n* (`subject.trait.avoids.toward(target) >= 3`)
4. *who*'s morale or loyalty, compared with *n*
5. *who*'s skill *S*, compared with *n*
6. *who* had *kind* last cycle (`subject.history.last('hurt')`)
7. *who* had *kind* in the last *m* cycles, compared with *n* (`subject.history.count('lost_mission', 2) > 0`)
8. chronicle entries of *kind* in the last *d* days, for the ship or for *who*, compared with *n* (`chronicle.count('death', 120) >= 2`)
9. storylet *X* arrived at least or at most *n* cycles or days ago (`storylet.STORYLET_01.daysSince >= 40`)
10. trait *T* on *who* changed at least or at most *n* cycles or days ago (`subject.trait.cyclesSince('grieving') >= 2`)

A range on one value (`>= 3` and `<= 4`) is two rows; the builder does not merge them **(my call)**, so a row always round-trips to the same text.

Parsing into rows is done in `Sim.Expressions` from the same rewrite the loader uses, so "fits in rows" and "is valid" never disagree. Rows are rendered by the server, as every form is, and posted as fields; the server writes the string.

### 4.6 Test ships

A new content kind, dev-only: `content/testships/base.yaml`, a list. Play never loads it; the loader checks it against the pack when developer mode reads the pack. The game's pack ships one, **USS Quality Assurance**; the test fixture pack ships its own.

```yaml
- id: uss_quality_assurance
  name: USS Quality Assurance
  notes: Every storylet and vignette in the pack can be cast here.
  date: 40              # the day number; the calendar shows it as a date
  cycle: 9
  crew:
    - { id: CREW_01, morale: 30, loyalty: 65 }
    - { id: CREW_03, fit: false }
    - { id: CREW_05, aboard: false }
  traits:
    - { trait: sensors_quirk, value: let_ride, daysAgo: 45, cyclesAgo: 7 }
    - { trait: shaken, on: CREW_02, value: 2, daysAgo: 6, cyclesAgo: 1 }
    - { trait: avoids, on: CREW_04, toward: CREW_06, value: 3, daysAgo: 12, cyclesAgo: 2 }
  history:
    CREW_02: { lost_mission: [1], hurt: [1] }
    CREW_04: { lost_crewmate: [3, 8] }
  chronicle:
    - { kind: death, daysAgo: 20, subjects: [CREW_07] }
    - { kind: death, daysAgo: 90, subjects: [CREW_08] }
  storylets:
    - { id: STORYLET_01, daysAgo: 45, cyclesAgo: 7 }
```

- **Crew** are starting-crew ids with changes (morale, loyalty, a skill level, fit, aboard); anyone not listed is as the starting crew file has them. A crew member may be listed as dead (`alive: false`), for chronicle entries about them.
- **History** lists, for each kind, the cycles ago it happened (1 is last cycle).
- **Timing** is written as days and cycles ago, so a ship never goes stale as the calendar changes.
- The ship is an `IStateSource`, built by a new class in `Sim.Storylets` (beside `StateSource` and `DryRun.Rewound`), so the editor reads it through exactly the interface the module reads the game through.
- The loader checks crew ids, trait ids, level names, `on` and `toward` against the trait's kind, history kinds, chronicle kinds, and storylet ids.

**A test** casts every storylet and vignette in the game's pack against the game's test ships and fails if any cannot be cast on at least one. The lint (4.9) says the same thing in the editor. USS Quality Assurance is written so that all of today's eight storylets and six vignettes cast on it **(my call: one ship to start; a second only when two storylets need contradictory states)**.

**Save as test ship.** Dev Play gains a button that writes the legacy's current state as a new entry in `content/testships/base.yaml`, named "<legacy> on <date>", through the editor's usual save (whole pack built, loader run, then one file written). Times are written as days and cycles ago from the legacy's today. Captured ships are edited as text.

### 4.7 Previews on a test ship

The storylet and vignette forms have a **ship select** above "What plays here" (USS Quality Assurance by default; the choice is kept in the URL, so it survives a reload). On every change, the form as posted is lowered as the loader would lower it and:

1. **The top line** says whether `requires` holds on this ship, and whether a cooldown would hold the storylet back, from the ship's storylet arrivals: "Requires holds · cast cooldown clear" or "Requires fails: `chronicle.count('death', 120) >= 2` is 1".
2. **Casting** runs `Casting.Cast` on the ship with a fixed stream for the sample number. If it casts, every preview renders with that cast. **Next sample** moves to the next cast that fits (and, for a check, rotates the handler among those fit to send).
3. **If no one fits,** each slot says why, from `Casting.Failed`: "subject: no one aboard has `trait.grieving >= 1`" or "out of tries". **Preview anyway** fills the slots from the ship's crew in order, ignoring the conditions, so the words can still be read; the previews say they are unconditioned.
4. **An option** whose `requires` fails for the cast is marked "Not offered with this cast" on its tab and in What plays here.

Previews are rendered by `Sim.Narrative` as play renders them, and draw no randomness from any legacy. Typing posts the form to the existing previews route for the kind, which patches only the previews and the sample control, as encounters do.

### 4.8 The vignette form

The storylet form's part, with four differences: no stakes; options are optional (a vignette with none has effect rows on itself); a vignette with options has a `default` select of its options ("If left: Let them rest"); and What plays here has no chronicle rows, since vignettes write none.

### 4.9 The trait form

Every key the loader accepts:

- id, name, **On** (ship, crew, or pair), **Shown** (a switch);
- **Levels**: named levels as rows (number and name), or a **max** for a counting trait; the form offers one or the other;
- **Every**: weeks and add, blank for none; **Expires**: weeks, blank for never;
- **Briefing**: the chip's text, blank for none;
- **Launch rules**: Sent, Not sent, and With target, each a list of rule rows (set or add the trait, loyalty, morale);
- **On a mission**: check bonus and "only with the target", blank for none;
- **When the target dies** (pairs): rule rows;
- notes.

In place of What plays here, **Where it's used**: every storylet, vignette, encounter, and test ship that reads or changes the trait, grouped by kind, each linked to its form, with what it does ("reads: `subject.trait.shaken >= 1`", "sets 1 on subject"). Above it, a warning when the trait is read but never set anywhere, or set but never read and has no rules of its own.

**The encounter form** gains "Trait" in an approach's condition select: a trait (from the defined ones), at least and at most (by level name where it has names), and on. Today it keeps a trait condition as written but cannot make a new one.

### 4.10 The lint on the storylets and vignettes lists

Above each list, as on the Lines page:

- items that cannot be cast on any test ship;
- traits read but never set by anything in the pack;
- `#handler#` outside a check's outcome;
- a slot used in the words but not in the cast, or cast but never used in the words **(my call: the second as advice, not a fault)**;
- an option whose `requires` cannot hold for any cast on any test ship.

Each names the item and links to its form.

### 4.11 The worked example: a storylet for the grieving

`grieving` is set by "The names on the wall" when the captain says "Not now", and nothing reads it. The worked example is a storylet that does, written in the browser and never in the file: a slot `subject` with the row "subject · trait grieving · is at least · 1", a script, and options whose effects clear `grieving` or let it run its four weeks. Its words are generated fiction under the author's standing allowance for slice 1e (GDD section 15, item 37), for the author to rewrite. USS Quality Assurance gets a grieving crew member so it casts. The plan's last task writes it through the form in a browser session and records what was awkward.

### 4.12 Tests

- The writer round-trips every storylet, vignette, and trait in the game's pack and the fixture without loss, `notes` included.
- A file with an inner comment is lossy and is not rewritten.
- Every expression in the game's pack parses into rows, and rows render back to the same string.
- A test ship builds an `IStateSource` that answers each of the interface's readings as written; the loader refuses a ship that names an unknown crew member, trait, level, or storylet.
- Every storylet and vignette in the game's pack casts on some game test ship.
- The previews route casts on the chosen ship and reports a failed slot's clause.
- Browser tests: write a storylet with one slot from rows and one option, save, and see it on the list; switch a slot to text and back.
- No golden moves: nothing here changes play.

## 5. Architecture

### 5.1 Modules

- **`Sim.Expressions`** gains the reading of an expression into rows and the writing of rows back (`Clauses.cs` already splits on `&&`; the shapes go beside it).
- **`Sim.Storylets`** gains the test ship's `IStateSource` and a preview entry point (`Preview.Storylet` and `Preview.Vignette`): cast this definition on this state with this sample number, and say why not. `Casting.Failed` widens in place into `Casting.Failures`, every slot that fails on its own, and `ExpressionBook.Reading` says what a failing clause read.
- **`Sim.Content`** gains the four models (storylet, vignette, trait, test ship), their writer, their faithfulness keys, `notes`, the inner-comment rule, and the test ships' loader checks (`ContentLoader.LoadTestShips`, which `Load` never calls; the editor asks for the ships apart, with `ContentFileSet.TestShips`). `ContentKind` gains `Storylet`, `Vignette`, and `Trait`, each read from its folder like the other list kinds; the storylet, vignette, and trait models share one effect model, `StoryletEffectModel`, which a trait's rules use too.
- **`Web`** gains the four kinds in `ContentKinds`, the forms, the expression widget, the trait panel, the lint, and Dev Play's "Save as test ship". A form says which expression widgets it holds, for the fields as posted, with `KindForm.Expressions` (each one's prefix, label, and slots in scope); the rows route adds and removes their clauses, switches them between Rows and Text, and checks a text as typed (`?check=`). A list whose rows hold other lists or widgets (a storylet's slots and options) carries every field posted under a row with it (`RowSet.Carry`), and a form names its nested lists with `KindForm.NestedRowSets`, settles a row whose select changed what it shows with `KindForm.Settle`, and puts a loader's refusal of a save beside its field with `KindForm.Place`. The storylet form's parts that the vignette form will share are `StoryletParts` (`Web/Forms/StoryletKinds.cs`). The 1d "What plays here" part is reused, as decision 10 intended; where it assumes a mission beat, it is generalised rather than copied. `Plays.Of` takes the form and makes a mission beat's panel (`Plays.Mission`, the 1d code) and a storylet's (`Plays.Storylet`, `Web/Forms/Plays.Storylets.cs`) cases of one part: the same `PlaysView` of groups, slots, and previews, with `PlaysView.Casting` for what the casting found on the test ship (null for a mission beat). The storylet's chronicle preview is written against the chronicle module's own situation, `ChronicleSamples.Storylet` (`Sim.Chronicle`).

### 5.2 Rules that hold

- The YAML is the stored form; the loader is the judge of a save, as since 1c-editor.
- A list file keeps only its leading comment. Any comment after it makes the whole file lossy, so the editor refuses to rewrite it rather than drop the comment.
- The builder never writes a string the loader would not accept, and never stores anything but the string.
- Test ships never reach play: the bridge and the simulator never read them.
- No event, state, or golden changes.

## 6. Roadmap and GDD changes

Applied with this design:
- **GDD 11.1, "Track: online"**, in both tables, is replaced and widened: serving the game at a URL, accounts, a hosted database, and one legacy per player (as before); a login on the developer pages; touch-first editor forms, starting with the storylet forms; and drafts kept in the browser so a reloaded tab loses nothing. It stays a track, to start whenever convenient.
- **GDD section 15, item 45** records this part's decisions.
- **GDD section 16, question 6**: whether the encounter and line previews move onto test ships.
- **The outline's** paragraph on slice 1e names this design.

Applied when built: GDD 7.5's editor paragraph (the new kinds, the expression widget, test ships), the roadmap's row "Done" with a pointer to "As built", and `docs/conventions.md` for `notes` and test ship ids.

## 7. Open, and left to the builder

- The look of a row and of the Rows / Text switch: from `docs/design/`'s recipes, mocked up first in `docs/screens/1e-6/`.
- Whether a test ship's skills can be overridden one by one or only as a whole set.
- Whether "Save as test ship" also captures morale history and injuries' days to heal; the minimum is what `IStateSource` reads.
- How the form says that an item is lossy (inner comment, unknown key) without hiding what it can show.

## 8. As built

Kept as each task of the plan (`2026-11-20-phase-1e-part-six-plan.md`) lands; differences from sections 4 and 5 are said here, and section 5 is changed to match.

**Task 1, models, writer, and faithfulness (2026-11-20).**

- The models are in `ContentModels.cs`, key by key from the loader. Where the plan's sketch differed, the loader won. A check's success and failure hold effects and text only, because the loader has no chronicle there: `StoryletCheckModel(Skill, Difficulty, Success, Failure)`, with `StoryletOutcomeModel(Effects, Text)` for each side. A storylet's `Script` is its `text:` key.
- One `StoryletEffectModel(What, Amount, Trait, Set, Level, On, Toward)` serves storylets, vignettes, and a trait's rules (`launch` and `onTargetDied`), since all three read the same keys. `What` is `loyalty`, `morale`, or `trait`. A rule that changes its own trait has no `Trait`. A `set` to a level's name goes in `Level`.
- `TraitModel` keeps `Shown` as written (null when absent, which means shown), `Levels` by value, and `Launch`, `Mission`, `Every`, and `ExpiresWeeks` as their own small records.
- The writer follows the files' key order. A storylet is written as `id`, `name`, `notes`, `stakes`, `requires`, `cast`, `cooldown`, `chronicle`, `text`, then `options`. A vignette is written as `id`, `name`, `notes`, `requires`, `cast`, `cooldown`, `text`, `default`, `options` or `effects`. A trait is written as in `traits/base.yaml`. Every expression is double-quoted, as the author writes one. Options are written as blocks, so a first save turns the game's flow-style options (STORYLET_05 and 06) into blocks, and quotes option texts that have a comma.
- `notes` is a `|-` block after `name`. A note the block cannot hold exactly is quoted on one line instead: one with a space at a line's end or at its start, or one that starts or ends with a blank line.
- Faithfulness: the new keys are in its lists. One addition: `shown` is read as a bool but is not dropped when false, because absent means true. A trait's `levels` keys are read as YAML integers, as an assignment's `offer` keys are.
- With the new kinds in `ContentKind`, `ContentFileSet.Index()` lists storylets, vignettes, and traits. The editor's pages still show its nine kinds until Task 6, so the two Web tests that went through every `ContentKind` now go through `ContentKinds.Kinds`. The id check for a new item (`Checks`) now sees storylet and vignette ids, as the loader does. Task 6 should add `Trait` to `Checks.LowerCase`.
- Canonicalizing the game's pack gives the same storylets, vignettes, and traits. The pack's lines can come in another order, because the writer puts an objective's own words in a fixed order. That predates this part, so the test compares the lines as a set.

**Task 2, the comments move into notes (2026-11-20).**

- The count before the migration: storylets 19 comment lines, traits 7, encounters 2, lines 9, and assignments 2, as the plan said. The game's vignettes file had none, and neither did the fixture's storylets and vignettes: the fixture's only inner comments were the two in `assignments/`. A tuning file or the calendar is not a list kind, so the rule does not look at them, and they keep their comments.
- A comment that sat on an option, an approach, or a key went into its item's notes, prefixed with that option's or approach's id (`make_time:`, `careful:`), or with the key's name when it sat on a trait's key (`expires:` on `shaken_heard`). A comment above an item went into that item's notes with its line breaks as written.
- Lines and assignments have no `notes`, as the plan chose. Their comments joined each file's leading comment: in the lines file each names the lines it heads ("Schedule lines (LINE_SCHEDULE_*)", "Chronicle lines for a death"), and in both assignments files the two trailing comments became one sentence each about `objectives` and `offer`.
- The rule is one file-level loss for a file, however many comments it has: "has a comment on line N (and M more) that saving from a form would drop; move it into notes, or edit the file as text". For a kind with no `notes` it says "move it into the comment at the top of the file" instead. The comments come from YamlDotNet's scanner with comments kept, and any on a line after the leading block counts, which is the block `ContentWriter.LeadingComment` keeps. So a comment after a blank line at the top is an inner comment, which the writer always dropped; that test now expects a refusal.
- A form beside a file-level loss used to say "Another item in this file has content the editor cannot keep". When every loss is the file's own, it now says "This file has content the editor cannot keep, so a save here is refused until it is fixed by hand."
- The storylets have no form until Task 8, so the Web test puts a comment in the gear file. The refusal of a storylets file is tested in `Sim.Content`: an upsert, a delete, and a canonicalize are all refused, and nothing is rewritten.
- `ContentOracleTests` finds its keys with a regex over the text, so it now skips text before the first item, where the assignments file's leading comment names `objectives:` and `offer:`.
- The loader gives the same pack before and after the migration, for both packs. This was checked once against the previous commit's files and not kept as a test. No golden moved.

**Task 3, expressions as rows (2026-11-20).**

- `Shapes.Read` checks the expression with `ExpressionBook.Check`, splits it with `ExpressionBook.Split`, and matches each clause's parsed tree (the DynamicExpresso tree the loader checks), not its text. A refused expression gives the check's error as its reason. The rewrite now keeps the level names it resolves (`Rewrite.Levels`), because the tree holds only a level's number and a row keeps the name as written: `subject.trait.came_back == 'withdrawn'` stays a name, and `== 0` stays a number.
- A row's `Value` is a whole number, or a level's name (written without quotes) on a trait compared with `==` or `!=`, which is the only place the loader takes a name. `Write` quotes a value that is not a number. Every comparison shape takes all six operators; the widget may offer fewer (Task 7).
- `Negated` is allowed on the two shapes that are true or false on their own: Presence and HistoryLast (`!subject.history.last('hurt')`) **(my call)**. A storylet's arrival (StoryletSince) and the ship's chronicle (ChronicleCount at the root) have `Who` "ship". `Unit` is set only on the two readings of time, and `Window` only on the two counts.
- The reasons, first in reading order (left to right through the tree): "uses ||", "uses arithmetic" (a negated reading counts, a negative number does not), "groups with !( )" (a `!` before anything but aboard, fit, or history.last), and "compares two readings" (crew identity, `subject != target`, counts too). Two more for what is valid and matches no shape **(my call)**: "puts the value before the reading" (`1 < subject.morale`, `'withdrawn' == subject.trait.came_back`), and "has a clause no row shows" (`cycle.number > 12`, `subject.fit == true`).
- Reading is from the tree, so spacing, double quotes, and parentheses around clauses do not stop an expression being rows; `Write` gives the house spelling. So `Write(Read(x))` equals `x` exactly for text already in the house spelling, and `Read(Write(rows))` always gives the rows back. Every expression in both packs was already in the house spelling, so no content changed.
- `Write` throws on a row missing a part its shape needs, or negated where it cannot be.

**Task 4, test ships (2026-11-20).**

- **Two ships, not one (my call, under 4.6's rule).** "A quirk in the sensors" needs `sensors_quirk` at `noticed`, and "The ghosts have names now" and "A ghost on the night watch" need it at `let_ride`, so no one ship casts all fourteen. USS Quality Assurance casts the other thirteen; **USS Quality Control** (`uss_quality_control`), four cycles out with the quirk just noticed, casts the first. The test asks that each casts on at least one ship, as the plan's Step 5 says.
- **The editor reads the ships apart (my call).** `ContentFileSet.Validate` is still the pack's loader alone, and `ContentFileSet.TestShips()` reads the ships against the pack it built (none, and no errors, when the pack does not load). A save that changes a file under `testships/` is refused when a ship is wrong, in the loader's words. Any other save is judged by the pack alone, and a ship it leaves naming something gone is listed on the index beside "Valid". Refusing every save over a stale ship would mean that deleting a storylet a ship names needs a hand edit first, and Task 14's captures will name most of them.
- The ships are edited as text before Task 6: `/dev/content/files/testships`, linked from the index with the other text files. `ContentKind.TestShip` (folder `testships`) has its model, reader, writer, and faithfulness keys, so the file is in `ContentFileSet.Index()`, and a canonicalize writes both packs' files back as they are.
- The architecture test (`Nothing_that_plays_reads_a_test_ship`) reads the source of `GameHost`, `SimRunner`, and `Web/Bridge` for the loader's entry and the ship's types, as the abort id's test does, rather than the assemblies: `Sim.Tests` references none of the three, and the other architecture tests read project files.
- What the loader asks, beyond the plan's list: a name; `date`, the day number of the ship's today, 0 or more (the game's start is day 37,482, so USS Quality Assurance is at 37,602); `cycle` 1 or more; a crew member, a trait on one holder, and a storylet each listed once; a held trait's `value` from 1 (a trait at 0 is not held, so it is left out) to its cap, and for a trait with levels only a level, by name or number, through the loader's own `TraitBook.SetValue`; morale and loyalty within the tuning's bounds; history ages from 1 (last cycle) to the ship's cycle or `history.cycles`, whichever is less, since the history keeps no more. `storylets` may name a vignette too, since the expressions' `storylet.X` reads either. Skills are overridden one by one, as the plan says; design 7's whole-set question stays open.
- The source: the roster is the living crew in the starting file's order, as the module's is, so a crew member with `aboard: false` is on it but not aboard. A pair's trait has no clock (`TraitSince` reads on a crew member or the ship only), as `StateSource` reads none.
- The pack test casts with no cooldown in the way (`cooling` always false): a ship's arrivals and the cooldowns are the preview's to weigh (Task 5).
- The fixture's ship (`test_ship_a`) casts its storylets but STORYLET_05 (which never casts) and its vignettes but VIGNETTE_03 (trait_a at 2). The loader skips the folder, so no golden moved.

**Task 5, the preview entry point (2026-11-20).**

- **A sample is a cast not yet seen (my call).** Sample 0 is `Casting.Cast` on its stream (`preview:<id>:<sample>`, the FNV hash `RngStream.For` already takes of a name, from seed 0, so no legacy's stream is touched). Each later sample casts on its own stream and passes the casts the samples before it found to the search's cooling test, as the module passes over a cast in its cooldown. So "Next sample" always moves to another cast that fits, and when none is left the samples go round again (sample n is the n mod k-th of the k found). Streams alone would give the same cast twice about one time in three with three who fit.
- **The cooldowns are reported, not applied (my call).** `IStateSource` says only when a storylet last arrived (`StoryletSince`), with any cast, so a test ship's cast cooldown is binding-blind: it holds for every cast or none. The preview reads both cooldowns from that one arrival with the module's own test (`Salience.InCooldown`, now shared: fewer cycles ago than the cooldown) and casts as if neither held, so the words can still be read. A cast cooldown can then say "held back" of a cast that never arrived, but never "clear" of one the module would hold back (review focus 3).
- `Failures` reads a slot that fails as if it held for anyone (its casts as they came, before its clauses narrowed them), so the slots after it are still read. Its first entry is what `Failed` always gave, and `Failed` is now that first entry, so Dev Play's output is unchanged. Too few crew for the slots is a failure of the slot's first clause, as before.
- `Why` holds the slots alone; a failing `requires` is `RequiresFailed`, "`<clause>` is `<reading>`", read on the first cast (in roster order) that reached the clause. `ExpressionBook.Reading` gives a comparison's reading (the side that reads the state, under any widening the parser added), and for any other clause whether it held ("false"). A reason drops the slot's name from the front of its clause ("no one aboard has trait.grieving >= 1"), and adds "though the clauses before it held for N" when it was not the first clause. One `SlotWhy("", "", "out of tries")` stands for no failure found but no cast either. `RequiresHolds` is true when there is a cast, or when no failure is on `requires`.
- **Anyway applies only when no one fits (my call),** so `Unconditioned` is never set on a cast that fits. The slots take the roster's first crew in order; with fewer crew than slots there is still no cast. `OptionOffered` and `Handler` then read the unconditioned cast.
- The handler for a check option is from those aboard, fit, and not in the cast, in roster order, the `sample mod count`-th; `Check.Handlers` reads `IRosterView`, which a test ship has not, so this reads the same through `IStateSource` and adds aboard, since a test ship can have someone away. With no check option, or no cast, there is no handler. A vignette's options have no `requires`, so each is offered.
- `Preview` builds a `Casting` for the pack on each call: the editor's candidate pack is new on each post anyway.

**Task 6, the editor's new kinds and their lists (2026-11-20).**

- The four kinds are in `ContentKinds`, so the index has thirteen kind sections and each new kind has its list with design 4.2's columns. "Casts on" is "n of m": the test ships on which `Preview` (sample 0, not anyway) finds a cast, so a cooldown does not count against it, as the preview reports cooldowns and does not apply them. It is blank while the pack does not load. "Requires" is clipped at 40 characters. "Cooldown" is as written ("cast 4, global 2"), blank when the tuning file's is used. A trait's "On" is "ship or crew" when the file leaves it out. "Levels or max" lists the levels by number ("1 noticed, 2 let_ride") or says "max 5". A test ship's "Date" is the calendar's short date, and "Crew" and "Traits held" count its entries.
- **A kind with no form opens as text (my call).** The forms come in Tasks 8, 11, and 12, so until then a storylet, vignette, or trait in a list (and on the index) links to its file's text view, as a test ship always does. The list has no "New" and says "The storylet form comes later. Until then each opens in the file's text." Every one of the four lists has "Edit as text" beside "New". When a kind's `KindForm` is registered, its links and "New" follow with no other change (`ContentKinds.HasForm`).
- The index's "Other files" loses the test ships file as well as the three the plan names, since its kind's list now links it **(my call)**. `TextFiles` keeps all four entries.
- **Lossy items (design 7, last item).** An item's own loss marks its row, as before. A loss of the whole file, such as Task 2's inner comment, is listed above the table on its kind's list, as it already was on the index. A form's "Read before saving" warning keeps its words, and when the item's file has a text view it adds "The fields below show what the form can keep. Edit the file as text to keep the rest, or to fix it." The form is still drawn below it. Until Task 8 no form has a text view, so a component test checks the link.
- **New ids.** `NewIds.Next` gives the next number after the highest `STORYLET_NN` (or `VIGNETTE_NN`), at the width the pack writes, skipping any id the item namespace already holds: `STORYLET_09` and `VIGNETTE_07` for the game's pack. `FormFlow.Blank` fills it into a new form's id field. `NewIds.FromName` makes a lower-case id from a name: words joined by `_`, articles dropped, marks taken off letters, apostrophes dropped, at most 40 characters ("Hold a service" gives `hold_service`). Task 8 will use the same for option ids. A trait created with no id takes one from its name on the server. `Checks.LowerCase` now holds traits and test ships, each with its own namespace.
- **The lock is a refusal (my call, against an older test).** A save that posts an `id` other than the route's is refused with "The id is fixed once the item exists. It is X.", and nothing is written. Before, such an id was ignored and the save went through. The test that said so (`ContentFormsTests`) now expects the refusal. A post with the same id, or with no id, saves as before.

**Task 7, the expression widget (2026-11-20).**

- **The look** was settled in `docs/screens/1e-6/expression-widget.html`, drawn with the editor's own stylesheet, and its screenshots at 1440 and 400 wide. The label sits at the left with the Rows / Text switch at its right, two buttons drawn with the tabs' tokens (`aria-pressed`, the selection fill and azure edge). A clause is a hairline box (`rule-2`, radius 6) holding its sentence of selects and steppers, with Remove at its right (under it at 40rem or less) and its text under it in the mono face, small and muted. Text is a mono box, then the line under it (why it stays text, or the error with the box's line marked at its character, as the text file page marks one), then the chips.
- **Field names (my call).** The rows go through `FormRows`, as the plan says, so they are named as every row set is: `p[N].kind`, `p[N].who`, and so on, not the plan's `p.N.kind`. `p.mode` is `rows` or `text`, and `p.text` the text. `not` is `true` or blank.
- **How a form holds widgets.** `KindForm.Expressions(values)` lists the widgets for the fields as posted (prefix, label, slots), since a storylet's slots come from the post. `KindForm.AllRowSets(values)` adds each widget's clauses to the form's row sets, which the rows route, the renumbering before a bind, and the bad-row check all read. A form's `Bind` calls `ExpressionField.Bind` for each, which gives the string or null (no clauses, or a blank text) and puts any error beside its field. No form holds a widget until Task 8; the tests use one made for them. **For Task 8:** `FormRows.Write` for a list drops every field under its rows (`options[0].` and anything after it), so a widget nested under an option's or a slot's row (`options[0].requires[1].kind`) would lose its clauses when that list is rewritten. Either give such widgets prefixes outside the list's, or teach the list to carry them.
- **The switch** posts `row=mode:{prefix}:text` (or `:rows`) to the rows route. To Text, the box holds `Shapes.Write` of the rows; a clause that is not complete is left out of it **(my call)**. To Rows, the text is read with `Shapes.Read`; when it is not rows (or does not load), it stays in Text and the line under it says why. Focus goes to the box, or to the first clause.
- **A select that changes what a row shows** (what it reads, whose it is for a trait, which trait, the operator) posts the form to the rows route as it changes, naming itself as the focus. That row alone is then made to fit **(my call)**: a who, name, or toward the shape does not offer is cleared or set to the first that fits (the ship for a chronicle count), and a blank operator, value, window, or unit takes a start (`>=`, 1, 1 cycle or 30 days, cycles). Other rows are left as posted, so a value the pack no longer offers stays shown, and the check refuses it, rather than being changed silently.
- **Values.** All six operators are offered. For a trait with named levels, "is" and "is not" pick a level by name (or "0, not held"), and the ordered operators pick a number labelled with its level's name. A name posted with an ordered operator is written as its number, since the loader takes a name only beside `==` or `!=`; a number beside `==` stays a number, so a stored `== 1` round-trips. Anything else is a stepper: a box with a step down and a step up that step it in the page (`editor.js`) and post nothing. Whose is the slots in scope, plus the ship for a trait or a trait's clock (when any trait may sit on the ship) and for a chronicle count; a storylet's arrival has no who.
- **Errors.** A part missing or a value that is not a whole number is refused beside that part ("Choose a trait.", "Enter a whole number."). A clause the loader's check refuses is refused beside its first select, in the check's words. A text the check refuses is refused beside the box, with "(character N)", and the line under the box marks the character (`ExpressionSpots.InBox`).
- **Checked as typed** through the rows route with `?check={prefix}`, which answers with the line under the box alone, patched by its id (500 ms after typing stops, and after a chip). A query and not a new route segment **(my call)**, since a literal segment beside `{kind}/{id}` would refuse an item whose id is that word.
- **The chips** insert the slots, `ship`, `trait.X` for each trait, each level's name quoted, the history and chronicle kinds quoted, and `storylet.X` for each storylet. `editor.js` takes a widget as a chip's group, as it takes a tab or a panel's group.
- With a pack that does not load there is no book: a stored expression opens as text, it is not checked here, and the line under it says the loader judges the save. `RowButton` gained a class and `aria-pressed` for the switch.

**Task 8, the storylet form (2026-11-20).**

- **Nested lists are carried (my call, of Task 7's two ways).** `RowSet.Carry` makes a row hold every field posted under it, so the cast (`cast[0].name`, its widget `cast[0].when`) and the options (`options[0].id` and the rest, the widget `options[0].requires`, the effect rows `options[0].effects[N]`, `options[0].success.effects[N]`, and `options[0].failure.effects[N]`) move, renumber, and go whole when a slot or an option is added, removed, or moved. `KindForm.AllRowSets` lists a form's own lists first, then its nested ones (`KindForm.NestedRowSets`), then the widgets' clauses, so a renumbering moves the nested fields before they are read. An option's own fields: `id`, `saved`, `label`, `settled` (`effects` or `check`), `skill`, `difficulty`, `text`, `success.text`, `failure.text`, `chronicleWeight`, and `chronicleText`. An effect row's: `what` (loyalty, morale, or trait), `trait`, `mode` (add or set), `amount` (a number, or a level's name for a set), `on`, and `toward`, blank `on` and `toward` being absent.
- **A new storylet** starts with stakes 1, one slot called `subject` with no clauses, and two blank options settled by their effects, since the loader asks for two to four **(my call)**. A slot with no clauses is refused beside its widget ("A slot needs a clause: who can fill it."), so the widget gained a line for an error of its own. The browser test writes two options, not the plan's one, for the same reason.
- **Option ids.** A stored option posts `saved=true` beside its id, which is shown as text. `StoryletKind.Widen` puts the stored option ids in `FormChoices.FixedOptionIds`, and a saved row whose id is not one of them is refused beside it ("An option's id is fixed once it is saved."). A new option's id box follows its label as it is typed (`editor.js`, `idFrom`, written to give what `NewIds.FromName` gives) until the author types in the box. Left blank, the server makes it from the label, with `_2`, `_3`, and so on when another option already has it.
- **Effect rows** are drawn as an expression clause is: a sentence of selects and steppers. What, trait, and add-or-set post as they change, and `StoryletParts.Settle` (through `KindForm.Settle`) makes that row fit: a trait with named levels is only set, to a level by name or 0; a pair trait asks toward whom; loyalty and morale take `on: crew` to start. A trait's `on` is blank for the first slot (the ship when there is no cast), a slot, or the ship where the trait may sit there. `handler` is offered only in a check's success and failure, and a post that names it elsewhere is refused.
- **Settled by** shows the effects and text, or the check's skill, difficulty, and Success and Failure panels. The way not shown keeps its fields as hidden inputs, as the encounter's decision does, so switching back loses nothing; a switch to a check starts the difficulty at 1.
- **The storylet's chronicle weight** (a key the model has and design 4.3 does not list) sits beside the cooldowns **(my call)**. Blank cooldowns are none, since a storylet with no `cooldown` has none (the tuning file's applies to vignettes alone), so the hint says "Blank: none".
- **The loader's refusals go to their fields (Step 5).** The encounter form puts only its own checks beside fields; the loader's refusals of a save went to the alert. Now `FormFlow` asks `KindForm.Place` of each, and the storylet form places an expression's refusal (`cast.subject, at character N: ...`, `requires`, `options.X.requires`) in its widget: beside the text with "(character N)", or beside the first select of the clause that holds the character (`ExpressionField.Place`, from the rows joined by `&&`). A text's refusal goes beside its box (the script, an option's text, an outcome's, a chronicle text), and an option's label, skill, difficulty, or chronicle weight beside that field, as do the stakes, the counts of options and slots, the cooldown, and the storylet's chronicle weight. Anything else (an effect's refusal, which the loader does not place, or "leaves every expression true") stays in the alert. With the pack loading, the widget's own check already refuses what the loader would, so the test breaks the pack with the storylet's own slot.
- **A slot's name** does not post as it is typed **(my call)**: posting on change would draw the form again and pull the focus back. The widgets read the slot names on the next row action or change of a select.
- **Chips** (design 4.3): `captain:`, each slot as a speaker, `best SKILL_ID:` for each skill, `>` and `=`, each slot as `#slot#`, `#storylet#`, `#handler#` in a check's outcomes and in a check option's chronicle text, and the pack's symbols. `editor.js` takes a `.chipbox` (one box and its chips) as a chip's group.
- **Saving unchanged.** The game's storylets file has flow-style options, which a first save writes as blocks (Task 1), so the byte-for-byte test writes the file as the writer would first (the pack is the same before and after), then opens and saves each of the eight storylets with what its form posts and finds every file unchanged. The fixture's storylets are opened and saved too, with the pack unchanged. "What plays here" comes in Task 9: `Plays.Of` is still null for a storylet.
- `Field` gained `InputAttributes`, for the label's `data-id-to` and the id box's `data-auto`. The storylets text file's note now says each storylet also opens in its form.

**Task 9, What plays here on a test ship (2026-11-20).**

- **Generalised, not copied.** `Plays.Of` takes the `KindForm` and switches on its kind: an encounter or an objective goes to `Plays.Mission`, the 1d code with its `Item`, `Group`, and `Slot` (unchanged in what it builds, and its tests unchanged), and a storylet to `Plays.Storylet`. The records the components draw (`PlaysView`, `PlaysGroup`, `PlaysSlot`, `PlaysPreview`) are shared; `PlaysView` gains `Casting` (`PlaysCasting`: the ships, the one chosen, the top line, each slot's reason, anyway, unconditioned, the options not offered, and the stale message), null for a mission beat. `PlaysPanel` draws `PlaysOnShip` for a storylet, and `PlaysPreviews` answers the plays route for both.
- **The candidate.** Each draw and each refresh binds the form with the kind's own `Bind`, upserts the model into the file set, and validates it; the test ships are read against that candidate pack. A new storylet whose id box is blank is previewed as `STORYLET_PREVIEW`. When the candidate does not load, the panel says why: the form's first refusal ("The form as it stands would not save (...)"), or the loader's first two errors. On a refresh the route then patches only that line (`#plays-status`, `data-stale`), so the previews already shown stay, and the stylesheet greys every preview while it says so (`:has`), which keeps the route stateless. On a full draw (a row action, a refused save) the stored storylet on disk is previewed in the candidate's place, greyed, as the last version that loaded **(my call)**; a new storylet that has never loaded shows the message alone.
- **The ship select.** The default is the first ship in the file, which is USS Quality Assurance in the game's pack **(my call: the file's order, not an id in code)**. The choice is the form field `ship`: read from `?ship=` when a form is opened or a new one is started, posted with every post, carried over a save and the create's redirect, and written to the address with `history.replaceState` when it changes, which also asks for the previews at once (the select's input stops there, so the form's own refresh does not post a second time). With no test ships, the preview casts on a ship with no changes, named "The starting crew", with anyway on and a line that says so.
- **What shows.** The top line is "Requires holds" or "Requires fails: <clause> is <reading>", then the cast cooldown and the global cooldown, each "clear" or "would hold it back", joined by " · ". The sample line names the cast ("Sample 1: Lt. Kell Varro as subject", and who would be sent on a check), "for the ship, with no one cast" for a storylet with no slots, or "No one fits on <ship>." with each slot's reason listed. **Preview anyway is a box, not a button (my call),** so it stays on across the refreshes that typing makes; it is shown when no one fits or while it is ticked. An unconditioned cast says so on its sample line and under the reasons. An option the cast is not offered is marked "(not offered)" on its tab and "Not offered with this cast." in the panel; both marks are always drawn, empty when offered, so the route can patch them by id.
- **A text in its tab and here (my call, reading design 4.3).** Each box keeps its place (the script in the definitions, an option's words, its success and failure words, and its chronicle text in its tab), and gains its preview under it, with the line that plays there now as its greyed placeholder while it is blank. The panel reads the storylet in order with copies of the same previews: the script, then for each option its words (or its success and failure words) and the chronicle entry it would write (one for each outcome of a check). A panel copy's id is the box's with `pp-` for `pv-`, and the route patches both. As on encounters, a placeholder follows the next full draw, not the refresh.
- **Rendering as play renders.** The script and the words go through `Prose.StoryletSituation` and `Prose.Storylet` with the candidate pack, the cast, and Preview's handler, with the ship's crew made into `CrewMember`s (fit and aboard is active, each skill at the ship's level) so a `best SKILL_ID:` speaker is the ship's best. The chronicle entry is written against `ChronicleSamples.Storylet`, the chronicle module's own situation, new here: the option's own text expanded by the `Narrator`, else the line the pack's selector picks, else the module's fallback, with its weight, and "Weight 0: no chronicle entry is written." Each preview's note says whose words won ("Its own words.", "Shared line X plays here.").
- **Refresh.** The storylet form posts the plays route on any input in the form, 700 ms after typing stops (`FormShell.OnInput`), not only in the panel as the encounter form does, since its boxes sit in the definitions and the tabs.
- `A_loader_refusal_of_a_slots_expression_is_shown_beside_its_box` now looks for the loader's words in the alert alone, since What plays here now says what the loader said of the form.
