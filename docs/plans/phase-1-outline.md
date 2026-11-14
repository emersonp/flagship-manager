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
| 1d. Reports | `Sim.Narrative`: the grammar engine and the text report built from beats | A finished mission reads as a report, not a log | 6 |
| 1e. Life aboard | `Sim.Storylets` with the expression language and quality view; morale; 5 vignettes and 2 captain storylets; captain's time; the inbox and crew file; `Sim.Chronicle` with the chronicle list | A full cycle of both layers in the browser (the Phase 1 result) | 3.7, 3.8, 5.1, 5.2, 5.4 |
| 1f. Tuning in play | Developer-mode pages: the mission bench in the browser, and the tuning page that edits the tuning file and re-runs the bench on the same seeds, before and after side by side | The author tunes without leaving the game | 4.7, 10.1 |

Slices 1a and 1b were built on 2026-11-14 from `2026-11-14-phase-1ab-plan.md`. The first part of 1c (a whole cycle, headless) was built on 2026-11-14 from `2026-11-14-phase-1c-headless-plan.md`: `Sim.Crew`, missions on the event bus, module state in the store, and `SimRunner cycles` playing real cycles. The second part of 1c (the cycle in the browser) was built on 2026-11-14 from `2026-11-14-phase-1c-web-plan.md`: `src/Web`, legacy selection, orders, briefing, resolution with paced beats and calls, aftermath, and the chosen design in `docs/design/`. Next is the content editor.

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

## Carried forward to the content editor

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
