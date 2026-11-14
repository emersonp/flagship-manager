# Slice 1c, part two: the cycle in the browser — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: use superpowers:subagent-driven-development to implement this plan task by task.

**Goal:** the author plays whole cycles in a browser: legacy selection, orders, briefing, a mission arriving beat by beat with its calls, aftermath, and the next cycle.

**Architecture:** a new `src/Web` project (ASP.NET Core minimal API, .NET 10) renders Razor components on the server and sends Datastar 1.0.5 stream events written by hand. Each request locks one legacy, loads it from SQLite, sends commands through `Game.Build(pack).Bus`, commits, and renders from the stored state. No rules are added; `Sim.*` changes only where a task says so.

**Tech stack:** C# / .NET 10, ASP.NET Core minimal APIs, Razor components (static server rendering through `HtmlRenderer`), Datastar 1.0.5 (self-hosted), SQLite (`Microsoft.Data.Sqlite`, already used), xUnit, `Microsoft.AspNetCore.Mvc.Testing`.

**Spec:** `docs/plans/2026-11-14-phase-1c-web-design.md`. **Reference:** `docs/research/datastar-1.0.5.md` (exact attribute and stream syntax; trust it over memory). **Carried notes:** `docs/plans/phase-1-outline.md`, "Carried forward to the second part of slice 1c".

## Global Constraints

- Every C# `if`, `else`, `for`, `foreach`, and `while` body is in braces on its own lines, one-liners included.
- Comments are terse and say why, not what. Each test has a one-line intent comment or a name that says it.
- No creative content: only placeholders that already exist in `content/` (`CREW_01`, `SKILL_A`, `OBJECTIVE_TEMPLATE_1`, `GEAR_01`) and plain interface words (Launch, Briefing, Roster). The ship's name is the placeholder `SHIP_NAME`. Never invent crew names, species, or story text.
- Oxford comma in all prose, including interface text and commit messages. Commit messages are plain: no attribution trailer or footer of any kind.
- `src/Sim.*` projects stay pure (the banned-API analyser applies): no clock, no database, no web types. Feature modules reference `Sim.Core` only. `Web` may reference `GameHost`, `Persistence`, `Sim.Content`, and every `Sim.*` project.
- No rule changes. If a screen seems to need one, stop and report.
- Stored forms are read strictly through `SimJson.Options`. Module state is read for pages only through `LegacyState.TryModule` or a view, never `Module<T>()` (which creates).
- A `LegacyState` is never shared between requests: lock, load, send, commit, render.
- Seeds, stream states, and any 64-bit number never appear in HTML, signals, or a stream.
- Datastar syntax is 1.0.5: `data-on:click`, `data-signals:name`, `data-bind:name`, `data-init`, events `datastar-patch-elements` and `datastar-patch-signals`. No Pro-only attributes. Signals are local only and start with an underscore.
- The gate is `./check.sh`; it must exit 0 with no warnings at the end of every task. The mission golden hash and the simulator golden hash must not move in this plan.
- Interface code in this plan is the contract; write the bodies test-first, and see each new test fail before the code that passes it.

## File map

| Path | Holds |
|---|---|
| `src/Persistence/ILegacyStore.cs`, `SqliteLegacyStore.cs`, `LegacyExport.cs` | Task 1: name, creation time, list, sequence check, events by cycle, schema 3, export format 3 |
| `src/Web/Web.csproj`, `Program.cs`, `WebApp.cs` | Task 2: the app, its settings, and its services |
| `src/Web/LegacySession.cs` | Task 2: lock, load, send, commit |
| `src/Web/Sse.cs`, `Render.cs` | Task 2: stream events and component-to-string rendering |
| `src/Web/wwwroot/lib/datastar.js` | Task 2: Datastar 1.0.5, vendored |
| `src/Web/Components/*.razor` | Tasks 2, 4, 5: pages and fragments |
| `src/Web/Bridge/BridgeView.cs`, `BridgeReader.cs`, `BeatLines.cs` | Task 3: the read model |
| `src/Web/Endpoints/*.cs` | Tasks 2, 4, 5: the requests |
| `src/Web/wwwroot/css/tokens.css`, `base.css`, `app.css`, `wwwroot/fonts/*` | Task 6: the visual design |
| `tests/Web.Tests/*` | Tasks 2 to 6 |
| `check.sh`, `run.sh`, `README.md`, docs | Task 7 |

---

### Task 1: Storage for a list of named legacies

**Files:** modify `src/Persistence/ILegacyStore.cs`, `SqliteLegacyStore.cs`, `LegacyExport.cs`; `tests/Persistence.Tests/*`; every caller of `LegacyRecord`, `Create`, and `Commit` (`src/SimRunner`, `tests/SimRunner.Tests`).

**Interfaces (produces):**

```csharp
public sealed record LegacyRecord(LegacyState State, string ContentPackId, int ContentPackVersion, string Name, DateTimeOffset CreatedAt);
public sealed record LegacyListing(Guid LegacyId, string Name, DateTimeOffset CreatedAt, int Week, int Cycle);

public interface ILegacyStore
{
    void Create(LegacyRecord record);
    LegacyRecord? Load(Guid legacyId, IReadOnlyDictionary<string, Type> moduleTypes);
    // Refuses with StaleLegacyException when the stored sequence is not expectedSequence.
    void Commit(LegacyState state, IReadOnlyList<EventEnvelope> newEvents, long expectedSequence);
    IReadOnlyList<EventEnvelope> ReadEvents(Guid legacyId);
    IReadOnlyList<EventEnvelope> ReadEvents(Guid legacyId, int cycle);
    IReadOnlyList<LegacyListing> ListLegacies();
}

public sealed class StaleLegacyException(Guid legacyId, long expected, long found) : Exception(...);
```

**Rules:**
1. `legacy` gains `name TEXT NOT NULL` and `created_at TEXT NOT NULL` (ISO 8601, UTC, round-trip format). `PRAGMA user_version = 3`; any other version on an existing database is refused with the existing message.
2. A name is trimmed, 1 to 60 characters; `Create` throws `ArgumentException` otherwise.
3. `expectedSequence` is the state's `NextSequence` as loaded (before the commands ran). `Commit` updates with `WHERE legacy_id = $id AND next_sequence = $expected`; zero rows changed throws `StaleLegacyException` and rolls everything back.
4. `ListLegacies` is ordered by `created_at` descending, then id.
5. `ReadEvents(id, cycle)` is ordered by sequence.
6. The export's format version is 3 and carries `Name` and `CreatedAt`; format 2 is refused. Export, import, export stays byte-identical.
7. `SimRunner` and existing tests pass a fixed name (`"LEGACY"`) and a fixed time where they create records; nothing in `Sim.*` learns about either.

**Tests:** name and time round-trip; bad names refused; list order and contents after two commits; stale commit refused and nothing written (state, modules, and events unchanged); events by cycle; version 2 database refused; export format 2 refused; byte-identical re-export.

- [ ] Write the failing tests. See them fail. Implement. `./check.sh`. Commit: "Store a legacy's name and creation time, list legacies, and refuse a stale commit".

---

### Task 2: The web app's skeleton, and legacy selection

**Files:** create `src/Web/*` as in the file map, `tests/Web.Tests/Web.Tests.csproj`, `LegaciesTests.cs`, `SseTests.cs`, `TestApp.cs`; add both projects to `StarshipSim.slnx`.

**Interfaces (produces):**

```csharp
public sealed class WebSettings
{
    public string ContentDirectory { get; set; } = "content";
    public string DatabasePath { get; set; } = "starship-sim.db";
    public int BeatDelayMs { get; set; } = 700;
}

// Built once at start-up: the loaded pack, the game, and the store.
public sealed class GameServices(ContentPack pack, Game game, ILegacyStore store, TimeProvider clock);

public sealed class LegacySession
{
    // Runs work under the legacy's lock on a freshly loaded state, commits what it published, and returns its result.
    // Returns null when the legacy does not exist. A CommandRejectedException rolls nothing forward and is rethrown.
    public Task<T?> Run<T>(Guid legacyId, Func<LoadedLegacy, T> work) where T : class;
    public Task<LoadedLegacy?> Read(Guid legacyId);
    public Task<Guid> CreateLegacy(string name);
}

public sealed class LoadedLegacy
{
    public LegacyRecord Record { get; }
    public LegacyState State { get; }
    public void Send(ICommand command);   // collects the envelopes for the commit
}

public static class Sse
{
    public static Task Start(HttpResponse response);                                   // text/event-stream headers
    public static Task PatchElements(HttpResponse response, string html, string? selector = null, string? mode = null);
    public static Task PatchSignals(HttpResponse response, string json);
    public static Task Redirect(HttpResponse response, string url);                    // a script element, as the reference shows
}

public sealed class Render(IServiceProvider services, ILoggerFactory loggers)
{
    public Task<string> Html<TComponent>(IDictionary<string, object?> parameters) where TComponent : IComponent;
}
```

**Rules:**
1. `Program.cs` reads `WebSettings` from configuration section `Web`, loads the content pack with the existing loader (start-up fails with the loader's errors listed if the pack is invalid), opens the SQLite store, and maps the endpoints. `public partial class Program` exists for the tests.
2. The seed of a new legacy comes from `RandomNumberGenerator` in the web layer. `CreateLegacy` creates the record, sends `FoundLegacy`, and commits, all under the lock.
3. The lock is one `SemaphoreSlim` per legacy id, held for load, work, and commit. `Read` takes it too.
4. A commit that throws `StaleLegacyException`, or any exception from `Send` other than `CommandRejectedException`, discards the state; the request answers 500 with a plain error page that says the legacy was not changed.
5. Datastar is vendored: download `https://cdn.jsdelivr.net/gh/starfederation/datastar@1.0.5/bundles/datastar.js` once into `src/Web/wwwroot/lib/datastar.js` and commit it, with the MIT licence text beside it as `datastar.LICENSE`. The layout loads it with `<script type="module" src="/lib/datastar.js"></script>`.
6. `Sse.PatchElements` writes exactly the wire format in the reference: `event: datastar-patch-elements`, optional `data: selector …` and `data: mode …`, one `data: elements …` line per line of HTML, then a blank line, flushed.
7. Pages: `GET /` renders `LegaciesPage` (a table of `ListLegacies()` with name, week, cycle, and creation date; an empty state; a form with a name field). `POST /legacies` (form post, `name`) creates and redirects (303) to `/legacy/{id}`; a bad name re-renders the page with the reason beside the field, status 400. `GET /legacy/{id}` renders `BridgePage`, which in this task shows only the top bar (name, week, cycle, and phase); an unknown or malformed id is a 404 page.
8. Markup is semantic and unstyled in this task: one `<main id="main">`, a `<header id="topbar">`, and an `<aside id="roster">`; forms are real forms. Class names are plain words. The visual design comes in Task 6.
9. Tests run the real app with `WebApplicationFactory<Program>`, a temporary database file per test, the repository's `content` directory, and `BeatDelayMs = 0`.

**Tests:** the empty list page; creating a legacy redirects and the list then shows it; a blank and an over-long name are refused with the reason and nothing is created; an unknown id is 404; the bridge shows week 1, cycle 1, and the orders phase; `Sse.PatchElements` output for multi-line HTML matches the reference byte for byte; two concurrent `Run` calls on one legacy run one after the other (both succeed, sequences do not collide); `Run` on a rejected command leaves the stored legacy unchanged.

- [ ] Write the failing tests. See them fail. Implement. `./check.sh`. Commit: "Add the web app: legacy selection, sessions, and stream events".

---

### Task 3: The read model

**Files:** create `src/Web/Bridge/BridgeView.cs`, `BridgeReader.cs`, `BeatLines.cs`; `tests/Web.Tests/BridgeReaderTests.cs`, `BeatLinesTests.cs`.

**Interfaces (produces):**

```csharp
public sealed record BridgeView(TopBarView TopBar, IReadOnlyList<CrewRow> Roster, SiteView? Site, BriefingView? Briefing, MissionView? Mission, AftermathView? Aftermath);
public sealed record TopBarView(string LegacyName, int Week, int Cycle, CyclePhase Phase, string? PackWarning);
public sealed record CrewRow(string Id, int Seniority, string Status, bool Fit, IReadOnlyList<SkillCell> Skills, IReadOnlyList<int> LightInjuryHealWeeks, int? ReturnsAtWeek, int Loyalty, int AwayMissions);
public sealed record SkillCell(string SkillId, int Level, int EffectiveLevel);
public sealed record SiteView(string AssignmentId, IReadOnlyList<ObjectiveView> Objectives);
public sealed record ObjectiveView(string Id, string TemplateId, IReadOnlyList<string> Skills, int TeamSizeMin, int TeamSizeMax, int? UnattendedDifficulty, bool HasTeam);
public sealed record BriefingView(IReadOnlyList<ObjectiveView> Objectives, IReadOnlyList<CrewRow> Crew, IReadOnlyList<GearChoice> Gear, int LoadoutLimit);
public sealed record GearChoice(string Id, string Tag);
public sealed record MissionView(string MissionId, string ObjectiveId, IReadOnlyList<string> Team, string LeadId, int Momentum, int Strain, IReadOnlyList<LogLine> Log, CallView? Call, string? Outcome);
public sealed record LogLine(int Index, string Kind, string Text);
public sealed record CallView(int CallId, string DecisionId, int Stakes, IReadOnlyList<string> Options, string LeadPrefers);
public sealed record AftermathView(string? Outcome, IReadOnlyList<CrewChange> Changes, IReadOnlyList<UnattendedView> Unattended);
public sealed record CrewChange(string CrewId, string Text);
public sealed record UnattendedView(string ObjectiveId, string TemplateId, int Roll, int Target, bool Succeeded);

public static class BridgeReader
{
    // Pure: no store, no clock. cycleEvents are this cycle's decoded events in sequence order.
    public static BridgeView Read(LegacyRecord record, ContentPack pack, IReadOnlyList<IEvent> cycleEvents);
}

public static class BeatLines
{
    public static LogLine Line(int index, Beat beat);
}
```

Use the real names and types from `src/Sim.Missions` (`MissionsState`, `SiteState`, `PendingCall`, the beat records in `Beats.cs`) and `src/Sim.Crew` (`CrewState`); where a field above does not match what those types hold (for example the call id's type), follow the code and say so in the report.

**Rules:**
1. Read module state with `TryModule` only. A legacy with no crew or missions state yet gives an empty roster and a null site, not an error.
2. `CrewRow.Skills[].EffectiveLevel` is the level less unhealed light injuries, floor 0 (the same figure the roster view gives). `Status` is the status's name. The dead are listed last.
3. `Site`, `Briefing`, `Mission`, and `Aftermath` are present by phase: site in orders and after; briefing only in briefing; mission in resolution and aftermath when a team was sent; aftermath in aftermath and advance.
4. The mission log is built from this cycle's `MissionBeat` events for the mission, in index order: one `LogLine` per beat. `Momentum` and `Strain` are the latest values a beat reports.
5. `BeatLines.Line` covers every beat type in `Beats.cs` with a plain structural sentence built only from ids, numbers, and enum names, for example `CREW_02 checks SKILL_A at ENCOUNTER_03: 9 against 11, partial.` `Kind` is a stable lower-case word per beat type (`stage`, `check`, `injury`, `call`, and so on) for styling. A test fails if a new beat type is added without a line (reflect over `Beat`'s subtypes).
6. `CallView.LeadPrefers` comes from the pending call as the resolver exposes it; if the resolver does not store the lead's preference on the pending call, compute it with the resolver's public `Prefers`, and if that needs more than the stored state gives, leave the field out and report it. Do not change `Sim.Missions`.
7. `AftermathView.Changes` is built from this cycle's `SkillRaised`, `CrewInjured`, `CrewDied`, and `CrewRecovered` events and the lead's loyalty change in the `MissionEnded` result, one line each, in event order.
8. `TopBarView.PackWarning` is set when the record's content pack id or version differs from the loaded pack.

**Tests:** build legacies with `Game.Build` and the automatic captain's choices (reuse `AutoCaptain` from `SimRunner` if it can be referenced from the test project; otherwise send the commands directly) and assert the model in each phase: orders, briefing, resolution with a call waiting, resolution ended, aftermath, and the next cycle's orders; a recovering and a dead crew member in the roster (use a pack with 1d20 check dice, as `SimulationTests` does, to reach them); the log's index order and count equal the mission's beat count; every beat type has a line; no line contains a seed or a number above 2^31.

- [ ] Write the failing tests. See them fail. Implement. `./check.sh`. Commit: "Add the bridge's read model and plain beat lines".

---

### Task 4: Orders, briefing, launch, and advance

**Files:** create `src/Web/Components/BridgePage.razor`, `TopBar.razor`, `Roster.razor`, `OrdersScreen.razor`, `BriefingScreen.razor`, `Refusal.razor`; `src/Web/Endpoints/BridgeEndpoints.cs`; `tests/Web.Tests/BriefingTests.cs`.

**Consumes:** `LegacySession`, `Sse`, `Render`, `BridgeReader`, and the commands `AdvancePhase` and `AssignTeam(objectiveId, crewIds, leadId, gearIds, parameters)`.

**Rules:**
1. `GET /legacy/{id}` renders `BridgePage` from `BridgeReader.Read`: `#topbar`, `#main` (the screen for the phase), and `#roster`. In resolution and aftermath this task renders a placeholder screen that names the phase; Task 5 replaces it.
2. Orders screen: the site's objectives as a table; one button, "To briefing", `data-on:click="@post('/legacy/{id}/advance')"`.
3. Briefing screen: one `<form id="briefing">` with: the objective (radio group; a single objective is preselected), one row per crew member with a checkbox `crew` (disabled when not fit), a radio `lead`, the gear as checkboxes `gear`, and three radio groups `priority`, `risk`, and `autonomy` with values −1, 0, and 1 (0 preselected). "Launch" posts the form with `@post('/legacy/{id}/launch', {contentType: 'form'})`; "Send nobody" posts to `/advance`. A local signal `_picked` counts ticked crew to show "n of min to max" beside the button; it is display only, and the server decides.
4. `POST /legacy/{id}/advance`: sends `AdvancePhase`; from aftermath it sends it twice (through the advance phase). Answers with a stream of three `PatchElements` events: `#topbar`, `#main`, and `#roster`, each rendered from the state after the commit.
5. `POST /legacy/{id}/launch`: binds the form (missing or unparseable fields are a refusal, not an exception), sends `AssignTeam` then `AdvancePhase` inside one `Run` (one commit). On `CommandRejectedException` nothing is committed and the answer is a stream with one event replacing `#refusal` (an element inside the form, `role="alert"`) with the reason. On success the answer is the same three patches as `/advance`.
6. Every refusal reason is shown verbatim from the exception; the web layer adds none of its own except for unparseable form fields ("The form was not complete.").
7. A request for an unknown legacy answers 404; a request in the wrong phase is a refusal from the bus, shown the same way.
8. With scripting off, `GET /legacy/{id}` still shows the full current state.

**Tests** (through the real app): orders shows the site's objectives; "To briefing" moves the phase and the stream carries the three patches in order; the briefing lists eight crew, with an unfit one disabled; a valid launch reaches resolution and stores `TeamAssigned`; each of these is refused with the bus's reason and stores nothing (compare the stored event count and state before and after): no crew, too many crew, a lead not on the team, an unfit crew member, gear over the limit, an out-of-range parameter, and a second launch in the same cycle; "Send nobody" reaches resolution with no mission; an incomplete form is refused; advancing from aftermath lands on the next cycle's orders at week 2.

- [ ] Write the failing tests. See them fail. Implement. `./check.sh`. Commit: "Play orders and briefing in the browser".

---

### Task 5: Resolution, calls, and aftermath

**Files:** create `src/Web/Components/ResolutionScreen.razor`, `MissionLog.razor`, `CallPanel.razor`, `AftermathScreen.razor`; modify `src/Web/Endpoints/BridgeEndpoints.cs`; create `tests/Web.Tests/ResolutionTests.cs`, `ParityTests.cs`.

**Rules:**
1. Resolution screen: the team and lead, momentum and strain, `<ol id="log">` with one `<li>` per `LogLine` (`id="beat-{index}"`, `data-kind`), then `#call` (the call panel when a call waits, empty otherwise), then "To aftermath" when the mission has ended or no team was sent.
2. Call panel: the decision id, the stakes, the lead's preference when known, and one button per option, each `data-on:click="@post('/legacy/{id}/answer', {contentType: 'form'})"` inside a small form carrying hidden `mission`, `call`, and the button's `answer`. Use whichever form mechanism the reference shows working for a button's own value; if a button value is not sent, give each option its own form.
3. Pacing. `/launch` (replacing Task 4's success answer) and `POST /legacy/{id}/answer` commit first, then stream: first the three patches (`#topbar`, `#main`, and `#roster`) with the log as it stood **before** this request's new beats; then, for each new beat, one event replacing `#log` whole (the full list up to that beat) followed by a delay of `BeatDelayMs`; then one event replacing `#call` (the next call, or empty) and `#main`'s footer action if the mission ended. A client that disconnects mid-stream loses nothing: the next `GET` shows everything.
4. `/answer` binds `mission`, `call`, and `answer`; sends `AnswerCall`; a refusal (wrong call, not an option, no call waiting, a double submit) replaces `#refusal` in the call panel with the reason and stores nothing.
5. `AdvancePhase` while a call waits is refused by the bus ("A call is waiting for an answer."); the screen offers no such button, and a hand-made request shows the refusal.
6. Aftermath screen: the outcome, the crew changes, and the unattended rolls from `AftermathView`; one button, "Next cycle" (`/advance`).
7. The stream honours `HttpContext.RequestAborted`: it stops writing, and never throws out of the endpoint.

**Tests:** a launch stream's `#log` events grow by exactly one `<li>` each and end with a call or a result; a reload while a call waits shows the call and the whole log; answering continues the log from the next index with no index repeated; a double answer is refused and stores nothing; a hand-made advance while a call waits is refused; the mission ends and "To aftermath" appears; aftermath lists the outcome and at least the lead's change; a whole cycle through the requests returns to orders at week 2; an aborted stream leaves the legacy readable and complete; **parity:** for seeds 1 to 3 and 6 cycles, a legacy played through the requests with the automatic captain's choices (read each choice from `AutoCaptain` against the loaded state, then post it) has exactly the event log `SimRunner`'s straight run gives for the same seed (the web test creates the legacy with that seed through a test hook on `LegacySession`, not through the form).

- [ ] Write the failing tests. See them fail. Implement. `./check.sh`. Commit: "Play resolution, calls, and aftermath in the browser".

---

### Task 6: The visual design (Hallmark, Midnight, app screens)

This task is design work under a skill. The implementer must load and follow the Hallmark skill at `/home/parker00/.claude/skills/hallmark/SKILL.md`, in **app-screen scope** (`references/flow.md` § App screens), with the theme **Midnight already dealt** (`references/themes/midnight.md`, deal `2/3, seed dark-sci-fi-starship-command-19f2·24a39ccf·2026-11-14·r0`). Do not ask the four questions and do not re-deal: the answers are in the spec's section 6.

**Files:** create `src/Web/wwwroot/css/tokens.css`, `base.css`, and `app.css`; `src/Web/wwwroot/fonts/*` (Mona Sans, self-hosted, with its licence); modify the components' markup and class names as the design needs; `.hallmark/log.json`.

**Rules:**
1. Start from `node <skill-dir>/scripts/theme.mjs scaffold midnight --scope app --dealt "2/3, seed dark-sci-fi-starship-command-19f2·24a39ccf·2026-11-14·r0"`; keep its tokens and stamp. Every colour and font goes through a token.
2. Dark only; square corners; no glow, glass, gradient, or texture. Azure marks only the primary action, the current phase, and the current selection.
3. Add two state tokens, used only as state and always with a text label: `--color-harm` (injured, incapacitated, dead, or lost) and `--color-caution` (a call waiting, or strain at 4 or more). Both desaturated to sit in the ground's blue, and both meeting 4.5:1 on `--color-paper` and `--color-paper-2`.
4. The shell: a top bar; main and roster side by side from 60rem, one column below, with the roster under the main region. The container is capped and centred.
5. The top bar shows the phase as five steps (orders, briefing, resolution, aftermath, and advance) with the current one marked in text as well as colour (`aria-current="step"`).
6. The mission log is the signature: a ruled list in which each beat line carries its kind as a quiet label, checks show their numbers in tabular figures, and harm and calls are marked by the state tokens. A new beat settles in once (opacity only, `--dur-short`), with a `prefers-reduced-motion` fallback.
7. Tables before cards for the roster, the site, and the briefing. Controls 32 to 40 pixels tall with a 44 pixel target. Every interactive element has all its states, with `:focus-visible` instant.
8. Each data region has its empty and error states drawn (no legacies yet; no team sent; the error page), and a control shows that its request is in flight (`data-indicator` with a local, underscore-named signal).
9. Fonts: fetch the Mona Sans variable woff2 files once and commit them under `wwwroot/fonts` with `@font-face` in `tokens.css` or `base.css`; fall back to `ui-sans-serif, system-ui`. No request leaves the machine at run time.
10. No behaviour changes: every test from Tasks 2 to 5 still passes. Where a test asserts markup the design must change, change the test to assert the same fact in the new markup, and list each such change.
11. Run the app (`dotnet run --project src/Web` with a scratch database and `Web__BeatDelayMs=0`), drive it to each screen, and run `node <skill-dir>/scripts/sloplint.mjs src/Web/wwwroot/css src/Web/Components --scope app --genre atmospheric --render --url http://localhost:<port>/<route>` for the legacies page and for a legacy in briefing, resolution with a call waiting, and aftermath. Fix every FAIL; fix or waive (with a reason, in the stylesheet's header) every REFLEX. Run the skill's finish review as the skill describes it, without dispatching subagents (do the review pass yourself and say so).
12. Save a screenshot of each of those four screens at 1440 by 900, and of briefing at 400 wide, to `docs/screens/1c/` as PNG files, and list them in the report.

**Report also:** the Picks block the skill asks for (app-scope form), the sloplint summary line, and any gate waived with its reason.

- [ ] Scaffold. Build the stylesheet and adjust markup. Run the tests. Run the linter and the review. Capture the screens. `./check.sh`. Commit: "Design the bridge: Midnight theme, mission log, and roster".

---

### Task 7: The gate, a run script, and the record

**Files:** modify `check.sh`; create `run.sh`; modify `README.md` (or create it), `docs/gdd.md` (7.5 and the roadmap row), `docs/plans/phase-1-outline.md`, `wayfinder/map.md`, `GLOSSARY.md`.

The controller supplies the doc text after Task 6. The code part:

1. `check.sh` also starts the web app on a scratch database and a free port, waits for `GET /` to answer 200, posts a new legacy, checks the redirect target answers 200, and stops the app; it prints one line, `Web: legacy created and opened. OK.` It must not leave a process or a file behind, and must fail the gate if any step fails.
2. `run.sh` runs the app from the repository root with the repository's content and a database at `./starship-sim.db` (ignored by git), and prints the address.

- [ ] Implement and test by hand. `./check.sh`. Commit: "Check the web app in the gate, and add a run script".

---

## Done when

- `./check.sh` exits 0 on a clean clone.
- `./run.sh` opens a game in which a legacy can be started and played through several cycles in the browser, including a mission with a call, with the browser closed and reopened mid-call.
- A legacy played through the browser's requests has the same event log as the simulator's for the same seed and choices.
- The stylesheet carries the Hallmark stamp and passes its linter in app scope.

## Deliberately not in this part

The content editor, the narrative report, the chronicle, the inbox and storylets, the bench and tuning pages, several teams per cycle, sign-in, and anything online.
