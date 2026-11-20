# Slice 1e-calendar (the calendar): implementation plan

> **For agentic workers:** steps use checkbox (`- [ ]`) syntax for tracking. Do the tasks in order; each ends with `./check.sh` and a commit. Keep the design's section 5 (architecture) current as you go: when the build differs, change section 5 in the same commit, and say so in a new "As built" section (section 8) of the design.

**Goal:** the engine keeps time in days and shows it as dates in the Bastion Calendar. A cycle moves the calendar 1d3+4 days, rolled when it advances; Dev Play's "Travel N weeks" moves it one roll times N. Every timed effect (healing, a return to duty, the morale drift, a trait's expiry and step) is due on a day and happens when the calendar passes it, in date order. Expressions read `cyclesSince` and `daysSince` on storylets and traits. No screen in play shows a week number; durations show in weeks, rounded to the nearest.

**Architecture:** the calendar is content (`content/calendars/`, named by `pack.yaml`) with its arithmetic in `Sim.Core`. `Sim.Cycle` owns the date: it rolls the length on its own stream, turns the cycle, and publishes one `DayReached` per day passed. `Sim.Crew` and `Sim.Traits` move from `WeekAdvanced` to `DayReached`, with their stored fields in days. `Sim.Storylets` records the day each storylet last arrived and serves the new readings through `IStateSource`. `Web` formats dates and durations in one place.

**Spec:** `docs/plans/2026-11-20-phase-1e-calendar-design.md`. Read it with this plan. **Lore:** `docs/lore/calendars.md` (read only; never edit anything in `docs/lore/`). **Design system:** `docs/design/bridge-design.md`.

## Global constraints

Part one's (`2026-11-16-phase-1e-part-one-plan.md`, "Global constraints") hold, with these changes:

- `SchemaVersion` is 12 and `CurrentFormatVersion` is 10 today. Task 2 raises them to 13 and 11, once for the whole slice; later tasks that change saved state do so inside that one raise (the store is refused, not migrated).
- This slice draws randomness only from one new stream, `calendar`, drawn once per cycle turn (keyed by the cycle that is ending, as every stream is). Nothing else draws from it, and no other stream moves.
- Content and tuning write durations only in `weeks:`. Nothing in `content/`, the fixture, or the code reads a duration in days from a file.
- `content/` may gain the calendar file, `calendar:` in `pack.yaml`, the calendar tuning file, the report lines for the cycle's length, and the worked example's storylet (design 4.6), and nothing else. The fixture gains a placeholder calendar (`MONTH_A`, `WEEKDAY_A`: 6 weekdays, 12 months of 30 days).
- The cycle's length never reaches a play-mode screen as a number, and nor does the cycle number.

## Review focus

1. **Date order holds across modules.** On a span of days, nothing due on a later day happens before something due on an earlier one, and on one day the crew module runs before the traits module. (Tasks 2 and 3)
2. **Nothing is due on a cycle.** No timed effect reads `Cycle`; rest (a cycle not sent) is the one thing that stays per cycle, and runs before the first day. (Tasks 2 and 3)
3. **Replay is exact.** The length draws only from `calendar`; a reload in any phase plays on identically; the golden seeds change once, in Task 2, for the calendar alone. (Task 2)
4. **Unset is never true.** `daysSince` and `cyclesSince` for something that never happened make every comparison false, including `<`. (Task 4)
5. **No week number escapes.** Play mode shows dates and rounded weeks only; the cycle number and day numbers appear in dev mode alone. (Task 5)

---

### Task 1: the calendar as content

**Files:** `src/Sim.Core/Content/CalendarContent.cs` (new), `src/Sim.Core/Calendar.cs` (new), `src/Sim.Core/Content/ContentPack.cs`, `src/Sim.Content/ContentLoader.Calendar.cs` (new), `src/Sim.Content/ContentLoader.cs`, `src/Sim.Content/ContentLoader.Tuning.cs`, `src/Sim.Content/ContentModels.cs`, `src/Web/ContentKinds.cs`, `content/calendars/base.yaml`, `content/pack.yaml`, `content/tuning/calendar.yaml`, the same three in `tests/fixtures/content/`, `docs/conventions.md`, `tests/Sim.Tests/CalendarTests.cs` (new), `tests/Sim.Tests/ContentLoaderTests.cs`.

**Produces** (shape):

```csharp
public sealed record WeekdayDef(string Id, string Name, string Short);
public sealed record MonthDef(string Id, string Name, int Days);
public sealed record CalendarDef(string Id, string Name, IReadOnlyList<WeekdayDef> Weekdays, IReadOnlyList<MonthDef> Months, CalendarDate Start)
{
    public int DaysPerWeek => Weekdays.Count;
    public int DaysPerYear { get; }                 // the sum of the months
}
public readonly record struct CalendarDate(int Year, int Month, int Day);   // Month is 0-based; Day is 1-based, as written
public sealed record CalendarTuning(int CycleMinDays, int CycleMaxDays);

public static class Calendar
{
    public static int DayNumber(CalendarDef calendar, CalendarDate date);   // day 0 is the 1st of the first month of year 1
    public static CalendarDate Date(CalendarDef calendar, int day);
    public static WeekdayDef Weekday(CalendarDef calendar, int day);        // day 0 is the first weekday
    public static int Days(CalendarDef calendar, int weeks);                // weeks * DaysPerWeek
}
```

- [ ] **Step 1:** Write `CalendarTests`: 13 Vortun 105 is day 37482 and a Stoneday; `Date(DayNumber(d)) == d` across a year boundary and for the last day of a month; every 1st of a Bastion month is a Stoneday; `Days(cal, 2) == 12`. Run them red.
- [ ] **Step 2:** Add the types and `Calendar`; run green.
- [ ] **Step 3:** The loader reads `content/calendars/*.yaml`, `calendar:` in `pack.yaml`, and `content/tuning/calendar.yaml` (`cycle: { minDays: 5, maxDays: 7 }`). It refuses: no weekday or month, a duplicate id, a month under 1 day, a start date outside its month or year 1, a pack naming no calendar or one that doesn't exist, `minDays` under 1 or above `maxDays`. Each message names the file and the field, and quotes `docs/conventions.md` where a rule lives there. Tests for each refusal.
- [ ] **Step 4:** Write `content/calendars/base.yaml` from `docs/lore/calendars.md`, citing it in a comment (design 4.2): ids lower case, `pelphear` for Pelpheär with the name keeping its diaeresis. Write the fixture's placeholder calendar.
- [ ] **Step 5:** The editor's index shows `calendars/base.yaml` and `tuning/calendar.yaml` as text files validated on save, as the tuning file is.
- [ ] **Step 6:** `docs/conventions.md`: the `calendars` content kind, and durations in `weeks:` only.
- [ ] **Step 7:** `./check.sh`; commit "Add the Bastion Calendar as content, with its arithmetic in Sim.Core".

### Task 2: days replace weeks

The core move, in one commit, because `WeekAdvanced` goes and everything that heard it must move with it.

**Files:** `src/Sim.Core/LegacyState.cs`, `src/Sim.Core/CycleContracts.cs`, `src/Sim.Cycle/CycleModule.cs`, `src/GameHost/Game.cs`, `src/Sim.Crew/CrewModule.cs`, `src/Sim.Crew/CrewState.cs`, `src/Sim.Traits/TraitsModule.cs`, `src/Sim.Traits/TraitsState.cs`, `src/Sim.Core/TraitContracts.cs`, `src/Sim.Core/Content/CrewContent.cs`, `src/Sim.Core/Content/TraitContent.cs`, `src/Sim.Content/ContentLoader.Crew.cs`, `src/Sim.Content/ContentLoader.Traits.cs`, `src/Persistence/SqliteLegacyStore.cs`, `src/Persistence/LegacyExport.cs`, `src/Persistence/ILegacyStore.cs`, `src/Web/LegacySession.cs`, `src/SimRunner/Simulation.cs`, and the tests that name weeks (`CrewModuleTests`, `TraitsModuleTests`, `CycleModuleTests`, `SqliteLegacyStoreTests`, and the rest the compiler finds).

- [ ] **Step 1: state and contracts.** `LegacyState.Week` becomes `Day`; add `StartDay`. Remove `WeekAdvanced`; add `DayReached(int Day)` and `CycleLengthRolled(int Days, int PlannedDays)` at version 1, and the command `Travel(int Weeks)`. `CrewInjured.ReturnsAtWeek` becomes `ReturnsAtDay`.
- [ ] **Step 2: the date at founding.** `CycleModule.Register(bus, pack)`. `FoundLegacy` sets `Day` and `StartDay` to the calendar's start day. Update its five callers (`Game.cs` and four test files).
- [ ] **Step 3: the turn** (design 4.4). Leaving the advance phase: roll `context.Rng("calendar").Next(min, max + 1)` (times N for `Travel`), publish `CycleLengthRolled`, `Cycle++`, publish `CycleAdvanced`, then for each day in order set `Day` and publish `DayReached`, then enter the itinerary. `Travel` is guarded to the advance phase and N from 1 to 10. Tests: the length is always 5 to 7 over 300 seeds and each value appears; one `DayReached` per day, in order; `Travel(5)` passes 25, 30, or 35 days and turns one cycle; `Travel` refused outside the advance phase.
- [ ] **Step 4: the crew module.** Rest moves to `CycleAdvanced` (a fit member not sent in the cycle just ended), before any day. On `DayReached`: light injuries whose `HealsAtDay <= day` heal, a member whose `ReturnsAtDay <= day` returns, then the drift runs if `(day - StartDay)` is a positive multiple of the week. Injuries are due at `Day + Calendar.Days(weeks)`. Fields: `LightInjuriesHealAtDay`, `ReturnsAtDay`.
- [ ] **Step 5: the traits module.** On `DayReached`, as `OnWeek` did, with `since = day - ChangedAtDay` against `Days(expires.weeks)` and `Days(every.weeks)`. `StoredTrait.ChangedAtWeek` becomes `ChangedAtDay`, and add `ChangedAtCycle` (for Task 4), both set on every change.
- [ ] **Step 6: the order on one day** (design 4.5, review focus 1). A test with an injury and a trait expiry due on the same day, and one with a trait expiring on day 3 of a span that would have stepped it on day 5: the step never happens.
- [ ] **Step 7: persistence.** `legacy.week` becomes `day`; add `start_day`; the export carries both. Raise `SchemaVersion` to 13 and `CurrentFormatVersion` to 11. An old store or export is refused with the existing message.
- [ ] **Step 8: compile the readers.** `BridgeReader`, `DevView`, and the Razor components that read weeks are changed only enough to compile and pass, showing the raw day where they showed a week; Task 5 replaces them. Say so in the commit.
- [ ] **Step 9: the golden seeds.** Run the simulator's and the web tests' fixed seeds. Every changed figure must be explained by the calendar (rolled lengths moving heal, return, drift, and trait dates). Note the before and after of the simulator's summary in the commit.
- [ ] **Step 10:** `./check.sh`; commit "Keep time in days: a cycle's length is rolled, and every timed effect is due on a day".

### Task 3: the report line for the cycle's length

**Files:** `src/Sim.Narrative/` (if the lines engine takes the situation; see Step 1), `content/lines/base.yaml`, the fixture's lines, `src/Web/Bridge/BridgeReader.cs`, `src/Web/Bridge/BridgeView.cs`, `src/Web/Components/ItineraryScreen.razor`, tests in `tests/Web.Tests/`.

- [ ] **Step 1:** Decide where the words live. If the lines engine can take a situation outside a mission (`schedule` with `ahead`, `on`, `delayed`, and the days early or late as a figure) for less than about a day's work, use it, so the author writes the words in the pack. Otherwise fixed words in `Web`, and say so in "As built". Either way the line reads `CycleLengthRolled` from the cycle's events.
- [ ] **Step 2:** Show the line at the top of the itinerary that follows the turn (design 7, left to the builder: the itinerary is where the new cycle is read; the aftermath comes before the roll). Travel's line names the days early or late against N weeks.
- [ ] **Step 3:** Tests: each of the three cases, and travel early and late; a reload shows the same line.
- [ ] **Step 4:** `./check.sh`; commit "Say whether the cycle ran ahead of schedule, on schedule, or delayed".

### Task 4: timing in expressions

**Files:** `src/Sim.Expressions/StateView.cs`, `src/Sim.Expressions/Rewrite.cs`, `src/Sim.Expressions/Whitelist.cs`, `src/Sim.Expressions/TraitReading.cs`, `src/Sim.Expressions/ExpressionBook.cs`, `src/Sim.Storylets/StoryletsState.cs`, `src/Sim.Storylets/StoryletsModule.cs`, `src/Sim.Storylets/Salience.cs` (where it records `Fired`), the storylets module's `IStateSource` adapter, `src/Sim.Core/TraitContracts.cs` (`ITraitView` gains the change's day and cycle), `tests/Sim.Tests/ExpressionsTests.cs`, `tests/Sim.Tests/StoryletsModuleTests.cs`.

- [ ] **Step 1: the spike question first.** In a test, check that DynamicExpresso compares an `int?` with an `int` under C#'s lifted rules (null against anything is false for `<`, `<=`, `>`, `>=`, `==`), with the whitelist on. If it does, the readings return `int?`. If not, the view returns a wrapper whose comparisons are false when unset, and the rewrite routes the comparison to it; record which in "As built".
- [ ] **Step 2: the readings.** `IStateSource` gains `StoryletSince(string storylet)` and `TraitSince(string trait, string crewId)`, each returning cycles and days, or nothing. `Fired` gains `LastDay`, set with `Last`. The rewrite maps `storylet.s.cyclesSince` and `storylet.s.daysSince` to a new root view, and `x.trait.t.cyclesSince` and `x.trait.t.daysSince` to the traits view (for example `x.trait.since("t").days`). `x.trait.t` alone and `.toward(y)` are unchanged; `.since` is refused on a pair trait in this slice, with a message that says so.
- [ ] **Step 3: checks at load.** A storylet id that doesn't exist is refused by name ("No storylet named nosuch"), with its position; vignettes are not read by `storylet.` (refused: "storylet reads storylets; vignettes are not counted"). The trait checks are those `x.trait.t` already has.
- [ ] **Step 4: "no hidden memory".** `TraitReading` treats the new readings as unknown (they turn on time, not on a trait's value), so the rule against hidden memory is unchanged for every existing storylet.
- [ ] **Step 5: tests.** B (`storylet.a.cyclesSince >= 3`) arrives three cycles after A and not before; C (`... && storylet.a.daysSince >= 40`) waits for days too; neither arrives if A never has, and `storylet.a.daysSince < 40` is also false then; a trait's readings reset when it changes and are unset at 0.
- [ ] **Step 6:** GDD section 3.8's table gains the four rows now, as built.
- [ ] **Step 7:** `./check.sh`; commit "Let expressions read how long ago a storylet arrived or a trait changed".

### Task 5: dates and weeks on screen

**Files:** `src/Web/Bridge/Dates.cs` (new: the one formatter), `src/Web/Bridge/BridgeView.cs`, `src/Web/Bridge/BridgeReader.cs`, `src/Web/Bridge/DevView.cs`, `src/Web/Components/TopBar.razor`, `src/Web/Components/CrewStatusText.razor`, `src/Web/Components/LegaciesPage.razor`, `src/Web/Components/DevPanel.razor`, `src/Web/wwwroot/` (the top bar's styles), tests in `tests/Web.Tests/` and `tests/browser/` where the top bar is covered.

**Produces:**

```csharp
public static class Dates
{
    public static string Short(CalendarDef calendar, int day);        // "13 Vortun 105"
    public static string Full(CalendarDef calendar, int day);         // "Stoneday, 13 Vortun 105"
    public static string FullNarrow(CalendarDef calendar, int day);   // "Stone, 13 Vortun 105"
    public static string Weeks(CalendarDef calendar, int days);       // nearest week; "under a week" below 4 days; "1 week", "2 weeks"
}
```

- [ ] **Step 1:** Tests for `Dates`: 3, 4, 8, 9, 14, and 15 days; a date on the last day of a year; the full forms. Run red, then green.
- [ ] **Step 2: the top bar.** `TopBarView` carries the day, not a week. It shows the short date where "Week" was, with the full date in a `title` and an accessible label (and the narrow form under the bar's narrow breakpoint). The cycle number shows only in dev mode, beside the dev badge. Follow `docs/design/bridge-design.md` for the figure style.
- [ ] **Step 3: durations.** The roster's status ("heals in 2 weeks", "returns in 1 week"), the aftermath's injury lines, and the briefing's trait chips where they show time left, all through `Dates.Weeks` from today's day. No "week N" remains.
- [ ] **Step 4: the list of legacies** shows each legacy's date where it showed its week.
- [ ] **Step 5: Dev Play.** The trait table's "Week" column becomes "Changed", a short date. Dev Play shows the cycle number, today's day number, and, in the advance phase, "Travel N weeks" (a number from 1 to 10 and a button) sending `Travel`.
- [ ] **Step 6: the guard** (review focus 5). A web test renders every play-mode screen for a fixture legacy and fails on the words "Week " followed by a figure, on "Cycle", and on any bare day number.
- [ ] **Step 7:** `./check.sh`, including the browser tests; commit "Show the date and weeks left in place of the week counter".

### Task 6: the simulator

**Files:** `src/SimRunner/Simulation.cs`, `src/SimRunner/Cli.cs`, `src/SimRunner/AutoCaptain.cs` if it reads weeks, `tests/SimRunner.Tests/SimulationTests.cs`.

- [ ] **Step 1:** The summary reports the spread of cycle lengths (counts of 5, 6, and 7 days, and the mean) and each legacy's last date. Anything per week becomes per 100 cycles or per 360 days, whichever the figure already compares by; say which in the output's heading.
- [ ] **Step 2:** A test: over 20 legacies × 50 cycles each length is 30% to 37% of turns, and the mean is within 0.1 of 6.
- [ ] **Step 3:** `./check.sh`; commit "Report the calendar in the simulator".

### Task 7: the worked example, and the record

**Files:** `content/storylets/base.yaml`, the GDD chapters named in design section 6, `GLOSSARY.md`, `docs/conventions.md`, `docs/gdd/11-roadmap.md`, `docs/plans/phase-1-outline.md`, `docs/gdd.md`, the design's "As built".

- [ ] **Step 1: the example** (design 4.6). One storylet in the game's pack that requires another storylet's `daysSince >= 40` and `cyclesSince >= 3`, so it arrives only after a trip. Generated words, in place, for the author to rewrite. A web test plays it: the first storylet, three cycles without it, "Travel 5 weeks", and it arrives.
- [ ] **Step 2: the GDD.** Section 2.3 rewritten (days, a rolled length, effects on days, travel as one roll); 3.1 and 3.2's fields in days and the age rule; 7.4's top bar, hover, and durations; 1's "Settled decisions" row for time. Bump the status line and name the slice. Rebuild and republish the GDD page (`tools/gdd-page/README.md`).
- [ ] **Step 3: the glossary.** **Day**, **Date**, **Calendar**; **Week** as a span of 6 days, never a counter; **Cycle** "moves the calendar 5 to 7 days". In-fiction names stay out.
- [ ] **Step 4: progress.** The roadmap's row and the outline's paragraph say "Done", with the date and a pointer to "As built". The chronicle list (part five) becomes "Next".
- [ ] **Step 5: As built.** The design's section 8, task by task: what changed from the design and why, the golden-seed figures before and after, and anything left for the author to judge in play.
- [ ] **Step 6:** `./check.sh`; commit "Give the game a storylet that waits for a journey, and record the calendar".
