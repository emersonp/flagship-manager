# Slice 1e, part five (the chronicle): implementation plan

> **For agentic workers:** steps use checkbox (`- [ ]`) syntax for tracking. Do the tasks in order; each ends with `./check.sh` and a commit. Keep the design's section 5 (architecture) current as you go: when the build differs, change section 5 in the same commit, and say so in a new "As built" section (section 8) of the design.

**Goal:** a new module, `Sim.Chronicle`, writes one dated entry, with frozen text, for the legacy's founding, each death, each call answered, each captain storylet answered, and each mission's end. Entries are weighted from a tuning file, overridable on a storylet, an option, or a decision. The player reads them on a Chronicle page from the top bar (newest first under full dates, All or Notable). Storylets read them with `chronicle.count(kind, days)` and `x.chronicle.count(kind, days)`. The worked example, "The names on the wall", arrives after a second death within 120 days.

**Architecture:** `Sim.Chronicle` depends on `Sim.Core` only. It listens on the bus after every other module, builds a `Situation` for each entry, and asks an `INarrator` (an interface in `Sim.Core.Narrative`, implemented in `Sim.Narrative` and passed in by `GameHost`) for the text, which it stores and never renders again. The facts it needs that live inside `Sim.Missions` today travel on Core contracts: a new `CallAnswered` event, and `CrewDied` with the encounter and approach that killed. It serves `IChronicleView`, which the storylets module reads for the new expressions and `Web` reads for the page.

**Spec:** `docs/plans/2026-11-20-phase-1e-part-five-design.md`. Read it with this plan. **Design system:** `docs/design/bridge-design.md`.

## Global constraints

Part one's (`2026-11-16-phase-1e-part-one-plan.md`, "Global constraints") hold, with these changes:

- `SchemaVersion` is 13 and `CurrentFormatVersion` is 11 today. Task 1 raises them to 14 and 12, once for the whole slice; later tasks that change saved state do so inside that one raise (an old store or export is refused, not migrated).
- **No randomness.** The chronicle draws from no stream. Line ties break by `StableHash` of the legacy seed, the kind, and the source sequence number, as reports do.
- **Every `Sim.*` project still references `Sim.Core` alone** (plus `Sim.Expressions` for its two users). `Sim.Chronicle` gets its words through `INarrator`; the architecture test is not loosened.
- **Text is frozen.** Nothing renders an entry's text after it is stored, not the page, not Dev Play, not the simulator.
- `content/` may gain `tuning/chronicle.yaml`, chronicle lines in `lines/base.yaml`, `chronicle:` on storylets, options, and decisions, the worked example's storylet, and the trait `grieving`, and nothing else. The fixture gains the same in placeholder form.
- No outcome figure moves. The golden event hash moves once, in Task 2, for `CallAnswered` and `CrewDied`'s new fields; note the before and after in the commit.

## Review focus

1. **Written once, never re-rendered.** An entry's text comes from the pack live at the moment of its event; a line edited afterwards changes nothing already written, across a reload and an export. (Tasks 1 and 3; the freezing test in Task 8)
2. **One event, at most one entry, in the true order.** A call answered and a death in the same encounter keep their order; a mission's deaths come before its own entry. (Tasks 2 and 3)
3. **The boundary holds.** `Sim.Chronicle` reads no `Sim.Missions` type, and no `Sim.*` project gains a reference. (Tasks 1 and 2)
4. **Days, not cycles.** `chronicle.count(kind, n)` counts entries whose day is within `n` days of today, today included, whatever the cycles were. A 35-day journey ages an entry by 35 days. (Task 5)
5. **Weight 0 is no entry.** It is not stored, not counted, and not shown. (Tasks 3 and 4)

---

### Task 1: the module, the narrator, and the founding entry

**Files:** `src/Sim.Chronicle/Sim.Chronicle.csproj` (new), `src/Sim.Chronicle/ChronicleModule.cs` (new), `src/Sim.Chronicle/ChronicleState.cs` (new), `src/Sim.Chronicle/ChronicleView.cs` (new), `src/Sim.Core/ChronicleContracts.cs` (new), `src/Sim.Core/Views.cs`, `src/Sim.Core/Narrative/INarrator.cs` (new), `src/Sim.Core/Narrative/NarrativeFacts.cs`, `src/Sim.Narrative/Narrator.cs` (new), `src/Sim.Core/Content/ContentPack.cs`, `src/Sim.Content/ContentLoader.Tuning.cs`, `src/GameHost/Game.cs`, `src/GameHost/GameHost.csproj`, the solution file, `src/Persistence/SqliteLegacyStore.cs`, `src/Persistence/LegacyExport.cs`, `content/tuning/chronicle.yaml`, `content/lines/base.yaml`, the fixture's same two, `tests/Sim.Tests/ChronicleModuleTests.cs` (new), `tests/Sim.Tests/ArchitectureTests.cs`, `tests/Persistence.Tests/`.

**Produces** (shape):

```csharp
// Sim.Core
public static class ChronicleKinds { public const string Founding = "founding", Death = "death", Mission = "mission", Call = "call", Storylet = "storylet"; }
public sealed record ChronicleSubject(string Id, string Name);
public sealed record ChronicleEntry(string Id, int Day, int Cycle, string Kind, int Weight, IReadOnlyList<ChronicleSubject> Subjects, string Text, IReadOnlyList<long> SourceSeq);
public interface IChronicleView
{
    IReadOnlyList<ChronicleEntry> Entries { get; }            // in the order written
    int Count(string kind, int days);                          // day >= today - days + 1
    int Count(string crewId, string kind, int days);
}
public sealed record ChronicleTuning(IReadOnlyDictionary<string, int> Weights, int Notable);

// Sim.Core.Narrative
public interface INarrator { string? Line(Situation situation, string key); }   // the best line, expanded to plain text; null when none holds
```

- [x] **Step 1: the project.** `Sim.Chronicle` references `Sim.Core` only. Add it to the solution, to `GameHost`, and to the event registry in `Game.Build`. Register it **last**, after the storylets module: `ChronicleModule.Register(bus, pack, new Narrator(pack))`. Before writing an entry, check that the bus lets a module read the sequence number of the event it is handling. If it doesn't, add that to the bus context in this step and say so in "As built".
- [x] **Step 2: the narrator.** `Narrator` in `Sim.Narrative` picks with `LineSelector.Select` (no `used` set) and expands with `Expander.Expand`, joining the parts as plain text. A line whose text is a script is refused by the loader for chronicle beats (Step 4), so the narrator never sees one. Tests: a line that holds is expanded; none holding gives null; the same key gives the same line.
- [x] **Step 3: tuning.** `content/tuning/chronicle.yaml` as design 4.4. The loader refuses a weight outside 0 to 5, a missing key, an unknown key, and `notable` outside 1 to 5, each naming the file and field. The editor's index shows it as a validated text file, as other tuning files are.
- [x] **Step 4: the beats.** `NarrativeFacts` gains the five chronicle beats (`chronicle_founding`, `chronicle_death`, `chronicle_mission`, `chronicle_call`, `chronicle_storylet`) with the facts of design 4.3. The loader refuses a script (a speaker line, `>`, or `=`) in a line for any of them.
- [x] **Step 5: the founding entry.** On `LegacyFounded`, write a `founding` entry: today's day and cycle, no subjects, the starting crew's size as a fact, the narrator's text or the fixed fallback "The ship took on her first crew." Write the default line in both packs (design 4.3).
- [x] **Step 6: state and persistence.** `ChronicleState` holds the entries and the counter (`chr_` and four digits). It is a module row like every other. Raise `SchemaVersion` to 14 and `CurrentFormatVersion` to 12; an old store or export is refused with the existing message. A persistence test: an entry survives commit, load, and export, text and all.
- [x] **Step 7: the view.** `IChronicleView` is served from the module's state. Tests for `Count`: the window includes today and the day `days - 1` back, and excludes the day before; it filters by subject.
- [x] **Step 8: architecture.** `ArchitectureTests` already covers `Sim.Chronicle` by its name. Run it to confirm it does.
- [x] **Step 9:** `./check.sh`; commit "Add the chronicle module, and record the founding".

### Task 2: the facts that live in missions

**Files:** `src/Sim.Core/MissionContracts.cs`, `src/Sim.Missions/MissionResolver.cs`, `src/Sim.Missions/MissionsModule.cs`, `src/Sim.Crew/CrewModule.cs`, the tests that build `CrewResult` or `CrewDied`, `tests/Sim.Tests/MissionsModuleTests.cs`, `tests/Sim.Tests/CrewModuleTests.cs`.

**Produces:**

```csharp
[EventType("CallAnswered", 1)]
public sealed record CallAnswered(string MissionId, string DecisionId, string EncounterId, string Answer, string LeadPrefers, string Standing, string LeadId) : IEvent;
// CrewResult gains: string? DiedAtEncounter, string? DiedAtApproach
[EventType("CrewDied", 2)]
public sealed record CrewDied(string CrewId, string MissionId, string? EncounterId, string? ApproachId) : IEvent;
```

- [x] **Step 1: the call.** When the missions module applies an answer (where the resolver yields its `AnswerBeat`), publish `CallAnswered` with the answer, preference, and standing as their lower-case words. Put it in the event order where the beat falls, so a death later in the same encounter is published after it. Test: answer a call that overrules the lead; the event carries `overruled`; the order with a later `CrewDied` holds.
- [x] **Step 2: the cause.** The resolver records, on the crew member's `CrewResult`, the encounter and approach of the check whose injury roll killed them. A collapse death names the encounter it collapsed in, with no approach. The crew module passes both onto `CrewDied` at version 2.
- [x] **Step 3: the golden hash.** Run the fixed seeds. The event hash moves for the new event and fields; no outcome, result count, or death count moves. Put the before and after in the commit.
- [x] **Step 4:** `./check.sh`; commit "Publish the call's answer and the cause of a death as Core contracts".

### Task 3: deaths, missions, and calls in the chronicle

**Files:** `src/Sim.Chronicle/ChronicleModule.cs`, `src/Sim.Chronicle/Situations.cs` (new), `src/Sim.Core/Content/MissionContent.cs` (`DecisionDef` gains `ChronicleWeight`), `src/Sim.Content/ContentLoader.Missions.cs`, `src/Sim.Content/ContentWriter.cs` and `ContentFaithfulness.cs` (the new key), `content/lines/base.yaml`, the fixture's lines, `tests/Sim.Tests/ChronicleModuleTests.cs`.

- [x] **Step 1: what it keeps.** From `SiteArrived` and `MissionStarted`, keep each open mission's objective, site, team, and lead until its `MissionEnded`. Keep nothing else.
- [x] **Step 2: death.** On `CrewDied`, write a `death` entry. The subject is the dead member, with the name and pronouns taken from the pack's crew definition (the roster no longer has them). The facts are those in design 4.3; cycles served is the current `Cycle`, since every crew member has been aboard since cycle 1, until crew have a join date. Weight comes from the tuning file.
- [x] **Step 3: call.** On `CallAnswered`, write a `call` entry with the lead as subject. Weight is `callOverruled` or `callBacked`, unless the decision sets `chronicle: { weight: n }`. Weight 0 writes nothing.
- [x] **Step 4: mission.** On `MissionEnded`, write a `mission` entry with the lead as subject, the facts of design 4.3, and the number of deaths from the result. Weight is `missionWon` for a success and `missionLost` otherwise. Then forget the mission.
- [x] **Step 5: lines.** Write the default lines of design 4.3 in both packs, with criteria on `standing` and `outcome` where the words differ. Fallback fixed words exist for each kind, but the game's pack never needs them.
- [x] **Step 6: tests.** A seeded mission with a death and an overruled call writes, in order, the call, the death, and the mission. A backed call weighs 2. A decision with `weight: 0` writes no call entry. The words name the encounter and the lead.
- [x] **Step 7:** `./check.sh`; commit "Record deaths, calls, and missions in the chronicle".

### Task 4: captain storylets in the chronicle

**Files:** `src/Sim.Core/Content/StoryletContent.cs` (`StoryletDef` and `StoryletOption` gain `Chronicle`), `src/Sim.Content/ContentLoader.Storylets.cs`, `ContentWriter.cs`, `ContentFaithfulness.cs`, `src/Sim.Chronicle/ChronicleModule.cs`, `src/Sim.Chronicle/Situations.cs`, the lines in both packs, `tests/Sim.Tests/StoryletContentTests.cs`, `tests/Sim.Tests/ChronicleModuleTests.cs`.

**Produces:** `public sealed record ChronicleDef(int? Weight, string? Text);` on a storylet (weight only) and on an option (weight and text).

- [x] **Step 1: content.** The loader reads `chronicle:` on a storylet and an option. It refuses `text` on a storylet, `chronicle:` on a vignette, and a weight outside 0 to 5. It checks an option's `text` for unbound symbols against the storylet's facts and cast, as the option's own text is checked.
- [x] **Step 2: the entry.** On `StoryletAnswered`, write a `storylet` entry. The subjects are the cast in slot order. The facts are the title, the option's label, and for a check option the handler and success. The weight is the option's, else the storylet's, else the tuning file's. The text is the option's `chronicle.text` expanded through the narrator's expander, else a line. `VignetteAnswered` writes nothing.
- [x] **Step 3: tests.** The option's weight wins over the storylet's. `text` is used in place of a line. A check option names its handler and result. A vignette leaves no entry.
- [x] **Step 4:** `./check.sh`; commit "Record the captain's storylets in the chronicle".

### Task 5: reading the chronicle in expressions

**Files:** `src/Sim.Expressions/Rewrite.cs`, `Whitelist.cs`, `StateView.cs`, `ExpressionBook.cs`, `TraitReading.cs`, `src/Sim.Storylets/StoryletView.cs` (its `IStateSource`), `src/Sim.Storylets/StoryletsModule.cs` (it is handed an `IChronicleView`), `src/GameHost/Game.cs`, `tests/Sim.Tests/ExpressionsTests.cs`, `tests/Sim.Tests/StoryletsModuleTests.cs`.

- [x] **Step 1: the source.** `IStateSource` gains `ChronicleCount(string kind, int days)` and `ChronicleCount(string crewId, string kind, int days)`. The storylets module reads them from `IChronicleView`. It gets the view the way it gets `ITraitView` today; if the chronicle being registered last gets in the way, say so in "As built".
- [x] **Step 2: the language.** `chronicle.count('kind', n)` at the root and `x.chronicle.count('kind', n)` on a slot. In the rewrite, check that the kind is one of `ChronicleKinds` ("No chronicle kind named deaths") and that `n` is a whole-number constant from 1, with positions, as `history.count` is checked. `chronicle` stops being refused, and `dept` is refused as "dept waits for departments (Phase 5)".
- [x] **Step 3: hidden memory.** `TraitReading` treats the chronicle reads as unknown, as it does `daysSince`, so the rule is unchanged for every existing storylet.
- [x] **Step 4: tests.** Two deaths 100 days apart pass `count('death', 120) >= 2`. A single travel of 5 weeks between them fails it. `x.chronicle.count('call', 60)` counts only entries with `x` as a subject. A bad kind and a non-constant `n` are refused with their positions.
- [x] **Step 5:** `./check.sh`; commit "Let storylets read the chronicle by kind and by crew member, over days".

### Task 6: the Chronicle page

**Files:** `src/Web/Bridge/BridgeReader.cs`, `src/Web/Bridge/BridgeView.cs` (a `ChronicleView` record), `src/Web/Bridge/BridgeRoutes.cs`, `src/Web/Endpoints/BridgeEndpoints.cs`, `src/Web/Components/ChroniclePage.razor` (new), `src/Web/Components/TopBar.razor`, `src/Web/Bridge/Dates.cs`, `src/Web/wwwroot/` (styles), `tests/Web.Tests/`, `tests/browser/` where the top bar is covered.

- [ ] **Step 1: the route.** `GET /legacy/{id}/chronicle`, with `?show=notable`. It reads without writing (the read that creates no state) and opens in any phase, a call waiting included. Leaving it goes back to the bridge, which draws the phase as it stands.
- [ ] **Step 2: the page** (design 4.7). Newest first, grouped under `Dates.Full`, ordered within a day by `sourceSeq`, newest first. Weight 4 and 5 are set apart, a memorial is marked and named in its head, and the All / Notable toggle is a pair of links. In dev mode each entry shows its kind, weight, cycle, and source sequence numbers. Follow `docs/design/bridge-design.md`, with no new component beyond the entry and the date heading.
- [ ] **Step 3: the top bar** gains a Chronicle link, placed within the design system.
- [ ] **Step 4: tests.** The page lists a fixture legacy's entries in order under their dates. Notable hides weight 1 and 2. The play-mode guard from the calendar (no week number, no `Cycle`, no bare day number) also covers this page. A reload with a call waiting returns to the call.
- [ ] **Step 5:** `./check.sh`, including the browser tests; commit "Add the Chronicle page".

### Task 7: chronicle lines in the editor, and the lint

**Files:** `src/Web/Forms/LineLint.cs`, the lines page's samples (wherever `schedule`'s samples are built), `tests/Web.Tests/`.

- [ ] **Step 1: samples.** Build one fixed sample situation per chronicle beat in code, as the `schedule` samples are, so the lines page previews chronicle lines.
- [ ] **Step 2: the lint.** A pack with no line for one of the five kinds fails, naming the kind. Neither pack fails.
- [ ] **Step 3:** `./check.sh`; commit "Preview and lint the chronicle's lines".

### Task 8: the simulator, the golden chronicle, and freezing

**Files:** `src/SimRunner/Simulation.cs`, `tests/SimRunner.Tests/SimulationTests.cs`, `tests/Sim.Tests/ChronicleModuleTests.cs` (or `GameContentTests.cs`), `tests/Persistence.Tests/`.

- [ ] **Step 1: the counts.** `SimRunner cycles` reports the entries by kind and the weight-0 entries skipped (each `weight: 0` is counted, not stored).
- [ ] **Step 2: the golden chronicle.** A fixed seed in the game's pack, run for 30 cycles, pins every entry's day, kind, weight, subjects, and text.
- [ ] **Step 3: freezing.** Write entries, commit, change every chronicle line in a scratch copy of the pack, load under it, and play on. The old entries' texts are unchanged, and new ones use the new lines.
- [ ] **Step 4:** `./check.sh`; commit "Report the chronicle in the simulator, and pin its words".

### Task 9: the worked example, and the record

**Files:** `content/storylets/base.yaml`, `content/traits/` (the trait `grieving`), the test pack's short version, the GDD chapters in design section 6, `GLOSSARY.md`, `docs/gdd/11-roadmap.md`, `docs/plans/phase-1-outline.md`, `docs/gdd.md`, and the design's "As built".

- [ ] **Step 1: the example** (design 4.8). "The names on the wall", with words written in place for the author to rewrite. If a cast slot can't prefer the member with the most losses, require `voice.history.count('lost_crewmate', 20) >= 1` and say so in "As built". A web test plays it: two deaths within 120 days, the storylet arrives, the service is held, and the chronicle shows the weight-4 entry with the option's text.
- [ ] **Step 2: the GDD.** Sections 3.8, 3.9, 6, 7.1, and 7.5 as design section 6 says. Bump the status line and name the slice. Rebuild and republish the GDD page (`tools/gdd-page/README.md`).
- [ ] **Step 3: the glossary.** Add **Entry** and **Notable**. **Chronicle** gains "read by storylets with `chronicle.count`".
- [ ] **Step 4: progress.** The roadmap's row and the outline's paragraph say "Done", with the date and a pointer to "As built". Part six becomes "Next".
- [ ] **Step 5: As built.** The design's section 8, task by task: what changed and why, the golden hash before and after, and what's left for the author to judge in play (the default lines, the weights, and the founding entry with no ship name).
- [ ] **Step 6:** `./check.sh`; commit "Give the game a storylet that remembers the dead, and record the chronicle".
