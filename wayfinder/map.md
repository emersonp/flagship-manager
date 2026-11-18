# Map: Starship Sim design

Tracker: this folder of markdown (to be migrated). No ticket files yet, by request; open tickets will be `wayfinder/tickets/*.md` when created.

## Destination

A game design document (`docs/gdd.md`) in which every decision that is expensive to reverse is settled, so Phase 1 can be planned and handed to agents. Cheap, reversible choices may remain as open questions in the GDD.

## Notes

- Domain: single-author game; Parker writes all creative content. Systems docs use placeholders (`SPECIES_A`, `OBJECTIVE_TEMPLATE_1`), never invented fiction.
- Vocabulary: `GLOSSARY.md` at the repo root. Use its terms.
- Stack is locked: C# backend, database, Datastar front end. Keep a pure simulation core so pieces can move to Godot or Rust later.
- Planning only. Throwaway prototypes are allowed to answer feel questions.
- Quality over speed; many small, testable iterations.
- Sessions: grilling + domain-modeling by default.

## Decisions so far

Settled by grilling and review on 2026-11-13 (no tickets; detail lives in `docs/gdd.md`):

- Destination: GDD v1 with load-bearing decisions resolved first.
- Stack: locked as above; the GDD documents trade-offs and seams only.
- Mission resolver: step-wise, resumable state machine with seeded RNG and serialisable state.
- Calls: scale with mission size. Most missions have zero to two; a site worked by three teams has more in total.
- State and history: current-state tables plus an append-only event log; the chronicle is a curated projection. Not full event sourcing.
- Qualities: typed core model, exposed to content through a read-only state view and one expression language; storylet effects are commands.
- Content and saves: content as validated data files in git; saves in a database keyed by legacy.
- Online: isolated legacies per player, server-authoritative, within one to three months. Shared universe later.
- Permanence: permadeath, single autosave. No game over: everything can be lost and the chronicle continues.
- Scale: every crew member is simulated identically, from a small hull up to roughly 500. No simulation tiers; attention is managed by salience and delegation.
- Lineage: one ship name; any new hull (upgrade or replacement) takes the next suffix.
- Time: fixed-span cycles; crew age and have careers; the roster turns over.
- Captain: a full crew member with extra loyalty. Leaves by age, death, terrible renown, or player choice. Successor chosen from a small pool (Admiralty candidates plus a qualified first officer).
- Delegation: department heads resolve storylets below their stakes threshold and report in a digest.
- Cycle: strict phases (orders, briefing, resolution, aftermath, advance), plus a standing duty roster editable outside them. A cycle is one week by default and longer in transit; all timing is in weeks.
- Structure: assignment (one to about four cycles), then a site per cycle offering one to four objectives (usually one; a choice between several is the frequent exception), then one mission per away team. Unattended objectives still resolve. Splitting into two or three teams means attempting more objectives with thinner teams.
- Growth: mission experience, mentoring, and slower shipboard training. Promotions need a minimum of away missions.
- Review answers (GDD section 15): mandates declinable only above a standing threshold and at a cost; the player names team leads; bands gated by insight and the org chart; the player picks the core that carries to a new hull; class upgrades by rare offers and storylines; families across generations; server-side portraits; YAML; the captain may join away teams at a cost; the unit of a game is a **legacy**.
- Second review round (GDD section 15, items 13 to 16): transit is one long cycle with its own choices and a player-set pace; insight is only vague at first; unattended objectives are author-set with a roll as default; transit gets a larger fixed captain's-time budget; families keep real ageing, with other relatives arriving at any time.
- Resources: personal renown, ship renown, Admiralty standing, and requisition. No money economy.
- Mission bench: a developer-mode simulator that runs one mission thousands of times, from the legacy selection screen, a mission offer, or the command line. Never touches legacy state.

- Mission feel: a hands-off mission with calls and initiatives does not feel passive. Parker played the throwaway prototype on 2026-11-13 and loved it. Prototype: `C:\Users\parker00\Dropbox\Work\f-prototype\mission-feel-prototype.html` (also a private artifact).
- Mission numbers: six fixes benched on the prototype's resolver and accepted on 2026-11-13 (abort is always a call, fast is +1 difficulty (since 2026-11-15 an encounter's approaches are authored), softer collapse, strain relief on success, "develop juniors" within reach under supervision, and halved personality weight in the call rule). Numbers live in a tuning file, reloadable in developer mode; Parker cares more about tweaking in play than about the starting values. Detail in GDD sections 4.2 to 4.7.
- Build plan: Phase 0 was planned in `docs/plans/2026-11-13-phase-0-skeleton.md` and built on 2026-11-13 (39 tests; `./check.sh` is the gate). Events store enums by name; exports carry a format version. Phase 1 is cut into six slices in `docs/plans/phase-1-outline.md`; each slice is planned in detail just before it is built.
- Phase 1, slices 1a and 1b: the mission resolver and command-line bench are built. Module state will be one JSON row per module per legacy (built in 1c). Design in `docs/plans/2026-11-14-phase-1ab-design.md`.
- Slice 1c, settled 2026-11-14: built in two parts (headless first, then the browser); one away team per cycle; eight authored crew with no recruitment yet, and results applied for real; reckless-lead loss of 17% accepted; rescue at the strain cap left as it is.
- Slice 1c, first part, built 2026-11-14: a whole cycle plays headless. A site arrives, a team is sent, the mission resolves with its calls answered, and the crew grow, are hurt, heal, and die. A legacy stored and reloaded after every command, including while a call waits, plays out identically. A fifth outcome, incomplete; "develop juniors" carried by the senior; all dice behind one seam. The placeholder assignment offers one or two objectives (weights 6:3) until a third objective template exists.
- Slice 1c, second part, built 2026-11-14: a whole cycle plays in the browser (`./run.sh`). The page is the state; one request is one command and one commit; briefing is one form; beats are paced by the server and nothing gives the ending away early; a browser-played legacy has exactly the simulator's event log. A legacy has a name.
- Visual design, settled 2026-11-14: Parker chose between four mock-ups and asked for a blend (layout, type, and card shape between two of them; the blues of a third, less saturated; no warm accents). The result is the design of record in `docs/design/`: blue-slate ground, one azure signal, Source Serif 4 for headings, Host Grotesk for text, Atkinson Hyperlegible Mono for figures, and the mission log as a numbered trail running into the call. Built with the Hallmark skill.
- Crew are generated in the long run (Parker, 2026-11-14): "long term, I don't want to be creating crew. I want to be creating species and cultures and life paths and name generators that allow random crew to be created." Authored content starts static and migrates to procedural. The author writes the sources (species, cultures, life paths, and name generators); the game generates people from a seed. A hand-authored crew member is a generated one with every field pinned. The eight starters are a stopgap.
- Slice 1c-editor, built 2026-11-14: everything authored has a display name (ids stay the keys); the editor at `/dev/content` in developer mode has a form per kind and the tuning and pack files as text; a save is validated whole by the loader before anything is written (one file, or two when a skill delete also clears it from the crew) and the running game swaps packs; a file the editor cannot keep faithfully is never rewritten; in the crew form a blank skill means the crew member lacks it and 0 means untrained; deleting a skill also removes it from the starting crew; decision ids are unique and `DECISION_ABORT` is reserved; an encounter may not chain to itself.
- Author's notes after slice 1c (2026-11-14, GDD section 15, items 18 to 21): finer skills later, grouped by department and attribute into grades seen through insight (Phase 5); vignettes may offer one small choice; two narrative structures, quality-based and salience-based, each where it fits, with salience choosing report lines from slice 1d (waypoint narrative was dropped in GDD v1.13); a hybrid model of typed stats and story-flag qualities. Touchstones are in `docs/touchstones.md`. Cheap follow-ups are open questions in GDD section 16.
- Slice 1d, built 2026-11-14: reports. `Sim.Narrative` chooses each beat's line by salience (the most specific criteria that hold) and expands symbols, names, pronouns, and verb forms; text is regenerated on view and draws no randomness; the log reads as prose with generated tags and the structural line on request; the aftermath has a report; the editor has lines (with a preview against sample missions), symbols, pronoun sets, a crew pronoun pick, and a lint. Check beats gained their after-values (golden hashes re-pinned once). Design and plan in `docs/plans/2026-11-14-phase-1d-*.md`, which is where this project's designs and plans live. Revised the same day on the author's note: an encounter's and an objective's words are written inside them (`text` for their own beats, `lines` for anything else there), `content/lines/` holds only the shared lines, and one past-tense sentence serves the log and the report (the `use` field is gone). See GDD section 6, "Where lines live", and 6.1.
- Slice 1d authoring, built 2026-11-15: writing in place. A line's text can be a script (speech by the beat's people, the captain, or the best at a skill; system notes; a report sentence), an approach can speak before its check, a call shows its script and its own button labels, and the encounter and objective forms end with "What plays here", which previews the unsaved form against samples built for the item. The game's pack in `content/` holds generated fiction by the author's one-time exception; the fixture stays placeholder. Decisions and limits in `docs/plans/2026-11-15-phase-1d-authoring-design.md`.
- Slice 1d review, built 2026-11-15: authored approaches. An encounter's `approaches` is a list of one to four the author writes, each with a name, skill, difficulty, optional requirements and outcomes, and words; the lead chooses the available one whose skill is highest for them (a tie to the first listed); an unwritten tier takes the tuning file's default; risk tolerance is gone from the operational parameters (it returns with the skills, attributes, and gear overhaul); "notice" is "initiative"; the bench reports approach position and "narrowed %"; the store's schema is 6 and old saves are refused. The encounter form is definitions, a tab for each approach, then "What plays here"; the briefing's away team table restacks below 56rem. A shared library of approaches is the author's to design. Design in `docs/plans/2026-11-15-phase-1d-approaches-design.md`; decisions in GDD section 15, item 24.
- Slice 1e, part one, built 2026-11-16: a storylet from a mission. An encounter's outcome or a decision's answer changes a story flag (every flag a number: a counter is added to, a stage is set, never both) on the ship, the actor, the lead, or the team; `Sim.Storylets` stores flags and, when the aftermath begins, offers storylets whose requirements hold (at most two a cycle, highest stakes first) under "Matters aboard", answered with fixed effects on loyalty and flags; the game's pack has two, a quirk in the sensors and someone shaken. Ids follow `docs/conventions.md`. Design in `docs/plans/2026-11-16-phase-1e-design.md`; decisions in GDD section 15, items 32 to 38.
- Slice 1e, part two, built 2026-11-17: morale, vignettes, and the captain's itinerary. `Sim.Crew` gives each crew member morale (missions, hurts, deaths, rest, weekly drift) that is a minus or plus on their checks at Low and High; a storylet or a vignette can require morale, loyalty, or a fact about the last cycle; vignettes (`content/vignettes/`) arrive with effects or one small choice and a default; a new first phase, the captain's itinerary, holds storylets (which must be answered) and vignettes, with the captain's time shown. Design in `docs/plans/2026-11-17-phase-1e-part-two-design.md`; decision in GDD section 15, item 39.
- Slice 1e, part three, designed 2026-11-17, not yet built: traits that bite. Story flags become traits (a quality is a stat or a trait), defined in `content/traits/` with named levels, rules at launch, a check bonus, a weekly step, expiry, and pairs of crew, owned by a new module `Sim.Traits`; the worked example is a shaken crew member stood down from the next away team. Storylets are always answered (no `ifIgnored`). The storylet engine in full is now part four; the crew file and the storylet forms are parts five and six; the skills and gear overhaul is slice 1h. Design in `docs/plans/2026-11-17-phase-1e-part-three-design.md`; decision in GDD section 15, item 40.
- Content editor: developer-mode pages that write the same YAML files in `content/`, validated by the loader on save. Its own slice, straight after 1c; each later slice adds a form for its content type. Git stays the record.

## Not yet specified

- **Salience and digest rules.** What reaches the inbox out of 500 crew, and how the digest stays readable.
- **Storylet casting at scale.** How cast slots bind across a large roster without repeating the same few people or costing too much.
- **Portrait pipeline.** Layer format, tinting method, and where compositing happens.
- **Simulator alarm thresholds.** The target bands for lethality, growth, and dominance.
- **Authoring load.** Whether one author can feed 500 crew with vignettes; how much the grammar must carry.
- **Transit.** What the choices in a transit cycle are. (A faster pace costs the crew; hull wear comes with dry dock.)
- **Insight distortion.** Traits that make insight wrong, in the observed and in the observer.
- **Away across cycles.** Whether a team can stay at a site while the ship moves on.
- **Careers detail.** Rank ladder, ageing rates, poaching offers, and ambitions.
- **Crew generation.** The content types for species, cultures, life paths, and name generators; how a life path builds skills, age, seniority, and history; whether names share the report's grammar engine (slice 1d); and where the slice sits (proposed: early in Phase 2, before anything replaces crew).
- **Succession detail.** What carries over to a new hull, and how the successor pool is generated.

## Out of scope

- **Shared universe** (the "array"): cross-legacy poaching and shared Admiralty events. Legacies stay isolated in this effort; only the seam is noted.
- **Tactical grid resolver.** Only its contract is designed here.
- **Diplomacy, dry dock, and trade layers.** Future mechanics; the bus must admit them, nothing more.
- **Creative content.** Parker's.
