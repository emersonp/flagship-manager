# Phase 1 Outline: the thinnest game

Phase 1 is too large for one plan. It is cut into six slices, each ending with something to run or play. Each slice gets its own detailed plan (in this folder, in the style of the Phase 0 plan) written just before it is built, against the code as it then stands.

**Spec:** `docs/gdd.md`. **Starts after:** `2026-11-13-phase-0-skeleton.md`. **Gate for every slice:** `./check.sh` green, and the golden seeds updated only on purpose.

Order matters: the mission comes first because it is proven fun (the prototype), and the tuning loop comes second because the author wants to tune in play.

| Slice | Adds | Ends with | GDD |
|---|---|---|---|
| 1a. Mission resolver | `Sim.Missions`: content types for objective templates, encounters, and the tuning file; `Start` and `Advance`; checks with tiers; momentum and strain; one decision with the call rule; the six accepted fixes | The prototype's four walkthroughs reproduced as tests; missions resolve headless | 3.4, 3.5, 4.1 to 4.7 |
| 1b. Mission bench | `SimRunner bench`: one mission run thousands of times, with generated teams, a call policy, and the result, call, death, and experience figures | The author changes a number in the tuning file and sees the bench move, from the command line | 10.1, 4.7 |
| 1c. A mission in the browser (two parts: headless, then the browser) | `Web` with Datastar; `Sim.Crew` with 8 crew; one single-cycle assignment template, two objective templates, and six encounters; orders, briefing, and resolution streamed beat by beat; a call answered in the page; legacy selection and autosave | The author plays one mission per cycle in the browser | 2.2, 7.4, 7.5 |
| 1c-editor. Content editor | Developer-mode pages in `Web` with a form per content type (crew, skills, encounters, objectives, and the tuning file), writing the same YAML files in `content/`; the loader validates on save and its errors show beside the field | The author edits content in the browser, not in YAML | 3, 7.4 |
| 1d. Reports | `Sim.Narrative`: the grammar engine, lines chosen by salience (the most specific matching criteria win), and the text report built from beats | A finished mission reads as a report, not a log | 6 |
| 1d-review. Authored approaches | An encounter's approaches as a list of one to four the author writes (name, skill, difficulty, requirements, outcomes, words); the lead chooses by skill; risk tolerance removed; "notice" renamed "initiative"; the encounter form with a tab for each approach; the bench reports approach position and "narrowed %" | The author defines an encounter's approaches and outcomes in the browser | 3.5, 4.2 to 4.4, 6, 10.1 |
| 1e. Life aboard | `Sim.Storylets` with the expression language and quality view (stats and story flags); quality-gated, salience-ranked selection; morale; 5 vignettes (some with a small choice) and 2 captain storylets; captain's time; the inbox and crew file; `Sim.Chronicle` with the chronicle list | A full cycle of both layers in the browser (the Phase 1 result) | 3.7, 3.8, 5.1, 5.2, 5.4 |
| 1f. Tuning in play | Developer-mode pages: the mission bench in the browser, and the tuning page that edits the tuning file and re-runs the bench on the same seeds, before and after side by side | The author tunes without leaving the game | 4.7, 10.1 |

Slice 1e is cut into seven parts (five until 2026-11-17, when the storylet engine in full was split from part three into a new part four; six until 2026-11-20, when the crew file was split from part five into a new part seven), captain's time is a slice of its own (1g), and the skills, attributes, and gear overhaul is 1h, before the playtest: see GDD section 11.1 and section 15, items 32 to 38. Part one is designed in `2026-11-16-phase-1e-design.md` and was built on 2026-11-16 from `2026-11-16-phase-1e-part-one-plan.md`: story flags set by an encounter's outcome or a decision's answer, `Sim.Storylets` offering storylets in the aftermath (at most two a cycle) with fixed answers, "Matters aboard" in the browser, and two storylets in the game's pack; part two is designed in `2026-11-17-phase-1e-part-two-design.md` and was built on 2026-11-17 from `2026-11-17-phase-1e-part-two-plan.md`: morale that moves and changes checks, vignettes on the storylet shape, requirements on stats and the last cycle, and the captain's itinerary as the first phase of each cycle, where storylets and vignettes arrive; part three, traits that bite, is designed in `2026-11-17-phase-1e-part-three-design.md` and was built on 2026-11-17 from `2026-11-17-phase-1e-part-three-plan.md`: story flags became traits with definitions, rules, and pairs, in a new module `Sim.Traits`, biting on missions and shown in the briefing, and the worked example of a shaken crew member stood down from the next away team. Part four, the storylet engine in full, is designed in `2026-11-18-phase-1e-part-four-design.md` and was built on 2026-11-19 from `2026-11-18-phase-1e-part-four-plan.md`: the expression language over the state view on DynamicExpresso, a rolling history of each crew member's recent cycles, casting with up to three named slots in place of `scope`, salience and cooldowns, options with `requires` or settled by a check with a handler the captain names, Dev Play's "Why these storylets", the simulator's never cast, check success, and losses on the budget, and the worked example "The one who came back". The calendar (1e-calendar), designed in `2026-11-20-phase-1e-calendar-design.md`, is next: days and dates in place of the week counter, a rolled cycle length, and every timed effect due on a day (GDD section 15, item 43). Part five, the chronicle list, follows it, and part six is writing storylets in the editor (renumbered on 2026-11-17 from four and five). Part seven, the crew file, comes after 1h, because 1h settles what a character is (skills and attributes) and the crew file is the page that shows it (GDD section 15, item 42).

Slices 1a and 1b were built on 2026-11-14 from `2026-11-14-phase-1ab-plan.md`. The first part of 1c (a whole cycle, headless) was built on 2026-11-14 from `2026-11-14-phase-1c-headless-plan.md`: `Sim.Crew`, missions on the event bus, module state in the store, and `SimRunner cycles` playing real cycles. The second part of 1c (the cycle in the browser) was built on 2026-11-14 from `2026-11-14-phase-1c-web-plan.md`: `src/Web`, legacy selection, orders, briefing, resolution with paced beats and calls, aftermath, and the chosen design in `docs/design/`. The content editor (1c-editor) was built on 2026-11-14 from `2026-11-14-phase-1c-editor-plan.md`: display names, safe reading and writing of content files, a swappable live pack, and forms for every kind of content. Reports (1d) were built on 2026-11-14 from `2026-11-14-phase-1d-plan.md`: `Sim.Narrative`, situations for every beat, lines chosen by salience, the log as prose with tags and the roll on request, the aftermath report, and lines, symbols, and pronoun sets in the editor with a preview and a lint. Next is 1e, life aboard.

## Carried forward to the second part of slice 1c (the browser)

From the review of the first part. None is a rule change; each is something the web layer must do or decide.

- **Visual design** (the author, 2026-11-14): designed with the Hallmark skill; dark mode; the mood is Mass Effect first, with modern Star Trek and Honor Harrington second. An original design in that mood: no names, logos, or assets from any of them.
- **Never share a legacy's state between requests.** Load, send, and commit per request, under a lock per legacy. A view flips a read-only flag on the state while it is built, so a command sent on another thread in that window would lose its write.
- **Reading for a page must not write.** Reading a module's state outside a guard or a view creates an empty one. Pages need a read that does not create.
- **Any exception from a command other than a refusal means discard and reload.** Map a refusal to a message beside the control, and anything else to an error page.
- **A team cannot be changed once assigned,** and sending nobody is just advancing the phase. Decide: the page builds the team and sends it with the launch, or a command to clear the team is added (which is a rule).
- **Validate at the edge.** A null list in a command raises an error inside a guard, not a refusal.
- **Concurrency.** The event log's primary key is an accidental optimistic lock today. Make it deliberate (commit only if the stored sequence is the one loaded).
- **Streaming beats.** A command returns a mission's beats in one batch, up to the call, already committed. The page paces them; the beat index allows resuming. Reading events from a sequence, or by cycle, will be wanted.
- **Answering a call.** Everything needed is in the missions module's state (mission id, call id, and options). A double submit is refused cleanly.
- **Seeds are 64-bit numbers** in module rows, stream states, the export, and the simulator's output. Never put those in the browser's signals.
- **Listing legacies.** The store has no list, and a legacy has no name or creation time. The stored content pack id and version are never checked against the loaded pack.
- **Import is two transactions.** A failed import can leave a legacy with no events; a retry then fails as already existing.
- **Small test gaps left from the first part:** no committed test reads every event type back from its stored form (a probe did, and all passed); a stored event missing a member that has a default in its record is not refused.
- **For the author:** experience in a skill a crew member does not have is dropped (the starter crew all list every skill), and light injuries have no cap across missions (the most seen on one crew member in 10,000 missions was 2).

## Small things left in the browser part

Seen in the last review; none blocks play.

- A refused answer shows the bus's own words, with resolver ids ("The pending call is 'abort:ENCOUNTER_07', not 'dec:ENCOUNTER_03'."). Plainer words belong with the narrative slice (1d).
- During a mission the roster holds until the mission ends, so a crew member who dies at line 15 still reads Active beside the log until the end. It lags the log; it never runs ahead.
- A check's own effects have no beat, so the meters can move with no log line saying why. The report (1d) should say it.
- A not-found answer to a stream request replaces only the main region; the top bar and roster keep the old legacy.
- At 400 wide the objectives table's skills column is cut at the edge of its scroll region.
- Two test gaps from the first part remain: no committed test reads every event type back from its stored form, and a stored event missing a member that has a default is not refused.

## Carried forward to going online

Noted while building the browser part; none matters while the game is local and single-player.

- **No protection against cross-site form posts.** Add antiforgery to every post, including Datastar's.
- **No sign-in and no ownership.** Anyone who can reach the server can open any legacy by its id.
- **The server binds to localhost** by default; bind deliberately when hosting.
- **One lock per legacy, held in memory, never released from the table.** Fine for one process; a second server needs the stale-commit check (already in the store) to be the real guard, and the lock table needs trimming.
- **The beat pause holds a request open.** Many players mean many open streams; measure before hosting.
- **Content Security Policy.** Datastar's default needs `unsafe-eval`; its nonce mode avoids that.
- **Request limits.** Form and body sizes are the server's defaults (30 MB); there is no rate limit on creating legacies, and no security headers.
- **The lock table also grows for ids that do not exist.**
- **SQLite** opens a connection per call, with no busy timeout and no write-ahead log.
- **A published build** has no solution file to anchor relative paths, so the content and database paths must be set.
- **The pack warning** prints the pack's id and version to the player.

## Carried forward from the content editor

From its final review; none blocks play.

- **Typing during a row action is lost.** Adding, removing, or moving a row, or ticking a draw tag, redraws the form; anything typed into another field in that instant (about 70 ms here, longer on a slow machine) is dropped, and focus returns to the control that was used.
- **A skill delete that fails half-way** (the one save that writes two files) says nothing was written when the crew file was, and the running game keeps the old pack until a reload. The pack on disk stays valid.
- **The aftermath says no objective was left unattended** when one was skipped because its template is gone.
- **Two tabs on one item:** the second save wins silently, and so does a save over a hand edit made after the form was opened. A hidden hash of the item as opened would let the save refuse.
- **A legacy does not notice content edits** unless the pack's version is raised by hand; the warning compares id and version only.
- **A new content kind touches about ten places** (the loader, the core record, the reader, the writer, the faithfulness tables, the index, the form, and the names). One descriptor per kind would cut the web side to two.
- **Long authored text** (slice 1d) needs a block form in the writer, new keys in the faithfulness tables, and a text area field in the forms.
- **The log's parts** (a name or text) are the seam for the grammar engine; it will want the id and kind in a part, not only the resolved string, and the abort call needs a name source.
- **Crew generation** will need a kind that spans files, or saves of more than one file.
- **For hosting:** developer mode must never be on in a hosted game, and start-up should refuse developer mode on a bind that is not loopback; the game's own posts have no origin check; request sizes are not limited outside the editor's text pages.

## Carried forward to the content editor (as planned)

**Direction from the author (2026-11-14):** crew will be generated from authored sources (species, cultures, life paths, and name generators), not written one by one. Keep the crew form small: it names the eight starters and nothing more. Forms worth investing in are the ones for sources. Design the grammar engine in slice 1d so that name generators can use it.

From the review of the browser part.

Helps:

- The loader returns errors per file, ready to show beside a field.
- The web app can already be pointed at any content folder (`Web__ContentDirectory`).
- A page and a stream fragment are the same component, and the refusal pattern (an alert beside the control) suits field errors. The legacies page shows the plain-form pattern.

Hinders:

- **The content pack is loaded once at start-up** and held for the life of the process. A reload needs a holder that can be swapped, and each request must take one snapshot of it.
- **A running mission keeps the tuning it started with, but the screens read objectives, gear, and assignments from the live pack,** so a legacy in mid-cycle can show empty rows after an edit.
- **The pack warning compares only id and version.** The editor must bump the version on save, or the comparison needs a hash of the content.
- **There is no developer-mode switch,** and pages that write files need protection the game's pages do not have yet.
- **The tests and the gate read `content/` directly,** so the editor's tests need a scratch copy.
- **Figures in a log line are found by pattern,** which assumes ids shaped like today's (`CREW_01`). An id such as `ENC-3` would have its digit set as a figure.
- **Errors that span files** (an objective naming a missing encounter) need a place to be shown.

## Decisions each slice plan must make first

Slices 1a and 1b are designed in `2026-11-14-phase-1ab-design.md`, which settles their two entries below and where module state lives (one JSON row per module per legacy, built in 1c).

- **1a:** the tuning file's schema, and how a mission in flight pins the tuning it started with.
- **1b:** what "rookie, mixed, and veteran" generated teams are, in numbers.
- **1c:** the crew and mission-in-flight tables; how Datastar streams beats and delivers a call.
- **1d:** the grammar file format.
- **1e:** the expression language's grammar (kept minimal: comparisons, `and`, `or`, and quality paths).
- **1f:** whether tuning edits write to the content folder directly or to a developer override file.

## What the author must write before each slice can be played

Placeholders work throughout, so nothing blocks the build.

- **1c:** names for 8 crew and 4 skills; text for 2 objective templates and 6 encounters.
- **1d:** report grammar lines.
- **1e:** 5 vignettes and 2 captain storylets.

## Carried forward from the prototype

- The prototype (`C:\Users\parker00\Dropbox\Work\f-prototype\mission-feel-prototype.html`) is the reference for slice 1a's behaviour, with the six fixes in GDD 4.2 to 4.4 applied on top. It still has the old rules.
- Its hash-by-key dice are replaced by `RngStream`, so the prototype's seeds will not reproduce; the walkthrough tests are rebuilt on new seeds.

## Carried forward from the Phase 0 review

The whole-branch review of Phase 0 (2026-11-13) left these for Phase 1. Each slice plan picks up the ones in its path.

Decide before slice 1a:

- **Where feature state lives.** `LegacyState` sits in `Sim.Core` and every handler gets the whole mutable object, so today a new mechanic must edit Core and the store's columns. That cuts against "a new mechanic is a new module".
- **SQLite schema version.** Add `PRAGMA user_version` and named insert columns before the first schema change.

Slice 1a (mission resolver):

- Pin a short `D6` and `Next` sequence; only `NextUInt64` is pinned today.
- Pass the content pack into `Simulation.Run`.
- Write one end-to-end test of bus, registry, `Commit`, `Load`, and continue. Document on `Send` that a throw means "discard the state and reload".
- Move the stream-pruning test to `EventBusTests`, and check that new-cycle streams survive.
- Replace the banned-token substring test with `Microsoft.CodeAnalysis.BannedApiAnalyzers`, and assert that `Persistence` depends only on Core.

Slice 1b (mission bench):

- Rewrite the `SimRunner` argument parser: a bad value, a flag with no value, or an unknown flag exits 2. Add exit-code and `--out` tests.
- Consider comparing full event logs for the golden seed, not only a hash.

When next touched:

- Event log: guard a `"null"` payload; test a duplicate name and version, and `ToEnvelope` on an unregistered type.
- Content loader: load the starter pack through `LoadDirectory` in a test; report `.yml` files and root-level files with a clear error.
- Store: make `Import` one transaction; strengthen `Events_are_isolated_by_legacy`; report a seed mismatch as a seed mismatch.
- Project files: strip `TargetFramework`, `ImplicitUsings`, and `Nullable` from each csproj.
- `PhaseEntered` carries `Cycle` in both payload and envelope; decide before real legacies exist.

## Carried forward from the review of slices 1a and 1b

Done in the first part of slice 1c, except the last two items (seeds in the browser, and the async bans), which belong to the browser part:

- **Experience per skill.** The result carries one experience number per crew member, but crew hold experience per skill. Make it a map by skill in the mission state and result, and put the helper's share on the check beat.
- **A version on the stored mission state**, and a deliberate choice between strict and tolerant reading. A tuning number added later makes a stored in-flight mission fail to load.
- **Answers bound to a call.** `Advance` applies an answer to whatever is pending and ignores one when nothing is. Require the call's id with the answer, and reject a mismatch.
- **"Withdrawal" means two things:** the team withdrew, or it ran every stage and achieved too little. Outcome names are stored, so decide on a fifth outcome first. Also decide whether withdrawing under a lead who preferred the safe option should count as overruling them.
- Mission seeds must be unique per mission within a legacy (mission dice use cycle 0). A `ulong` seed loses precision as a JSON number in the browser. State and beats are stored in one transaction.
- Ban `Thread.Sleep(TimeSpan)` and `Task.Delay` in the pure core when async code arrives.

Decided by the author on 2026-11-14: "develop juniors" is carried by the senior (GDD 4.2), so it never beats best qualified; the reckless-lead loss is accepted; rescue at the strain cap stays as it is. The original notes:

- **"Develop juniors" beats best-qualified on evenly skilled teams**, because a supervising senior assists at skill minus 1 and an ordinary helper at half. Generated veteran team, 10,000 runs: 99.0% full success with it, 95.2% without.
- **Reckless-lead loss is about 17%**, against the 13% accepted on the prototype.
- **Rescue at the strain cap is incidental.** It exists only when the encounter has a decision. Offering the abort call at the cap, when it has not yet been asked, would make it dependable; it is one condition.

When next touched:

- Resolver: tests for the injury thresholds, the medical bonus, the experience bands, and lead succession in a collapse; an injuries part on the check beat; an injury beat that names its cause, so the bench can show deaths per 100 checks; `Start` should enforce the objective's team size and distinct member ids.
- Loader: a repeated `injuryRoll` or `withdraw` in one effect list should be an error; the worst-case slot check is greedy and can under-report.
- Bench and command line: guards on `Summary` and `Run` for the browser (slice 1f); `--replay` parsed before content loads; an unwritable `--out` reported cleanly.

Done from the Phase 0 list: the dice mapping is pinned, the `SimRunner` parser is strict with exit-code tests, the banned-API analyser replaced the substring test, `Persistence` is asserted to depend only on the core, and the csproj files no longer repeat shared properties.

## Carried forward from reports

From building slice 1d; none blocks play.

- **Settled from earlier lists:** a refused answer now says what happened in plain words; a check's effects are cited by its tags; the abort call is phrased by lines with `decision: DECISION_ABORT`; the log's parts carry names apart from text, and the engine works on situations, not on resolved strings.
- **Verb agreement follows a pronoun.** `#actor:checks|check#` gives the plural form for a plural set, so it belongs after `#actor.they#`; after a name, the author writes the singular. Generated crew may want the engine to know which it follows.
- **Repetition is avoided within one mission only.** Memory across missions needs per-legacy narrative state (1e).
- **Sample missions are fixed and small** (each objective template on four seeds, two teams). Rare beats (supervised checks, skips) may never appear in them, so the lint reports such lines as never chosen.
- **The lines page runs the samples** once per pack, then keeps them while that pack is live.
- **`tests/browser/edit.sh` was not extended** to write a line and see it win: the browser scripts need `playwright-cli`, and the web step of `check.sh` needs `python3` and `setsid`, none of which this Windows machine has. The same behaviour is covered by the web tests through the real app.
- **Variants and contexts** are not yet criteria: variants change the resolver's draw, so they come with the content that needs them.
