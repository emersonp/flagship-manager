# 2. Core loop

A chapter of the [Flagship: Ready Room GDD](../gdd.md). Section numbers are permanent ids; cross-references such as "section 4.3" find their chapter through the index.

## 2.1 Three levels

- An **assignment** is what the ship has been sent to do. It lasts one to about four cycles, and there is usually only one at a time. Some are **mandates**.
- Each cycle of an assignment presents a **site** with one to four **objectives**: the things an away team could do there. Most sites offer one, so the usual cycle is one team on one mission. A site with several is the frequent exception, and is where the opportunity-cost choice lives.
- A **mission** is one away team sent on one objective.

The player often has no choice of assignment. The choice is in how to carry it out: which objectives to attempt, and with whom. An objective nobody is sent on still resolves, **unattended**, and usually badly. Sending two or three teams means attempting several objectives at once with thinner teams, which is the same dilemma as team selection: one strong team on one objective, or weaker teams on more of them.

What happens at one site can change the objectives offered at the next.

## 2.2 One cycle

One cycle has six strict phases. The **duty roster** sits outside them and can be edited at any time.

1. **Captain's itinerary.** Storylets that grew out of the last cycle wait for an answer, and vignettes arrive, to be read or answered in any order; the captain's time is shown here. Built in slice 1e, part two: every storylet must be answered before the orders, and a vignette left alone takes its default.
2. **Orders.** The inbox shows the current site and its objectives. Between assignments it shows the assignments on offer, or the mandate. Declining a mandate needs enough Admiralty standing, and costs some of it.
3. **Briefing.** The player chooses which objectives to attempt and builds one away team for each, up to three. For each team they name the lead, choose a loadout from requisition, and set **operational parameters**.
4. **Resolution.** Missions advance encounter by encounter on one interleaved timeline. **Calls** pause a mission until answered; **initiatives** arrive as they happen. Unattended objectives resolve at the end.
5. **Aftermath.** Reports, injuries, and growth land. Since slice 1e, part two, storylets fire at the next itinerary instead, where the player will spend **captain's time** (slice 1g). Department heads resolve what falls below their threshold and file a **digest**.
6. **Advance.** Unattended storylines progress, time moves on by the cycle's length, offers expire, and the event log is committed.

Crew who are away are not cast in shipboard storylets that cycle.

Target length for a routine cycle is five to ten minutes. A site worked by three teams is allowed to take longer; that is the game's set piece.

## 2.3 Cycle length

A cycle is one decision point, not a fixed span: it runs from one away mission to the next. The engine keeps time in **days** and shows it as **dates** in the ship's calendar, which is content (`content/calendars/`, named by `pack.yaml`): the game's is the Bastion Calendar, with weeks of 6 days and a year of 12 months of 30 days, and a legacy starts on 13 Vortun 105. Content and the player think in **weeks**; a week is 6 days, a span and never a counter.

- **A cycle's length is rolled when it turns**, and is not known before: 1d3+4 days (`content/tuning/calendar.yaml`, `cycle: { minDays: 5, maxDays: 7 }`), drawn from its own stream, `calendar`, so no other roll moves. 5 days is ahead of schedule, 6 on schedule, and 7 delayed; a line at the top of the next itinerary says which, in words the pack writes. No play screen shows the length or the cycle's number.
- **Travel of N weeks is one cycle**, and moves the calendar by one roll times N: a 5-week journey takes 25, 30, or 35 days. One roll per week would flatten every trip to its average. The report line says how many days early or late it arrived. As built in slice 1e-calendar, only Dev Play travels ("Travel N weeks", 1 to 10); real transit is Phase 7.
- **Every timed effect is due on a day.** Something written as 2 weeks falls due 12 days after it began, and happens when the calendar passes that day, which may be in the middle of a later cycle. Healing, a return to duty, a trait's expiry, and a trait's step count from their own start; the morale drift runs every 6th day from the legacy's start day. The days of a turn are passed one by one, so nothing due on a later day happens before something due on an earlier one, and on one day the crew module runs before the traits module.
- **Rest stays per cycle.** A fit member who was not sent recovers once per cycle, before its first day, since rest is about not being sent, not about time.
- **Durations are written in weeks**, in content and tuning alike (`weeks:`); the loader refuses one written in days (`docs/conventions.md`). On screen a duration is shown in weeks, rounded to the nearest (section 7.5).

**Transit** between assignments is a single longer cycle of several weeks with no missions: training, healing, ageing, and storylines run for the whole span, and the inbox shows one combined digest. Transit has its own, different choices in place of missions, and a larger fixed budget of captain's time, so it is when a captain catches up with the crew. The player also sets the pace, within what the hull can do: a faster transit takes fewer weeks, and costs the crew: less training and recovery time, and lower morale. Hull wear is added when dry dock exists.

Everything time-based (ageing, healing, training, and expiries) is measured in days, written in weeks, never in cycles, so a long transit and a run of busy cycles are handled by the same rules. A storylet's cooldowns are the exception: they count cycles (section 3.7), since they are about how often the captain is asked, not about time aboard.

How it got here: until slice 1e-calendar (2026-11-20) a cycle was one week, the engine counted weeks, and every timed effect fell due on a cycle. The design is `docs/plans/2026-11-20-phase-1e-calendar-design.md`.
