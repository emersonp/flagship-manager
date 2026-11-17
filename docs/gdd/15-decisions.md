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
    - Every story flag is a number, after Fallen London: a mark today is a count tomorrow, and "has this happened twice?" must not need a rewrite. A flag is a **counter** (only ever added to) or a **stage** (only ever set), never both; a story that needs both uses two flags. See 3.8.
    - The first thread is thin. The shaken crew member asks the captain for a word; an earlier draft had them ask to be kept off the next away team, which needs a flag that changes the briefing and one that moves when a mission launches. Those are part three (section 11.1). In part one loyalty has no effect on play yet and no rule reads a flag; that is accepted.
    - Naming conventions were adopted the same day (`docs/conventions.md`): ids are upper case for items with their own page in the editor and lower case with underscores for what lives inside an item or is typed into text (approaches, options, flags, symbols, tags); `.`, `-`, `#`, `:`, and `|` are never in an id; a flag is read beside other properties through `flag` (`subject.flag.shaken`).
