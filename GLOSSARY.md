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
One round of orders, briefing, resolution, and aftermath. Usually one week long, and longer in transit.
_Avoid_: Turn, week (a cycle is not always one), tick

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
How clearly the player can see a crew member's personality, morale, and relationships: hidden, rough, or clear.
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
The away team member whose personality decides whether a decision becomes a call or a notice.

**Operational parameters**:
The captain's standing instructions to an away team: priority, risk tolerance, and how readily to call in.
_Avoid_: Settings, stance

**Encounter**:
One situation within a mission, resolved by a check and possibly a decision.
_Avoid_: Event (reserved for the technical bus), room, node

**Check**:
A graded test of an away team's skills, gear, and traits against an encounter's difficulty.
_Avoid_: Roll

**Approach**:
One of the two ways an away team can attempt a check: careful or fast. The team lead chooses.

**Tier**:
The graded result of a check: critical, success, partial, failure, or disaster.
_Avoid_: Grade, degree of success

**Momentum**:
The running advantage a team carries forward from good outcomes within a mission.

**Strain**:
The running burden a team carries forward from bad outcomes within a mission.

**Decision**:
A choice arising in an encounter. It becomes either a call or a notice.

**Call**:
A decision the team lead refers to the captain. The mission waits for the answer.
_Avoid_: Callback, interrupt

**Notice**:
A decision the team lead made alone and reported afterwards.
_Avoid_: FYI, report-after

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

**Tuning file**:
The one file in the content pack that holds every number the mission resolver uses. A mission keeps the tuning it started with.
_Avoid_: Config, settings, balance file

**Mission bench**:
The developer tool that runs one mission many times and reports how it tends to go. It never touches a legacy.
_Avoid_: Simulator (that plays whole cycles), test harness

### Life aboard

**Quality**:
Any readable fact about the legacy that content can test: a skill level, a relationship strength, a chronicle fact, or a story flag.

**Story flag**:
A free-form quality that exists only to track storyline progress.

**Storylet**:
A unit of narrative content with requirements, a cast, and mechanical effects.

**Captain storylet**:
A storylet that requires a choice and costs captain's time.

**Vignette**:
A short storylet with no choice. It carries information.
_Avoid_: Flavour event

**Storyline**:
A chain of storylets linked by story flags. It advances whether or not the captain attends to it.

**Captain's time**:
The per-cycle budget the captain spends on captain storylets and discretionary actions. Does not carry over.

**Digest**:
The per-cycle summary of what department heads resolved without the captain.

**Salience**:
The measure of how much a piece of content deserves the player's attention this cycle.

**Developer mode**:
A setting of the running game that adds the content editor and, later, the bench and tuning pages. Off in normal play.
_Avoid_: Debug mode, admin

**Display name**:
What the screens show for an authored thing. Optional; the id is shown when there is none. Never used as a key.
_Avoid_: Label, title

**Content pack**:
The author-written data: names, species, templates, storylet text, and grammars. Separate from systems.
