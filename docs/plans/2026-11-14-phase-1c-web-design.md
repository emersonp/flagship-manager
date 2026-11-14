# Phase 1, slice 1c, part two: the cycle in the browser

**Slice:** the second part of 1c. The first part plays whole cycles headless; this part puts the browser on top and adds no rules.
**Spec:** `docs/gdd.md` sections 2.2, 7.4, and 7.5. **Builds on:** `main` at 2026-11-14 (slice 1c, part one). **Reference:** `docs/research/datastar-1.0.5.md`.
**Ends with:** the author opens the game in a browser, starts a legacy, reads the site, picks a team, watches the mission arrive beat by beat, answers its calls, reads the aftermath, and goes on to the next cycle. Closing the browser at any point loses nothing.

## 1. Decisions

From the author, 2026-11-14:

1. **Visual design by the Hallmark skill.** Dark mode only. The mood is Mass Effect first, with modern Star Trek and Honor Harrington second. An original design: no names, logos, or assets from any of them.

Made here, each cheap to reverse:

2. **Server-rendered HTML with Datastar 1.0.5, self-hosted.** The one script file lives in `wwwroot`; the game works with no network. No Datastar .NET package: the two stream events are a few lines to write, and the package brings an F# runtime with it.
3. **Razor components rendered on the server** (`.razor` files, no interactivity, no Blazor script). A page is a component; a fragment sent in a stream is the same component rendered to a string. One template for both, so the page after a reload is exactly what the stream built.
4. **The page is the state.** Every screen is rendered from the stored legacy. Nothing the game needs lives only in the browser, so a reload at any moment (mid-mission, with a call waiting) shows where things stand.
5. **One request, one command, one commit.** Each request takes the legacy's lock, loads it, sends its commands, commits, and renders. A refusal becomes a message beside the control. Any other failure discards the loaded state and shows an error. State is never shared between requests.
6. **Briefing is a plain form.** The team, lead, gear, and parameters are form fields, posted once with "Launch". The team is not stored until launch, so there is nothing to undo and no new command. "Send nobody" just advances the phase.
7. **Beats are paced by the server.** A launch (or an answer) resolves the mission to its next call or its end and commits. The response stream then sends the mission log again and again, one beat longer each time, with a pause between. The log is replaced whole each time, so a dropped connection or a reload cannot double a beat. The pause is a setting (zero in tests).
8. **Beat lines are plain and structural** in this slice: who checked what, against what, and how it went, built from placeholder ids. The narrative report replaces them in slice 1d.
9. **A legacy has a name and a creation time.** The player types the name; the web layer supplies the time. Both are stored beside the legacy, not in the simulation state.
10. **No numbers the browser can damage.** Seeds and stream states never leave the server. A legacy is addressed by its id in the URL.
11. **Signals are used only for what is local to the page** (which crew are ticked, to count them and enable "Launch"). Their names start with an underscore, so they are never sent.

## 2. Screens

One shell: a top bar (the ship's placeholder name, the legacy's name, the week, the cycle, and the phase as five steps), the main region, and the roster beside it.

| Screen | Shows | Actions |
|---|---|---|
| Legacies (`/`) | The stored legacies: name, week, cycle, crew alive, and when created. An empty state that says what a legacy is. | Open one. Start a new one (a name; the seed is drawn by the server). |
| Orders | The site: its objectives, each with its template id, skills, team size, and whether it has an unattended difficulty. | To briefing. |
| Briefing | The objective chooser (when the site offers several), the eight crew as rows to tick (fit or not, skills, injuries), the lead, gear up to the loadout limit, and the three operational parameters. | Launch. Send nobody. |
| Resolution | The mission log, arriving beat by beat; momentum and strain; a call when one is waiting, with its options and the lead's preference. | Answer the call. To aftermath, once the mission has ended. |
| Aftermath | The outcome; what changed for each crew member (levels, injuries, deaths, the lead's loyalty); each unattended objective's roll. | Next cycle. |
| Roster (beside every screen) | The eight: status, skill levels, light injuries and when each heals, the week an incapacitated member returns, loyalty, and away missions. The dead stay listed, marked. | None in this slice. |

The signature of the interface is the **mission log**: the one object this game is known by.

Every region drawn from stored data has its four states: loading (while a request is in flight, the control that sent it shows it), empty, error, and full.

## 3. Requests

| Request | Does | Answers with |
|---|---|---|
| `GET /` | Lists legacies. | The page. |
| `POST /legacies` | Creates a legacy and founds it. | A redirect to it. |
| `GET /legacy/{id}` | Loads it. | The page for its phase. |
| `POST /legacy/{id}/advance` | `AdvancePhase` (from aftermath it advances twice, through the advance phase to the next cycle's orders). | A stream that replaces the main region, the top bar, and the roster. |
| `POST /legacy/{id}/launch` | `AssignTeam`, then `AdvancePhase` into resolution, in one commit. | A stream: the resolution screen, then the log beat by beat, then the call or the result. A refusal: the reason beside "Launch", and nothing stored. |
| `POST /legacy/{id}/answer` | `AnswerCall`. | A stream: the log continuing, then the next call or the result. |

A request for a legacy that does not exist is a 404 page. A stored legacy whose content pack id or version differs from the loaded pack shows a warning in the top bar and still opens.

## 4. Storage additions

- `legacy` gains `name` and `created_at`; the schema version moves to 3. An older database is refused, as before; there are still no real legacies to migrate.
- `ListLegacies()` returns id, name, creation time, week, and cycle, newest first.
- `Commit` checks that the stored sequence is the one the state was loaded at, and refuses otherwise. With the lock this never fires; it is the guard for the day a second server exists.
- `ReadEvents(legacyId, cycle)` reads one cycle's events, for the log and the aftermath.
- The developer export carries the name and creation time (format 3).

## 5. Reading without writing

Pages read module state only through the non-creating read (`TryModule`) and the views. A small read model per screen (`BridgeView`: phase, site, briefing choices, mission log, call, aftermath, and roster) is built from the state and the cycle's events in one place, so the components hold no logic and the tests can assert on the model.

## 6. Visual design

**Superseded on 2026-11-14.** The author chose between four mock-ups and asked for a blend; the design of record is `docs/design/bridge-design.md` and `docs/design/bridge-mockup.html`. What follows is the first direction, kept for the record.

Dealt by Hallmark: the **Midnight** theme, as an app screen.

- Ground: midnight blue to every edge. Surfaces step lighter, cut square. No rounded corners, glass, glow, or texture.
- Type: Mona Sans, self-hosted. Headings at the expanded width and a light weight; everything else at the normal width. Figures are tabular. Labels are sentence case, a step smaller, and never tracked capitals.
- One azure signal: the primary action, the current phase, and the current selection. Nothing else.
- State colours, used only as state and each with a text label beside it: harm (injured, incapacitated, dead, or lost) and caution (a call waiting, strain high). They are desaturated to sit in the blue.
- Density: controls 32 to 40 pixels tall with a 44 pixel target; tables before cards.
- Motion is state only: a new beat settles in once; reduced motion turns that off.
- The shell collapses to one column below 60rem, with the roster under the main region.

The stylesheet carries the Hallmark stamp and passes its linter (`sloplint --scope app`).

## 7. Tests

- **Store:** list, name and creation time, the sequence check, events by cycle, and the schema refusal at version 2.
- **Read model:** each phase's model from a known legacy; the log's lines for each beat type; the aftermath's changes.
- **Requests** (the real app in memory, beat pause zero): create and open a legacy; a whole cycle through the six requests; each refusal shows its reason and stores nothing; a double answer is refused; an unknown legacy is a 404; two requests at once on one legacy both succeed in turn; a reload mid-call shows the call.
- **Stream format:** the events are well formed, the log grows by one beat per event, and it ends with the call or the result.
- **In a real browser** (one scripted run, kept out of the gate if it is slow): start a legacy, launch a team, answer a call, reach the next cycle; no console errors.
- **Parity:** a legacy played through the requests with the automatic captain's choices has the same event log as `SimRunner` for the same seed.

## 8. Not in this part

The content editor (the next slice), reports and the chronicle (1d), storylets and the inbox (1e), the bench and tuning pages (1f), several teams, sign-in, and anything online.
