# Flagship: Ready Room, game design document v1

Status: draft v1.32, 2026-11-22. Split into one file per chapter, with every section number unchanged. Revised after the author's reviews (section 15), the mission prototype (section 4.7), the author's notes on skills and narrative structure after slice 1c (section 15, items 18 to 21), the review of slice 1d, which made an encounter's approaches the author's to write (section 15, item 24), and the first part of slice 1e, which brought story flags and the first storylets (section 15, items 32 to 38), and its second part, which brought morale, vignettes, and the captain's itinerary (section 15, item 39), and its third part, designed and then built, which turns story flags into traits that bite on missions and revisits the roadmap (section 15, item 40), and the quality-of-life changes that followed it: the last team picked again, deleting a legacy, and Dev Play decided by its route, with ids and links into the editor (sections 7.4 and 7.5), and its fourth part, designed and then built, which brings the storylet engine in full: the expression language over the state view, casting with named slots, salience, cooldowns, and an option settled by a check (sections 3.7, 3.8, 5.1, and 7.5; section 15, item 41), and a review of the roadmap that moves the crew file after the skills overhaul (section 11.1; section 15, item 42), and the calendar, designed and then built as slice 1e-calendar, a slice before the chronicle list: time kept in days and shown as dates, a cycle that moves the calendar a rolled 5 to 7 days, and every timed effect due on a day (sections 1, 2.3, 3.1, 3.2, 3.8, and 7.5; section 11.1; section 15, item 43), and slice 1e's fifth part, the chronicle, designed and then built: a new module, `Sim.Chronicle`, writing each entry once with frozen text, the one stored use of the grammar engine, read by storylets as `chronicle.count` in days, and shown on a Chronicle page (sections 3.8, 3.9, 6, 7.1, 7.5, and 11.1; section 15, item 44), and the design of slice 1e's sixth part, writing storylets in the editor: a builder for expressions, notes in place of comments, and test ships for the previews, with the online track widened to carry the tablet (section 11.1; section 15, item 45; section 16, question 6), and that part built: forms for storylets, vignettes, and traits, the expression widget, notes, test ships with "Save as test ship" in Dev Play, and the worked example, "Time for it later", written in the browser (sections 7.5 and 11.1), and the design of slice 1f, tuning in play, cut into two parts: the mission bench in play, and legacy mode (section 11.1; section 15, item 46), and the answers to the six open questions, which drop an objective's type, base stakes, and separate unattended costs, leave rewards to Phase 6, drop ending a stage early, and move every editor preview onto test ships, placed as slice 1f-previews after 1f part one (sections 3.4, 3.5, 11.1, and 16; section 15, items 47 to 52), and slice 1f's first part, the mission bench in play, built: the Tuning page, a draft of the tuning file saved in place beside the mission bench, saved and draft on the same seeds, real teams from a legacy or a test ship, every objective, replay as the report, and its entry points, with the worked example that halved the strain of a failed check and took rookie loss from 34.7% to 18.1% (sections 4.7, 7.5, 10.1, and 11.1), and an architectural review of the code and the roadmap after it, whose five priorities join the roadmap: slice 1h-seams before 1h, 1h designed next, saves that last the playtest, modules that own their content before Phase 2, and content kept in git and served online as published snapshots each legacy pins (sections 11 and 11.1; section 15, item 53; section 16, question 7), and the design of slice 1f-previews: encounter, objective, and line previews on real runs on test ships, searched and counted, in place of slice 1d's samples (section 11.1; section 15, item 54; section 16, question 8), and the game's name, Flagship: Ready Room, with the GDD published as two pages, progress and the whole (section 15, item 55).

Vocabulary follows `GLOSSARY.md`; ids and names follow `docs/conventions.md`; the games and shows the design draws on are listed in `docs/touchstones.md`. Creative content is placeholder throughout this document (`SPECIES_A`, `OBJECTIVE_TEMPLATE_1`, `SKILL_A`); the game's own pack in `content/` holds generated fiction, by the author's one-time exception (section 15, item 23); numbers are starting points for the balance simulator, not commitments.

## How this document is organised

- **One chapter, one file**, in `docs/gdd/`, named by its number: `docs/gdd/04-mission-resolution.md`.
- **Section numbers are permanent ids.** Code, tests, and plans cite them ("GDD 4.2", "section 15, item 24"), so a chapter keeps its number wherever it is listed. A new chapter takes the next unused number, from 17. A removed section leaves its number unused.
- **Parts group chapters for reading.** A chapter can move to another part without being renumbered, so the numbers within a part need not run in order.
- **This file is the index.** It holds the status line and the table of chapters, and nothing else. Plans written before v1.17 cite `docs/gdd.md` and a section number; the number finds the chapter in the tables below.
- **The page** is built from this index and the chapter files by `tools/gdd-page/`. Each chapter's description below appears on the page.

## The game

What the player does, and why it is interesting. Read these first.

| # | Chapter | What it holds |
|---|---|---|
| 1 | [Summary](gdd/01-summary.md) | The pitch, what choosing an away team weighs, and the settled decisions |
| 2 | [Core loop](gdd/02-core-loop.md) | Assignments, sites, objectives, and missions; the six phases of a cycle; cycle length and transit |
| 9 | [Combinatorics](gdd/09-combinatorics.md) | How content multiplies, and what each new item yields |
| 12 | [Art](gdd/12-art.md) | Portraits, the interface, icons, and ageing |

## Systems

How each system works, as rules.

| # | Chapter | What it holds |
|---|---|---|
| 4 | [Mission resolution](gdd/04-mission-resolution.md) | The check, momentum and strain, calls and initiatives, growth and harm, the resolver contract, and tuning |
| 5 | [Storylet system](gdd/05-storylets.md) | Quality and salience, selection each cycle, captain storylets and vignettes, storylines, captain's time, delegation, and insight |
| 6 | [Narrative report generation](gdd/06-narrative.md) | Beats, lines chosen by salience, scripts, and a guide to writing lines |

## Reference

The shape of every kind of content and state, field by field.

| # | Chapter | What it holds |
|---|---|---|
| 3 | [Data model](gdd/03-data-model.md) | Crew, traits, relationships, templates, encounters, gear, storylets, qualities and flags, the event log and chronicle, and skills |

## Engineering

How the game is built and checked.

| # | Chapter | What it holds |
|---|---|---|
| 7 | [Architecture](gdd/07-architecture.md) | Modules, the event bus, determinism, the save format, and the front end |
| 8 | [Engine recommendation](gdd/08-engine.md) | Why C# and Datastar, and the seams for a later move |
| 10 | [Balance simulator](gdd/10-balance-simulator.md) | `SimRunner`, its policies, metrics, and alarms, and the mission bench |

## Records

Plans, progress, and the history of decisions.

| # | Chapter | What it holds |
|---|---|---|
| 11 | [Roadmap](gdd/11-roadmap.md) | The phases, and progress slice by slice |
| 13 | [Pushback](gdd/13-pushback.md) | Where the brief was changed or should be watched |
| 14 | [Risks](gdd/14-risks.md) | The ten risks and their mitigations |
| 15 | [Decisions from review](gdd/15-decisions.md) | The author's answers and notes, numbered, by date |
| 16 | [Open questions](gdd/16-open-questions.md) | Cheap choices still to settle |
