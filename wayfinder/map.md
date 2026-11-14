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
- Qualities: typed core model, exposed to content through a read-only quality view and one expression language; storylet effects are commands.
- Content and saves: content as validated data files in git; saves in a database keyed by legacy.
- Online: isolated legacies per player, server-authoritative, within one to three months. Shared universe later.
- Permanence: permadeath, single autosave. No game over: everything can be lost and the chronicle continues.
- Scale: every crew member is simulated identically, from a small hull up to roughly 500. No simulation tiers; attention is managed by salience and delegation.
- Lineage: one ship name; any new hull (upgrade or replacement) takes the next suffix.
- Time: fixed-span cycles; crew age and have careers; the roster turns over.
- Captain: a full crew member with extra loyalty. Leaves by age, death, terrible renown, or player choice. Successor chosen from a small pool (Admiralty candidates plus a qualified first officer).
- Delegation: department heads resolve storylets below their stakes threshold and report in a digest.
- Cycle: strict phases (orders, briefing, resolution, aftermath, advance), plus a standing duty roster editable outside them. A cycle is one week by default and longer in transit; all timing is in weeks.
- Structure: assignment (one to about four cycles), then a site per cycle offering two to four objectives, then one mission per away team. Unattended objectives still resolve. Splitting into two or three teams means attempting more objectives with thinner teams.
- Growth: mission experience, mentoring, and slower shipboard training. Promotions need a minimum of away missions.
- Review answers (GDD section 15): mandates declinable only above a standing threshold and at a cost; the player names team leads; bands gated by insight and the org chart; the player picks the core that carries to a new hull; class upgrades by rare offers and storylines; families across generations; server-side portraits; YAML; the captain may join away teams at a cost; the unit of a game is a **legacy**.
- Second review round (GDD section 15, items 13 to 16): transit is one long cycle with its own choices and a player-set pace; insight is only vague at first; unattended objectives are author-set with a roll as default; transit gets a larger fixed captain's-time budget; families keep real ageing, with other relatives arriving at any time.
- Resources: personal renown, ship renown, Admiralty standing, and requisition. No money economy.
- Mission bench: a developer-mode simulator that runs one mission thousands of times, from the legacy selection screen, a mission offer, or the command line. Never touches legacy state.

- Mission feel: a hands-off mission with calls and notices does not feel passive. Parker played the throwaway prototype on 2026-11-13 and loved it. Prototype: `C:\Users\parker00\Dropbox\Work\f-prototype\mission-feel-prototype.html` (also a private artifact).
- Mission numbers: six fixes benched on the prototype's resolver and accepted on 2026-11-13 (abort is always a call, fast is +1 difficulty, softer collapse, strain relief on success, "develop juniors" within reach under supervision, and halved personality weight in the call rule). Numbers live in a tuning file, reloadable in developer mode; Parker cares more about tweaking in play than about the starting values. Detail in GDD sections 4.2 to 4.7.
- Build plan: Phase 0 is planned task by task in `docs/plans/2026-11-13-phase-0-skeleton.md` (every file compiled and tested before it was written down). Phase 1 is cut into six slices in `docs/plans/phase-1-outline.md`; each slice is planned in detail just before it is built.

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
- **Succession detail.** What carries over to a new hull, and how the successor pool is generated.

## Out of scope

- **Shared universe** (the "array"): cross-legacy poaching and shared Admiralty events. Legacies stay isolated in this effort; only the seam is noted.
- **Tactical grid resolver.** Only its contract is designed here.
- **Diplomacy, dry dock, and trade layers.** Future mechanics; the bus must admit them, nothing more.
- **Creative content.** Parker's.
