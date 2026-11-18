# Phase 1, slice 1e, part three: traits that bite

**Spec:** `docs/gdd.md` sections 3.2, 3.5, 3.7, 3.8, 5.1 to 5.3, and 7.1, and section 15, items 36, 38, and 40. **Builds on:** `main` at 2026-11-17 (`3a1381f`), and slice 1e's first two parts (`2026-11-16-phase-1e-design.md`, `2026-11-17-phase-1e-part-two-design.md`).
**Status:** designed 2026-11-17; being built (see "As built", section 9).
**Ends with:** the author plays the worked example in the browser. A crew member is shaken by a disaster. They ask for a word, and the captain promises to stand them down from the next away team. The briefing shows the promise beside their name and says what sending them would cost. Leaving them aboard keeps it; sending them breaks it, costs loyalty and morale, and brings a storylet about the broken word. `shaken` eases with the weeks. Elsewhere, a disaster on a call the lead made leaves the actor avoiding the lead, which costs them on every check they share until it fades.

This records what the author said about part three on 2026-11-17, in a design interview, and designs it. Items marked **(my call)** were proposed, not asked for. Section 5 is the architecture and is kept current as the part is built.

## 1. What the author said (2026-11-17)

- **One thread first.** Part three is the roadmap's worked example built end to end, not the storylet engine in full. The engine (the expression language, casting, salience, storylet cooldowns, an option settled by a check) becomes a new part four, and the later parts are renumbered (section 7).
- **No library for the engine.** Asked whether this is a "not invented here" problem: what part three builds is game rules (launch, the briefing, checks, the week), which any engine would leave to us. The expression language, in part four, should sit on an off-the-shelf evaluator (NCalc or DynamicExpresso), not a hand-written parser. Yarn Spinner 3's saliency is prior art to read before salience is designed.
- **Qualities are stats or traits.** "Story flag" is retired. A **quality** is anything a requirement reads; a **stat** is typed and owned by a system (skills, morale, loyalty); a **trait** is named by content, numeric, and stored generically. GDD 3.2's traits and part one's flags are one thing. A trait that only marks story progress is hidden.
- **Traits stay lower case.** The author wanted one rule for all traits and, having seen `subject.trait.SHAKEN`, did not like how upper case reads. Traits are lower case, an exception written into the conventions.
- **Stages are numbers.** Explored against StoryNexus and Fallen London, where every quality is a number and meaning lives in level descriptions. Chosen: a trait may name its levels, and one that does can only be set (to a named level or 0). This replaces decision 38's counter-or-stage rule.
- **A definitions file**, even if a little heavy, as long as it is structured for future change.
- **A broken promise is allowed, with a cost.** The player can send a crew member who was promised a rest. The briefing warns and says what it will cost.
- **Storylets are events, as in CK3.** A storylet presented between missions is always answered; there is no `ifIgnored`. What can be left alone, and get worse, is a trait.
- **Pair traits,** for "I just can't be around so-and-so": a trait held by one crew member that points at another, with a penalty when both are sent.
- **The roster's trait cell is an interim fix,** until the crew file. It must be documented as one.
- **The simulator's captain answers by `first` or `random`.** No `ignore` policy.
- **Seniority** belongs with ranks in Phase 6.

## 2. Goals, non-goals, anti-goals

**Goals**

1. Every flag becomes a trait, by name, in content, code, events, the GDD, and the glossary. Old saves are refused.
2. `content/traits/` defines the traits that have rules: shown or hidden, where they sit, named levels, a cap, a briefing line, launch rules, a check bonus, a weekly step, an expiry, and what happens when a pair's target dies. A trait with no definition behaves as a flag does today.
3. A trait can sit on a pair of crew members, holder and target, made by an encounter's effect and read and changed by storylets and vignettes of scope `pair`.
4. Traits bite on missions: an approach can require or forbid one, a trait can give a check bonus while it holds, and a trait's launch rules fire when a mission starts.
5. A new module, `Sim.Traits`, owns trait state and every rule. The storylets module keeps selection and answers.
6. The briefing shows each picked crew member's trait chips and what launching will do. The roster shows shown traits, as an interim fix.
7. The simulator's captain uses `first` or `random`, keeps promises when it can, and reports fire counts for each storylet, vignette, and option, with a "never fired" warning.
8. The game's pack gains the worked example (`shaken`, `shaken_heard`, `stood_down`, two storylets) and `avoids` with a vignette and a storylet. The test fixture gains placeholder traits of every shape, including `owes` and `looks_up_to` (section 6).

**Non-goals**

- The storylet engine in full (part four): the expression language, casting beyond a pair's own target, salience, storylet cooldowns, and an option settled by a check. Requirements stay structured lists.
- Traits a crew member is founded with (GDD 3.2's personality traits). The definitions can hold them, but the crew files do not yet name any; that waits for the skills and gear overhaul (1h) or crew generation.
- Conditional modifiers in general (GDD 3.2's `when:` lists). A definition gets one bonus, the check bonus, with one condition, `withTarget`.
- The crew file (part five) and forms for traits (part six). The traits file opens as text.
- Relationships (Phase 4). A pair trait is not a relationship; see section 8.

**Anti-goals**

- An ignorable storylet. Storylets are events and are answered.
- A launch the game blocks. The briefing warns and never refuses a team for its traits.
- The resolver reading trait state. It sees a snapshot fixed at launch, as it sees morale.
- A rule in code that knows a trait's id. Every rule is in the definition.

## 3. Approaches considered

**Where a trait's rules live.**

- **A. A definitions file, `content/traits/` (chosen).** One place says what a trait does on its own; the loader checks every use against it; part six gives it a form. Heavier than one example needs; the author accepted that for a shape that grows.
- **B. Rules written on the effect that sets the trait.** Least to build, but a trait set in two places would carry two rule sets, and nothing could check they agree.
- **C. Code that knows certain ids.** Rejected: content must invent traits with no code change.

**Counter and stage.**

- **A. Keep decision 38:** a declared `kind`, enforced. Catches "added to a stage", at the cost of a field on every trait and a second trait whenever a story needs both.
- **B. Fallen London's way:** numbers only, any operation, nothing enforced.
- **C. Named levels carry the meaning (chosen).** A trait that names its levels can only be set; one that does not can be added to and set. The rule follows from what the author declared.

**An engine from a library.** Ink and Yarn Spinner own their own variable stores and flow; Dendry and StoryNexus-style engines are JavaScript or hosted. None knows a roster, the event bus, or replay. Rejected for the engine; an expression evaluator is the right library, in part four.

## 4. Design

### 4.1 Words

| Word | Meaning |
|---|---|
| **Quality** | Anything a requirement reads: a stat or a trait. |
| **Stat** | Typed, simulated, owned by a system, changed only through its commands: skills, morale, loyalty. |
| **Trait** | Named by content, a whole number of 0 or more, stored generically. 0 means absent and is not stored. On the ship, on one crew member, or on a pair. |
| **Pair trait** | A trait held by one crew member (the **holder**) that points at another (the **target**). |
| **Definition** | A trait's entry in `content/traits/`, optional unless the trait has rules or sits on a pair. |
| **State view** | The read-only view through which modules read stats and traits (was "quality view"). |

Read beside other properties, a trait is read through `trait`: `subject.trait.shaken`, `ship.trait.sensors_quirk`, `#subject.trait.shaken#`. In content where the key already says it is a trait, the id stands alone: `{ trait: shaken, add: 1, on: actor }`.

### 4.2 The rename

Mechanical, and its own first commit, with no change of behaviour beyond the new module boundary (section 5):

- YAML: `flag:` becomes `trait:` in effects and requirements, in both packs. Text tokens `#x.flag.y#` become `#x.trait.y#`.
- Code: `FlagChange`, `FlagRange`, `MissionFlag`, `StoredFlag`, `FlagIds`, `SetFlag`, `FlagSetByDeveloper`, and the loader's and editor's words become their trait names. `FlagChanged` is replaced by a new event type, `TraitChanged` (GDD 7.2: a changed meaning is a new type).
- Screens: "flag" in tags and Dev Play's panel becomes "trait".
- Docs: the GDD, the glossary, the conventions, and the outline. Earlier plans are history and are not rewritten.
- `SchemaVersion` 10 to 11 and `CurrentFormatVersion` 8 to 9; old saves are refused, as with every change of shape.

### 4.3 Definitions

`content/traits/base.yaml`, every key optional except `id`:

```yaml
- id: stood_down
  name: Stood down                      # display name, as other items have
  shown: true                           # default true; false: a marker, never on the roster or the crew file
  on: crew                              # ship | crew | pair; default: any but pair, as today
  levels: { 1: promised, 2: broken }    # named values; a trait with levels can only be set
  max: 2                                # optional cap; required with a rising weekly step
  briefing: STOOD_DOWN_BRIEFING         # the chip beside them in the briefing ("Promised a rest")
  launch:                               # when a mission starts
    sent:    [{ set: broken }, { loyalty: -2 }, { morale: -4 }]
    notSent: [{ set: 0 }]
  expires: { weeks: 6 }                 # back to 0 after this many weeks unchanged
- id: avoids
  on: pair
  max: 5
  briefing: AVOIDS_BRIEFING             # shown when holder and target are both picked
  launch:
    withTarget: [{ morale: -3 }]        # holder and target both sent
  mission: { checkBonus: -1, withTarget: true }
  every: { weeks: 4, add: -1 }
  onTargetDied: [{ set: 0 }]
- id: shaken
  max: 5
  every: { weeks: 3, add: -1 }
```

- **`on`.** `ship`, `crew`, or `pair`. Every use of the trait (an effect's `on`, a requirement's scope) is checked against it. A trait with no definition may sit on the ship or on a crew member, never on a pair.
- **`levels`.** Names for values, lower case. Content may write a name where it would write the number: `{ trait: stood_down, set: broken }`, and a requirement's `is: broken` (sugar for `min` and `max` both at that value). A trait with levels may not be added to, by an effect, a rule, or Dev Play: "stood_down has named levels, so it is set, not added to." The names show in chips ("Stood down: promised").
- **`max`.** Every change is clamped to 0 and `max`. Without `max`, a trait has no cap, as today.
- **Rules' effects** use the storylet effect vocabulary, with two shorthands **(my call)**: `{ set: x }` or `{ add: n }` with no `trait:` is this trait; `on` defaults to the holder. `on` may be `holder`, `target` (pair traits only), or `crew` (everyone living, for loyalty and morale). A rule may change other traits: `onTargetDied: [{ trait: grieving, add: 1 }]`.
- **`launch`.** `sent` fires for a holder on the team; `notSent` for a holder who is alive and not on the team, but only when a mission starts (a cycle in which nobody is sent changes nothing, so a promise of "the next away team" waits for one); `withTarget` for a pair trait whose holder and target are both on the team. A pair trait may also have `sent` and `notSent`, which read the holder alone.
- **`mission`.** `checkBonus` (a whole number, may be negative) on every check the holder makes, while the trait holds when the mission starts. With `withTarget: true` (pair traits only), only when the target is on the team too.
- **`every`.** `{ weeks: N, add: n }`, n not 0. N weeks after the trait last changed, by anything, it changes by n, and the clock restarts. Not allowed on a trait with levels. A rising step (n above 0) needs `max`: "A trait that rises every week needs a max."
- **`expires`.** `{ weeks: N }`. N weeks after it last changed, it is set to 0. Allowed on any trait. With both `every` and `expires`, both clocks start at the last change.
- **`onTargetDied`.** Effects when a pair's target dies, run before the pair trait is dropped. Pair traits only.
- **Loader messages** name the trait and the key, in the style of the existing ones: "A trait on a pair needs a definition with on: pair.", "Only a trait on a pair can have withTarget.", "stood_down has no level named 'brokn'; its levels are promised and broken."

### 4.4 How a trait leaves

A trait never goes below 0, and a trait at 0 is not stored. It reaches 0 by:

1. an effect (`set: 0`, or `add` with a negative number) from an encounter, an answer, or a vignette;
2. a weekly step down;
3. expiry;
4. a launch rule;
5. a death: a crew member's own traits go with them; for a pair trait, the target's death runs `onTargetDied` and then drops it, and the holder's death drops it with no rule;
6. Dev Play's panel.

Each publishes `TraitChanged` with `To: 0` and its cause (section 5.3).

### 4.5 Pair traits

- **Made by encounters.** An effect gains `toward`: `{ trait: avoids, add: 1, on: actor, toward: lead }`. `toward` is allowed only with `on: actor` or `on: lead`, names `lead` or `actor`, and never the same word as `on`. When actor and lead are the same crew member, no pair is made and nothing is logged. A trait whose definition says `on: pair` must be used with `toward`, and `toward` only with such a trait.
- **Stored** with the holder's id and the target's: `crewId` and `targetId`.
- **Storylets and vignettes** gain `scope: pair`. One arrives once for each holder and target whose requirements hold. Its subject is the holder; `target` is a speaker in its text, an `on:` for its effects, and `of: target` on a stat or last-cycle requirement (`{ last: hurt, of: target }`). A trait requirement in a pair storylet reads the pair trait when the trait is defined `on: pair`, and the holder's own trait otherwise. The panel's head reads "CREW_01 · avoids CREW_03". A storylet for a pair still needs a trait among its requirements, as every storylet does.
- **Withdrawn** when either dies, as a crew storylet is when its subject dies.
- **Answers cannot make a pair** in this part: nothing in a storylet names a second crew member until casting (part four), except a pair storylet's own target, so `{ trait: x, on: subject, toward: target }` is allowed in a pair storylet **(my call)**.

### 4.6 Traits on missions

- **An approach's `requires`** gains a trait entry: `{ trait: sensors_quirk, max: 0, on: ship }`, with `on` `ship`, `lead`, or `anyone` (someone standing), and `min`, `max`, or `is` as on a storylet. The loader's rule that one approach has no `requires` stands. Pair traits cannot be required here **(my call: no example needs one)**.
- **The snapshot.** When the team is built at launch, each `TeamMember` carries the member's own traits (`Traits`: id to value) and their check bonuses (`TraitBonuses`: a list of trait id and amount, one entry per trait so the log can name it); `Team` carries the ship's traits. The resolver evaluates requirements and adds bonuses from the snapshot and never reads trait state. Bonuses are worked out from the traits as they stand before the launch rules run **(my call: the promise is broken by launching, but the crew member walks onto the shuttle as they were)**.
- **`withTarget` bonuses** are fixed at launch: if the target falls mid-mission, the bonus stays **(my call: simpler, and the log already says so on each check)**.
- **The log** tags a check with each trait bonus, as it tags morale: "avoids −1".
- **Launch rules** run when the mission starts (`MissionStarted`), in roster order, each holder's traits in definition order.

### 4.7 The briefing

- **Chips.** Beside each crew member in the briefing's team table, a chip for each shown trait with a `briefing` line ("Promised a rest"). A pair trait's chip shows only when both are picked ("Avoids CREW_03").
- **What launching will do.** Above the Launch button, a list of the launch rules the current picks would fire, with their effects in the figure style: "Breaks the promise to CREW_03: Loyalty −2, Morale −4"; "CREW_01 and CREW_03 together: Morale −3 CREW_01". The server renders every rule that could fire with the condition it needs (this member picked; this member not picked; both picked), from the same code the module runs; the page shows those whose condition the picks meet, as it already updates its counts from the picks. `notSent` rules that clear a trait are not listed (keeping a promise costs nothing).
- **Nothing blocks the launch,** and there is no confirmation dialog.

GDD 7.5 calls the screen where the team is picked the briefing; the author's word in the interview was "the orders". It is the briefing.

### 4.8 The roster, Dev Play, and the itinerary

- **Roster, as an interim fix.** The roster gains a "Traits" cell: a chip for each shown trait, with its level's name or its value ("Shaken 2", "Stood down: promised", "Avoids CREW_03"). This is interim: the crew file (part five) replaces it, and the cell is removed then. The GDD (7.5) and the code say so.
- **Dev Play** lists every trait, hidden ones too, and can set any trait, a pair trait included, and only to a named level on a trait with levels.
- **Itinerary heads** name a pair ("CREW_01 · avoids CREW_03"), and a storylet's "where it came from" names the trait and its encounter, as now.

### 4.9 Storylets and vignettes

Part one's and two's rules stand, with these changes:

- Requirements and effects read and write traits (the rename), with `is:` for named levels, `of: target` on stats and facts for pair scope, and `on: target` on effects for pair scope.
- Effects send `ChangeTrait` to the traits module, and loyalty and morale as now.
- "Where it came from" reads the encounter from the trait view.
- **No hidden memory** is checked with named levels and caps: an option must move a required trait out of range, and a `set` to a named level counts as the number it names.
- **Storylets are events.** Every storylet is answered before the orders, as now; GDD 5.3's `ifIgnored` is removed (decision 40).

### 4.10 The simulator

- **Policies.** `SimRunner cycles --answers first|random` (default `first`). `random` picks each storylet's option, and each vignette's, uniformly, from its own stream, `sim-captain`, hashed with the offer id, so the game's streams and the walkthrough's golden hash do not depend on the policy. Vignettes: `first` leaves each to its default, as now; `random` answers each with a random option.
- **Promises.** When the simulator picks a team, it leaves out crew who hold a trait with a `launch.sent` rule, or a pair trait whose `withTarget` rule would fire, unless it then cannot field the objective's smallest team; then it adds them back in roster order.
- **Fire counts.** At the end of a run: for each storylet, vignette, and option, how many times it fired per 100 cycles; for each trait, how many times it was set from 0. Any storylet, vignette, or option that never fired in the run is listed under "Never fired". A "never fired" line is a warning, not a failure, and `check.sh` does not read it.
- **Golden hashes.** The simulation's moves (new events, new content in the fixture). The walkthrough's moves only if the fixture's mission sets a trait with a definition; the test says why.

### 4.11 The editor

- `content/traits/base.yaml` appears on the editor's index as a text file, checked by the loader on save, as the storylets and vignettes files are.
- The lint, today in `src/Web/Forms/TextFiles.cs`: "Nothing sets {trait} on {where}, which {id} requires" counts launch rules and `onTargetDied` as setters; a new finding, "{trait} is defined and never used".

### 4.12 The game's pack

In generated words (section 15, item 37):

- **`shaken`** gains a definition: shown, cap 5, eases 1 every 3 weeks.
- **`shaken_heard`** gains one: hidden, levels heard (1) and turned_away (2), expires after 12 weeks, so a crew member shaken again much later can ask again.
- **`stood_down`** as in 4.3, briefing line "Promised a rest".
- **"Someone shaken"** gains a third answer. *Make the time:* `shaken` −1, `shaken_heard` heard, everyone else's loyalty −1. *Stand them down from the next away team:* `shaken_heard` heard, `stood_down` promised. *Tell them to carry on:* `shaken_heard` turned_away, their loyalty −2.
- **"You gave your word"**, a new storylet for the crew, requiring `stood_down` is broken: apologise (their loyalty +1, `stood_down` 0) or stand by the order (morale −2 to all crew, `stood_down` 0).
- **`avoids`** as in 4.3, set by one approach's disaster outcome: `{ trait: avoids, add: 1, on: actor, toward: lead }`, the actor blaming the lead who sent them in. The interview's example had it follow a call the lead made, but a decision's effects have no actor, so an approach's outcome is where actor and lead meet **(my call; the encounter and approach are the builder's, in the pack's voice)**. A vignette of scope `pair` on it ("I'd rather not share a shuttle"), and a storylet requiring `avoids` 3 to 4, "Clear the air": order them to work it out (`avoids` 0, holder's loyalty −1) or keep them apart (`avoids` set to 5, out of the storylet's range; four weeks later the weekly step brings it back to 4, and they are in each other's way again).
- **`sensors_quirk`** gains its first bite: one approach that reads the forward array requires `{ trait: sensors_quirk, max: 1, on: ship }`, so letting the quirk ride (2) closes it.

The test fixture: placeholder traits of every shape (`trait_a` to `trait_f`): a named-level trait with launch rules, a counter with a falling step, a rising one with a cap, an expiring one, and the three pair shapes of section 6, plus one pair storylet and one pair vignette.

## 5. Architecture

### 5.1 Modules

| Module | Order | Change in part three |
|---|---|---|
| `Sim.Crew` | 100 | Handles `AdjustLoyalty` and `AdjustMorale` from the traits module as from the storylets module; `AdjustMorale` gains a cause, so a rule's morale change is published with cause `trait`, not `answer` (as built, Task 3). |
| `Sim.Missions` | 200 | Builds each `TeamMember`'s trait snapshot and bonuses from `ITraitView` at launch; checks trait requirements on approaches from the snapshot; its result lists trait changes (`MissionTrait`, with `TargetId`) in place of flags. Never sends a trait command. |
| `Sim.Traits` (new) | 250 | Owns trait state and the definitions' rules. Serves `ITraitView`. Handles `ChangeTrait` and `SetTrait` (Dev Play). Applies a mission's trait changes on `MissionEnded`, runs launch rules on `MissionStarted`, weekly steps and expiry on `WeekAdvanced`, and `onTargetDied` and drops on `CrewDied`. Sends `AdjustLoyalty` and `AdjustMorale` for rule effects. |
| `Sim.Storylets` | 300 | Loses trait state and trait storage. Reads traits through `ITraitView`; sends `ChangeTrait` for answers' and vignettes' trait effects. Gains pair scope. |

The traits module sits after missions (200), so a death on the mission is known when the mission's trait changes are applied, and before storylets (300), so the itinerary reads traits as they now stand. **(my call: the interview said 150; 250 is what the death ordering needs.)**

### 5.2 Contracts in `Sim.Core`

```csharp
// Commands
public sealed record ChangeTrait(string Trait, string? CrewId, string? TargetId, int Amount, bool Set, string Cause, string? EncounterId = null) : ICommand;
public sealed record SetTrait(string Trait, string? CrewId, string? TargetId, int Value) : ICommand;   // Dev Play; was SetFlag

// Events
[EventType("TraitChanged", 1)]
public sealed record TraitChanged(string Trait, string? CrewId, string? TargetId, int From, int To, string Cause, string? EncounterId) : IEvent;
[EventType("TraitSetByDeveloper", 1)]
public sealed record TraitSetByDeveloper(string Trait, string? CrewId, string? TargetId, int Value) : IEvent;

public static class TraitCauses { Mission, Answer, Vignette, Launch, Weekly, Expired, Death, Developer }   // lower-case strings

// View
public interface ITraitView
{
    int Value(string trait, string? crewId, string? targetId = null);
    IReadOnlyList<TraitHeld> Held(string? crewId);              // a crew member's traits, pairs they hold included; null: the ship's
    string? EncounterId(string trait, string? crewId, string? targetId = null);
    IReadOnlyList<TraitBonus> Bonuses(string crewId, IReadOnlyCollection<string> team);
    IReadOnlyList<LaunchPreview> Preview(IReadOnlyCollection<string> roster);   // every launch rule that could fire, with its condition, for the briefing
}

// Team
public sealed record TraitBonus(string Trait, int Amount);
// TeamMember gains IReadOnlyDictionary<string, int> Traits and IReadOnlyList<TraitBonus> TraitBonuses (last, with defaults).
// Team gains IReadOnlyDictionary<string, int> ShipTraits.
```

`IRosterView` keeps the stats. "The state view" in the GDD names the two views together; no type is renamed for it.

### 5.3 Causes

`TraitChanged.Cause` is `mission` (an encounter's effect), `answer` (a storylet's), `vignette`, `launch`, `weekly`, `expired`, `death` (`onTargetDied`, or a holder's or target's death dropping it), or `developer`.

### 5.4 Content

`Sim.Core.Content` gains `TraitDef` (id, name, shown, on, levels, max, briefing, launch, mission, every, expires, onTargetDied) and `TraitRuleEffect`; `ContentPack` gains `Traits`. The loader reads `content/traits/` before encounters, storylets, and vignettes, since they are checked against it. `TraitDef` resolves level names to numbers once, at load.

### 5.5 Persistence

`TraitsState` is a new module row. The storylets module's row loses its flags. `SchemaVersion` 11 and `CurrentFormatVersion` 9 (section 4.2), raised once in the rename commit even though later commits add fields, since nothing is released between them **(my call)**.

### 5.6 Rules that hold

- Feature modules never reference each other: storylets reaches traits through `ITraitView` and `ChangeTrait`, and missions through `ITraitView` alone.
- No rule in code names a trait id.
- Determinism: the traits module draws no randomness. The simulator's `random` policy draws only from `sim-captain`.

## 6. Worked examples for pair traits

The rules must express all three. The pack builds `avoids`; the fixture holds placeholder versions of each.

**`avoids`: "I can't be around them."** Set by a disaster, actor toward lead. Both sent: holder's morale −3 at launch; −1 on the holder's checks while both stand; eases 1 every 4 weeks; the target's death ends it. A pair vignette on it; at 3, a pair storylet.

**`owes`: "They pulled me out."** Set by a critical on a rescue approach, actor toward lead.

```yaml
- id: owes
  on: pair
  mission: { checkBonus: 1, withTarget: true }
  onTargetDied: [{ morale: -10 }, { trait: grieving, add: 1 }]
```

The holder sent without the target costs nothing; the target sent without the holder ("I should be there") cannot be said in this part: a rule fires for a holder, not a target. Recorded as a known gap (section 8).

**`looks_up_to`: a junior and a model.** Set by a junior's success while the target leads. +1 to the holder's checks while the target leads: "leads" is not a condition this part has, so the fixture's version uses `withTarget` (while both stand). `onTargetDied: [{ morale: -15 }, { trait: grieving, add: 1 }]`, with `grieving` easing 1 every 2 weeks.

## 7. Roadmap changes (applied with this design)

- **1e, part three** is "Traits that bite": this design.
- **1e, part four (new)** is "The storylet engine in full": the expression language on an off-the-shelf evaluator, casting, salience, storylet cooldowns, and an option settled by a check from a crew member the captain names.
- **1e, part five** is the crew file and the chronicle list (renumbered on 2026-11-17 from part four); it removes the roster's interim trait cell.
- **1e, part six** is writing storylets, vignettes, and traits in the editor (renumbered on 2026-11-17 from part five).
- **1h (new): skills, attributes, and gear overhaul,** moved from "Unplaced" after the author's note on the GDD page ("Somewhere in here, ish, I need to redo the mechanics for skills and gear"). Before the playtest, so the playtest tunes the mechanics it keeps. Needs design first.
- **Phase 4** loses `ifIgnored`; its result becomes "Traits left alone get worse", through traits that rise each week and vignette defaults.
- **Phase 6** gains seniority: today a number on each crew member that picks a new lead when the lead falls; with ranks it becomes rank and time in rank.
- **Track: art** gains planets and a sector map beside portraits (the author's to-do).

## 8. Open, and left to the builder

**For the author, noticed while writing this down:**

1. **Pair traits and relationships.** GDD 3.3 has relationships (`kind: rival`, `strength: 0..100`), built in Phase 4. `avoids` reads like a weak rivalry. The design keeps them apart (a pair trait is a story mark; a relationship is a stat a system simulates) and leaves the question of whether relationships become pair traits with levels to Phase 4.
2. **A rule for the target.** `owes` wants "the target sent without the holder". Rules fire for the holder only. Adding `targetSent` is small, if the author wants it now.
3. **"While the target leads"** is not a condition yet (`looks_up_to`).

**Left to the builder:**

- Names of the new state and view types, following section 5.
- Which encounter and approach set `avoids`, and which approach reads `sensors_quirk`, in the pack's voice.
- The words of the new storylets, the vignette, and the briefing lines, within section 4.12.
- Whether the briefing's preview is rendered rows toggled by the page, or a fragment fetched on change, as long as one code path decides both the preview and the outcome.

## 9. As built

Kept as each task of the plan (`2026-11-17-phase-1e-part-three-plan.md`) lands; differences from sections 4 and 5 are said here, and section 5 is changed to match.

**Task 1, the rename (2026-11-17).**

- `Sim.Traits` is carved out as section 5 says: `TraitsState` (a list of `StoredTrait`: trait, crew id, target id, value, `ChangedAtWeek`, encounter id), `TraitView`, and `TraitsModule` at order 250. `TraitHeld` is the view's record of one trait. Contracts are in `Sim.Core/TraitContracts.cs`; `FlagChanged`, `FlagSetByDeveloper`, and `SetFlag` are gone.
- `SimRunner cycles` (20 legacies x 50 cycles, seed 1) gives the same figures before and after, on the fixture and on the game's pack; both golden hashes moved only for the renamed event types and the beat's renamed field.
- The fixture's placeholder flags `flag_a` to `flag_d` became `trait_a` to `trait_d`, still with no definitions. So the new placeholder traits of later tasks start at `trait_e`, not at `trait_a` as the plan names them.
- Text tokens of the form `#x.flag.y#` were never built (the narrative has no fact for a flag), so there was nothing to rename there.
- Dev Play's route is `/dev/legacy/{id}/trait`, its form field `trait`. The editor's encounter form posts `trait`, `traitMode`, `traitAmount`, and `traitOn`.
- A crew member's traits still go silently with them when they die; `TraitChanged` with cause `death` arrives with pairs in Task 3.

**Task 2, definitions (2026-11-17).**

- `TraitDef` is as section 5.4 says, with `TraitStep`, `TraitLaunch`, `TraitMission`, and `TraitRuleEffect` beside it in `Sim.Core/Content/TraitContent.cs`, and `TraitDef.Plain(id)` for a trait with no definition. A definition's cap is its `max`, else its highest level. The loader checks every use against the definitions through one class, `TraitBook` (`Sim.Content`): a level's name in a `set` or an `is`, an `add` to a trait with levels, and a use where the definition does not put the trait.
- `is:` takes a level's name or a number. A trait with levels is set only to 0 or one of its levels, by content and by Dev Play alike.
- The traits module refuses `ChangeTrait` that adds to a trait with levels (the loader already refuses such content; the guard is a backstop) and clamps every change to the cap. Each week it steps and expires stored traits in the order they were first set, as the plan says.
- The lint shows on the traits page as well as the storylets and vignettes pages.
- The fixture's definitions: `trait_b` (falls 1 every 3 weeks, cap 5), `trait_e` (levels, launch rules, expires; set by STORYLET_02's first answer), and `trait_f` (hidden, rises to a cap of 3; added to by STORYLET_01's second answer). The fixture's `trait_e` has no display name, since the web tests' odd-names copy gives every item one.
- **Neither golden hash moved,** though the plan expected the simulation's to. In 20 legacies x 50 cycles on either pack, no mission sets a trait at all, so no storylet is ever offered (0.0 per 100 cycles, as before part three); the weekly step has nothing to step. This is the "never fired" problem Task 6 is to report; it is not changed here.

**Task 3, pairs (2026-11-17).**

- `TraitChange` gains `Toward` and `MissionTrait` gains `TargetId`, both last with null defaults; `StatRange` and `LastCycle` gain `OfTarget`. Every offer event (`StoryletOffered`, `StoryletAnswered`, `StoryletWithdrawn`, `VignetteArrived`, `VignetteAnswered`) gains `TargetId`, null but for a pair's, so the simulation's golden hash moved.
- `AdjustMorale` gains a cause, and `MoraleCauses` gains `trait`: a rule's morale change (here `onTargetDied`, from Task 4 the launch rules) is not mistaken for an answer's in the itinerary's tags. Section 5.1 says so.
- A pair storylet or vignette arrives for each holder in roster order, with each other living crew member as the target in roster order; offer ids end `:{target}`. A vignette for a pair cools down for each pair apart.
- A death: the traits module runs `onTargetDied` for each pair whose target died, in the order stored, then sets every trait on the dead or pointing at them to 0 with cause `death`, publishing each (in Task 1 the dead's own traits went silently). A rule effect on the target, who has just died, is passed over.
- The editor's encounter form holds `toward` on an approach's outcome (a "Toward" select: lead, actor, or no one), so the fixture's pair outcomes stay editable; a decision's rows have no toward, since a pair needs an actor.
- The lint counts a trait whose definition gives it a check bonus, launch rules, or `onTargetDied` as read, so `owes` and `looks_up_to`, which no storylet reads, are no warning.
- The fixture's pair traits are `trait_g` (avoids), `trait_h` (owes), `trait_i` (looks up to), and `trait_j` (the grief two of them leave), set by ENCOUNTER_05's careful disaster, ENCOUNTER_07's careful critical, and ENCOUNTER_03's careful success, each the actor toward the lead; STORYLET_03 and VIGNETTE_04 are for a pair. None of those outcomes comes up in the 20 x 50 simulation.
- A pair's head names the first pair trait the storylet requires by its id ("CREW_01 · trait_g CREW_03"); Task 5 gives traits their display names.
