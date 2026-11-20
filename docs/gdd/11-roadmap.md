# 11. Roadmap

A chapter of the [Starship Sim GDD](../gdd.md). Section numbers are permanent ids; cross-references such as "section 4.3" find their chapter through the index.

Each phase ends with something playable, a simulator run, and a short list of what the author learnt by playing. Art and online are separate tracks that can start whenever convenient.

| Phase | Adds | Playable result |
|---|---|---|
| 0. Skeleton | Core, bus, event log, content loader and validator, RNG streams, `SimRunner` running an empty cycle | Nothing to play; a green simulator run |
| 1. Thinnest game | 8 crew, 1 skill set, 1 single-cycle assignment template, 2 objective templates, 1 team, 6 encounters, checks with tiers, momentum and strain, 1 decision with the call rule, text report; mission bench (command line, then developer page) with the tuning file and tuning page; morale; 5 vignettes and 2 captain storylets; captain's time; inbox and crew file; chronicle list | A full cycle of both layers in the browser |
| 2. Growth and loss | Experience, mentoring, injuries, death, memorials, several objectives at some sites, unattended objectives, mandates | The central trade-off exists |
| 3. Who you send | Personality-driven calls and initiatives, operational parameters, gear and requisition | Team choice changes what you are asked |
| 4. Life aboard | Relationships, storylines with progress traits and traits that rise when left alone, ready-room talks, vignette budget, discretionary actions | Traits left alone get worse |
| 5. Roster | Departments, org chart, duty roster, shipboard training, department heads, digest, insight; finer skills grouped by department and attribute, with grades seen through insight | Football Manager's weekday |
| 6. Careers | Ageing, ranks and promotion, seniority, ambitions, poaching, personal and ship renown, Admiralty standing | Rosters turn over |
| 7. Split teams and long assignments | Two to three missions at once on an interleaved timeline; assignments of several cycles with carried state; transit cycles | One strong team or two thin ones |
| 8. Succession | Hull loss and succession, captain succession and pool, chronicle screen | A legacy survives its first disaster |
| 9. Scale | Larger hulls up to roughly 500, salience tuning, casting performance | Management by exception |
| 10. Families | Relatives of crew (siblings, cousins, and proteges) can arrive at any time; children join the fleet after real ageing | Names recur in the chronicle |
| Unplaced: a shared library of approaches | Approaches defined once and used by many encounters, while an encounter can still write one of its own. The author will design this; not enough is known yet | Not yet known |
| Track: online | Accounts, hosted Postgres, one legacy per player | Play from anywhere |
| Track: art | Portraits (paper-doll spec, compositor, first part set), planets, and a sector map | Faces in crew files; places on a map |
| Later | Dry dock, diplomacy, tactical grid, shared universe; much later, a way to write a speech of more than one paragraph in a script | |

Phase 1 uses the real architecture at the smallest size. Nothing in it is thrown away.

## 11.1 Progress

What has been built, slice by slice. A phase is cut into slices when it is planned, so later phases have one row until then. Every slice ends with `./check.sh` green and a review. Detail for Phase 1's slices is in `docs/plans/phase-1-outline.md`.

| Phase | Slice | What it adds | Status |
|---|---|---|---|
| 0. Skeleton | Whole phase | Core, event bus, event log, SQLite store, content loader, RNG streams, and an empty cycle in `SimRunner` | Done 2026-11-13 |
| 1. Thinnest game | 1a. Mission resolver | Checks with tiers, momentum and strain, decisions with the call rule, and the tuning file | Done 2026-11-14 |
| 1. Thinnest game | 1b. Mission bench | One mission run thousands of times from the command line, with before and after comparison | Done 2026-11-14 |
| 1. Thinnest game | 1c, part one. A whole cycle, headless | Crew that grow, are hurt, heal, and die; missions on the event bus; module state stored; the simulator plays real cycles | Done 2026-11-14 |
| 1. Thinnest game | 1c, part two. The cycle in the browser | Legacy selection, orders, briefing, resolution with paced beats and calls, aftermath, and the visual design | Done 2026-11-14 |
| 1. Thinnest game | 1c-editor. Content editor | Developer-mode pages that write the content files, validated on save; display names for crew, skills, gear, objectives, encounters, and assignments | Done 2026-11-14 |
| 1. Thinnest game | 1d. Reports | The grammar engine, lines chosen by salience, the log as prose with tags, the aftermath report, and lines, symbols, and pronoun sets in the editor | Done 2026-11-14 |
| 1. Thinnest game | 1d-authoring. Writing in place | Scripts (speech, notes, a report sentence, an attempt before a check, call scripts and button labels), the "What plays here" panel with live previews, and generated fiction in the game's pack | Done 2026-11-15 |
| 1. Thinnest game | 1d-review. Authored approaches | An encounter's approaches are the author's to name and tune, with requirements and outcomes of their own; the lead chooses by skill; "notice" becomes "initiative"; risk tolerance is removed; the encounter form gains a tab for each approach; the briefing's away team table restacks instead of scrolling | Done 2026-11-15 |
| 1. Thinnest game | 1e, part one. A storylet from a mission | An encounter's outcome or a decision's answer changes a story flag (every flag is a number) on the ship, the one who acted, the lead, or the team; a storylet whose flag requirements hold arrives in the aftermath as a dilemma with two to four options and fixed effects (loyalty and flags); at most two a cycle; the simulator's captain answers them; two storylets in the game's pack | Done 2026-11-16 |
| 1. Thinnest game | 1e, part two. Vignettes and morale | Morale for each crew member, moved by missions, hurt, deaths, rest, and a weekly drift, a plus or minus on checks at its extremes; vignettes on the storylet shape, with effects or one small choice and a default, arriving on flags, morale or loyalty, and four facts about the last cycle, with a cooldown; the captain's itinerary, a new first phase of the cycle where storylets and vignettes arrive, with the captain's time shown; five vignettes in the game's pack | Done 2026-11-17 |
| 1. Thinnest game | 1e, part three. Traits that bite | Story flags become traits (a quality is a stat or a trait); trait definitions in `content/traits/` with named levels, a cap, a briefing line, launch rules, a check bonus, a weekly step, expiry, and a rule for a pair's target dying; traits on a pair of crew; a new module, `Sim.Traits`; approaches that require or forbid a trait; the briefing's trait chips and what launching will do; the roster's trait cell (interim, until part five); the worked example, a shaken crew member stood down from the next away team, and `avoids`; the simulator's captain answering by `first` or `random`, with fire counts and "never fired". Designed and built in `docs/plans/2026-11-17-phase-1e-part-three-design.md` (see its "As built"). The simulator shows no game mission setting a trait in 20 legacies x 50 cycles, so no storylet fires unaided: the author's to judge in play | Done 2026-11-17 |
| 1. Thinnest game | 1e, part four. The storylet engine in full | The expression language over the state view (stats, traits, and each crew member's recent history, such as who was hurt or on a lost mission), on DynamicExpresso, checked at load; casting with up to three named slots; salience ranking in place of a fixed order; cooldowns per cast and per storylet; options with `requires`, and an option settled by a check with a handler the captain names; every storylet and vignette in both packs migrated; the itinerary's cast and handler list; Dev Play's "Why these storylets"; the simulator's never cast, check success, and losses on the budget; the worked example, "The one who came back" and "Still not talking". Added on 2026-11-17, split from part three. Designed and built in `docs/plans/2026-11-18-phase-1e-part-four-design.md` (see its "As built"). Department heads' routing (5.1 step 4) and the index (step 1) wait | Done 2026-11-19 |
| 1. Thinnest game | 1e-calendar. The calendar | Time kept in days and shown as dates in the Bastion Calendar (`docs/lore/calendars.md`); weeks of 6 days as the unit of mechanics; every timed effect due on a day; a cycle moves the calendar 1d3+4 days, rolled when it advances, and travel of N weeks one roll times N; `cyclesSince` and `daysSince` for storylets and traits in expressions; no week number, the cycle number in dev mode only. Added on 2026-11-20 (section 15, item 43). Designed in `docs/plans/2026-11-20-phase-1e-calendar-design.md` and planned in `docs/plans/2026-11-20-phase-1e-calendar-plan.md` | Next |
| 1. Thinnest game | 1e, part five. The chronicle list | A list of what the captain decided and who was lost. Renumbered on 2026-11-17 from part four; the crew file was split from it on 2026-11-20 and moved after 1h (part seven) | Later |
| 1. Thinnest game | 1e, part six. Writing storylets in the editor | Forms for storylets, vignettes, and traits with the "What plays here" panel, live previews against samples, and chips, as encounters have. Renumbered on 2026-11-17 from part five | Later |
| 1. Thinnest game | 1f. Tuning in play | The mission bench and the tuning page in the browser | Later |
| 1. Thinnest game | 1g. Captain's time | A budget each cycle spent from a menu of good things to do aboard, mostly mechanical with some narrative wrapping: a poker night, a captain's day or a concert (one or the other), a drill ordered through a named post. Needs a few named posts on the starting crew; departments and the org chart stay in Phase 5 | Later |
| 1. Thinnest game | 1h. Skills, attributes, and gear overhaul | A fuller skill list with attributes, and gear reworked to match. Risk tolerance, removed from the operational parameters in the 1d review, returns with it. Before the playtest, so the playtest tunes the mechanics it keeps. Moved on 2026-11-17 from "Unplaced", after the author's note on the GDD page. It settles what a character is, so the crew file (1e, part seven) follows it | Needs design first |
| 1. Thinnest game | 1e, part seven. The crew file | A page for each crew member (skills and attributes as 1h leaves them, morale, loyalty, and their shown traits), replacing the roster's interim trait cell. Split on 2026-11-20 from part five and moved after 1h, so the page is built once, on the finished shape of a character (section 15, item 42) | Later |
| 1. Thinnest game | Playtest | Three legacies of 20 to 30 cycles against the checklist below | Later |
| 2. Growth and loss | Not yet cut | Mentoring, memorials, several objectives at some sites, and mandates (experience, injuries, death, and unattended objectives arrived early, in 1c) | Not started |
| 3. Who you send | Not yet cut | Gear and requisition (personality-driven calls and operational parameters arrived early, in 1a) | Not started |
| 4. Life aboard | Not yet cut | Relationships, storylines and ready-room talks, traits that rise when left alone, the vignette budget, and discretionary actions | Not started |
| 5. Roster | Not yet cut | Departments, org chart, duty roster, training, department heads, digest, and insight; finer skills with department and attribute grades | Not started |
| 6. Careers | Not yet cut | Ageing, ranks and promotion, seniority (today a number on each crew member that picks a new lead when the lead falls; with ranks, rank and time in rank), ambitions, poaching, renown, and Admiralty standing | Not started |
| 7. Split teams and long assignments | Not yet cut | Several missions at once, assignments of several cycles, and transit | Not started |
| 8. Succession | Not yet cut | Hull loss and succession, captain succession, and the chronicle screen | Not started |
| 9. Scale | Not yet cut | Larger hulls, salience tuning, and casting performance | Not started |
| 10. Families | Not yet cut | Relatives and children of crew | Not started |
| Unplaced | A shared library of approaches | Approaches shared between encounters, beside an encounter's own | Not yet designed; the author's to design |
| Track: online | Not yet cut | Accounts, hosted database, and one legacy per player | Not started |
| Track: art | Not yet cut | Portraits (paper-doll spec, compositor, and the first part set), planets, and a sector map. Renamed on 2026-11-17 from "Track: portraits" | Not started |

Before Phase 2 starts, the Phase 1 playtest should show all of these:

1. The central trade-off bites: the author has hesitated between sending the best crew and developing a junior.
2. A loss landed: someone died or a mission was lost, and it mattered.
3. Calls feel right: zero to two per mission, and who was sent changed what was asked.
4. The numbers have been tuned in play at least once, including the dice.
5. Both layers feed each other: a storylet changed who was sent, and a mission outcome reached the inbox.
6. Authoring is sustainable: an encounter or a storylet takes minutes to add.
7. The author wants to play one more cycle.

The throwaway mission prototype (risk 1) was built and played on 2026-11-13. The author's verdict: it does not feel passive. Its balance findings are in section 4.
