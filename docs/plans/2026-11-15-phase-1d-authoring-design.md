# Phase 1, slice 1d authoring: writing in place

**Spec:** `docs/gdd.md` section 6 and 6.1, and section 15, items 22 and 23. **Builds on:** `main` at 2026-11-15 (slice 1d, revised so an encounter's and an objective's words are written inside them).
**Ends with:** the author opens an encounter, sees every beat it can produce with what the log says there now, writes over any of them, and watches the result as they type. A call can be an exchange between the lead, a crew member, and the captain; a check can be an intention, then an outcome.

This records the decisions the author made in review on 2026-11-15 and what was built from them. There is no separate plan: the author asked for it to be built at once.

## 1. Decisions

From the author:

1. **The editor is where narrative is written.** The YAML files are the stored form: readable and diffable, rarely edited by hand.
2. **One list of what plays.** An encounter's or an objective's form ends with "What plays here": every beat the item can produce, in mission order. The list is the editor: each row is a box.
3. **A blank box shows the fallback.** An unwritten box shows, greyed, the text of the line that plays there now; the author writes over it, and clears it to fall back again.
4. **"Write one for here."** A beat with no fixed box shows what plays there and a button that starts a line of the item's own from that sentence.
5. **Lines of the item's own sit under their beat**, not in a table of their own. Their criteria are rows of fact and value, as on the Lines page, and the criterion the item implies is shown first, locked.
6. **Previews as the author types**, before anything is saved, with problems shown on the row.
7. **Samples are built for the item** from the starting crew, with a "Next sample" control. A line with criteria is shown against a sample made to satisfy them; one that can never hold at the item says so.
8. **Chips** insert the speakers, names, and symbols a beat allows. Type-ahead can come later.
9. **The lists say how much of each item is written.**
10. **One reusable part**, for every later storylet kind.
11. **A text can be a script**, typed as plain text, one entry to a line: `speaker: words`, `> note`, narration, and a last line `= sentence` for the report.
12. **Who can speak:** the beat's own people, the captain, and `best SKILL_ID`, the team member still standing who is best at that skill and is not the lead. An entry whose speaker is absent is left out. Named characters and cast requirements wait for storylet casting.
13. **An "attempt" text** for each approach is said before the check's result. The check does not pause and is not a player action.
14. **A call is one screen:** its script, then the answers, with authored button labels. Several screens, with the captain choosing questions, is a conversation system and waits for Phase 4.
15. **The report** uses a script's `=` sentence, and the shared line's sentence when a script gives none.
16. **Left out:** a "play it" button that runs the encounter, and a view on the Lines page of which items override a shared line.
17. **Generated fiction, once.** The game's pack in `content/` is filled with generated names and words so the author can see lines in use. The fixture in `tests/fixtures/content` stays placeholder.

Deferred by the author, recorded in GDD section 16: how procedurally generated characters appear in the editor and its samples, and a way to write a speech of more than one paragraph inside a script.

## 2. What was built

**Scripts** (`Sim.Core/Narrative/Script.cs`). `Scripts.Parse` reads a text into entries (narration, speech, note) and an optional summary. One sentence is a script of one narration entry, so every earlier line is unchanged. `-` alone is a script with no entries. The loader checks each entry's speaker and tokens against the line's beat, and a `best` speaker's skill against the pack.

**The engine.** Selection is untouched. `BeatSituations` adds the fact `teamStanding` and, for scripts, the people `best SKILL_ID`. A new beat kind `attempt` holds what a check holds without its tier and margin; `Prose` renders a check as its attempt line, if any, then its own line. `BeatProse` carries entries and the report's sentence. Text is still regenerated on view and draws no randomness: the simulator's figures are identical.

**Content.** An approach's `text` takes `attempt` beside the tiers. A decision takes `labels` (`bold`, `safe`, `withdraw`: 1 to 40 characters), kept in `ContentPack.ChoiceLabels` so `DecisionDef`, which a mission's stored state holds, does not change. The writer writes a script as a YAML block, one entry to a line.

**The bridge.** The log shows a script entry by entry, speakers named, notes set apart; the newest beat's entries arrive in turn. The call panel shows the call's script and the decision's button labels.

**The editor.** `Web/Forms/Plays.cs` builds the panel's model from the form as posted: the groups for an encounter or an objective, the item's words lowered as the loader would lower them, a sample situation for each row, and what `Prose.Render` says there. `PlaysPanel.razor` draws it. Typing posts the form to `/dev/content/{kind}/{id}/plays`, which answers with the previews only, each patched by its id, so no box is touched. `write:<beat>` on the rows route starts a line from the shared one. A line's criteria are fixed fields (`f0`, `v0` to `f5`, `v5`), so the existing row machinery carries them; a line can add six criteria to the one its item implies. `wwwroot/js/editor.js` is the editor's one script: a chip inserts its text at the caret.

## 3. Limits to know about

- A line written inside an item can add at most six criteria. A shared line has no limit.
- The samples are synthetic: ordinary values for every fact, the item's own where it has them, and the criteria's where a line tests them. They show how a text reads, not how often its beat occurs.
- "Cannot happen here" is advice from what the form holds (the approaches' skills, the decision, the tags, comms), not a proof.
- The newest beat's entries are revealed by a style rule, so a beat that follows quickly can appear before a long script has finished arriving.
- The preview was exercised through the server and by tests. It was not clicked through in a browser in this session.
