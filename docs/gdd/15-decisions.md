# 15. Decisions from review

A chapter of the [Starship Sim GDD](../gdd.md). Section numbers are permanent ids; cross-references such as "section 4.3" find their chapter through the index.

Answered by the author on 2026-11-13.

1. **Cycle length.** One week by default, and variable: see 2.3.
2. **Declining a mandate.** Allowed only above an Admiralty standing threshold, and it costs standing. The cost grows on repeat.
3. **Team lead.** The player names the lead, defaulting to the highest rank.
4. **Visibility.** Bands in words, gated by insight and the org chart: see 5.6.
5. **A new hull after a loss.** The player picks a core of survivors to carry over. The rest are reassigned.
6. **Class upgrades.** Several routes, all rare: Admiralty offers the player may decline, and storylines.
7. **Families.** Relationships now. Later (Phase 10), time stays honest: children join only after real ageing, and other relatives (siblings, cousins, and proteges) can arrive at any time.
8. **Portraits.** Composited on the server into a cached image.
9. **Content format.** YAML with a schema.
10. **Simulator alarm bands.** Start at 1 to 3 deaths per 100 crew-missions and 40 to 60 cycles to mid-skill, then adjust from play.
11. **The captain on away missions.** Sometimes, at a cost in captain's time.
12. **The unit of a player's game** is a **legacy**.
13. **Transit.** One long cycle with a combined digest, its own choices, and a player-set pace bounded by the hull: see 2.3.
14. **Insight accuracy.** Only vague at first. Distortion comes later through traits of the observed and of the observer: see 5.6.
15. **Unattended objectives.** Set per objective by the author, defaulting to a roll against difficulty with no team.
16. **Captain's time in transit.** A larger fixed budget, not scaled by weeks.
17. **The cost of a faster transit.** The crew pays: less training and recovery time, and lower morale. Hull wear is added when dry dock exists.

Notes from the author on 2026-11-14, after slice 1c:

18. **Skills.** Basic skills stay for prototyping. Later, skills are finer, each belongs to a department, an attribute, or (usually) both, and the skills under one give a grade that department heads see with varying insight: see 3.10 and 5.6. Built in Phase 5.
19. **Vignettes may have a choice**, small in scope: see 5.2.
20. **Narrative structures.** Quality-based and salience-based, each where it fits: see section 5, 5.3, and section 6. Salience arrives first, choosing report lines in slice 1d. Waypoint narrative was a third structure here until v1.13, when the author dropped it: its distance score was a data model nobody had defined, and qualities with salience give storylines and conversations the same behaviour. After 1d the author added: the narrative of an encounter, an objective, or any other storylet is written inside it, and only what belongs to none of them is a shared line; selection is unchanged (section 6, "Where lines live").
21. **A hybrid model.** Skills and the other Football Manager numbers are proper stats; story state is qualities: see 3.8.
22. **Writing is a first-class part of the game's tools** (after slice 1d). The browser editor is where narrative is written; the files stay readable. An item's narrative is written in the item, on one list of its beats, with previews as the author types. A line's text can be a script, so a call can be an exchange and a check can be an intention, a silence, and an outcome. Built as slice 1d authoring; the reusable parts are meant for every later storylet kind.
23. **Generated fiction in the game's pack, once.** To see lines in use, the author authorised a one-time exception to the placeholder rule on 2026-11-15: `content/` now holds generated names and words (an original setting in the spirit of starship exploration drama), on the placeholders' ids and numbers. The rule stands everywhere else: this document, the test fixture, and anything the systems ship. The author will critique and replace it.

Notes from the author on 2026-11-15, in the review of slice 1d (design in `docs/plans/2026-11-15-phase-1d-approaches-design.md`):

24. **Authored approaches.**
    - An encounter's approaches are written by the author: one to four, usually two, each named and tuned for its situation. "Careful" and "fast" were the engine's ideas, not the author's. The game's pack and the fixture were converted, the old two becoming the first and second approach and keeping their ids.
    - An approach may need something of the team (a skill level on someone standing, or a gear tag) before it is available. The author will not write a fourth requirement until the form edits more than three.
    - The lead chooses, among the available approaches, the one whose skill is highest for the lead after injury, and a tie goes to the first listed. Personality no longer weighs the choice, so risk tolerance had no job and is removed from the parameters; it returns with the skills, attributes, and gear overhaul. Choosing by personality trait is for when crew have more than caution and discipline.
    - Each tier of an approach may write its own effects; one it does not write takes the tuning file's default, which are the numbers in use before. The momentum that fast earned by rule is gone from the tuning file and written into the converted fast approaches.
    - "Notice" is renamed "initiative" throughout.
    - A shared library of approaches, used by many encounters beside an encounter's own, is wanted later and is the author's to design (section 11, unplaced).
    - Saves from before are refused, not converted: the store's schema version is 6.
    - Two consequences of choosing by skill, measured on the bench and accepted for now. "Develop juniors" costs about 1 point of full success where it cost 2 or more. Crew gain levels more slowly: a test that saw a level in 20 cycles now needs 30. Both are the author's to tune. The bench's figures, before the 1d review and after it, are in section 4.7.
    - The briefing's away team table restacks below 56rem instead of scrolling inside its card.

Answered by the author on 2026-11-15, from the open questions:

25. **How grades are shown.** Words at the lower insight levels, letters at the higher: see 5.6.
26. **How a grade is computed.** The plain mean of the skills under it, with the shape reported separately: see 3.10.
27. **How many insight levels.** Four: hidden, rough, clear, and keen. Keen is the level that shows a grade's shape: see 5.6.
28. **Checks against a grade.** No; checks always name a skill. Decided again with the bench in Phase 5: see 3.10.
29. **A vignette nobody answers.** The default option applies, authored per vignette and usually the quiet one: see 5.2.
30. **Generated characters in the editor.** Eventually the editor previews and samples against a testing and sample pool of crew that never appears in the main game. Built when crew generation is designed.
31. **A long speech in a script.** Deferred to much later: it is on the roadmap's "Later" row (section 11). Until then a script entry is one line.

Notes from the author on 2026-11-16, before slice 1e (design in `docs/plans/2026-11-16-phase-1e-design.md`):

32. **Storylets come to the captain.** They are not shopped for from a menu. Expect none to two a cycle. A storylet is often a choice between two bad options.
33. **Many storylets grow out of missions.** An encounter leaves a quality on the ship or on a crew member, and a storylet fires because it is there.
34. **Vignettes are mostly flavour**, with a small +1 or -1 here and there.
35. **Captain's time is a different thing from storylets.** It is a menu of good things the captain chooses to do (a poker night, a captain's day or a concert, a drill ordered through the chief of security), mostly mechanical, with some narrative wrapping. A captain storylet therefore does not cost captain's time. Sections 5.2 and 5.4 and the glossary still describe the earlier idea, in which captain's time paid for storylets; they are rewritten when slice 1g is designed.
36. **Slice 1e is built as one thin thread first, then widened** (section 11.1). The first thread is a storylet from a mission, playable in the browser.
37. **A little generated narrative is allowed in the game's pack for slice 1e's storylets**, in place of placeholders. The test fixture and this document stay placeholder.

Decided by the author on 2026-11-16, while slice 1e's first part was designed and built:

38. **Every flag is a number, and the first thread is thin.**
    - Every story flag is a number, after Fallen London (flags became traits, and the counter-or-stage rule gave way to named levels, in item 40): a mark today is a count tomorrow, and "has this happened twice?" must not need a rewrite. A flag is a **counter** (only ever added to) or a **stage** (only ever set), never both; a story that needs both uses two flags. See 3.8.
    - The first thread is thin. The shaken crew member asks the captain for a word; an earlier draft had them ask to be kept off the next away team, which needs a flag that changes the briefing and one that moves when a mission launches. Those are part three (section 11.1). In part one loyalty has no effect on play yet and no rule reads a flag; that is accepted.
    - Naming conventions were adopted the same day (`docs/conventions.md`): ids are upper case for items with their own page in the editor and lower case with underscores for what lives inside an item or is typed into text (approaches, options, flags, symbols, tags); `.`, `-`, `#`, `:`, and `|` are never in an id; a flag is read beside other properties through `flag` (`subject.flag.shaken`).

Decided by the author on 2026-11-17, before slice 1e's second part (design in `docs/plans/2026-11-17-phase-1e-part-two-design.md`):

39. **Morale, vignettes, and the captain's itinerary.**
    - **Morale bites.** Low morale is a small minus on a crew member's checks and high morale a small plus; it is not only shown. It moves with the mission's result, being hurt, a crewmate's death, a cycle not sent, and a weekly drift back to a resting value.
    - **A vignette can arrive on a flag, a crew member's morale, or a fact about the last cycle** (sent, hurt, rested, lost a crewmate), a small fixed set ahead of part three's expression language.
    - **The captain's itinerary** is a screen after the aftermath and before the briefing. It lists storylets and vignettes, taken in any order; every storylet must be answered before the briefing. It shows the captain's time, 3, which does nothing yet. Built as the first phase of each cycle, before the orders.

Decided by the author on 2026-11-17, in a design interview before slice 1e's third part (design in `docs/plans/2026-11-17-phase-1e-part-three-design.md`):

40. **Traits that bite, and storylets as events.**
    - **Part three is one thread first:** the worked example (a shaken crew member stood down from the next away team) built end to end. The storylet engine in full (the expression language, casting, salience, storylet cooldowns, an option settled by a check) is a new part four, and the crew file and the storylet forms become parts five and six (section 11.1).
    - **No library for the storylet engine.** What part three builds is game rules any engine would leave to us. The expression language, in part four, sits on an off-the-shelf evaluator (NCalc or DynamicExpresso) rather than a hand-written parser.
    - **Qualities are stats or traits.** A quality is anything a requirement reads. A stat is typed and owned by a system (skills, morale, loyalty). A trait is named by content, numeric, and stored generically; section 3.2's traits and the story flags of item 38 are one thing, and "flag" is retired. A trait that only marks story progress is hidden. The "quality view" is the state view.
    - **Trait ids are lower case** for every trait, an exception to the rule that items with their own editor page are upper case: traits are typed into text far more than they are opened as pages (`docs/conventions.md`).
    - **Stages are numbers, with named levels.** After StoryNexus and Fallen London, where every quality is a number and its meaning lives in level descriptions: a trait may name its levels, and one that does can only be set, to a named level or 0. This replaces item 38's counter-or-stage rule.
    - **Traits are defined in `content/traits/`** when they have rules or sit on a pair: shown or hidden, where they sit, levels, a cap, a briefing line, launch rules, a check bonus, a signed weekly step, expiry, and what a pair's target's death does. Heavier than one example needs, accepted for a shape that grows.
    - **Pair traits:** a trait held by one crew member that points at another, for "I just can't be around so-and-so", with a cost when both are sent.
    - **A promise can be broken.** The briefing warns and says what sending someone will cost; nothing blocks the launch.
    - **Storylets are events, as in Crusader Kings III,** and are always answered when presented between missions. There is no `ifIgnored` (section 5.3 is rewritten). What can be left alone and get worse is a trait.
    - **The roster's trait cell is interim,** until the crew file (part five), and is documented as such.
    - **The simulator's captain answers by `first` or `random`.** No `ignore` policy.
    - **The roadmap was revisited the same day:** the skills, attributes, and gear overhaul became slice 1h, before the playtest (the author's note on the GDD page); planets and a sector map joined the art track; seniority joined Phase 6.

Decided by the author on 2026-11-18, in a design interview before slice 1e's fourth part (design in `docs/plans/2026-11-18-phase-1e-part-four-design.md`):

41. **The storylet engine in full, carried by one example.**
    - **All five pieces at once:** the expression language, casting, salience, storylet cooldowns, and an option settled by a check, held together by one worked storyline that uses each ("The one who came back").
    - **Expressions replace the structured lists** for storylets, vignettes, and options. All content in both packs is migrated, and the loader refuses the list form. Encounter approaches keep their structured `requires`.
    - **DynamicExpresso, with C#'s operators**, checked at load time, after a spike that showed it could be sandboxed with a rewrite and a whitelist of our own (NCalc was the fallback, and was not needed).
    - **The state view's vocabulary is fixed** (section 3.8). Anything else is refused by name. An expression reads only a trait with a definition.
    - **Vignettes run on the same engine,** and their cooldown becomes the shared `cooldown` field.
    - **Recent history is a real store:** a rolling record of each crew member's last N cycles, from which part five's chronicle grows. There are two kinds of loss: a crewmate dying aboard (`lost_crewmate`) and a teammate dying beside you on the away team (`lost_teammate`). A lost mission is one whose objective failed or was abandoned; a death alone does not make a mission lost.
    - **Cast slots replace `scope`:** up to three ordered, named slots. Casting is sampled: a seeded order, a cap on tries (`storylets.castTries`, 100), and at most one cast per storylet per cycle.
    - **Salience, simple first:** stakes, then specificity, then freshness, then a seeded tie. Routing to department heads stays out, as there are no heads yet, and the index waits for a pack of hundreds.
    - **Cooldowns** are `{ cast, global }` in cycles. The rule against hidden memory applies only when a storylet has no cast cooldown.
    - **Options can carry `requires`,** and one option must have none. **A check option** has success and failure only, and the captain names a handler, scored by skill, morale, and trait bonus with the mission dice.
    - **The itinerary shows the cast.** **Dev Play's "Why these storylets"** lists only this cycle's contenders, with a count line and a search for any other, since a list of every storylet would not scale to hundreds.
    - **The simulator** picks handlers by its policy, and reports "never cast", check success rates, and losses on the budget.
    - **Old saves are refused,** and the history starts empty.
    - **Casting at 500 crew** is planned (candidates for a slot from the traits it needs, read from the traits module) and built when the simulator's "out of tries" report shows the need.

Decided by the author on 2026-11-20, in a review of the roadmap after slice 1e's fourth part:

42. **The crew file waits for the skills overhaul.** Slice 1e's part five is split. The chronicle list stays part five and is next. The crew file becomes part seven and is built after slice 1h, because 1h settles what a character is (the skill list, attributes, and how grades are shown), and the crew file's main section shows exactly that. Building it first would mean building it twice. Morale, loyalty, and traits are unchanged by 1h, so nothing is lost by waiting. The roster's trait cell stays interim until part seven.

Decided by the author on 2026-11-20, in a design interview for the calendar (design in `docs/plans/2026-11-20-phase-1e-calendar-design.md`):

43. **The calendar: days kept, weeks shown.**
    - **Mechanics are in weeks of 6 days; the engine keeps days.** Every timed effect, one-off or repeating, is due on a day and happens when the calendar passes it, whatever the cycles' lengths.
    - **A cycle moves the calendar 1d3+4 days,** rolled when it advances and reported afterwards (ahead of schedule, on schedule, delayed). **Travel of N weeks** is one cycle and one roll times N, so a trip is early or late as a whole.
    - **Cooldowns, "the last cycle", and history stay in cycles.** Expressions gain `cyclesSince` and `daysSince` for storylets and traits, so a storylet can wait for both cycles and days.
    - **Content writes durations only in weeks;** expressions read days.
    - **Shown in weeks, rounded to the nearest,** with "under a week" below 4 days. The player's choices about time are in whole weeks.
    - **No week number.** Play mode shows the date, short (`13 Vortun 105`), with the full date and weekday on hover. Dev mode also shows the cycle number.
    - **The Bastion Calendar** from `docs/lore/calendars.md`, as content: 12 months of 30 days, 6 named weekdays, and a start on 13 Vortun 105. One calendar now; others later.
    - **Ages** count on the 360-day year, with a birth date rolled from the legacy's seed. Not built until ageing (Phase 6).
    - **No fixed-length setting:** seeded dice keep tests predictable. **Old saves are refused.**
    - **The lore is the author's:** agents read `docs/lore/` and never edit it, and commit its changes on their own (`AGENTS.md`).

Decided by the author on 2026-11-20, in a design interview for the chronicle (design in `docs/plans/2026-11-20-phase-1e-part-five-design.md`):

44. **The chronicle: written once, frozen, and readable.**
    - **What goes in:** the founding, deaths (memorials), calls answered, captain storylets answered, and every mission's result. Vignettes stay out; unattended objectives wait for open question 4.
    - **Stored, not projected.** A new `Sim.Chronicle` module writes each entry when its event happens, and its **text is rendered once and frozen**: the one stored use of the grammar engine. No backfill from the event log; **old saves are refused.**
    - **Written in lines,** a situation per kind, with a default line for each in the pack; a storylet option's own `chronicle.text` wins.
    - **Weight** per kind in the tuning file (founding and death 5, storylet 3, call 3 when the captain overruled the lead and 2 otherwise, mission 2 when lost and 1 when won), overridable on a storylet, an option, or a decision; `weight: 0` writes no entry.
    - **Read in days:** `chronicle.count(kind, days)` and `x.chronicle.count(kind, days)`. `history.count` stays in cycles, because the history is stored per cycle.
    - **The page:** newest first under full dates, weight 4 and 5 set apart, All (default) or Notable (3 and up), no pagination.
    - **The worked example** is "The names on the wall", gated on `chronicle.count('death', 120) >= 2`. As built, its voice is anyone who lost a crewmate in the last 20 cycles, since a cast slot cannot prefer the one with the most losses.

Decided by the author on 2026-11-20, in a design interview for writing storylets in the editor (design in `docs/plans/2026-11-20-phase-1e-part-six-design.md`):

45. **Writing storylets in the editor: a builder, notes, and test ships.**
    - **Forms for storylets, vignettes, and traits,** in that order; storylets and vignettes are two kinds sharing one form part. The trait forms also let the encounter form write a new trait condition. Each file still opens as text.
    - **Expressions are a hybrid:** rows of selects and steppers for clauses joined by `&&` in ten known shapes, the language as text for the rest, with a switch between them. The YAML keeps storing the string; the builder is a view of it. Writing storylets is first class, as easy as playing.
    - **Notes in place of inner comments:** a `notes:` field on storylets, vignettes, traits, and encounters, since the writer keeps only a file's leading comment. The existing comments move into it once.
    - **Test ships.** A dev-only content kind, `content/testships/`, starting with **USS Quality Assurance**: a ship's state (crew, traits, history, chronicle, storylet arrivals, in days and cycles ago) that implements the state the expressions read. The editor's previews cast on it for real, say which clause failed when no one fits, and offer "Preview anyway". Dev Play can save a legacy's state as a test ship. Every storylet and vignette must cast on some test ship.
    - **The tablet waits for hosting:** reaching the editor from another device, touch-first forms, and drafts in the browser join the online track, which is widened to carry them. This part only avoids working against them.
    - **The worked example** is a storylet that reads `grieving`, written entirely in the browser. As built, it is "Time for it later": hearing the names, or a talk that gets through, clears `grieving`, and giving it time lets it run. A second test ship, USS Quality Control, was needed, because two storylets read the sensor quirk at different levels.

Decided by the author on 2026-11-21, in a design interview for tuning in play (design in `docs/plans/2026-11-21-phase-1f-design.md`):

46. **Tuning in play: two modes, a draft, and real teams.**
    - **Two parts.** Part one, the mission bench in play, measures one mission many times; part two, legacy mode, runs whole legacies on the same seeds, so that what storylets and vignettes do between missions is measured too. Part two is designed when part one has been used.
    - **The order stays 1f, 1g, 1h.** Starting conditions (morale, traits, ship traits, injuries) come only from real teams, from a legacy or a test ship, built by the launch's own code; generated teams stay as they are, and `TeamGenerator` and `bench.teams` belong to 1h.
    - **A draft, saved to the content folder.** The page holds a draft in the server's memory, lost on restart; Run benches the saved tuning against it on the same seeds; Save writes the file through the editor's validated path, refused if the file changed since the draft began. No override file.
    - **A form generated from the file, saved in place:** each number a field with the file's comment as its hint, dice as steppers, and only the changed values replaced, so comments survive. Numbers the mission bench cannot see are read-only until legacy mode.
    - **One page, "Tuning",** with Mission and (later) Legacy tabs sharing the draft; reached in developer mode from the editor, legacy selection, Dev Play, and "Bench this" on an objective.
    - **Run on demand,** streamed and cancelled by any change; every command-line input; an every-objective summary; changes uncoloured, and greyed within the noise; replay of the worst run or any seed as the narrated report. No alarms until legacy mode.
    - **The worked example** is the rookie loss rate, about 34% on the game's pack.
