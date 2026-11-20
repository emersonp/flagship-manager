# Starship Sim: Game Design Document v1

Status: draft v1.22, 2026-11-19. Split into one file per chapter, with every section number unchanged. Revised after the author's reviews (section 15), the mission prototype (section 4.7), the author's notes on skills and narrative structure after slice 1c (section 15, items 18 to 21), the review of slice 1d, which made an encounter's approaches the author's to write (section 15, item 24), and the first part of slice 1e, which brought story flags and the first storylets (section 15, items 32 to 38), and its second part, which brought morale, vignettes, and the captain's itinerary (section 15, item 39), and its third part, designed and then built, which turns story flags into traits that bite on missions and revisits the roadmap (section 15, item 40), and the quality-of-life changes that followed it: the last team picked again, deleting a legacy, and Dev Play decided by its route, with ids and links into the editor (sections 7.4 and 7.5), and its fourth part, designed and then built, which brings the storylet engine in full: the expression language over the state view, casting with named slots, salience, cooldowns, and an option settled by a check (sections 3.7, 3.8, 5.1, and 7.5; section 15, item 41).

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
