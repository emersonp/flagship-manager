# Phase 1, slice 1e-calendar: the calendar

**Spec:** `docs/gdd.md` sections 2.2, 2.3, 3.1, 3.2, 3.8, and 7.4, and section 15, items 40 to 43. **Lore:** `docs/lore/calendars.md` (the author's; read, never edited). **Builds on:** `main` at 2026-11-20 (`f51d1ac`), and slice 1e's first four parts.
**Status:** designed 2026-11-20; being built (section 8).
**Ends with:** the author starts a legacy on 13 Vortun 105. The top bar shows the date, not a week, and hovering over it shows "Stoneday, 13 Vortun 105". A cycle ends, and the report says the run was delayed: the calendar moves 7 days, to 20 Vortun. An injury written as two weeks heals on the day 12 days after it was taken, which may fall in the middle of a later cycle. A storylet that requires `storylet.a.daysSince >= 40` arrives only after the author presses Dev Play's "travel 5 weeks". Dev Play still shows the cycle number.

This records what the author said about the calendar on 2026-11-20, in a design interview, and designs it. Items marked **(my call)** were proposed by me, not asked for by the author.

## 1. What the author said (2026-11-20)

- **Mechanics are in weeks of 6 days.** Content and the player think in weeks ("time to heal: two weeks"). The engine keeps time in days.
- **Every effect is due on a day.** Something written as 5 weeks falls due 30 days later. Whether the calendar gets there in six 5-day cycles or a little over four 7-day cycles, it happens when that day passes. Repeating effects (a weekly drift, a step every 4 weeks) count from their own start in the same way.
- **A cycle runs from away mission to away mission.** A basic cycle moves the calendar 1d3+4 days: 5 is ahead of schedule, 6 on schedule, 7 delayed.
- **Travel of N weeks is one cycle** and moves the calendar (1d3+4) × N days, on **one roll**. One roll per week would flatten every trip to its average.
- **The length is rolled when the cycle advances** and is not known beforehand. A report line afterwards says how it went.
- **Time is kept in days and shown in weeks.** Every choice the player makes about time is in whole weeks. A duration is shown rounded to the nearest week, and as "under a week" below 4 days.
- **Cooldowns, "the last cycle", and history stay in cycles.** But storylets can also need days: B may need 3 cycles since A, and C may need 3 cycles and 40 days since A, so C comes 3 cycles later only after a long journey.
- **Expressions read days; content writes durations in weeks.** `weeks:` stays the only duration key in content.
- **There is no week number.** Play mode shows only the date. Dev mode also shows the cycle number.
- **The date is mostly short.** `13 Vortun 105`, in the top bar too. The full date, with the weekday, is shown on hover: `Stoneday, 13 Vortun 105`.
- **The Bastion Calendar** (`docs/lore/calendars.md`): 12 months of exactly 30 days (5 weeks), 6 named weekdays, years counted from the Bastion era. A legacy starts on the 3rd Stoneday of Vortun, 105: **13 Vortun 105**.
- **One calendar now.** The lore names other calendars with a conversion to come; the content is shaped so another can be added, with no conversion or choice yet.
- **Ages** advance on the 360-day year, and a crew member's birth date is rolled from the legacy's seed within the year their age implies.
- **No fixed-length setting.** Seeded dice already keep tests predictable; a test that needs a date sets the day directly.
- **Old saves are refused.**
- **A slice of its own**, before the chronicle list (1e, part five), so the chronicle has dates from its first entry.

## 2. Goals, non-goals, anti-goals

**Goals**
- The calendar replaces the week counter everywhere: state, events, saves, the top bar, Dev Play, the simulator, and tests.
- Every timed effect falls due on a day and happens on that day, in a stated order.
- A cycle's length is rolled, reported, and moves the calendar; "travel N weeks" exists as a rule and a Dev Play button.
- Storylet and trait timing can be read in expressions, in cycles and in days.
- The Bastion Calendar is content, so renaming a month is a data change.

**Non-goals**
- Real transit (a destination, pace, a transit screen, its own choices): Phase 7.
- Other calendars and conversions between them.
- Ageing that does anything: Phase 6. This slice only fixes how age is counted (section 4.10).
- Moving the length of a cycle from a story outcome (a delay caused by an encounter).

**Anti-goals**
- A week number, anywhere.
- A duration in content written in days.
- A cycle used as a unit of time for an effect.

## 3. Approaches considered

How the days a cycle passes over reach the modules that own timed effects.

**A. One event per day passed (chosen, my call).** The cycle publishes `DayReached(day)` once for each day the calendar moves through, in order. Each module handles what falls due that day, in the bus's fixed subscriber order. Date order across modules comes for free: a trait that expires on the 3rd day is gone before its step on the 5th. A 35-day trip is 35 events, which the log carries easily.

**B. One event for the whole span.** `CalendarAdvanced(from, to)`, and each module walks the days itself. Each module is in date order, but not across modules: the crew module would heal everyone for the whole span before the traits module saw its first day.

**C. Keep a week event and count marks.** Fire a weekly event per 6-day mark passed. This is the rule the author replaced: one-off effects would still round to weeks.

## 4. Design

### 4.1 Words

- **Day**: the unit the engine keeps time in. A legacy's date is a day number.
- **Week**: 6 days. The unit content, mechanics, and the player use for durations. Never a counter.
- **Date**: a day number written in the calendar: `13 Vortun 105`. The **full date** adds the weekday: `Stoneday, 13 Vortun 105`.
- **Cycle**: unchanged: one round from away mission to away mission. It has a **length** in days, rolled when it advances.
- **Calendar**: the content that names months and weekdays. In-fiction names (Bastion, Vortun, Stoneday) stay out of `GLOSSARY.md`, which fixes systems vocabulary.

### 4.2 The calendar in content

`content/calendars/base.yaml` (a new content kind, in the plural per `docs/conventions.md`):

```yaml
# The Bastion Calendar. Source: docs/lore/calendars.md (the author's; do not edit it from here).
- id: bastion
  name: Bastion Calendar
  weekdays:                       # the week's length is the number of weekdays
    - { id: stoneday, name: Stoneday, short: Stone }
    - { id: quorday, name: Quorday, short: Quor }
    - { id: kinclause, name: Kinclause, short: Kinclause }
    - { id: embertally, name: Embertally, short: Ember }
    - { id: menday, name: Menday, short: Menday }
    - { id: hearthfall, name: Hearthfall, short: Hearth }
  months:
    - { id: krellis, name: Krellis, days: 30 }
    - { id: vortun, name: Vortun, days: 30 }
    # ... zhortuna, moss, pharoska, pelphear (Pelpheär), barx, bast, venjenten, almonten, orphelten, othra
  start: { day: 13, month: vortun, year: 105 }
```

- `pack.yaml` names the calendar in use: `calendar: bastion`. A pack must name one that exists (my call).
- The loader checks: at least one weekday and one month, ids unique, every month at least 1 day, and the start date inside its month. It does not require a month to be whole weeks; the Bastion months are.
- **The week's length comes from the calendar** (6 here), and the loader turns every `weeks: N` in content and tuning into days with it. The test fixture's placeholder pack has its own calendar with placeholder names (`MONTH_A`, `WEEKDAY_A`) and the same shape: 6 weekdays, months of 30 days.
- The first day of the era, day 0, is the 1st of the first month of year 1, and a weekday's position counts from it (day 0 is the first weekday). In the Bastion Calendar every month begins on Stoneday, since a 30-day month is 5 whole weeks and a year is 60.

### 4.3 The date in state

- `LegacyState.Week` is replaced by `LegacyState.Day`: the day number, counted from the era's first day (my call: one number serves every date, including birth dates before the legacy, with no conversion). 13 Vortun 105 is day (105 − 1) × 360 + 30 + 12 = 37482.
- The legacy also keeps its **start day**, the anchor for effects that repeat on the week (section 4.5).
- `Cycle` is unchanged.

### 4.4 A cycle moves the calendar

When the aftermath ends and the cycle turns:

1. **The length is rolled** from its own stream, `calendar`, so no other roll moves: a whole number from `cycle.minDays` to `cycle.maxDays` in `content/tuning/calendar.yaml` (5 and 7: 1d3+4). For travel of N weeks, the one roll is multiplied by N.
2. **`CycleLengthRolled(days, plannedDays)`** is published, where `plannedDays` is one week (6), or N weeks for travel. It is what the report line reads.
3. **The cycle turns** (`Cycle` + 1), and whatever happens once per cycle runs, as now: the morale lift for a fit member who was not sent ("rest") stays per cycle, because it is about not being sent, not about time.
4. **For each day passed, in order, `DayReached(day)`** is published, and `Day` moves to it before the event runs.

The report line, at the top of the itinerary that follows (my call on where; settled while building, section 8): ahead of schedule (fewer days than planned), on schedule (the same), or delayed (more). Its words are lines in the game's pack, so the author writes them. For travel, the line says how many days early or late.

**Travel in Dev Play.** A Dev Play button, "Travel N weeks" (N from 1 to 10), shown in the advance phase, turns the cycle as a trip would: one cycle, one roll times N, and every day passed. Real transit, a cycle with no mission, is Phase 7.

### 4.5 Effects on days

Everything that happened "at a week" now happens on a day.

| Effect | Due | Owner |
|---|---|---|
| A light injury heals | The day taken + `injury.lightWeeks` × 6 | Crew |
| An incapacitated member returns | The day taken + `injury.incapacitatedWeeks` × 6 | Crew |
| Morale drifts towards resting | Every 6th day from the legacy's start day (every Stoneday from 13 Vortun) | Crew |
| A trait expires | `expires.weeks` × 6 days after its last change | Traits |
| A trait steps | `every.weeks` × 6 days after its last change; the step restarts the count | Traits |

**On one day, the order is the bus's subscriber order:** the crew module (healing, then returns, then the drift), then the traits module (expiry, then steps, in the order traits were first set, as now). Section 5.4 states it as a rule, and a test holds it.

Stored fields become days: `HealsAtDay` (each light injury), `ReturnsAtDay`, a trait's `ChangedAtDay`. A trait also keeps `ChangedAtCycle`, for `cyclesSince` (section 4.6).

### 4.6 Timing in expressions

Four new readings in the state view (section 3.8 of the GDD):

| Read | Type | Meaning |
|---|---|---|
| `storylet.s.cyclesSince` | number, or unset | Cycles since storylet `s` last arrived, anywhere on the ship |
| `storylet.s.daysSince` | number, or unset | Days since it last arrived |
| `x.trait.t.cyclesSince` | number, or unset | Cycles since the trait last changed on `x` |
| `x.trait.t.daysSince` | number, or unset | Days since it last changed |

- **Unset** means it never happened (or a trait is at 0, which is not stored). Any comparison with an unset reading is false, so `storylet.a.daysSince >= 40` and `storylet.a.daysSince < 40` are both false before A has arrived. "A has arrived" is then `storylet.a.cyclesSince >= 0` (my call; the builder may add a plainer `storylet.a.arrived` if the rewrite makes it cheap).
- The storylet id and the trait are checked at load, as other reads are ("No storylet named nosuch").
- **Arrived** means it was offered on the itinerary. Storylets are always answered in the cycle they arrive, so arrival and answer fall on the same cycle and day.
- These read whole days and cycles. A duration an effect sets stays in weeks.
- `x.trait.t` stays a number, and `.toward(y)` is unchanged. If DynamicExpresso cannot read `x.trait.t.daysSince` beside the number, the builder picks a form such as `x.traitDaysSince('t')` and records it in "As built".

The worked example in the game's pack (my call): one storylet that needs 40 days since another, so it waits for a trip. The author rewrites its words.

### 4.7 What the player sees

- **The top bar** shows the short date, `13 Vortun 105`, where it showed the week and cycle. Hovering shows the full date. Narrow screens use the weekday's short form in the full date (`Stone, 13 Vortun 105`).
- **The full date is rare:** on hover, and where a date stands alone as a heading (the chronicle, later). Everywhere else a date is short.
- **Durations are in weeks**, rounded to the nearest week: 4 to 8 days is "1 week", 9 to 14 "2 weeks", and under 4 days "under a week". The roster's heal and return times, a trait chip's time left, and anything later that counts down use one formatter, in `Web`.
- **Nothing shows a week number or a day number.** The cycle number appears only in dev mode: the top bar's dev badge, Dev Play, and the simulator.
- The list of legacies shows each legacy's date where it showed its week.

### 4.8 Ages

No crew member has an age in code or content yet; ageing is Phase 6. This slice fixes only the rule, in the GDD: a year is the calendar's days in a year (360), and a crew member written with `age: 31` gets a birth date rolled from the legacy's seed within the year that makes them 31 on the start day. Nothing is built for it.

### 4.9 Dev Play, the simulator, and the bench

- **Dev Play** shows the cycle number and the date, a trait's last change as a date, and the "Travel N weeks" button.
- **The simulator** reports dates where it reported weeks, and the spread of cycle lengths it rolled (a check that the roll is 5, 6, or 7, about a third each).
- **The bench** runs single missions and is unaffected.

### 4.10 Saves and tests

- **Old saves are refused**, with the message other format changes have used.
- The `legacy` table's `week` column becomes `day`, and the store keeps the start day.
- **The test runs with fixed seeds change once, on purpose:** every cycle now rolls a length, so heal, return, drift, and trait dates move. Each changed figure is checked to be the calendar, not a slip.
- A test that needs a particular date sets `Day` directly; there is no fixed-length setting.

## 5. Architecture

### 5.1 Modules

- **`Sim.Cycle`** owns the date: it rolls the length, turns the cycle, and publishes `CycleLengthRolled` and `DayReached`. It takes the pack at registration, for the calendar and the tuning. It publishes the days one at a time (changed while building, 2026-11-20): the first after everything that heard `CycleAdvanced` has run, each later one after the day before and everything it set off, and the itinerary's `PhaseEntered` after the last. A subscriber of its own, ahead of every module, moves `Day` to the day reached. It handles a new command, `Travel(weeks)`, offered only by Dev Play and accepted only in the advance phase, where it turns the cycle as `AdvancePhase` would, with N weeks planned in place of one (changed while planning, 2026-11-20: turning a cycle from the orders would need a cycle with no mission, which Phase 1 does not have).
- **The calendar's arithmetic lives in `Sim.Core`** (changed while planning, 2026-11-20, from a library of its own: it is a content type and a few pure functions, with no dependencies, which is what `Sim.Core/Content` already holds): converting a day number to a date and back, the weekday, and weeks to days. `Sim.Content` loads it; `Web` formats with it.
- **`Sim.Crew`** and **`Sim.Traits`** move from `WeekAdvanced` to `DayReached`, with the stored fields in days. Rest moves to `CycleAdvanced`. Content keeps its durations in weeks (`LightInjuryWeeks`, `ExpiresWeeks`, `TraitStep.Weeks`); each module turns them into days with `Calendar.Days` when it sets a due day or compares one.
- **`Sim.Storylets`** records the day as well as the cycle each storylet last arrived, and serves the new readings.
- **`Web`**: the top bar, the date and duration formatters, Dev Play's travel button, the report line. The report line's words are lines for a `schedule` beat in the pack (`Sim.Core.Narrative.NarrativeFacts`), chosen and expanded by the lines engine as a storylet's are, apart from any mission; `Web.Bridge.Schedule` builds the situation from `CycleLengthRolled` and falls back to fixed words when no line holds (changed while building, 2026-11-20). The line sits at the top of the itinerary that follows the turn. `CycleLengthRolled` is stamped with the cycle that ended, so the bridge reads it from that cycle's events.

### 5.2 Contracts in `Sim.Core`

- `LegacyState.Week` becomes `Day`; `StartDay` is added.
- `WeekAdvanced` is removed; `DayReached(int Day)` and `CycleLengthRolled(int Days, int PlannedDays)` are added, as new event types at version 1.
- `CrewInjured.ReturnsAtWeek` becomes `ReturnsAtDay`.
- `Travel(int Weeks)`: a command.

### 5.3 Persistence

- `legacy.week` becomes `legacy.day`, and `legacy.start_day` is added. The list of legacies reads the day.
- The schema version moves, so an old save is refused.

### 5.4 Rules that hold

- Every timed effect is due on a day, and happens when `DayReached` reaches it, never earlier and never on a cycle boundary.
- On one day: crew before traits; within crew, healing, returns, then drift; within traits, expiry then steps.
- The cycle's length uses only the `calendar` stream.
- No screen in play mode shows a week number, a day number, or the cycle number.
- Content writes durations only in weeks; the loader refuses `days:` (my call), with a message that quotes `docs/conventions.md`.

## 6. Roadmap and GDD changes (applied when built)

- **GDD 2.3, cycle length:** the rewrite of this section: days, a rolled length, effects on days, travel as one roll. "Most last one week" becomes the 1d3+4 rule.
- **GDD 3.1, 3.2, 3.8:** fields in days (`healsAtDay`, `changedAtDay`), the four new readings, and the age rule.
- **GDD 7.4:** the top bar's date, the hover, and the duration rule.
- **`GLOSSARY.md`:** **Day**, **Date**, **Calendar**; **Week** rewritten as a span of 6 days, not a counter; **Cycle** loses "usually one week long" for "moves the calendar 5 to 7 days".
- **`docs/conventions.md`:** the `calendars` content kind; durations in `weeks:` only.
- Roadmap, the outline, and section 15, item 43 were updated when this was designed (2026-11-20).

## 7. Open, and left to the builder

- Where the report line about the cycle's length sits: the aftermath, the itinerary, or both.
- The exact message for a refused save, and for a `days:` key.
- Whether `storylet.s.arrived` is worth adding beside `cyclesSince >= 0`.
- The form of the trait readings if DynamicExpresso can't put a property on a number (section 4.6).
- The game's pack: the report lines' words and the worked example's storylet, written in place and left for the author to rewrite.

## 8. As built

Kept as each task of the plan lands. Differences from sections 4 and 5 are said here, and section 5 is changed to match.

### Task 1: the calendar as content (2026-11-20)

As designed: `CalendarDef`, `WeekdayDef`, `MonthDef`, `CalendarDate`, and `CalendarTuning` in `Sim.Core/Content/CalendarContent.cs`, and `Sim.Core.Calendar` with `DayNumber`, `Date`, `Weekday`, and `Days`. `ContentPack` carries `Calendar` (the one `pack.yaml` names) and `CalendarTuning`, both required. 13 Vortun 105 is day 37482, a Stoneday. `Date` also converts a day before day 0 (a birth date, later), counting years down from 1 the same way.

Small calls made while building:

- **A weekday's `short` is optional** and falls back to its name; the Bastion Calendar writes one for every weekday anyway. A weekday's and a month's `name` are required, since they are what a date shows. A calendar's `name` is required too.
- **Calendar ids are their own namespace**, like traits and symbols: lower case, unique among calendars, and free to repeat an item's id. Weekday and month ids are unique within their own list.
- **A refused calendar is reported once**, in its own file: `pack.yaml` reports only a calendar that is missing or defined nowhere.
- **The editor** lists `tuning/calendar.yaml` ("Calendar tuning") and `calendars/base.yaml` ("Calendars") among its text files (`Web/Forms/TextFiles.cs` and the index), checked with the whole pack on save. The plan named `ContentKinds.cs` and `ContentModels.cs` for this; neither needed to change, since a calendar is not an item kind with a form.
- The fixture's placeholder calendar is `calendar_a` (`WEEKDAY_A` to `WEEKDAY_F`, short `WD_A` and so on; `MONTH_A` to `MONTH_L`), starting on 13 `month_b` 105, so it matches the game's day number.

### Task 2: days replace weeks (2026-11-20)

As designed: `LegacyState.Day` and `StartDay`, both set to the calendar's start day at founding; `WeekAdvanced` gone, and `CycleLengthRolled`, `DayReached`, and the command `Travel(weeks)` added; `CrewInjured.ReturnsAtDay`; the crew's `LightInjuriesHealAtDay` and `ReturnsAtDay`, and a trait's `ChangedAtDay` and `ChangedAtCycle`. The store's `legacy` table has `day` and `start_day`, and the export carries both; the schema is 13 and the export format 11, and an older one is refused with the message used before.

Changed or settled while building:

- **The days are chained, not published all at once.** The bus queues events, so a turn that published every `DayReached` at once would put a day's own events (a recovery, an expiry) after every later day in the log, though the modules would still have run in date order. The cycle module publishes the first day from its own `CycleAdvanced` subscriber, after rest, and each next day from its own `DayReached` subscriber, after the crew and traits modules; the log reads in date order. Section 5.1 says so.
- **Where content's weeks become days.** Design 4.2 has the loader turn every `weeks:` into days. The modules do it instead, with `Calendar.Days`, as the plan's steps say: the content types keep weeks, so the editor and the loader's messages still speak in weeks.
- **On one day, rest is not in it.** Rest runs on `CycleAdvanced`, before the first day; on a day the crew module heals light injuries, then returns the recovering, then drifts (on every 6th day from `StartDay`), for everyone at once rather than member by member. Neither order changes a figure: drift does not read injuries.
- **`Travel`** is guarded by the cycle module alone: founded, in the advance phase, and 1 to 10 weeks (`Travel.Most`). It has no Dev Play button yet; Task 5 adds it.
- **The readers show the raw day** (Step 8): the top bar's "Day 37482", the legacies list's "Day", the roster's "returns day N" and "heals day N", the aftermath's lines, and Dev Play's "Day" column. Task 5 replaces them.
- **The golden seeds.** `Golden_seed_1` and the CLI's 2 × 3 summary are re-pinned (235 events to 263). With the roll pinned at 6 days in a scratch copy of the fixture, SimRunner's 200 × 200 matches the build before this one exactly under both answer policies, but for 6 more events a turn (one `CycleLengthRolled` and six `DayReached` where one `WeekAdvanced` was): the calendar stream moves no other. With the roll, 1d3+4:

| Run (seed 1) | Before | After |
|---|---|---|
| Fixture, 20 × 50 (check.sh) | 40169 events; 906 full, 84 partial, 6 incomplete, 4 withdrawn, 0 lost; 1 death; storylets 10.4 offered, 9.9 answered | 46064 events; 905, 85, 6, 4, 0; 1 death; 10.3, 9.8 |
| Fixture, 200 × 200, first answers | 38834 full, 1028 partial, 101 incomplete, 32 withdrawn, 5 lost; 5 deaths | 38826, 1033, 103, 32, 6; 5 deaths |
| Game, 200 × 200, first answers | 38816, 1033, 103, 34, 10; 8 deaths | 38805, 1039, 105, 36, 11; 8 deaths |
| Game, 200 × 200, random answers | 38700, 1076, 118, 70, 36; 24 deaths | 38672, 1095, 123, 74, 36; 22 deaths |

  The small moves are injuries healing and the incapacitated returning between cycles rather than on them, which changes who is fit to send.

### Task 3: the report line for the cycle's length (2026-11-20)

As designed: the line says ahead of schedule, on schedule, or delayed, and for a journey how many days early or late, and the author writes its words in the pack.

Settled while building:

- **The words are lines.** The lines engine already serves storylets apart from a mission, so a new beat, `schedule`, cost little. Its facts are `schedule` (`ahead`, `on`, or `delayed`), `days` (how many days early or late; 0 on schedule), `weeks` (the weeks planned), and `travel` (true for a journey of more than one week); `#days#` says the days early or late ("4 days") and `#planned#` the weeks planned ("5 weeks"). No fact or token gives the cycle's length, so a line cannot show it. The editor's Lines page lists the beat as "The cycle's length". With no line that holds, `Web.Bridge.Schedule` says it in fixed words ("The ship arrived delayed."; "The journey, planned at 5 weeks, arrived 4 days late.").
- **Where it sits.** At the top of the itinerary that follows the turn, under its heading, and nowhere else: the aftermath comes before the roll. The first itinerary of a legacy, which no turn began, has none.
- **Which cycle holds the roll.** `CycleLengthRolled` is published before the cycle turns, so it is stamped with the cycle that ended, as its roll's stream is. Rather than move the stamp (and the golden seeds a second time), the bridge reads it from that cycle's events, stored or not yet committed, as the briefing already reads the last cycle's team. `BridgeReader.Read` takes it as `turned`.
- **The same line on a reload.** A tie between lines is broken by the legacy's seed and the cycle, so the page drawn again from the store says what the page drawn in the turn said.
- **The game's pack** has six lines, one per case for a cycle and for a journey, generated for the author to rewrite (`LINE_SCHEDULE_*` in `content/lines/base.yaml`). The fixture has placeholders.
