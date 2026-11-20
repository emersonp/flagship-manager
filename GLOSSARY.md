# Starship Sim

An open-ended starship management sim: the player holds the chair of a ship whose name outlives its hulls, captains, and crew. This glossary fixes the systems vocabulary; all in-fiction names are the author's to write.

## Language

### Structure of a game

**Legacy**:
One player's isolated game world: one lineage, its crew, and its chronicle. The unit of saving and of online isolation.
_Avoid_: Save, campaign, legacy, captaincy (as a synonym), world

**Chair**:
The player's seat. The player is the chair, not any one captain.
_Avoid_: Player character

**Captain**:
The crew member currently holding the chair. A full crew member with one extra role.
_Avoid_: Player, commander

**Captaincy**:
One captain's tenure in the chair, from appointment to departure.
_Avoid_: Reign, term

**Cycle**:
One round of the captain's itinerary, orders, briefing, resolution, and aftermath. Usually one week long, and longer in transit.
_Avoid_: Turn, week (a cycle is not always one), tick

**Captain's itinerary**:
The first screen of a cycle: the storylets that must be answered and the vignettes that may be, in any order, and the captain's time.
_Avoid_: Inbox (until it has a backlog), matters aboard

**Week**:
The unit of time. A legacy counts weeks from 1, and anything that takes time (healing, training, ageing) is due at a week, never at a cycle.
_Avoid_: Day, turn

**Transit**:
A single long cycle between assignments in which no missions run.

**Hull**:
One physical ship. Hulls are destroyed, refitted, or replaced.
_Avoid_: Ship (when the lineage is meant)

**Lineage**:
The succession of hulls carrying one ship name, distinguished by suffix.
_Avoid_: Dynasty (reserved for captains), ship line

**Succession**:
The event in which a lineage moves to a new hull, or the chair moves to a new captain.
_Avoid_: Respawn, replacement

**Chronicle**:
The permanent, curated history of a legacy, readable by the player and queryable by storylets.
_Avoid_: History log, journal, event log (that is the technical record beneath it)

**Memorial**:
A chronicle entry for a dead crew member or lost hull that later content can reference.

### Crew

**Crew member**:
Any simulated person aboard. All crew members are simulated identically regardless of ship size.
_Avoid_: Character, NPC, unit

**Roster**:
Every living crew member of the ship. The dead leave the roster and stay in the chronicle.
_Avoid_: Crew list, party

**Fit**:
Said of a crew member who can be sent on an away team this week. A lightly injured crew member is fit, at a penalty; an incapacitated one is not.
_Avoid_: Available, healthy

**Department**:
A functional grouping of crew members under one head.

**Department head**:
The crew member who runs a department, fills its duty roster by default, and resolves storylets below their stakes threshold.
_Avoid_: Sub-manager, officer (too broad)

**Duty roster**:
The standing assignment of each crew member's post, department, and training focus. Persists until changed.
_Avoid_: Schedule, lineup

**Relationship**:
A directed tie from one crew member to another with a kind and a strength. Exists only where there is history.

**Org chart**:
Who reports to whom aboard. Always visible to the player.

**Insight**:
How clearly the player can see a crew member's skills, grades, personality, morale, and relationships: hidden, rough, or clear.

**Attribute**:
A broad capacity, such as fitness or presence, that groups skills across departments. Most skills belong to one department and one attribute.
_Avoid_: Stat (that is wider), ability

**Grade**:
A crew member's standing in a department or attribute, computed from the skills under it. It has a height and a shape (good across the board, or one standout skill), and insight decides how much of it shows.
_Avoid_: Rating, score
_Avoid_: Visibility, fog, scouting

**Personal renown**:
A crew member's reputation in the fleet.

**Ship renown**:
The lineage's reputation. Persists across hulls and captains.
_Avoid_: Score, prestige, legacy points

**Admiralty standing**:
How much the Admiralty trusts the current captaincy.

**Requisition**:
The budget from which gear is drawn.
_Avoid_: Money, credits

### Missions

**Assignment**:
What the Admiralty has sent the ship to do. Lasts one or more cycles.
_Avoid_: Mission (that is one team's part), orders, quest

**Mandate**:
An assignment the Admiralty orders. It can be declined only with enough Admiralty standing, and at a cost.
_Avoid_: Mandatory mission, order

**Site**:
The place an assignment brings the ship to in one cycle, with its own set of objectives.
_Avoid_: Planet, location, node

**Objective**:
One thing an away team could do at a site.
_Avoid_: Mission (before a team is sent), task, quest

**Unattended**:
Said of an objective no team was sent on. It still resolves.

**Mission**:
One away team sent on one objective.
_Avoid_: Assignment, objective

**Away team**:
The crew members sent on one mission, with a loadout and operational parameters.
_Avoid_: Squad, party

**Team lead**:
The away team member whose personality decides whether a decision becomes a call or an initiative.

**Operational parameters**:
The captain's standing instructions to an away team: priority and how readily to call in.
_Avoid_: Settings, stance

**Encounter**:
One situation within a mission, resolved by a check and possibly a decision.
_Avoid_: Event (reserved for the technical bus), room, node

**Check**:
A graded test of an away team's skills, gear, and traits against an encounter's difficulty.
_Avoid_: Roll

**Approach**:
One of the ways an encounter offers to attempt its check, written by the author. The team lead chooses among those available, by skill.

**Tier**:
The graded result of a check: critical, success, partial, failure, or disaster.
_Avoid_: Grade, degree of success

**Outcome**:
What a tier of an approach does to the mission: progress, momentum, strain, or an injury roll. Not the result of a mission or of an injury roll, which keep the word 'result' in prose.

**Momentum**:
The running advantage a team carries forward from good outcomes within a mission.

**Strain**:
The running burden a team carries forward from bad outcomes within a mission.

**Decision**:
A choice arising in an encounter. It becomes either a call or an initiative.

**Call**:
A decision the team lead refers to the captain. The mission waits for the answer.
_Avoid_: Callback, interrupt

**Initiative**:
A decision the team lead made alone and reported afterwards.
_Avoid_: Notice, FYI, report-after

**Stakes**:
The graded weight of a decision or storylet. Compared against a threshold to decide who resolves it.

**Beat**:
One recorded moment of a mission or storylet, the raw material for narrative text.

**Mission log**:
The beats of one mission in order, as the captain sees them arrive.
_Avoid_: Feed, timeline, history

**Bridge**:
The screen the captain plays a cycle from: the phase's main view with the roster beside it.
_Avoid_: Dashboard, HUD, main screen

**Report**:
The narrative account of a finished mission, generated from its beats.

**Script**:
A line's text when it is more than one sentence: entries of speech (with a speaker), system notes, and narration, and optionally the sentence the report uses in their place.
_Avoid_: Dialogue tree, conversation (a script has no choices in it)

**Attempt**:
The moment before a check's result is known, which an approach can give words of its own.

**Line**:
An authored sentence or script for one kind of beat, with criteria that say when it fits. The most specific line whose criteria hold is the one shown, in the log and in the report alike. A line about one encounter or objective is written inside it and holds only there; a **shared line** belongs to none and is written on its own.
_Avoid_: Template, string, message

**Symbol**:
A named set of options a line can use (`#hesitates#`); one is picked for each beat.

**Situation**:
What a line is chosen for: the facts of one beat and the people in it.
_Avoid_: Context (reserved for an assignment's theme)

**Tuning file**:
The one file in the content pack that holds every number the mission resolver uses. A mission keeps the tuning it started with.
_Avoid_: Config, settings, balance file

**Mission bench**:
The developer tool that runs one mission many times and reports how it tends to go. It never touches a legacy.
_Avoid_: Simulator (that plays whole cycles), test harness

### Life aboard

**Quality**:
Anything a requirement can read: a stat or a trait (section 15, item 40). Read through the state view.

**Stat**:
A typed, simulated number owned by a system: a skill, a grade, a personality axis, morale, loyalty, renown, or standing. A quality content can read, but changes only through commands.
_Avoid_: Attribute (that is a grouping of skills)

**Trait**:
A quality named by content: a whole number of 0 or more (0 is absent), stored generically, on the ship, a crew member, or a pair of crew. It may have a definition that names its levels and gives it rules. Shown traits appear to the player; hidden ones mark story progress. Replaces the story flag in slice 1e, part three.
_Avoid_: Flag, story flag (retired 2026-11-17)

**Pair trait**:
A trait held by one crew member (the holder) that points at another (the target): "avoids CREW_03".
_Avoid_: Relationship (a stat, Phase 4)

**State view**:
The read-only view through which modules and content read stats and traits.
_Avoid_: Quality view (its name until 2026-11-17)

**Story flag**:
See Trait (its name until slice 1e, part three).

**Storylet**:
A unit of narrative content with requirements, a cast, and mechanical effects.

**Expression**:
A condition in the expression language, read through the state view, true or false: `subject.trait.shaken >= 1 && subject.history.last('hurt')`. Storylets, vignettes, and options are written with them; encounter approaches are not.
_Avoid_: Formula, script (a script is a storylet's text)

**Slot**:
A named place in a storylet's or vignette's cast, filled by one crew member: `subject`, `target`, or any lower-case name not reserved. At most three, read in order.
_Avoid_: Role, scope (its forerunner, until slice 1e, part four)

**Cast**:
One binding of every slot to a crew member, which a storylet or vignette arrives with. As a verb: to find one.
_Avoid_: Binding (in prose), subject (one slot of it)

**Contender**:
A storylet or vignette that was cast this cycle and is out of its cooldowns, so salience ranks it against the budget.
_Avoid_: Candidate

**Handler**:
The crew member the captain names to settle an option by a check. Anyone living, fit, aboard, and not in the cast.
_Avoid_: Delegate (a department head's role, Phase 5)

**History**:
Each living crew member's record of their last N cycles: whether they were sent, hurt, rested, lost a crewmate or a teammate, or were on a lost mission. Read by expressions. The chronicle grows from it.
_Avoid_: Chronicle (the curated, player-facing record), last cycle

**Morale**:
How a crew member feels, 0 to 100, seen as Low, Steady, or High. Missions, hurts, deaths, rest, and a weekly drift move it; at Low it is a minus on their checks and at High a plus.
_Avoid_: Mood, happiness

**Captain storylet**:
A storylet that arrives and requires a choice. It is an event: always answered, never ignored.

**Vignette**:
A short, free storylet that carries information and changes something small. Usually it has no choice; sometimes it offers one small one, with a default if unanswered. It arrives on the captain's itinerary and does not hold it.
_Avoid_: Flavour event

**Storyline**:
Storylets linked by traits, often one trait counting the storyline's progress. It advances through answers and through traits that rise or fall on their own, whether or not the captain attends to them.
_Avoid_: Waypoint

**Captain's time**:
The per-cycle budget the captain spends on captain storylets and discretionary actions. Does not carry over.

**Digest**:
The per-cycle summary of what department heads resolved without the captain.

**Salience**:
How well a piece of content fits the moment. Among contenders it is a score (stakes, then specificity, less freshness) that decides what reaches the itinerary this cycle; among report lines, the line whose criteria match most specifically wins.

**Developer mode**:
A setting of the running game that adds the content editor and, later, the bench and tuning pages. Off in normal play.
_Avoid_: Debug mode, admin

**Display name**:
What the screens show for an authored thing. Optional; the id is shown when there is none. Never used as a key.
_Avoid_: Label, title

**Content pack**:
The author-written data: names, species, templates, storylet text, and grammars. Separate from systems.
