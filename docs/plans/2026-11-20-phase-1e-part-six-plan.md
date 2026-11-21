# Slice 1e, part six (writing storylets in the editor): implementation plan

> **For agentic workers:** steps use checkbox (`- [ ]`) syntax for tracking. Do the tasks in order; each ends with `./check.sh` and a commit. Keep the design's section 5 (architecture) current as you go: when the build differs, change section 5 in the same commit, and say so in a new "As built" section (section 8) of the design.

**Goal:** the author opens the editor, taps **New storylet**, and writes one from start to finish in a form: a name, a cast slot built from rows, a script with chips for the speakers, and options with their words and effects. "What plays here" shows it as it would read on **USS Quality Assurance**, cast for real, with "Next sample" rotating whoever fits. They save, the loader accepts it, and in Dev Play it arrives. Vignettes and traits get forms too. A `notes:` field takes the place of inner comments. Test ships are a new dev-only content kind. Nothing is typed into a YAML file.

**Architecture:** `Sim.Expressions` learns to read an expression into rows of ten known shapes and to write rows back, from the same rewrite the loader uses. `Sim.Content` gains models, a writer, and faithfulness keys for storylets, vignettes, traits, and test ships. It also gains `notes`, the inner-comment rule, and a separate loader entry for test ships, which `ContentPack` never carries. `Sim.Storylets` gains `TestShipSource`, an `IStateSource` built from a test ship, and `Preview`, which casts a definition from a candidate pack on that source with a fixed stream for each sample. `Web` gains the four kinds, the expression widget, the storylet, vignette, and trait forms, the trait's "Where it's used" panel, the lint, and Dev Play's "Save as test ship". The 1d "What plays here" part is generalised so these forms can use it, not copied.

**Spec:** `docs/plans/2026-11-20-phase-1e-part-six-design.md`. Read it with this plan. **Design system:** `docs/design/bridge-design.md`, and the editor's existing forms (`src/Web/Components/Content/`) for their recipes.

The design's three stages are Tasks 6 to 10 (storylets), Task 11 (vignettes), and Tasks 12 and 13 (traits). Tasks 1 to 5 lay the shared ground.

## Global constraints

Part one's (`2026-11-16-phase-1e-part-one-plan.md`, "Global constraints") hold, with these changes:

- **Nothing here changes play.** `SchemaVersion` (14) and `CurrentFormatVersion` (12) stay as they are, and no event, state, or module changes. Until Task 15, no golden hash or golden chronicle moves. Task 15 adds a storylet to the game's pack, and that may move them. If it does, record the before and after in that commit.
- **Test ships never reach play.** `ContentPack` has no test ships. They are loaded by their own entry, `ContentLoader.LoadTestShips`, which only `Web`'s developer pages and tests call. `GameHost`, `SimRunner`, and the bridge never call it. An architecture test checks this (Task 4).
- **The YAML holds the string.** The expression builder is a view of the string the loader reads. Nothing stores rows. The server writes the string from posted rows, and a row always renders back to the text it was read from.
- **The loader judges a save,** as since 1c-editor: the whole pack is built, the loader runs, then one file is written.
- **Every `Sim.*` project still references `Sim.Core` alone** (plus `Sim.Expressions` for its users). The architecture test is not loosened.
- **No hover, and selects and steppers for known sets.** The tablet waits for the online track, but nothing here works against it.
- `content/` may gain `notes:` on storylets, vignettes, traits, and encounters (with the inner comments moved into them), edits to leading comments (Task 2), `testships/base.yaml`, and, in Task 15, the worked example's storylet and a grieving crew member on the test ship. Nothing else changes. The fixture gains the same in placeholder form.

## Review focus

1. **No save loses anything.** Every storylet, vignette, and trait in both packs round-trips through its model and the writer, `notes` included. A file with an inner comment is refused and not rewritten. (Tasks 1 and 2)
2. **Rows and text never disagree.** "Fits in rows" comes from the loader's own rewrite. Every row set renders to a string the loader accepts, and every expression in the game's pack parses into rows and renders back unchanged, character for character. (Task 3)
3. **The preview does not lie.** It casts with `Casting.Cast` on an `IStateSource`, the same path the module uses. It never claims a storylet can arrive when the real casting would refuse it. "Preview anyway" says it is unconditioned. (Tasks 5 and 9)
4. **Test ships stay out of play.** (Task 4)
5. **Saved ids are stable.** An option's id locks once it has been saved, and so do a storylet's and a trait's. A new id never collides with an existing one. (Tasks 6 and 8)

---

### Task 1: models, writer, and faithfulness for storylets, vignettes, and traits

**Files:** `src/Sim.Content/ContentModels.cs` (`ContentKind` gains `Storylet`, `Vignette`, `Trait`; new models), `src/Sim.Content/ContentReader.cs`, `src/Sim.Content/ContentWriter.cs`, `src/Sim.Content/ContentFaithfulness.cs`, `src/Sim.Content/ContentFiles.cs` (folders for the kinds), `src/Sim.Content/ContentEdits.cs`, `src/Sim.Content/ContentLoader.Storylets.cs` and `ContentLoader.Traits.cs` and `ContentLoader.Missions.cs` (accept `notes`), `tests/Sim.Tests/ContentWriterTests.cs`, `tests/Sim.Tests/ContentFaithfulnessTests.cs`.

**Produces** (shape; the models mirror the YAML, every key the loader accepts, not the validated records):

```csharp
public sealed record CastSlotModel(string Name, string Condition);
public sealed record ChronicleModel(int? Weight, string? Text);
// Effects as the storylet loader reads them: morale, loyalty, or a trait change, with on and toward as written.
public sealed record StoryletEffectModel(string What, int? Amount, string? Trait, bool Set, string? Level, string? On, string? Toward);
public sealed record StoryletCheckModel(string Skill, int Difficulty, IReadOnlyList<StoryletEffectModel> Success, IReadOnlyList<StoryletEffectModel> Failure, string? SuccessText, string? FailureText, ChronicleModel? SuccessChronicle, ChronicleModel? FailureChronicle);
public sealed record StoryletOptionModel(string Id, string Label, string? Requires, IReadOnlyList<StoryletEffectModel> Effects, StoryletCheckModel? Check, string? Text, ChronicleModel? Chronicle);
public sealed record StoryletModel(string Id, string? Name, int Stakes, string? Requires, IReadOnlyList<CastSlotModel> Cast, IReadOnlyList<StoryletOptionModel> Options, int? CastCooldown, int? GlobalCooldown, string? Script, ChronicleModel? Chronicle, string? Notes) : IContentModel;
public sealed record VignetteModel(/* the storylet's shape, minus stakes and chronicle, plus Default and its own Effects */ ...) : IContentModel;
public sealed record TraitModel(/* id, name, on, shown, levels or max, every, expires, briefing, launch rules, onMission, onTargetDied, notes */ ...) : IContentModel;
// EncounterModel gains: string? Notes = null
```

Write the exact fields from what `ContentLoader.Storylets.cs` and `ContentLoader.Traits.cs` accept today, key by key. Where the shape above is wrong, the loader wins. Say so in "As built".

- [x] **Step 1: notes in the loader.** `notes` is an optional string on a storylet, a vignette, a trait, and an encounter. The loader reads it and drops it, and no definition record carries it. An unknown key is still refused.
- [x] **Step 2: the models and the reader.** `ContentReader.ReadEntries` reads each kind leniently, as it does the others. `ContentEdits.Get<StoryletModel>` and the rest work.
- [x] **Step 3: the writer.** Write each kind in the house style of the existing files, with ids and names first, then `notes` as a block scalar (`|-`) after `name`, effects as flow maps (`{ trait: avoids, add: 2, on: subject, toward: target }`), and scripts as block scalars. Open the current files and match their key order, so a first save rewrites as little as it can.
- [x] **Step 4: faithfulness keys.** Add the new kinds' keys to `NullKeys`, `EmptyListKeys`, `YamlIntKeys`, `RawIntKeys`, `FlagKeys`, and the rest, as the loader reads each one (`shown` as a bool, `stakes` and `difficulty` as YAML integers, cooldowns as the loader reads them).
- [x] **Step 5: tests.** Every storylet, vignette, and trait in the game's pack and the fixture round-trips without loss: the trees compare equal, and the loader gives the same definitions from the rewritten file. `notes` round-trips, including a multi-line note with a colon and a `#` in it. An encounter with `notes` round-trips.
- [x] **Step 6:** `./check.sh`; commit "Read and write storylets, vignettes, and traits as models, with notes".

### Task 2: the comments move into notes, and an inner comment is a loss

**Files:** `content/storylets/base.yaml`, `content/vignettes/base.yaml`, `content/traits/base.yaml`, `content/encounters/base.yaml`, `content/lines/base.yaml`, `content/assignments/base.yaml`, the fixture's `storylets/`, `vignettes/`, and `assignments/`, `src/Sim.Content/ContentFaithfulness.cs`, `src/Sim.Content/ContentWriter.cs` (`LeadingComment` stays as it is), `tests/Sim.Tests/ContentFaithfulnessTests.cs`, `tests/Web.Tests/ContentFormsTests.cs`.

Today's inner comments, by file: storylets 19 lines, traits 7, encounters 2, vignettes some, lines 9, and assignments 2. The fixture has some in storylets, vignettes, and assignments. Count them again before you start. Tuning files and the calendar are text files, which keep their comments, so they are out of scope.

- [x] **Step 1: the migration, by hand.** Each inner comment in storylets, vignettes, traits, and encounters becomes its item's `notes`, worded as it stands. A comment on an option or an effect goes into its item's notes, prefixed with the option's id ("`hold_service`: Everyone's loyalty dips but the subject's"). A comment that explains the file as a whole joins the leading comment. Put no blank line inside the leading comment, because `LeadingComment` ends it at the first blank line.
- [x] **Step 2: lines and assignments** (not in the design; **my call**, to be confirmed in "As built"). Neither kind gets `notes`. Their inner comments are file-level: section headings in `lines/base.yaml`, and two trailing comments that explain `objectives` and `offer` in `assignments/base.yaml`. Move each into its file's leading comment, so the forms that edit those kinds keep working once Step 3 makes an inner comment a loss. In the lines file, name the section each comment heads ("Calendar lines: ...", "Chronicle lines: ..."). If the author would rather lines had `notes`, that is a small change to Task 1, and it belongs in "As built".
- [x] **Step 3: the rule.** `ContentFaithfulness.Analyse` reports a file-level loss for every list file whose text has a comment after the leading block: "has a comment on line N that saving from a form would drop; move it into notes, or edit the file as text". Detect it from the YAML scanner's comment tokens, not with a regex, so `#subject#` in a quoted string is not a comment. Every form that saves a list file already refuses one with a file-level loss. Check that each does, and add the test.
- [x] **Step 4: tests.** After the migration, no list file in either pack has an inner comment, and every list file is rewritable (a guard test over both packs). A storylets file with a comment added after an option is lossy and is not rewritten, and its form says why. A comment inside a quoted string is not a loss. The loader gives the same pack before and after the migration (compare the definitions, not the text). No golden moves.
- [x] **Step 5:** `./check.sh`; commit "Move the content files' inner comments into notes, and refuse to drop one".

### Task 3: expressions as rows

**Files:** `src/Sim.Expressions/Shapes.cs` (new), `src/Sim.Expressions/Rewrite.cs` (exposing the top-level clause split and the parsed tree it already makes), `src/Sim.Expressions/Clauses.cs`, `tests/Sim.Tests/ExpressionShapesTests.cs` (new).

**Produces:**

```csharp
public enum ShapeKind { Presence, Trait, PairTrait, Stat, Skill, HistoryLast, HistoryCount, ChronicleCount, StoryletSince, TraitSince }
// Who: a slot name, or "ship". Op: one of == != >= <= > <. Unit: "cycles" or "days". Negated: !subject.fit.
public sealed record Row(ShapeKind Kind, string Who, string? Name, string? Toward, string? Op, string? Value, int? Window, string? Unit, bool Negated);
public sealed record RowsRead(IReadOnlyList<Row>? Rows, string? Reason);   // Rows null: the reason it stays text ("uses ||")
public static class Shapes
{
    public static RowsRead Read(string expression, IReadOnlyList<string> slots, ExpressionBook book);
    public static string Write(IReadOnlyList<Row> rows);      // clauses joined by " && ", each in the house spelling
}
```

- [x] **Step 1: what is read.** An expression is rows when the rewrite's top-level split gives clauses joined by `&&`, and each clause's tree matches one of the ten shapes in design 4.5. Match on the tree the rewrite produces, not on the text. Then "fits in rows" can never disagree with "is valid": an expression the rewrite refuses is not rows, and the reason given is the rewrite's error.
- [x] **Step 2: the reasons.** An expression that is valid but not rows gets a one-line reason: "uses `||`", "uses arithmetic", "groups with `!( )`", or "compares two readings". Give only one, the first found in reading order.
- [x] **Step 3: writing.** `Write` spells each row the way the pack writes it today (spaces around operators, single quotes for level names, no parentheses). A range is two rows and stays two clauses.
- [x] **Step 4: tests.** Every `requires`, cast condition, and option `requires` in the game's pack and the fixture reads as rows. `Write(Read(x).Rows) == x` holds for each, character for character. Where the pack's spelling varies (spaces, quote style), normalise the pack in this task and say so in the commit, rather than losing the round trip. For each shape, a row written and read back is the same row, and its string is accepted by `ExpressionBook.Check`. `a || b`, `x + 1 > 2`, and `!(a && b)` give text with their reasons. A clause naming an unknown slot gives the rewrite's error.
- [x] **Step 5:** `./check.sh`; commit "Read an expression into rows of known shapes, and write rows back".

### Task 4: test ships

**Files:** `src/Sim.Core/Content/TestShipContent.cs` (new), `src/Sim.Content/ContentLoader.TestShips.cs` (new), `src/Sim.Content/ContentModels.cs` (`ContentKind.TestShip`, `TestShipModel`), `ContentReader.cs`, `ContentWriter.cs`, `ContentFaithfulness.cs`, `src/Sim.Storylets/TestShipSource.cs` (new), `content/testships/base.yaml` (new), `tests/fixtures/content/testships/base.yaml` (new), `tests/fixtures/README.md`, `tests/Sim.Tests/TestShipTests.cs` (new), `tests/Sim.Tests/ArchitectureTests.cs`.

**Produces:**

```csharp
// Sim.Core.Content
public sealed record TestShipCrew(string Id, int? Morale, int? Loyalty, IReadOnlyDictionary<string, int>? Skills, bool? Fit, bool? Aboard, bool? Alive);
public sealed record TestShipTrait(string Trait, string? On, string? Toward, int Value, int DaysAgo, int CyclesAgo);
public sealed record TestShipEntry(string Kind, int DaysAgo, IReadOnlyList<string> Subjects);
public sealed record TestShipArrival(string Storylet, int DaysAgo, int CyclesAgo);
public sealed record TestShipDef(string Id, string Name, int Date, int Cycle, IReadOnlyList<TestShipCrew> Crew, IReadOnlyList<TestShipTrait> Traits,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<int>>> History, IReadOnlyList<TestShipEntry> Chronicle, IReadOnlyList<TestShipArrival> Storylets);
// Sim.Content
public static TestShipsLoadResult LoadTestShips(IReadOnlyDictionary<string, string> files, ContentPack pack);
// Sim.Storylets
public sealed class TestShipSource(ContentPack pack, TestShipDef ship) : IStateSource { public IReadOnlyList<string> Roster { get; } }
```

- [x] **Step 1: the file and the loader** (design 4.6). The loader reads `content/testships/*.yaml` as a list and checks each ship against the pack: crew ids, trait ids, level names (a trait's `value` may be a level name or a number), `on` and `toward` against the trait's kind, history kinds, chronicle kinds, storylet ids, and whole-number ages from 0, with `cyclesAgo` no greater than `cycle`. Each error names the file, the ship, and the field. Today's skill overrides are per skill. Leave a whole-set override to the open question in design 7.
- [x] **Step 2: kept out of play.** `ContentLoader.Load` skips the `testships/` folder, so a bad test ship never makes the game's pack invalid. The editor validates both: it calls `LoadTestShips` after `Load`, and shows its errors with the pack's. An architecture test checks that `GameHost`, `SimRunner`, and `Web/Bridge` do not reference `LoadTestShips` or `TestShipDef`. The test reads the assemblies, as `ArchitectureTests` does.
- [x] **Step 3: the source.** `TestShipSource` answers each of `IStateSource`'s readings as written. Anyone not listed is as the starting crew file has them, at the tuning file's starting morale and loyalty. Someone with `alive: false` is neither aboard nor in `Roster`. `History(crew, kind, cycles)` counts the listed cycles-ago values within the window. `StoryletSince` and `TraitSince` come from the ages. `ChronicleCount` counts entries with `daysAgo < days`, so today is included, as `IChronicleView.Count` does.
- [x] **Step 4: USS Quality Assurance.** Write `content/testships/base.yaml` so that all eight storylets and six vignettes in the game's pack cast on it, with `notes` that say what each crew member's state is there for. The fixture gets its own small ship, for its own storylets.
- [x] **Step 5: the test of the pack.** Every storylet and vignette in the game's pack casts (`Casting.Cast` with a fixed stream) on at least one of the game's test ships. A failure names the item and, for each ship, the clause from `Casting.Failed`.
- [x] **Step 6: other tests.** Each reading of the source, written against a small ship by hand. The loader refuses an unknown crew member, trait, level name, history kind, chronicle kind, and storylet, a `toward` on a crew trait, and a ship trait with `on`. A test ship file round-trips through the writer (it is edited as text, but "Save as test ship" in Task 14 writes it).
- [x] **Step 7:** `./check.sh`; commit "Add test ships, and USS Quality Assurance, which every storylet can be cast on".

### Task 5: the preview entry point

**Files:** `src/Sim.Storylets/Preview.cs` (new), `src/Sim.Storylets/Casting.cs` (only if `Failed` needs to report every slot, not the first), `tests/Sim.Tests/StoryletPreviewTests.cs` (new).

**Produces:**

```csharp
public sealed record SlotWhy(string Slot, string Clause, string Reason);            // "no one aboard has trait.grieving >= 1", "out of tries"
public sealed record PreviewCast(
    bool RequiresHolds, string? RequiresFailed,                                       // the clause and what it read ("is 1")
    bool CastCooldownClear, bool GlobalCooldownClear,
    IReadOnlyDictionary<string, string>? Cast,                                        // null: no one fits
    IReadOnlyList<SlotWhy> Why,
    IReadOnlyDictionary<string, bool> OptionOffered,                                  // by option id, for the cast
    string? Handler, bool Unconditioned);
public static class Preview
{
    // The definition is the pack's item with this id: the editor builds a candidate pack with the form's item in it.
    public static PreviewCast Storylet(ContentPack pack, string id, IStateSource state, IReadOnlyList<string> roster, int sample, bool anyway);
    public static PreviewCast Vignette(ContentPack pack, string id, IStateSource state, IReadOnlyList<string> roster, int sample, bool anyway);
}
```

- [x] **Step 1: the cast.** Cast with `Casting.Cast`, using a stream made from `StableHash("preview", id, sample)` and the module's own `MaxSearch` and cooldown test. Cooldowns are read from the state's storylet arrivals, as the module reads them. Nothing draws from a legacy's streams.
- [x] **Step 2: why not.** When no one fits, `Why` gives each slot's failing clause, from `Casting.Failed`. If `Failed` gives only the first slot today, widen it in place to give every slot that fails on its own, and keep its current callers' output the same. "Out of tries" when every slot has someone but no combination fits.
- [x] **Step 3: anyway.** With `anyway`, fill the slots from the roster in order, ignoring conditions, and set `Unconditioned`.
- [x] **Step 4: options and handler.** `OptionOffered` uses `Casting.Open` for the cast. For a check option, the handler is the sample's pick among those fit to send, rotating with `sample`.
- [x] **Step 5: tests.** On a hand-built ship: a storylet that fits casts the same as `Casting.Cast` with the same stream. Samples 0, 1, and 2 give different casts when three crew fit. A failing slot names its clause. A failing `requires` names its clause and value. A cooldown that holds is reported. `anyway` fills in roster order. An option whose `requires` fails is not offered.
- [x] **Step 6:** `./check.sh`; commit "Cast a storylet for preview on any state, and say why when no one fits".

### Task 6: the editor's new kinds and their lists

**Files:** `src/Web/ContentKinds.cs`, `src/Web/Forms/TextFiles.cs`, `src/Web/Forms/KindForm.cs`, `src/Web/Components/Content/ContentIndex.razor`, `ContentIndexView.cs`, `KindList.razor`, `KindPage.razor`, `src/Web/Endpoints/ContentEndpoints.cs`, `src/Web/Endpoints/ContentFormEndpoints.cs` (new ids), `tests/Web.Tests/ContentPagesTests.cs`, `tests/Web.Tests/ContentTextFilesTests.cs`.

- [x] **Step 1: the kinds.** Add **Storylets** (`storylets`), **Vignettes** (`vignettes`), **Traits** (`traits`), and **Test ships** (`testships`) to `ContentKinds`, with nouns and the columns of design 4.2. "Casts on" is "1 of 1" from `Preview` on each test ship. "Used by" counts what Task 12's panel lists, and shows a blank until then. "Traits held" counts a ship's trait entries.
- [x] **Step 2: the text files.** The storylets, vignettes, and traits files leave the index's list of text files. `TextFiles` keeps their entries, still reached at `/content/files/{name}`, and still with part four's expression errors at line and column. Each list page has an "Edit as text" link to its file. Test ships open only as text. Their list links each ship to the file's text view, with no form.
- [x] **Step 3: lossy items.** An item the form cannot keep is listed and marked as today, from Task 2's rule as well as the old ones, and its form says why and links to the text view (design 7, last item: decide the wording against `ContentFormWarning.razor`, and show what the form can).
- [x] **Step 4: new ids.** "New storylet" suggests the next free id in the pack's pattern (`STORYLET_09`; vignettes likewise). The author may change it before the first save. The id locks after that, shown as text, not as a field. A trait's id is made from its name, lower case with underscores, as `docs/conventions.md` says, and it also locks once saved.
- [x] **Step 5: tests.** The index lists the four kinds and no longer lists the three files as text. Each list shows its columns for the game's pack. "Edit as text" opens the text view, and saving there still works. A new storylet's suggested id is free. A saved item's id cannot be changed by posting a different one (the post is refused).
- [x] **Step 6:** `./check.sh`; commit "List storylets, vignettes, traits, and test ships in the editor".

### Task 7: the expression widget

**Files:** `docs/screens/1e-6/` (mock-ups), `src/Web/Forms/ExpressionField.cs` (new: rows from posted fields and back, and the chips' lists), `src/Web/Components/Content/ExpressionField.razor` (new), `ExpressionRow.razor` (new), `src/Web/Forms/ExpressionSpots.cs` (an error's place within one box), `src/Web/wwwroot/css/app.css`, `src/Web/wwwroot/js/editor.js` (chips at the caret, if it does not already do this for a field), `tests/Web.Tests/ExpressionFieldTests.cs` (new).

- [ ] **Step 1: the mock-up.** Before any Razor, mock up a row, a row set with add and remove, the Rows / Text switch, and the text mode with its reason and an error at its character, in `docs/screens/1e-6/` at 1440 and 400 wide, from `docs/design/`'s recipes. Settle the look there (design 7, first item).
- [ ] **Step 2: posted fields.** A widget with prefix `p` posts `p.mode` (`rows` or `text`), and either `p.text` or rows as `p.N.kind`, `p.N.who`, `p.N.name`, `p.N.toward`, `p.N.op`, `p.N.value`, `p.N.window`, `p.N.unit`, and `p.N.not`, through `FormRows` as other row sets are. The server makes the string with `Shapes.Write`. It never stores the rows.
- [ ] **Step 3: rows.** Each row is a sentence of selects and steppers, built from the pack and the slots in scope: who (the slots, and the ship where the shape allows it), traits with their level names, skills, history kinds, chronicle kinds, and storylet ids. The row's text is shown under it, small. Changing the shape's select re-renders the row through the existing rows route.
- [ ] **Step 4: text.** A box, checked as typed through `ExpressionBook.Check`, with the loader's error placed at its character. A one-line reason says why it is not rows. Chips for paths insert at the caret.
- [ ] **Step 5: the switch.** It is always shown. Going to Text fills the box with `Shapes.Write` of the rows. Going back to Rows parses with `Shapes.Read`. When that fails, it stays in Text and shows the reason. On opening, a stored expression shows as rows when it reads as rows, and as text otherwise.
- [ ] **Step 6: tests.** Posting rows gives the string `Shapes.Write` gives. A stored `||` expression opens as text with its reason. Switching to text and back with no edit gives the same rows. A bad text is shown with its error at its character and is not saved.
- [ ] **Step 7:** `./check.sh`; commit "Add the expression widget: rows for the common shapes, text for the rest".

### Task 8: the storylet form

**Files:** `src/Web/Forms/StoryletKinds.cs` (new: `StoryletKind`, and the parts it will share with `VignetteKind`), `src/Web/Components/Content/StoryletForm.razor` (new), `CastSlots.razor` (new), `OptionTabs.razor` (new, on the pattern of `ApproachTabs.razor`), `StoryletEffectRows.razor` (new, beside `EffectRows.razor`), `src/Web/Forms/KindForm.cs` (registered), `src/Web/Components/Content/ItemMain.razor`, `tests/Web.Tests/ContentStoryletFormsTests.cs` (new), `tests/browser/edit.sh`.

- [ ] **Step 1: definitions** (design 4.3, item 1). Id, name, stakes (a stepper from 1 to 5), `requires` (the widget, with every slot in scope), the cast (up to three slots, each a name and the widget with the slots before it in scope, with add and remove), cooldowns (cast and global, steppers, blank for none), the script (a box with chips), and notes ("Author's notes", multi-line, at the foot).
- [ ] **Step 2: option tabs.** One tab per option, in order, with add, remove, and move. Each tab holds the id and label, `requires` (the widget), **Settled by** ("Its effects" or "A check"), the words, and **Chronicle** (design 4.3, item 2). A new option's id is made from its label as it is typed, in lower case, with the articles dropped ("Hold a service" becomes `hold_service`). It can be edited until it is saved, and then it locks.
- [ ] **Step 3: effect rows** (design 4.4). The selects for what, amount, on, and toward follow from the trait: whether it is a ship, crew, or pair trait, and whether it has level names. Rows write exactly the flow maps the loader reads. A check has a skill select, a difficulty stepper, and Success and Failure panels, each with effect rows. `handler` is offered as a target only inside them.
- [ ] **Step 4: chips** (design 4.3). Script speakers (`captain:`, each slot, and `best SKILL_ID:` for each skill), tokens (each slot as `#name#`, and `#handler#` inside a check's outcomes only), and the pack's symbols, inserted at the caret.
- [ ] **Step 5: save.** Through the editor's usual save. A loader error goes back to its field, as on the encounter form. An error inside an expression goes to that widget, at its character.
- [ ] **Step 6: tests.** Each storylet in the game's pack opens, and saving it with no change leaves its file byte-for-byte the same. A new storylet with one slot from rows and one effects option saves, appears on the list, and loads. A check option with success and failure effects saves. Removing and moving options keeps their ids. A saved option's id cannot be changed.
- [ ] **Step 7: browser.** In `tests/browser/edit.sh`, write a storylet with one slot from rows and one option, save, and see it on the list. Then switch a slot to text and back.
- [ ] **Step 8:** `./check.sh`, including the browser tests; commit "Add the storylet form".

### Task 9: What plays here, on a test ship

**Files:** `src/Web/Forms/Plays.cs` (generalised, so a storylet can be an item), `src/Web/Components/Content/PlaysPanel.razor`, `PlaysSample.razor`, `PlaysPreviews.razor`, `PlaysPreviewBox.razor`, `src/Web/Components/Content/ShipSelect.razor` (new), `src/Web/Endpoints/ContentFormEndpoints.cs` (the plays route), `tests/Web.Tests/ContentStoryletFormsTests.cs`.

- [ ] **Step 1: generalise, don't copy.** Find where `Plays` assumes a mission beat (`Item`, `Group`, `Slot`, and the `Encounter or Objective` guard), and make that a case of the part, not its premise. Encounters and objectives must render exactly as before. Their existing tests are the guard.
- [ ] **Step 2: the ship select.** It sits above What plays here. USS Quality Assurance is the default, and the choice is kept in the URL (`?ship=`), so it survives a reload. A pack with no test ships shows the starting crew with "Preview anyway" on, and says so.
- [ ] **Step 3: on every change.** The plays route builds the candidate file set with the form's item in it, validates it, and runs `Preview.Storylet` on a `TestShipSource`. If the candidate does not load, the panel says what the loader said and keeps the last good preview, greyed.
- [ ] **Step 4: what shows** (design 4.7). The top line ("Requires holds · cast cooldown clear", or "Requires fails: `chronicle.count('death', 120) >= 2` is 1"), then the script, each option's words or its success and failure words, and the chronicle line each would write. A blank box shows what would play there, greyed. **Next sample** moves on. When no one fits, each slot says why, and **Preview anyway** fills the slots in order, with the previews marked unconditioned. An option that is not offered is marked "Not offered with this cast" on its tab and in the panel.
- [ ] **Step 5: rendering.** The previews are rendered by `Sim.Narrative` as play renders them, with the cast's names and pronouns. Typing posts to the plays route, which patches only the previews and the sample control, as on encounters.
- [ ] **Step 6: tests.** The plays route casts on the chosen ship, and the panel names the cast. A slot that no one fits names its clause. "Preview anyway" is marked. A different `?ship=` gives a different cast. The encounter and objective panels are unchanged.
- [ ] **Step 7:** `./check.sh`; commit "Show what a storylet plays as, cast for real on a test ship".

### Task 10: the lint on the storylets list

**Files:** `src/Web/Forms/StoryletLint.cs` (new, beside `LineLint.cs`), `src/Web/Components/Content/KindPage.razor`, `tests/Web.Tests/StoryletLintTests.cs` (new).

- [ ] **Step 1: the checks** (design 4.10). An item that cannot be cast on any test ship. A trait that is read but never set by anything in the pack (reuse `TextFileFlow.TraitWarnings`'s reckoning; don't write a second one). `#handler#` outside a check's outcome. A slot used in the words but not cast (a fault). A slot cast but never used in the words (advice). An option whose `requires` holds for no cast on any test ship, tried over a bounded number of samples per ship. Say the bound in the message.
- [ ] **Step 2: shown.** Above the list, as on the Lines page. Each finding names the item and links to its form. Faults and advice look different.
- [ ] **Step 3: tests.** The game's pack has no faults. A fixture item for each check gives its finding.
- [ ] **Step 4:** `./check.sh`; commit "Lint the storylets list".

### Task 11: the vignette form

**Files:** `src/Web/Forms/StoryletKinds.cs` (`VignetteKind`), `src/Web/Components/Content/VignetteForm.razor` (new), the plays part, `StoryletLint.cs`, `tests/Web.Tests/ContentStoryletFormsTests.cs`.

- [ ] **Step 1: the form** (design 4.8). The storylet form's parts, with no stakes and no chronicle. Options are optional: a vignette with none has effect rows on itself. A vignette with options has a `default` select ("If left: Let them rest").
- [ ] **Step 2: previews and lint.** `Preview.Vignette` on the ship. No chronicle rows. The lint's checks run on the vignettes list too.
- [ ] **Step 3: tests.** Each vignette in the game's pack opens and saves byte-for-byte unchanged. A vignette with no options saves its own effects. Removing the option a `default` names is refused with a message on the default.
- [ ] **Step 4:** `./check.sh`; commit "Add the vignette form".

### Task 12: the trait form, and where it's used

**Files:** `src/Web/Forms/TraitKind.cs` (new), `src/Web/Forms/TraitUses.cs` (new), `src/Web/Components/Content/TraitForm.razor` (new), `TraitUsesPanel.razor` (new), `tests/Web.Tests/ContentTraitFormsTests.cs` (new).

- [ ] **Step 1: the form** (design 4.9). Every key the loader accepts: id, name, On, Shown, levels as rows (number and name) or a max (the form offers one or the other), Every, Expires, Briefing, the three lists of launch rules as rule rows, On a mission, When the target dies (pairs only), and notes.
- [ ] **Step 2: where it's used.** Every storylet, vignette, encounter, and test ship that reads or changes the trait, grouped by kind. Each one links to its form (a test ship links to its text) and says what it does ("reads: `subject.trait.shaken >= 1`", "sets 1 on subject"). Reads come from `Casting.Reads` and the encounter requirements. Changes come from effects and rules. Above the list, a warning when the trait is read but never set, or set but never read and has no rules of its own. The traits list's "Used by" column (Task 6) counts these.
- [ ] **Step 3: tests.** Each trait in the game's pack opens and saves byte-for-byte unchanged. `grieving` shows the storylet that sets it, and warns that nothing reads it (until Task 15). Changing a level's name that a storylet reads is refused by the loader at the storylet.
- [ ] **Step 4: browser.** Open a trait, add a level, save, and see it offered in a storylet's row.
- [ ] **Step 5:** `./check.sh`, including the browser tests; commit "Add the trait form, with where each trait is used".

### Task 13: a new trait condition on an approach

**Files:** `src/Web/Forms/MissionKinds.cs` (`EncounterKind`), `src/Web/Components/Content/ApproachTable.razor` or `ApproachTabs.razor`, `tests/Web.Tests/ContentMissionFormsTests.cs`.

- [ ] **Step 1:** An approach's condition select gains "Trait": a trait (from those defined), at least and at most (by level name where the trait has names), and on. It writes the `RequirementModel` the loader reads today.
- [ ] **Step 2: tests.** A new trait condition saves and loads, and an existing one opens and saves unchanged.
- [ ] **Step 3:** `./check.sh`; commit "Let the encounter form write a new trait condition".

### Task 14: Save as test ship

**Files:** `src/Web/Endpoints/DevPlayEndpoints.cs`, `src/Web/Components/DevPanel.razor`, `src/Web/Forms/TestShipCapture.cs` (new), `tests/Web.Tests/DevPlayTests.cs`.

- [ ] **Step 1: what is captured.** The minimum is what `IStateSource` reads (design 7, third item). For each crew member: morale, loyalty, skills where they differ from the starting file, fit, aboard, and alive. Also the traits held, history within the longest window any expression in the pack reads, chronicle entries, and storylet arrivals, all as days and cycles ago from the legacy's today. Read it through the same views `StateSource` reads. Leave morale history and days to heal out unless something reads them, and say so in "As built".
- [ ] **Step 2: the save.** A button in Dev Play appends a ship named "<legacy> on <date>", with an id made from the name, through the editor's usual save (whole pack built, the loader run, then one file written). The panel says where it went, and links to the file.
- [ ] **Step 3: tests.** A captured ship's `TestShipSource` answers the same as the legacy's `StateSource` for every crew member, trait, and history kind. Capturing writes nothing to the legacy's store.
- [ ] **Step 4:** `./check.sh`; commit "Save a legacy's state as a test ship from Dev Play".

### Task 15: the worked example, and the record

**Files:** `content/testships/base.yaml` (a grieving crew member), `content/storylets/base.yaml` (written by the form only), the GDD chapters in design section 6, `docs/conventions.md`, `GLOSSARY.md`, `docs/gdd/11-roadmap.md`, `docs/plans/phase-1-outline.md`, `docs/gdd.md`, and the design's "As built".

- [ ] **Step 1: the ship.** Give USS Quality Assurance a crew member with `grieving` at 1, a few days ago. Task 4's test still passes.
- [ ] **Step 2: the storylet, in the browser** (design 4.11). Run the app and drive the editor in a browser session. Tap **New storylet** and write it only through the form: a slot `subject` with the row "subject · trait grieving · is at least · 1", a script, and options whose effects clear `grieving` or let it run its four weeks. Use "What plays here" and "Next sample" on the way. Save. The words are generated fiction under the author's standing allowance for slice 1e (GDD section 15, item 37), for the author to rewrite. Note every awkward moment as it happens.
- [ ] **Step 3: it arrives.** In Dev Play, on a legacy where someone is grieving (set the trait with Dev Play's control if no seed gets there), the storylet arrives. A web test plays the same from the saved file. If the golden event hash or golden chronicle moves, it moves for the new storylet alone. Record the before and after in the commit.
- [ ] **Step 4: the GDD.** Section 7.5's editor paragraph gets the new kinds, the expression widget, test ships, and notes. Bump the status line and name the slice. Rebuild and republish the GDD page (`tools/gdd-page/README.md`).
- [ ] **Step 5: conventions and glossary.** `docs/conventions.md` says where `notes` goes and how it's worded, and how test ship ids are made. `GLOSSARY.md` gains **Test ship**, **Notes**, and **Row** (of an expression).
- [ ] **Step 6: progress.** The roadmap's row and the outline's paragraph say "Done", with the date and a pointer to "As built". The next row becomes "Next".
- [ ] **Step 7: As built.** The design's section 8, task by task: what changed and why, including Task 2's choice for lines and assignments, any pack expressions respelled in Task 3, and the bound in Task 10's lint. Then the awkward moments from Step 2, and what's left for the author to judge (the example's words, and whether one test ship is enough).
- [ ] **Step 8:** `./check.sh`; commit "Write a storylet for the grieving in the editor, and record part six".
