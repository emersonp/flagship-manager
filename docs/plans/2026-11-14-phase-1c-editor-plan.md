# Slice 1c-editor: the content editor — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: use superpowers:subagent-driven-development to implement this plan task by task.

**Goal:** the author edits the game's content in the browser, in developer mode: names for everything, and the numbers and structure of skills, gear, starting crew, assignments, objective templates, and encounters; saved to the same YAML files, validated by the loader, and live in the running game without a restart.

**Architecture:** content records gain an optional display name. `Sim.Content` gains a writer and an edit API over an in-memory set of files (the loader already validates such a set). The web app holds the live pack in a swappable holder and adds developer-mode pages under `/dev/content` built from the existing design system. A save builds the candidate file set, validates it with the loader, and only then writes one file and swaps the pack.

**Tech stack:** C# / .NET 10, YamlDotNet (already used by the loader), ASP.NET Core minimal APIs with server-rendered Razor components and Datastar 1.0.5 (as the game), xUnit.

**Spec:** `docs/plans/2026-11-14-phase-1c-editor-design.md`. **Design system:** `docs/design/bridge-design.md` and the existing `src/Web/wwwroot/css`. **Datastar reference:** `docs/research/datastar-1.0.5.md`.

## Global Constraints

- Every C# `if`, `else`, `for`, `foreach`, and `while` body is in braces on its own lines, one-liners included.
- Comments are terse and say why, not what. Each test has a one-line intent comment or a name that says it.
- No creative content: placeholders only (`CREW_01`, `SKILL_A`, `NAME_PLACEHOLDER`), in the repository's `content/`, in tests, and in examples. Never invent names, species, or story text. Interface words are plain, in sentence case, with the Oxford comma. Commit messages are plain: no attribution of any kind.
- No rule changes. `src/Sim.Core`, `src/Sim.Crew`, `src/Sim.Cycle`, and `src/Sim.Missions` change only where a task says so (Task 1 adds `Name` to content records). Neither golden hash moves. The parity tests pass unchanged.
- `src/Sim.*` stays pure (the banned-API analyser applies): `Sim.Content` may read and write text handed to it, but file access for the editor lives in `Web` or in a clearly separate class in `Sim.Content` that is not on the banned list today (the loader's `LoadDirectory` already reads files; follow it).
- The repository's `content/` is never written by a test or by the gate. Tests that save use a scratch copy.
- Editor routes exist only when `Web:DeveloperMode` is true. A file path is never taken from a request: it comes from the pack's index of which file holds which item.
- A save is all or nothing: validate the whole candidate pack with the loader before writing; write atomically (a temporary file in the same folder, then a rename); swap the live pack only after the write.
- Requests take one snapshot of the live pack and use it throughout. A `LegacyState` is never shared between requests (as today).
- The editor's pages use the existing tokens and component recipes. Every colour and font goes through a token; `:focus-visible` on every control; controls 32 to 40 pixels tall with a 44 pixel target; tables before cards; works at 400 pixels wide with no horizontal page scroll.
- Datastar 1.0.5 syntax; local signals start with an underscore; no Pro-only attributes. Every action button is disabled while its request is in flight, as in the game.
- The gate is `./check.sh`; it must exit 0 with no warnings at the end of every task. `tests/browser/play.sh` must still pass at the end of Tasks 1, 3, and 6.
- Interface code in this plan is the contract; write the bodies test-first and see each new test fail before the code that passes it.

## File map

| Path | Holds |
|---|---|
| `src/Sim.Core/Content/*.cs` | Task 1: `Name` on content records |
| `src/Sim.Content/ContentLoader*.cs` | Task 1: reading `name` |
| `src/Web/Bridge/*`, `src/Web/Components/*` | Task 1: names on the screens and in the log |
| `src/Sim.Content/ContentFiles.cs`, `ContentWriter.cs`, `ContentEdits.cs`, `ContentModels.cs` | Task 2: the file set, the writer, and the edit API |
| `src/Web/ContentHost.cs`, `src/Web/ContentEditor.cs`, `src/Web/Program.cs`, `GameServices` | Task 3: the swappable pack, the save pipeline, and developer mode |
| `src/Web/Endpoints/ContentEndpoints.cs`, `src/Web/Components/Content/*.razor` | Tasks 3 to 5: the pages and forms |
| `tests/Sim.Tests`, `tests/Web.Tests` | Each task |
| `tests/browser/edit.sh`, `check.sh`, `run.sh`, docs | Task 6 |

---

### Task 1: Display names

**Files:** modify the content records in `src/Sim.Core/Content`, the loader, `content/*` (add no names: the placeholders stay as ids), `src/Web/Bridge/*`, the components, and their tests.

**Produces:** `string? Name` as the last member of `SkillDef`, `CrewDef`, `GearDef`, `ObjectiveDef`, `EncounterDef`, `DecisionDef`, and `AssignmentDef` (default `null`), and in the web app:

```csharp
// Display names for one pack: the name when set, else the id. Unknown ids come back unchanged.
public sealed class Names(ContentPack pack)
{
    public string Skill(string id);
    public string Crew(string id);
    public string Gear(string id);
    public string Objective(string templateId);
    public string Encounter(string id);
    public string Decision(string id);
    public string Assignment(string id);
}
```

**Rules:**
1. `name` is optional in every list file. When present it is trimmed and must be 1 to 80 characters with no line break; otherwise a loader error naming the file and the id.
2. Names are for display only. Ids stay in events, module state, form field values, element ids, and references. No golden hash moves; no stored format changes.
3. Every place a screen or a log line shows a skill, crew member, gear item, objective template, encounter, decision, or assignment id now shows its name through `Names` (the read model takes the pack already): the roster, the briefing, the orders table, the route, the call, the aftermath, the legacies list's crew count (unchanged), and every beat line. Where an id is shown as an id on purpose (the objective's own id column), it stays.
4. Beat lines keep their shapes; only the ids inside them are replaced by names. A name is plain text: HTML-encode it wherever it is rendered, and the figure-wrapping must not treat digits inside a name as figures unless they stand alone as a number (a name like `NAME 7` is the author's problem only if it corrupts markup, which it must not).
5. The crew module's roster in a running legacy shows the pack's current name for the crew id (names are read from the pack at render time, not stored in the legacy).

**Tests:** the loader accepts a name, trims it, and refuses an empty, an over-long, and a multi-line one, for each of the seven kinds; with names set in a test pack, each screen's model and every beat line show the names, and with none set they show ids exactly as before (the existing tests pass unchanged); a name containing `<b>` and `&` is encoded in the page and the stream; events and both golden hashes are unchanged; the parity tests pass.

- [ ] Write the failing tests. See them fail. Implement. `./check.sh`, then `tests/browser/play.sh`. Commit: "Give everything authored a display name".

---

### Task 2: Reading, changing, and writing content files

**Files:** create `src/Sim.Content/ContentFiles.cs`, `ContentModels.cs`, `ContentEdits.cs`, and `ContentWriter.cs`; tests in `tests/Sim.Tests/ContentEditsTests.cs` and `ContentWriterTests.cs`.

**Produces:**

```csharp
// The text of every content file, by path relative to the content root ("crew/starting.yaml").
public sealed record ContentFileSet(IReadOnlyDictionary<string, string> Files)
{
    public static ContentFileSet ReadDirectory(string root);
    public ContentLoadResult Validate();                       // ContentLoader.Load(Files)
    public IReadOnlyList<ContentItemRef> Index();             // every item of every kind, with the file that holds it
}

public enum ContentKind { Skill, Gear, Crew, Assignment, Objective, Encounter }
public sealed record ContentItemRef(ContentKind Kind, string Id, string? Name, string File);

// Plain, mutable-by-copy models the forms bind to; one per kind. They mirror the YAML, not the validated records.
public sealed record SkillModel(string Id, string? Name);
public sealed record GearModel(string Id, string? Name, string Tag);
public sealed record CrewModel(string Id, string? Name, int Seniority, IReadOnlyDictionary<string, int> Skills, int Caution, int Discipline);
public sealed record AssignmentModel(string Id, string? Name, IReadOnlyList<string> ObjectiveTags, IReadOnlyDictionary<int, int> Offer, int LoadoutLimit);
public sealed record ObjectiveModel(string Id, string? Name, IReadOnlyList<string> Tags, int TeamSizeMin, int TeamSizeMax, int Need, int? UnattendedDifficulty, IReadOnlyList<StageModel> Stages);
public sealed record StageModel(string Slot, IReadOnlyList<string> DrawTags, int CountMin, int CountMax, bool Skippable);
public sealed record EncounterModel(string Id, string? Name, IReadOnlyList<string> Tags, string? Chain, ApproachModel Careful, ApproachModel Fast, DecisionModel? Decision);
public sealed record ApproachModel(string Skill, int Difficulty, string? GearTag);
public sealed record DecisionModel(string Id, string? Name, int Stakes, IReadOnlyList<EffectModel> Bold, IReadOnlyList<EffectModel> Safe);
public sealed record EffectModel(int? Progress, int? Momentum, int? Strain, string? InjuryRoll);

public static class ContentEdits
{
    public static T? Get<T>(ContentFileSet files, ContentKind kind, string id) where T : class;
    // Replaces the item with the model's id in the file that holds it, or adds it to the kind's first file (creating the conventional file if the kind has none).
    public static ContentFileSet Upsert(ContentFileSet files, ContentKind kind, object model);
    public static ContentFileSet Delete(ContentFileSet files, ContentKind kind, string id);
    public static ContentFileSet ReplaceText(ContentFileSet files, string path, string text);   // tuning files and pack.yaml
    // Writes only the files that differ from before, each through a temporary file and a rename; deletes nothing.
    public static void Write(string root, ContentFileSet before, ContentFileSet after);
}
```

Read the loader's own YAML types and the files in `content/` before settling the models: where the YAML has a shape this plan missed (a field, an effect key, a count written as a number or a pair), follow the YAML and say so in the report. Encounters in the repository also have per-tier effects if the YAML shows them; model whatever the files hold, completely, so that nothing is lost on a save.

**Rules:**
1. **Nothing is lost.** For every file in the repository's `content/`: reading it, upserting each of its items unchanged, and validating gives a pack equal to the original (compare the two packs as JSON through `SimJson.Options`). An item the models cannot represent fully is a test failure, not a silent drop.
2. **Canonical layout.** The writer emits a list file as a leading comment block (kept verbatim from the file as read, if it had one), then one block-style item per entry with keys in a fixed order (`id`, `name`, then the kind's fields in the order of section 2 of the design), short lists and maps in flow style as the repository's files have them (`tags: [field, hazard]`, `skills: { SKILL_A: 6 }`), no trailing spaces, and one newline at the end. A `name` that is null is omitted. Writing an unchanged file set twice gives identical bytes; rewriting the repository's files with the writer and loading them gives the same pack.
3. **Strings are written safely.** A name or id that YAML would misread (a leading `-`, `:`, `#`, quotes, `yes`, `no`, `null`, a number, or leading or trailing spaces) is quoted so it reads back as the same string. Test with a list of awkward names.
4. **Only the edited file changes.** `Upsert` and `Delete` return a set in which only the one file's text differs; other files are the same string instances.
5. **`Write` is atomic per file** and writes nothing outside `root`: a path in the set that is rooted, contains `..`, or does not end in `.yaml` throws `ArgumentException` before anything is written. If a rename fails the old file is intact.
6. `Index` reads items leniently enough to list a file whose pack is invalid (the editor must be able to open a broken pack to fix it): an item with no id is skipped; a file that does not parse contributes no items and is reported by `Validate`.
7. Ids are case-sensitive and unique across the pack (the loader's rule); `Upsert` of a new id that exists in another kind is left to `Validate` to refuse.

**Tests:** rule 1 for every file; the layout rules on small hand-written files, byte for byte; awkward strings; only the edited file changes; add to an empty kind; delete; `ReplaceText`; `Write` to a temporary directory (changed files only; atomic; the path guards); `Index` on a broken file; `Get` on a missing id is null.

- [ ] Write the failing tests. See them fail. Implement. `./check.sh`. Commit: "Read, change, and write content files without losing anything".

---

### Task 3: The live pack, the save pipeline, and the editor's shell

**Files:** create `src/Web/ContentHost.cs` and `ContentEditor.cs`; modify `GameServices`, `Program.cs`, `WebSettings`, and every endpoint and component that reads the pack or the game; create `src/Web/Endpoints/ContentEndpoints.cs` and `src/Web/Components/Content/ContentPage.razor`, `KindPage.razor`, and `ContentShell.razor`; tests in `tests/Web.Tests/ContentHostTests.cs` and `ContentPagesTests.cs`.

**Produces:**

```csharp
public sealed class WebSettings { /* existing */ public bool DeveloperMode { get; set; } }

// The pack in play and the game built from it. Swapped whole; readers take Current once per request.
public sealed class ContentHost
{
    public LivePack Current { get; }
    public void Swap(ContentPack pack);
}
public sealed record LivePack(ContentPack Pack, Game Game, Names Names, int Generation);

public sealed class ContentEditor
{
    // One save at a time. Builds the candidate, validates it, and only then writes and swaps.
    public Task<SaveResult> Save(Func<ContentFileSet, ContentFileSet> change, CancellationToken cancellation = default);
    public ContentFileSet Read();                    // the files on disk now
}
public sealed record SaveResult(bool Saved, IReadOnlyList<ContentError> Errors);
```

**Rules:**
1. `GameServices` no longer hands out a fixed pack and game: endpoints and the session take `ContentHost.Current` once at the start of a request and use that snapshot for the load, the commands, the read model, and the render. A swap during a request does not change what that request sees. The event registry and module types come from the snapshot's game.
2. Start-up still fails with the loader's errors if the pack on disk is invalid and developer mode is off. In developer mode the app starts with the last valid pack it can build; if there is none, it starts with the editor only: game routes answer a page saying the content pack is invalid, with a link to the editor.
3. `ContentEditor.Save`: under one lock, read the files from disk, apply `change`, validate; on errors return them and write nothing; otherwise `ContentEdits.Write`, then `ContentHost.Swap` with the validated pack. If the write throws, nothing is swapped.
4. Developer mode: `Web:DeveloperMode` (default false). When false, no route under `/dev` is mapped (404). `run.sh` sets `Web__DeveloperMode=true`; `check.sh` and the tests set it explicitly.
5. Pages (server-rendered, the game's layout and stylesheet):
   - `GET /dev/content`: the pack's id and version; "valid" or the loader's errors as a list; one well per kind with a table of its items (name, id, and file) from `ContentFileSet.Index()`, a count, and a link to the kind's page; links to the two tuning files and the pack file; the number of legacies in the store with the sentence "Edits apply to games in progress."; a link back to the legacies.
   - `GET /dev/content/{kind}` for each `ContentKind` (lower-case plural path segments: `skills`, `gear`, `crew`, `assignments`, `objectives`, and `encounters`): the kind's items in a table with its at-a-glance fields, each linking to its form route (the forms come in Tasks 4 and 5; until then the link target answers 404), and a "New" link.
   - The game's top bar and the legacies page show a "Content" link in developer mode only.
6. A request for an unknown kind is 404. Names and ids are HTML-encoded everywhere.
7. The editor's pages have their own small layout component (`ContentShell`) with the page title, the "Legacies" and "Content" links, and a slot for the alert; they reuse the stylesheet's wells, tables, buttons, and type.

**Tests:** a request that takes its snapshot before a swap renders with the old pack (drive `ContentHost` directly, and through the app with a test hook that swaps between the load and the render); a legacy waiting on a call still answers after a swap that renames a crew member, and its page shows the new name; a new legacy after a swap that changes a starting crew member's skill level starts with the new level; `Save` with a valid change writes exactly one file in a scratch content copy and swaps; with an invalid change writes nothing, returns the loader's errors, and the live pack is unchanged; two concurrent saves apply in turn (both land; neither is lost); a failing write (make the target folder read-only, or inject a writer that throws) swaps nothing; developer mode off gives 404 for `/dev/content` and no "Content" link; on, the pages list the repository's kinds and items; the invalid-pack start in developer mode.

- [ ] Write the failing tests. See them fail. Implement. `./check.sh`, then `tests/browser/play.sh`. Commit: "Hold the pack so it can be swapped, and add the content editor's shell".

---

### Task 4: Forms for skills, gear, starting crew, and assignments

**Files:** create `src/Web/Components/Content/SkillForm.razor`, `GearForm.razor`, `CrewForm.razor`, `AssignmentForm.razor`, and shared pieces (`FormShell.razor`, `Field.razor`, `DeleteControl.razor`); extend `ContentEndpoints.cs`; tests in `tests/Web.Tests/ContentFormsTests.cs`.

**Routes** (each kind `k` in `skills`, `gear`, `crew`, and `assignments`): `GET /dev/content/k/new`; `GET /dev/content/k/{id}`; `POST /dev/content/k` (create); `POST /dev/content/k/{id}` (save); `POST /dev/content/k/{id}/delete`.

**Rules:**
1. A form is a real `<form>` posted with Datastar (`contentType: 'form'`), as the briefing is. The answer to a post is a stream: on success, a patch of the form's region with the saved values and a notice ("Saved."), or for a create or delete a redirect by script to the item's page or the kind's list (follow the reference's way of navigating from a stream; if it has none that is safe, answer the patched list or form instead and say which you chose); on refusal, a patch of the form's alert with the reasons, the form keeping what was typed.
2. The id is typed only when creating: it must match the loader's id rule (read it from the loader; show the rule's words beside the field); on an existing item it is shown, not editable, and the post's id comes from the route, never the form.
3. Field checks the form does itself, shown beside the field (`aria-describedby`, the harm colour, and words): required fields; whole numbers in range (take each range from the loader or the tuning where one exists: a crew skill level is 0 to the crew tuning's maximum; caution and discipline are in the range the loader accepts; a loadout limit is 0 or more; offer keys and weights are 1 or more). Everything else is the loader's: its errors for the candidate pack are listed in the form's alert in its own words.
4. References are chosen from what exists: a gear item's tag from the tags the loader knows (read where the loader gets them); a crew member's level for each skill in the pack (a new skill appears as a field at level 0); an assignment's objective tags from the tags used by objective templates (checkboxes), and its offer as rows of "number of objectives" and "weight" with "Add" and "Remove" (these re-render the form from the posted fields without saving).
5. Delete asks for confirmation in the page (a second button revealed by the first; no dialog). A delete the loader refuses (a skill still used) shows the loader's reasons and deletes nothing.
6. Every button is disabled while its request is in flight. Every save goes through `ContentEditor.Save`.
7. The crew form says, in one sentence under its heading, that crew will be generated from sources later and that this form only names the starting eight. It offers no "New" beyond what the loader allows, and no bulk tools.

**Tests** (the real app on a scratch copy of `content/`, developer mode on): for each of the four kinds: the list, the form's values, a save that changes the name and a number (the file on disk changes and the live pack shows it), a create, a delete, a refused delete, each field check, a loader refusal shown in the alert with nothing written, the id not editable (a posted `id` field is ignored), unknown id 404, and HTML in a name shown as text; the offer rows' add and remove; the repository's own `content/` is byte-identical after the whole test run (assert it).

- [ ] Write the failing tests. See them fail. Implement. Check each form in a real browser. `./check.sh`. Commit: "Edit skills, gear, starting crew, and assignments in the browser".

---

### Task 5: Forms for objective templates and encounters

**Files:** create `src/Web/Components/Content/ObjectiveForm.razor`, `EncounterForm.razor`, `StageRows.razor`, and `EffectRows.razor`; extend `ContentEndpoints.cs`; tests in `tests/Web.Tests/ContentFormsTests.cs` (or a second file).

Same routes and rules as Task 4 for `objectives` and `encounters`, plus:

1. **Objective template:** name, tags (free text tags are allowed here: a comma-separated field, trimmed, lower-case as the loader requires; show the tags already in use as suggestions the author can click to add), team size minimum and maximum, need, unattended difficulty (blank means none), and the stages as rows in order: slot (text), draw tags (chosen from the tags encounters use), count minimum and maximum, and skippable; "Add stage", "Remove", and "Move up" and "Move down" on each row. The form shows, beside each stage, how many encounters in the pack match its draw tags (computed from the pack; zero is shown in the caution colour with the words "no encounters match").
2. **Encounter:** name, tags (as above), chain target (a select of encounters, or none), the careful and fast approaches (skill select, difficulty, and gear tag select or none), whether it has a decision, and if so the decision's id (typed when new, fixed afterwards), name, stakes, and the bold and safe effects as rows: an effect is one of progress, momentum, or strain with a whole number, or an injury roll of light or severe; "Add effect" and "Remove" on each list. Model exactly what the YAML holds (Task 2's models); if encounters carry more than this, the form carries it too.
3. Row changes ("Add", "Remove", and "Move") post the form to a `…/rows` route that re-renders the form from the posted fields and saves nothing; field names are indexed so the posted order is the order saved.
4. An encounter that is a chain target cannot be deleted while something chains to it (the loader's refusal, shown).
5. Where the loader's rules relate two fields (fast is authored one harder than careful is a convention, not a rule: do not enforce it; team size minimum not above maximum is a rule: check it beside the field).

**Tests:** as Task 4 for both kinds, plus: stage rows add, remove, and move, and the saved file has them in the shown order; the match count beside a stage; effect rows; adding and removing a decision; every field of the repository's objectives and encounters survives opening the form and saving unchanged (the file's parsed content is identical); the repository's `content/` is byte-identical after the run.

- [ ] Write the failing tests. See them fail. Implement. Check both forms in a real browser at 1440 and 400 wide. `./check.sh`. Commit: "Edit objective templates and encounters in the browser".

---

### Task 6: Tuning and pack files, the gate, and the record

**Files:** create `src/Web/Components/Content/TextFileForm.razor`; extend `ContentEndpoints.cs`; create `tests/browser/edit.sh`; modify `check.sh`, `run.sh`, `README.md`; docs by the controller.

1. `GET` and `POST /dev/content/files/{name}` for exactly three names: `missions` (`tuning/missions.yaml`), `crew-tuning` (`tuning/crew.yaml`), and `pack` (`pack.yaml`). A text area with the file's text, "Save", and the alert. The save goes through `ContentEditor.Save` with `ReplaceText`; a refusal shows the loader's errors and writes nothing. The pack file's id cannot change (refuse a save that changes it, in the editor, with a reason). A note on the tuning pages says the page that edits numbers and re-runs the bench comes later, and that a mission in flight keeps the tuning it started with.
2. `check.sh`: the web smoke check runs with developer mode on and a scratch copy of `content/` (never the repository's): after creating and opening a legacy, `GET /dev/content` answers 200 and lists `CREW_01`; a `POST` that names `CREW_01` `NAME_PLACEHOLDER` answers a success, the scratch file contains the name, and the legacy's page shows it; then, with developer mode off on a second start (or by a test in `Web.Tests` if a second start costs too much time: say which), `/dev/content` is 404. It still prints one line: `Web: legacy created and opened, and content edited. OK.`
3. `tests/browser/edit.sh` (outside the gate, with `playwright-cli`, a scratch content copy, and developer mode on): open the editor, rename a crew member and a skill through the forms, start a legacy and see both names in the briefing; break an encounter (choose no skill, or whatever the form permits that the loader refuses) and see the refusal with nothing written; add a stage to an objective, save, and see it; no console errors. It prints `Browser: content edited and refused. OK.`
4. `run.sh` turns developer mode on; the README gains a short "Edit the content" section.
5. Run the design linter on the editor's pages (`sloplint --scope app --genre atmospheric --render` on the content index, a kind's list, the crew form, and the encounter form) and fix every FAIL; take screenshots of those four at 1440 by 900 and the encounter form at 400 wide into `docs/screens/1c-editor/`.

- [ ] Implement and test. `./check.sh`, `tests/browser/play.sh`, and `tests/browser/edit.sh`. Commit: "Edit tuning and pack files, check the editor in the gate, and add its browser run".

---

## Done when

- `./check.sh` exits 0 on a clean clone and never writes to the repository's `content/`.
- In developer mode the author can rename and change every kind of content in the browser, and the running game shows it without a restart.
- An edit the loader refuses is shown with its reasons and writes nothing.
- With developer mode off the editor does not exist.

## Deliberately not in this slice

Species, cultures, life paths, and name generators; report and storylet text; the tuning page with the bench; undo beyond git; anything for hosting.
