# 7. Architecture

A chapter of the [Starship Sim GDD](../gdd.md). Section numbers are permanent ids; cross-references such as "section 4.3" find their chapter through the index.

## 7.1 Modules

| Module | Responsibility | Depends on |
|---|---|---|
| `Sim.Core` | Domain types, event bus, seeded RNG, expression language, state view | Nothing |
| `Sim.Content` | Load and validate content packs | Core |
| `Sim.Missions` | Assignment, site, and objective generation; the default resolver | Core |
| `Sim.Storylets` | Selection, casting, salience, and effects | Core |
| `Sim.Traits` | Traits on the ship, crew, and pairs, and their rules (designed for slice 1e, part three) | Core |
| `Sim.Crew` | Growth, morale, relationships, careers, and roster | Core |
| `Sim.Chronicle` | Renown, succession, and chronicle projection | Core |
| `Sim.Narrative` | Salience selection and expansion of lines (the grammar engine) | Core |
| `Sim.Cycle` | Phase state machine; the only orchestrator | Core |
| `Persistence` | Database, event log, and legacy load and save | Core |
| `Web` | ASP.NET endpoints and Datastar views | All, through commands and queries |
| `SimRunner` | Headless balance simulator | All except Web and (optionally) Persistence |

`Sim.Storylets` as built in slice 1e, part one: it owns the story flags and the waiting storylets. It hears of missions only through `MissionEnded`, whose result lists the flags the mission changed, and stores them with the week and the encounter. When the aftermath begins it offers what is due (`StoryletOffered`); `AnswerStorylet` applies an option's effects at once, flags in its own state (`FlagChanged`) and loyalty through the crew module's `AdjustLoyalty` command, and publishes `StoryletAnswered`. A guard on `AdvancePhase` holds the aftermath while a storylet waits. On `CrewDied` it drops the flags on that person and withdraws their storylets (`StoryletWithdrawn`). It reaches crew only through the roster view. Its contracts live in `Sim.Core`, so the bridge and the simulator never reference the module, and the simulator's captain answers each waiting storylet with its first option.

As built in slice 1e, part two: the cycle opens on a new phase, the captain's itinerary, and `Sim.Storylets` offers storylets there instead of in the aftermath, and holds the itinerary, not the aftermath, while one waits. It also sends vignettes there (`VignetteArrived`), applies a vignette's effects as it arrives or its answer (`AnswerVignette`, `VignetteAnswered`), and gives each unanswered one its default when the orders open. It records who was sent, hurt, and killed when a mission ends, for requirements on the last cycle, and reads morale and loyalty through the roster view. `Sim.Crew` owns morale: it changes it after a mission and each week, takes `AdjustMorale` from answers, publishes `MoraleChanged` with a cause, and hands missions each member's morale bonus in the team it builds, so `Sim.Missions` never reads morale.

Designed for slice 1e, part three (`docs/plans/2026-11-17-phase-1e-part-three-design.md`, section 5; not yet built): story flags become **traits** and leave `Sim.Storylets` for a new module, **`Sim.Traits`**, ordered after missions and before storylets (crew 100, missions 200, traits 250, storylets 300), so a death on the mission is known when the mission's trait changes are applied and the itinerary reads traits as they now stand.

| Module | In part three |
|---|---|
| `Sim.Traits` | Owns trait state (on the ship, a crew member, or a pair) and every rule in the definitions in `content/traits/`. Serves `ITraitView`. Handles `ChangeTrait` and `SetTrait` (Dev Play). Applies a mission's trait changes on `MissionEnded`, runs launch rules on `MissionStarted`, weekly steps and expiry on `WeekAdvanced`, and a pair's `onTargetDied` on `CrewDied`. Publishes `TraitChanged` with a cause (mission, answer, vignette, launch, weekly, expired, death, developer). Sends `AdjustLoyalty` and `AdjustMorale` for rules' effects. |
| `Sim.Missions` | At launch, fixes each team member's traits and trait check bonuses onto `TeamMember`, and the ship's traits onto `Team`, from `ITraitView`; the resolver reads only that snapshot, for an approach's trait requirements and for bonuses. Its result lists trait changes, a pair's target included. |
| `Sim.Storylets` | Selection and answers only. Reads traits through `ITraitView`; sends `ChangeTrait` for answers' and vignettes' trait effects; gains storylets and vignettes for a pair. |
| `Sim.Crew` | Unchanged; takes loyalty and morale commands from the traits module as from the storylets module. |

No rule in code names a trait id. The simulator's `random` answer policy draws from its own stream, `sim-captain`.

Rules that keep agents from rewriting old code:

- Feature modules never reference each other. They react to events and read through the state view (the quality view until 2026-11-17: section 15, item 40).
- Everything under `Sim.*` is pure: no database, no clock, and no ambient randomness. State in, commands and events out.
- A new mechanic is a new module that subscribes to existing events and publishes new ones. Existing modules change only to react to the new events, if they need to.

## 7.2 Event bus

- In-process, synchronous, and ordered. One player action runs as one database transaction.
- **Commands** ask for a change (`AssignTeam`, `AnswerCall`, `ApplyEffect`). Exactly one handler each.
- **Events** state what happened (`EncounterResolved`, `CrewInjured`, `RelationshipChanged`, `CycleAdvanced`). Any number of subscribers. Every event is appended to the log.
- Events are versioned and additive. A field is never removed or repurposed; a changed meaning is a new event type.
- Handlers run in a declared order, so a replay with the same seed gives the same result.
- Storylet and encounter effects are data that compiles to commands. Adding an effect type means adding a command, not editing the storylet engine.

## 7.3 Determinism

Each random draw comes from a named stream: `hash(legacySeed, cycle, streamName)`. Adding a new mechanic that draws from its own stream does not disturb the draws of existing ones, so golden-seed tests survive new features.

## 7.4 Save format

- Postgres in production, SQLite allowed locally, behind one persistence interface.
- Every row is keyed by `legacyId`. This is the online isolation boundary, and the future seam for the shared universe.
- Current-state tables for crew, relationships, flags, missions in flight (resolver state as JSON), and the inbox.
- `event_log` (append-only, JSON payload) and `chronicle`.
- Each legacy pins the content pack version it was created under and records each upgrade.
- Single autosave: state commits after every action. There is no load.
- **Developer mode** (off for normal play): snapshot, restore, and export a legacy to a JSON file. Needed for bug reports and to feed real legacies into the simulator. Without it, ironman plus early bugs would destroy the author's own games.

Why not full event sourcing: rebuilding state from the log requires every old event to replay correctly forever. Over years of changing mechanics that is a standing tax. State tables plus the log give the chronicle, storylets that reference history, and debugging, without the tax.

## 7.5 Front end

Server-rendered HTML with Datastar. The server holds all state; the browser holds none. Resolution streams beats over server-sent events, and a call arrives as a fragment containing a form. Three screens carry the game: the inbox, the crew file, and the assignment screen. The duty roster, chronicle, and ship screen follow.

Built in slice 1c (`src/Web`, run with `./run.sh`):

- **The page is the state.** Every screen is drawn from the stored legacy, so a reload at any moment, even with a call waiting, shows where things stand. Nothing the game needs lives only in the browser.
- **One request, one command, one commit.** Each request locks the legacy, loads it, sends its commands, commits, and renders. A refusal is shown beside the control that caused it and stores nothing.
- **Datastar 1.0.5, self-hosted,** with the stream events written by hand; pages and stream fragments are the same server-rendered components. The game needs no network.
- **Briefing is one form,** sent whole at launch. Sending nobody just moves the phase on.
- **Beats are paced by the server.** A launch or an answer resolves the mission to its next call or its end and commits; the response then sends the log one beat longer each time, with a pause that is a setting. The roster, the meters, the call, and the outcome hold back until the log reaches them, so nothing gives the ending away. A dropped connection loses nothing.
- **The screens so far:** the list of legacies, orders, briefing, resolution, and aftermath, with the top bar (week, cycle, and the six phases) and the roster beside each. The inbox and the crew file come with slice 1e.
- **Beat lines are prose** (slice 1d): an authored line, then its tags in the mono figure style, then a disclosure ("Roll" on a check) with the structural line: `CREW_02 checks SKILL_B at ENCOUNTER_01, helped by CREW_05: 6 + 3 against 15, failure (−6).` A stage line keeps its "Stage N:" head before the prose; N counts encounters, so an objective whose stages draw two encounters each runs past its own number of stages. The aftermath opens with the report. A refused call answer says what happened in plain words.
- **A legacy played through the browser has exactly the simulator's event log** for the same seed and choices; a test holds the two together.
- **Everything authored has a display name** (slice 1c-editor): skills, crew, gear, objective templates, encounters, decisions, and assignments. Ids stay the keys in saves, events, and references; the screens and the log show the name and fall back to the id. Two items that share a name show their ids beside it.
- **The content editor** (slice 1c-editor, `/dev/content`, developer mode only; `run.sh` turns it on): a form for each kind of content, and the tuning and pack files as text. Slice 1d adds lines (with a preview that expands the line as typed against fixed sample missions on the starting crew, and says whether it wins each beat or which line beats it), symbols, pronoun sets, the crew's pronoun pick, and a lint on the lines page that names lines no sample chooses and beats with no line. A save builds the whole pack as it would be, runs the loader on it, and only then writes one file and swaps the pack in the running game; a save the loader refuses writes nothing and shows the reasons. The encounter and objective forms end with "What plays here" (slice 1d authoring): the item's beats in mission order, each with a box for its own words or the line that plays there now, lines of the item's own under their beat with their criteria as rows, live previews of the unsaved form against samples built for the item, and chips that insert what a beat can name. A file holding anything the editor cannot keep faithfully is never rewritten. Names and gear reach games in progress at once; a legacy's crew keep the levels they started with. Git stays the record.
- **The captain's itinerary** (slice 1e, part two): the first screen of a cycle. Its head says what needs the captain and shows the captain's time (3, "Not spent yet"; slice 1g spends it). A section "Storylets" holds the storylet panels described below, and a section "Vignettes" one panel for each vignette that arrived: its name, whom it is about and the fact that brought it ("CREW_03, lost a crewmate"), its script, and either its tags (no choice) or two buttons with a note of the answer taken if it is left. Panels can be answered in any order. "To orders" is disabled while a storylet waits. The roster gains a "Morale" column in words (Low, Steady, High), and the aftermath's crew changes give each member's net morale change from the mission. The vignettes file opens in the editor as a text file.
- **Traits on screen** (designed for slice 1e, part three; not yet built): the briefing shows a chip beside each picked crew member for each trait with a briefing line ("Promised a rest"; a pair's only when both are picked), and above Launch, what launching will do ("Breaks the promise to CREW_03: Loyalty −2, Morale −4"), worked out by the same code that applies the rules; nothing blocks the launch. **The roster gains a "Traits" cell as an interim fix:** chips for each shown trait, until the crew file (1e, part five) replaces it and the cell is removed. Dev Play shows and sets every trait, hidden ones and pairs included. The traits file opens in the editor as a text file.
- **Matters aboard** (slice 1e, part one): as first built, a storylet waited in the aftermath, in a section above the report; since part two the same panels are on the captain's itinerary with one panel for each, in arrival order: its name, where it came from (the encounter that set the flag, and the flag), its script, and its answers as buttons with their authored labels. "Next cycle" is disabled with the reason while any waits. Once answered, the panel stays for the rest of the aftermath in a settled form: the option's text, then tags for what changed ("Loyalty −1 all crew", "flag_a 1 to 0"). The storylets file appears on the editor's index as a text file, validated by the loader on save like the tuning file; the storylet form is part six (renumbered on 2026-11-17 from part five).
- **The look** is recorded in `docs/design/` (the mock-up, the tokens, and the component recipes): a blue-slate ground, one azure signal, a serif for headings over a technical grotesk, monospaced figures, and the mission log as one numbered trail, newest first, under the call that waits on it. Screens are in `docs/screens/1c/`.
