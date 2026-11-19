# Phase 1, slice 1e, part four: the storylet engine in full

**Spec:** `docs/gdd.md` sections 3.7, 3.8, 4.2, 5.1 to 5.3, and 7.5, and section 15, items 36, 37, and 40. **Builds on:** `main` at 2026-11-18 (`22022fa`), and slice 1e's first three parts (`2026-11-16-phase-1e-design.md`, `2026-11-17-phase-1e-part-two-design.md`, `2026-11-17-phase-1e-part-three-design.md`).
**Status:** designed 2026-11-18.
**Ends with:** the author plays "The one who came back" in the browser. A mission is lost. A survivor is cast with someone who wasn't on it, and wants nothing to do with them. The storylet ranks above the older ones on salience. The captain picks "Have someone talk to them" and names who goes, and a check settles it. On failure a follow-up storylet comes, and its cooldown lets it come back once. Dev Play's "Why these storylets" shows why each contender arrived or didn't. Every storylet and vignette in both packs is written in the expression language.

This records what the author said about part four on 2026-11-18, in a design interview, and designs it. Items marked **(my call)** were proposed by me, not asked for by the author. Section 5 is the architecture and is kept current as the part is built.

## 1. What the author said (2026-11-18)

The author accepted each recommendation put to them, with one change (Dev Play's view, below).

- **The whole engine, carried by one example.** All five of the roadmap row's pieces are built in part four: the expression language, casting, salience, storylet cooldowns, and an option settled by a check. They are held together by one worked storyline that uses each.
- **Expressions replace the structured lists.** A storylet's, a vignette's, and an option's `requires` become one expression. All content in both packs is migrated, and the loader refuses the list form, as it refuses `flag:`.
- **DynamicExpresso, with C# operators** (`&&`, `||`, `!`, `==`), checked at load time. A spike comes first. If DynamicExpresso can't be sandboxed or checked at load time, NCalc with bracketed paths and our own check over its parse tree is the fallback, and the spike's result is reported to the author before anything is built on it.
- **Encounter approaches keep their structured `requires`.** Expressions are for storylets, vignettes, and options.
- **Vignettes run on the same engine:** expressions, casting, and salience. They keep their cooldown, which becomes the shared `cooldown` field.
- **Recent history is a real store:** a rolling record of each crew member's last N cycles. The chronicle (part five) grows from it.
- **Routing to department heads stays out.** There are no heads yet. Salience only orders arrivals and fills the per-cycle budget.
- **Options can carry `requires`.** One option must have none, so a storylet can always be answered.
- **Cast slots replace `scope`.** Up to three ordered named slots, each with an expression that may read the slots before it.
- **Casting is sampled:** a seeded order, a tuning cap on tries, and at most one cast per storylet per cycle. Today's one arrival for each matching crew member ends.
- **Salience, simple first:** stakes, then specificity, then freshness, then a seeded tie-break.
- **Cooldowns:** `{ cast, global }` in cycles. The rule against hidden memory applies only when a storylet has no `cooldown.cast`.
- **A check option:** success and failure only. The captain names a handler, who is scored by skill, morale, and trait bonus with the mission dice.
- **The state view's vocabulary** is fixed (section 4.3). Anything else is refused by name.
- **The itinerary shows the cast** and keeps the encounter line.
- **Dev Play's "Why these storylets"** lists only this cycle's contenders. Any other storylet is found by search. A count line goes on top. The author's change: listing every storylet won't scale to hundreds.
- **The simulator** picks handlers by its policy, and reports "never cast", check success rates, and losses on the budget.
- **Old saves are refused.** The history starts empty.

## 2. Goals, non-goals, anti-goals

**Goals**

1. One expression language, on DynamicExpresso, over a read-only state view. It is checked against the definitions when the pack loads, and every error names the storylet, the key, and the position.
2. Casting with up to three named slots, a seeded order, a cap on tries, and `on:` and `toward:` taking any slot.
3. Salience that ranks storylets and vignettes against their budgets, with weights in tuning.
4. Cooldowns per cast and per storylet.
5. An option settled by a check, with a handler the captain names.
6. A rolling history of each crew member's recent cycles, read by expressions.
7. Dev Play's "Why these storylets", the simulator's new reports, and the worked example in the game's pack. All content is migrated in both packs.

**Non-goals**

- Department heads, delegation, and routing (5.1 step 4, 5.5).
- Expressions on encounter approaches.
- Investment and "recency of the underlying change" in salience.
- Relationships (`rel()`), grades, personality, and the chronicle in expressions. Each waits for its system.
- Forms for storylets (part six). The files stay text in the editor.
- The crew file (part five).

**Anti-goals**

- Expressions that reach anything but the state view: no reflection, no types beyond the view's, and no side effects.
- An expression error found at play time that the loader could have found.
- Randomness outside named seeded streams. Replay must stay exact.
- A storylet that can't be answered. One option always has no `requires`, and a check option with no handler is disabled with its reason, never left as the only choice **(my call: the loader requires an option with neither `requires` nor `check`)**.

## 3. Approaches considered

**The evaluator.**

- **A. DynamicExpresso (chosen).** C# syntax. It reads members on typed objects, parses against declared parameter types, and reports the error's position. Reflection is off by default.
- **B. NCalc.** It accepts `and`, `or`, and `not`, and makes custom functions easy. Its variables are flat, so dotted paths need brackets or our own resolver, and checking at load time would need our own walk of the tree. This is the fallback.
- **C. A hand-written parser.** Rejected by decision 40.

**Casting.**

- **A. Enumerate every binding.** Exact, and cheap on 6 to 12 crew. Three slots on 500 crew is 10^8.
- **B. A seeded shuffle with a cap (chosen).** Bounded, replayable, and the same code at any crew size.

**Salience.**

- **A. The GDD's formula,** `stakes × recency × investment − repetition`. Two of its terms have nothing to read yet.
- **B. Yarn Spinner's "most conditions wins", plus stakes and freshness (chosen).** Every term can be computed today, the weights are in tuning, and the author can predict the result by reading the files.

## 4. Design

### 4.1 Words

| Word | Meaning |
|---|---|
| **Expression** | A string of the expression language, read through the state view, true or false. |
| **Slot** | A named crew member a storylet is cast with: `subject`, `target`, or any lower-case name. |
| **Cast** | One binding of every slot, which a storylet arrives with. |
| **Contender** | A storylet or vignette that cast this cycle and is out of its cooldowns. |
| **Salience** | A contender's score. The highest arrive, up to the budget. |
| **Handler** | The crew member the captain names for an option settled by a check. They are read as the slot `handler` in that option's effects and text. |
| **History** | Each crew member's record of their last N cycles. |

### 4.2 The shape

```yaml
- id: STORYLET_05
  name: STORYLET_05_NAME
  stakes: 3
  cast:                                   # optional; none: a storylet for the ship. Up to three slots, read in order.
    subject: "subject.fit && subject.history.count('lost_mission', 3) > 0 && subject.trait.came_back == 0"
    target: "target.aboard && target.history.count('lost_mission', 3) == 0"
  requires: "ship.trait.trait_a == 0"     # optional; read after the cast, with every slot bound
  cooldown: { cast: 8, global: 2 }        # optional, in cycles; both default to 0
  text: |-
    subject: STORYLET_05_LINE_1
    = #subject# STORYLET_05_REPORT
  options:
    - id: talk
      label: STORYLET_05_OPTION_A_LABEL
      check: { skill: SKILL_B, difficulty: 4 }
      success: { effects: [{ trait: came_back, set: settled, on: subject }, { morale: 3, on: subject }], text: STORYLET_05_SUCCESS }
      failure: { effects: [{ trait: came_back, set: withdrawn, on: subject }, { trait: avoids, add: 1, on: subject, toward: target }], text: "#handler# STORYLET_05_FAILURE" }
    - id: leave_it
      label: STORYLET_05_OPTION_B_LABEL
      effects: [{ trait: came_back, set: withdrawn, on: subject }, { trait: avoids, add: 2, on: subject, toward: target }]
      text: STORYLET_05_OPTION_B_TEXT
    - id: rank_it
      label: STORYLET_05_OPTION_C_LABEL
      requires: "subject.loyalty >= 60"
      effects: [{ trait: came_back, set: settled, on: subject }, { loyalty: -1, on: subject }]
      text: STORYLET_05_OPTION_C_TEXT
```

- **`cast`.** Each slot is cast from the living crew. Its expression reads `ship`, `cycle`, the slot itself, and the slots before it, and never a later one. No slot is bound twice to the same crew member **(my call: the engine enforces it, so authors don't write `b != a`)**. A storylet with no `cast` is for the ship. Its text and effects may name no slot. `scope` is gone, and the loader refuses it: "scope was replaced by cast in part four; a storylet for crew casts subject."
- **`requires`.** Optional. It is read once every slot is bound, and may read every slot. Splitting conditions between a slot and `requires` is the author's choice. Either way they count towards specificity (4.5).
- **Must read a trait.** The rule from part one stands: an expression across `cast` and `requires` must read at least one trait, since only traits are moved by its answers. It may be met by the cooldown rule instead (4.6). Vignettes are exempt, as now.
- **Speakers, `on:`, and `toward:`** take any slot name, plus `ship` and `crew` where they do today. `toward:` may not name the slot in `on:`, and only a trait defined on a pair takes it. `handler` is a slot only inside a check option's `success` and `failure`.
- **Slot names** are lower case (`docs/conventions.md`) and may not be `ship`, `crew`, `captain`, `cycle`, `best`, or `handler`.

### 4.3 The state view in expressions

A slot is read as a crew member. Every name below is one typed property or method on a read-only view object, so the parser checks it at load time.

| Read | Type | Meaning |
|---|---|---|
| `x.morale`, `x.loyalty` | int | Stats, as today. |
| `x.skills.SKILL_A` | int | Skill level. The loader checks the skill id. |
| `x.fit` | bool | Not injured or incapacitated. |
| `x.aboard` | bool | Alive and not on a mission. In Phase 1 it is true whenever the itinerary is open. It is here so content can say so. |
| `x.trait.t` | int | The trait's value on them. The loader checks `t` against the definitions and where they put it. |
| `x.trait.t.toward(y)` | int | A pair trait `x` holds toward `y`. Only a trait defined on a pair. |
| `x.trait.t == 'level'` | bool | A level by name, as `is:` was. The loader checks the name. |
| `x.history.count('kind', n)` | int | Cycles among the last `n` in which `kind` happened to them. |
| `x.history.last('kind')` | bool | It happened in the last cycle. This replaces `{ last: kind }`. |
| `x == y`, `x != y` | bool | The same crew member. |
| `ship.trait.t` | int | A ship trait. |
| `cycle.number` | int | The cycle now starting. |

History kinds are `sent`, `hurt`, `rested`, `lost_crewmate` (someone died on the cycle's mission and they are alive, sent or not), `lost_teammate` (someone died on their mission and they came home), and `lost_mission` (on a mission whose objective failed or was abandoned). The last is the author's decision of section 8, and the two kinds of loss are the author's, item 5. `n` is 1 to `history.cycles`. Anything not in the table is refused by name, for example "rel() waits for relationships (Phase 4)" and "grade waits for 1h".

How `x.trait.t` is checked (the trait id is a member name, not a declared property) is the spike's main question. Either a rewrite to `x.Trait("t")` before parsing, with the id checked by the rewrite, or a typed dynamic member with a checking binder. See section 8.

### 4.4 Casting

For each storylet and vignette out of its global cooldown:

1. If it has no `cast`, read `requires` on the ship. It holds or it doesn't.
2. Otherwise draw a seeded order of the living crew from the stream `cast:<id>:<cycle>`. Then search depth first: for slot 1, take crew in that order whose expression holds; for each, fill slot 2 likewise, and so on, then read `requires`. Every expression read counts as a try. Stop at the first full binding that holds and is out of its `cooldown.cast`, or when `storylets.castTries` (tuning, 100; the author's, section 8 item 6) is spent.
3. The result is one cast, "no cast", or "out of tries". The last two are told apart for Dev Play and the simulator.

A storylet arrives at most once a cycle. Waiting storylets keep their cast. If any cast member dies, the storylet is withdrawn, as a subject's death withdraws one today. A storylet already waiting is not cast again.

### 4.5 Salience and arrival

Each contender gets a score:

`salience = stakes × salience.stakes + specificity × salience.specificity − freshness`

- **Specificity** is the number of clauses at the top level of `&&` across every slot's expression and `requires`. Counted once, at load.
- **Freshness** is `salience.repeat × fires in the last salience.window cycles`, over every cast. A storylet that has just fired loses ground to one that hasn't.
- **Ties** go to a seeded draw from `salience:<cycle>`.
- Tuning (my starting values): `salience.stakes` 10, `salience.specificity` 1, `salience.repeat` 3, `salience.window` 20. Vignettes have no stakes. They use the same formula with stakes 0.

Storylets arrive highest salience first, up to `storylets.perCycle` (2). The rest arrive in a later cycle if they still hold, and nothing is lost. Vignettes do the same against `vignettes.perCycle` (3), and the rest are dropped, as now. Part two's vignette order (the longest since it last arrived first) is replaced by freshness.

### 4.6 Cooldowns

- `cooldown.cast: N`. The same binding (the same crew in the same slots) does not arrive again within N cycles of its last arrival.
- `cooldown.global: M`. The storylet does not arrive at all within M cycles of its last arrival.
- Both are optional and default to 0. A vignette with no `cooldown` takes `{ cast: vignettes.cooldown }`, which is 6, as now.
- **No hidden memory, revised.** A storylet with `cooldown.cast` of 0 must still have every option, and every check outcome, move a trait it reads out of the range its expression allows. The loader checks this by evaluating the expression against the trait values each option leaves, holding all else as the option found it **(my call: exact only for trait clauses. A clause on a stat or history counts as "may still hold". The builder may narrow the rule to "every option changes a trait its expressions read" if evaluating proves fragile, and says so in section 9)**. With `cooldown.cast` above 0, a storylet may recur by design.

### 4.7 An option settled by a check

- `check: { skill, difficulty }` with `success:` and `failure:`, each holding `effects` and `text`. An option with `check` has no `effects` or `text` of its own.
- **Choosing it** asks the captain for a handler: anyone living, fit, aboard, and not in the cast. The panel lists them by name with their skill ("Okafor, SKILL_B 6") and a word for their chance (long shot, even, likely, sure), worked out from the dice. If no one qualifies, the option is disabled with "No one fit to send."
- **The roll.** The score is the handler's skill + morale modifier (4.2's ±1) + their trait check bonuses (with `withTarget` read against the cast). It adds the check dice (`check.dice`) against `difficulty + 7`. A margin of 0 or more is success **(my call: the mission's tiers collapse at the success line)**. The stream is `storylet-check:<offer id>`. There is no assist, momentum, strain, or experience **(my call)**.
- **The log** reads like a check's: "Okafor (SKILL_B 6, morale +1) rolled 7 against 11: success". Then the outcome's text.
- **Command:** `AnswerStorylet` gains `HandlerId`. It is required for a check option and refused on any other.
- Vignettes take no check options **(my call: a vignette's choice is small and free)**.

### 4.8 History

- The storylets module (it owns `LastCycleRecord` today) keeps, for each crew member, the kinds that happened to them in each of the last `history.cycles` (tuning, 20) cycles. It is built from the events it already reads at the aftermath.
- `x.history.last(k)` is `count(k, 1) > 0`. `LastCycleRecord` is folded into it.
- It is served to other modules as `IHistoryView` so part five's chronicle can read it **(my call)**.
- Dead crew drop out of it. The chronicle will keep them.

### 4.9 The itinerary

- A storylet's head lists its cast by name under the title ("Okafor · Vance"). For a ship storylet it lists none.
- "Where it came from" stays: "since the Collapsed Gallery" when a trait an expression reads on a slot (or the ship) was last changed by an encounter. The first such trait in reading order wins.
- A check option's button opens the handler list in place, inside the panel, without a modal. Once answered, the settled panel shows the roll line, then the outcome's text and tags.
- An option whose `requires` is false is not shown.

### 4.10 Dev Play: "Why these storylets"

A section on Dev Play's legacy page, for the itinerary now open (or the next one, between cycles):

- **The count line:** "212 storylets, 40 vignettes: 9 eligible, 4 cast, 2 arrived". Eligible means out of global cooldown and with a first slot that matched someone.
- **The contenders:** each storylet and vignette that cast this cycle, in salience order. Each shows its cast, its score in parts (stakes, specificity, freshness, and the tie draw), whether it arrived or lost on the budget, and a link into the editor.
- **A search** by id or name for any other. It says why it isn't here: the first clause that failed and on which slot ("subject: subject.history.count('lost_mission', 3) > 0 held for no one"), "out of tries after 50", or the cooldown holding it and when it ends.
- It is read-only. It reads the same function the module runs, through a dry run that draws from the same streams without advancing anything **(my call: one code path for the play and the explanation, as the briefing's preview did in part three)**.

### 4.11 The simulator

- **Handlers.** Under `first`, the captain picks the handler with the highest skill, with ties going to roster order. Under `random`, the pick comes from `sim-captain`.
- **Reports**, beside the fire counts:
  - "Never cast", separate from "Never eligible".
  - For each check option, its success rate.
  - For each storylet, how often it cast but lost on the budget.
- The golden hashes move. The test says why.

### 4.12 The editor

- The storylets and vignettes files stay text, checked by the loader on save. Expression errors show with their position.
- The lint's "Nothing sets {trait} on {where}" reads traits out of expressions.

### 4.13 The game's pack

In generated words (section 15, item 37):

- **Migration.** STORYLET_01 to 04 and the six vignettes are rewritten with `cast` and expressions, keeping slot names `subject` and `target` so their scripts don't change. Their behaviour is the same apart from arriving at most once a cycle.
- **`came_back`**, a new trait: hidden, on crew, levels settled (1) and withdrawn (2), expires after 12 weeks.
- **"The one who came back"** (STORYLET_05), as in 4.2. It is cast from a survivor of a lost mission and someone who wasn't on it, and its first answer is a check. Its wording and its skill are the builder's, in the pack's voice.
- **"Still not talking"** (STORYLET_06): cast `subject` with `came_back == 'withdrawn'` and `target` toward whom they hold `avoids`, with `cooldown: { cast: 4 }`. One option, a check again, settles it (`came_back` 0, `avoids` 0). The other leaves things as they are, so it comes back once more before `came_back` expires.
- **Specificity** places STORYLET_05 above "Someone shaken" in a cycle where both hold. The builder checks this with the numbers, and changes the clauses rather than the tuning if it doesn't.
- **The fixture** gains placeholder storylets for every new shape: three slots, a check option, an option with `requires`, both cooldowns, a vignette with a cast, and one storylet that can't cast (for "never cast").

## 5. Architecture

### 5.1 Modules

| Module | Order | Change in part four |
|---|---|---|
| `Sim.Storylets` | 300 | Casting, salience, cooldowns, check options, and the history. Reads expressions through `Sim.Expressions`. Serves `IHistoryView`, and the dry run Dev Play reads. As built (Task 4): `Casting.cs` holds every storylet's and vignette's checked expressions, compiled for the life of the module, and the search of 4.4, whose result is a cast, "no cast", or "out of tries" (`CastResult`); `StateSource` is the `IStateSource` over the roster, trait, and history views. As built (Task 5): `Salience.cs` holds `Salience.Contend`, the one function that decides what arrives. It reads the state and changes nothing, and returns a `Contest`: every storylet and vignette as a `Contender` with its `Fate` (arrived, lost on the budget, waiting, cooling, no cast, out of tries), its cast, its `Score` in parts, and, in a global cooldown, when it may return. The module runs it when the itinerary opens and applies the arrivals; Task 7's dry run reads it with copies of the streams. `Casting` also counts each one's specificity. As built (Task 6): `Casting` also keeps each option's `requires` and says whether an option is open (`Open`); `Check.cs` lists who may be sent, works out their score and chance, and rolls; `StoryletView.cs` serves `IStoryletView`, the open answers and their handlers, which the guard, the bridge, and the simulator all read. As built (Task 7): `DryRun.cs` serves `IStoryletDryRun`, Dev Play's "Why these storylets": `Salience.Contend` on a copy of the module's state with streams made new from the seed, put back as the open itinerary stood when it opened, or opened ahead for the next one between cycles. `Casting` tells apart a miss whose every holding cast is cooling (`CastOutcome.Cooling`, with the first such cast), says whether the first slot held for anyone (for "eligible"), and finds the first clause that held for no one (`Failed`). The contest's types moved to `Sim.Core` (5.2). As built (Task 8): the module keeps the contest it opened the itinerary with, in memory only, and `IStoryletView.Opened` serves it, so the simulator's reports read the same `Salience.Contend` result the itinerary opened with. That is the smallest read that lets it: no event carries what did not arrive (eligible, lost on the budget, out of tries), a new event would move every hash, and asking the dry run again each cycle would run the contest twice. |
| `Sim.Expressions` (new, a library, not a module) | — | Wraps DynamicExpresso: parses an expression against the state view's types, gives errors with positions, counts clauses for specificity, lists the traits read (for the lint and "where it came from"), and evaluates against a view. Referenced by `Sim.Content` (to check at load) and `Sim.Storylets` (to run). It holds no state **(my call)**. As built (Task 2): `ExpressionBook` (built from the trait definitions, skill ids, history kinds, and `history.cycles`) with `Check` and `Evaluate`; `CheckedExpression`; the view objects; and `IStateSource`, which the view objects read and the storylets module implements over its roster, trait, and history views. Task 4 adds `OnTraits`, an expression read with only its traits known (for the no-hidden-memory check), and `LastRead`, the kinds read with `last()` (for a vignette's head). Task 7 adds `Split`: an expression's top-level `&&` clauses, each checked on its own (for Dev Play's first failing clause). |
| `Sim.Content` | — | Reads `cast`, `requires`, `cooldown`, and `check`. Refuses `scope` and the list form. Checks expressions through `Sim.Expressions` against `TraitBook`. As built (Task 4): `cast`, `requires`, and an option's `requires`; each storylet and vignette carries the traits its expressions read (`Reads`) and those it needs above 0 to arrive (`Needs`, for the lint). As built (Task 5): `cooldown`, and the tuning's `salience.*`. As built (Task 6): `check`, `success`, and `failure`; `handler` as a slot in an outcome's effects and text; each outcome's text as a line with the fact `outcome`. |
| `Sim.Crew`, `Sim.Traits` | 100, 250 | No change beyond being read through their views. |

### 5.2 Contracts in `Sim.Core`

```csharp
// Content
public sealed record Expr(string Source);   // parsed and checked at load; the compiled form is cached by Sim.Expressions
public sealed record CastSlot(string Name, Expr Condition);
public sealed record Cooldown(int Cast, int Global);
public sealed record StoryletCheck(string Skill, int Difficulty, StoryletOutcome Success, StoryletOutcome Failure);
public sealed record StoryletOutcome(IReadOnlyList<StoryletEffect> Effects, string Text);
// StoryletDef: Scope and Requires (list) replaced by Cast (IReadOnlyList<CastSlot>), Requires (Expr?), Cooldown.
// StoryletOption gains Requires (Expr?) and Check (StoryletCheck?). StoryletEffect's on/toward become slot names.
// VignetteDef: the same replacements; Fact is gone.

// Commands
public sealed record AnswerStorylet(string OfferId, string OptionId, string? HandlerId = null) : ICommand;

// Events (changed meaning, so new types, GDD 7.2)
[EventType("StoryletOffered", 2)]  public sealed record StoryletOffered(string OfferId, string StoryletId, IReadOnlyDictionary<string, string> Cast, string? EncounterId, int Salience) : IEvent;
[EventType("StoryletAnswered", 2)] public sealed record StoryletAnswered(string OfferId, string StoryletId, string OptionId, string? HandlerId, StoryletRoll? Roll) : IEvent;
[EventType("StoryletWithdrawn", 2)] public sealed record StoryletWithdrawn(string OfferId, string StoryletId) : IEvent;
[EventType("VignetteArrived", 2)]  public sealed record VignetteArrived(string OfferId, string VignetteId, IReadOnlyDictionary<string, string> Cast, int Salience) : IEvent;
public sealed record StoryletRoll(string Skill, int Score, int Dice, int Against, bool Success);

// Views
public interface IHistoryView { int Count(string crewId, string kind, int cycles); }
```

Exact names are the builder's, following this shape.

As built (Tasks 4 to 7):

```csharp
// Content (Sim.Core/Content/StoryletContent.cs)
public sealed record Expr(string Source);
public sealed record CastSlot(string Name, Expr Condition);
public sealed record TraitRead(string Trait, string Slot, string? Toward);      // moved here from Sim.Expressions; Slot "ship" for the ship
public sealed record Cooldown(int Cast, int Global) { public static readonly Cooldown None; }                 // Task 5
public sealed record SalienceTuning(int Stakes, int Specificity, int Repeat, int Window) { Default = (10, 1, 3, 20) }   // Task 5
public sealed record StoryletOption(string Id, string Label, IReadOnlyList<StoryletEffect> Effects, Expr? Requires = null, StoryletCheck? Check = null)
{ IEnumerable<IReadOnlyList<StoryletEffect>> Outcomes; }                       // Task 6: its own effects, or success's then failure's
public sealed record StoryletCheck(string Skill, int Difficulty, StoryletOutcome Success, StoryletOutcome Failure) { const int Base = 7; int Against => Difficulty + Base; }
public sealed record StoryletOutcome(IReadOnlyList<StoryletEffect> Effects);   // its text is a line of storyletAnswer with outcome success or failure
public enum Chance { LongShot, Even, Likely, Sure }                            // Sim.Core.Content; Chances.Of(dice, score, against) and Chances.Word
public sealed record StoryletDef(string Id, int Stakes, IReadOnlyList<CastSlot> Cast, Expr? Requires, IReadOnlyList<StoryletOption> Options, string? Name = null)
{ IReadOnlyList<TraitRead> Reads { get; init; } IReadOnlyList<TraitRead> Needs { get; init; } Cooldown Cooldown { get; init; } = Cooldown.None; }   // set by the loader
public sealed record VignetteDef(string Id, IReadOnlyList<CastSlot> Cast, Expr? Requires, IReadOnlyList<StoryletEffect> Effects, IReadOnlyList<StoryletOption> Options, string? Default, string? Name = null)
{ Reads; Needs; string? Fact { get; init; } Cooldown? Cooldown { get; init; } }   // Fact: the first kind its first slot reads with last(), for its head;
// Cooldown: as written, or null for ContentPack.CooldownOf's { cast: vignettes.cooldown }
// StoryletEffect keeps its shape: LoyaltyOn and MoraleOn are a slot or "crew"; TraitChange.On is "ship", a slot, or "" (the first
// slot, or the ship with no cast); TraitChange.Toward is a slot. StoryletScopes keeps Ship and Crew, and gains Reserved and MaxSlots.
// ContentPack gains CastTries (storylets.castTries), and from Task 5 Salience (salience.*) and CooldownOf(VignetteDef).
// StoryletScopes gains Handler ("handler"). DiceDef gains AtLeast(n), the exact chance a roll reaches n (Task 6).

// Commands (Task 6)
public sealed record AnswerStorylet(string OfferId, string OptionId, string? HandlerId = null) : ICommand;   // NoOneFit = "No one fit to send."

// Events: Cast on every one; Salience (the total) from Task 5, and HandlerId and Roll from Task 6, inside version 2.
[EventType("StoryletOffered", 2)]   StoryletOffered(string OfferId, string StoryletId, IReadOnlyDictionary<string, string> Cast, string? EncounterId, int Salience)
[EventType("StoryletAnswered", 2)]  StoryletAnswered(string OfferId, string StoryletId, IReadOnlyDictionary<string, string> Cast, string OptionId, string? HandlerId = null, StoryletRoll? Roll = null)
StoryletRoll(string Skill, int Level, int Morale, IReadOnlyList<TraitBonus> Traits, int Dice, int Against, bool Success) { int Score; }   // Score not stored
[EventType("StoryletWithdrawn", 2)] StoryletWithdrawn(string OfferId, string StoryletId, IReadOnlyDictionary<string, string> Cast)
[EventType("VignetteArrived", 2)]   VignetteArrived(string OfferId, string VignetteId, IReadOnlyDictionary<string, string> Cast, int Salience)
[EventType("VignetteAnswered", 2)]  VignetteAnswered(string OfferId, string VignetteId, IReadOnlyDictionary<string, string> Cast, string? OptionId, bool Defaulted)

// Views: HistoryKinds gains LostTeammate ("lost_teammate"). LastCycleFacts, Requirement, TraitRange, StatRange, and LastCycle are gone.
// Task 6, served by the storylets module:
public interface IStoryletView { IReadOnlyList<OpenAnswer> Answers(string offerId); Contest? Opened { get; } }   // Opened: Task 8
public sealed record OpenAnswer(string OptionId, IReadOnlyList<HandlerOdds>? Handlers);          // Handlers null: no check; empty: no one fit
public sealed record HandlerOdds(string CrewId, int Skill, int Score, int Against, Chance Chance);

// Task 7, served by the storylets module: Dev Play's "Why these storylets", a dry run that advances no stream and changes nothing.
// cycleEvents: this cycle's, from which the open itinerary is put back as it opened.
public interface IStoryletDryRun { Contest Contest(IReadOnlyList<IEvent> cycleEvents); FailedClause? Failed(string id, IReadOnlyList<IEvent> cycleEvents); }
// Task 5's contest, moved here from Sim.Storylets so the bridge can read it (Fate and Score renamed).
public enum ContestFate { Arrived, LostOnBudget, Waiting, Cooling, CastCooling, NoCast, OutOfTries }   // CastCooling: Task 7
public sealed record SalienceScore(int Stakes, int Specificity, int Freshness, int Tie) { int Total; }
public sealed record Contender(string Id, ContestFate Fate, IReadOnlyDictionary<string, string>? Cast, SalienceScore? Score, int? ReturnsAt, int Tries) { bool Eligible; }
public sealed record Contest(IReadOnlyList<Contender> Storylets, IReadOnlyList<Contender> Vignettes) { int Cycle; bool Open; }
public sealed record FailedClause(string Slot, string? Clause, int? Before) { const string Requires = "requires"; }   // Clause null: too many casts to search
// Task 8: IStoryletView.Opened, the contest the last itinerary opened with, as the module ran it (Cycle set, Open true). It is
// held in memory only: null before an itinerary has opened in this process, and after a load. The game never reads it.
```

### 5.3 Persistence

`StoryletsState` gains the history, each storylet's and each cast's last arrival cycle, and the cast on each waiting offer. It loses `LastCycle` and the subject and target fields. `SchemaVersion` goes from 11 to 12 and `CurrentFormatVersion` from 9 to 10. Old saves are refused.

As built (Task 4): `Waiting` and `Vignettes` hold each offer's `Cast` (slot to crew id, in reading order); `Arrivals` holds one `Arrival` (`Id`, `Cast`, `Cycle`) for each storylet or vignette and cast, the latest, and loses an entry when a member of its cast dies. The versions are raised here, once for the part.

As built (Task 5): `Fired` holds one entry for each storylet or vignette that has arrived (`Id`, `Last`, and `Cycles`): its last arrival with any cast, for the global cooldown, and the cycles of its arrivals within `salience.window`, for freshness. Older cycles fall out when the itinerary opens. A death does not touch it, since the storylet did arrive, so a death cannot lift a global cooldown. `Arrivals` stays as it was and serves the cast cooldown alone. No version raise: the store's strict read refuses a Task 4 store, which has no `Fired`, as it would any store from before the part.

As built (Task 8): `StoryletsState.Opened`, the contest the last itinerary opened with, is `[JsonIgnore]`: never saved, never read back, and read by nothing in the game, so it changes neither the store nor play. A legacy loaded from the store has none until its next itinerary opens. No version raise.

### 5.4 Rules that hold

- Feature modules never reference each other. `Sim.Expressions` is a library both the loader and the storylets module use.
- Expressions see only the view objects. DynamicExpresso's reflection stays off, and no type outside the view is registered.
- Determinism: casting draws from `cast:<id>:<cycle>`, ties from `salience:<cycle>`, and checks from `storylet-check:<offer>`. The simulator's picks, an option and a handler, draw only from `sim-captain` (Task 8), and its reports read the module's own contest and the rolls, drawing nothing. Dev Play's dry run never advances a stream: it makes its own from the seed and never reads or writes the legacy's saved ones (Task 7).
- No rule in code names a trait, storylet, or slot id beyond the reserved words of 4.2.

## 6. The spike (first task)

About an hour, before anything else is built. It answers these questions, and the answers go to the author:

1. Can DynamicExpresso parse `subject.trait.shaken > 0 && subject.history.count('hurt', 5) >= 2` against declared types, and refuse `subject.trait.nosuch` and `subject.rel` at parse time with a position?
2. How is `x.trait.t` best made checkable: a rewrite before parsing, or a typed dynamic member? Does `x.trait.t == 'level'` work, or do levels need `x.trait.t.is('level')`?
3. With reflection off and only the view types known, can an expression reach anything else (`GetType()`, `System.IO`, a static method)?
4. Can the top-level `&&` clauses be counted from the parsed tree?
5. Cost: evaluating 200 storylets × 50 tries per cycle, compiled once at load. Expect well under a millisecond per expression.

If any of 1 to 3 fail, try NCalc against the same list and report both.

## 7. Roadmap and GDD changes (applied when built)

- 11.1: part four is designed here. On completion it is marked done with the date, and part five is next.
- 3.7: the sketch becomes "as built", with `cast`, an expression `requires`, `cooldown`, and `check`. The `and` of the old sketch becomes `&&`.
- 3.8: the state view's table (4.3).
- 5.1: steps 1 to 3 as built (an index is not built, see section 8). Step 4 is marked as waiting for department heads.
- 7.5: the itinerary's cast and handler list, and Dev Play's "Why these storylets".
- 15: item 41, the decisions of section 1.
- The outline's slice 1e paragraph.

## 8. Open, and left to the builder

**Settled by the author after the design was written (2026-11-18):**

1. **5.1 step 1, the index** (only evaluating storylets whose tags or qualities could have changed) is deferred. At the size of Phase 1 every storylet is cast every cycle, which is cheap. It is worth building when a pack has hundreds and the simulator says so.
2. **A check's outcomes are binary:** success and failure only. A partial ("it helped a little") would be one more field if wanted later.
3. **"Lost mission"** means a mission whose objective failed or was abandoned (4.3). A death alone does not make a mission lost; that is `lost_crewmate`.
4. **An expression reads only a defined trait** (after the spike). A trait read in an expression must have an entry in `traits/`, or the loader refuses it with its position ("No trait named nosuch"). A minimal entry is `- id: some_trait`. Effects and encounters keep part three's rule: a trait with no definition behaves as a flag did. The fixture gives `trait_a`, `trait_c`, and any other trait its expressions read a minimal entry where it has none.
5. **Two kinds of loss** (during Task 4). Both are meaningful: someone dying aboard ship, and someone dying beside you on the away team. `lost_crewmate` keeps part two's meaning (every living crew member, sent or not, when someone died on the cycle's mission), and `lost_teammate` is 4.3's narrower one (those who came home from the mission where someone died).
6. **`storylets.castTries` is 100** (during Task 4), not 50. At 50, a pair over 8 crew could go "out of tries" before reaching its one holder about one cycle in four (section 9, Task 4), which is more than once a cycle changes. 100 covers a pair over up to 10 crew. The author asked for a plan for 500 crew. It follows.

**Casting at 500 crew (the author's request, 2026-11-18).** Today's search is blind. A slot tries crew in the seeded order, and every expression read is a try. So a pair can cost n + n(n − 1) = n² reads, which is 100 at 10 crew, 250,000 at 500 crew, and about 1.25 × 10⁸ for three slots. Raising `castTries` with the roster cannot keep up. The plan is to build it when a pack's roster outgrows `castTries`, which the simulator's "out of tries" report (Task 8) will show:

1. **Candidates from the traits.** For each slot, the loader finds the traits the slot's expression is false without: read with that trait at 0, `OnTraits` is false whatever the other traits are, as the no-hidden-memory check reads it. The candidates for that slot are then only the crew who hold such a trait, and the traits module lists them from its own store through a new read on `ITraitView` (`Holders(trait)`, and for a pair trait, holder and target). Crew outside the candidates are not read and cost no try. A slot that needs no trait keeps today's whole-roster order.
2. **Pairs from the pair trait.** When a slot's expression needs `x.trait.t.toward(y)` above 0 and `x` is bound, the candidates for `y` are `x`'s targets for `t`, read from the same store. A pair then costs its holders × their targets, not n².
3. **Replay stays exact** (focus 3). Candidates are listed in roster order, then given the seeded order from `cast:<id>:<cycle>` as now.
4. **`castTries` keeps its meaning:** reads, not crew. It stays the guard against a slot that needs nothing (`subject.fit` over 500 crew). That slot can still go "out of tries", the simulator reports it, and the author tightens the first slot.
5. **With 8.1's index** (only casting storylets whose read traits or history changed), this keeps a cycle's casting near today's cost at 500 crew and hundreds of storylets. Both are deferred until the simulator shows the need.

**Left to the builder:**

- How `x.trait.t` is made checkable (section 6, question 2), and the level syntax if `== 'level'` doesn't work.
- Exact names of the new types, the Dev Play route, and the tuning keys, following sections 4 and 5.
- The skill, difficulty, and words of STORYLET_05 and 06, within section 4.13.
- Whether the no-hidden-memory check evaluates or is narrowed (4.6), said in section 9.

## 9. As built

Kept as each task of the plan lands. Differences from sections 4 and 5 are said here, and section 5 is changed to match.

### Task 1: the spike (2026-11-18)

Run on DynamicExpresso.Core 2.19.6 (MIT), .NET 10, in a throwaway console with stub view types shaped as 4.3. NCalc was not tried: all of questions 1 to 3 pass.

1. **Parsing and refusing: yes, with a rewrite in front.** `subject.trait.shaken > 0 && subject.history.count('hurt', 5) >= 2` parses against declared types and evaluates. `subject.rel` is refused by the parser itself: "No property or field 'rel' exists in type 'Crew' (at index 23)". `subject.trait.nosuch` is refused by the rewrite with the id's position ("No trait named nosuch", 29). Reading a later slot is refused as an unknown identifier with its position. A non-bool expression, `&&&`, and `and` are all refused with positions.
2. **`x.trait.t`: a rewrite before parsing.** A typed dynamic member (a `DynamicObject`) parses any name with no check, and fails or succeeds only when run, so it is rejected. The rewrite is a small tokenizer that turns `x.trait.t` into `x.trait["t"]`, `x.trait.t.toward(y)` into `x.trait.toward("t", y)`, and `x.skills.S` into `x.skills["S"]`. It checks each id against the book as it goes: the trait exists, it is on crew or the ship as written, `toward` only on a pair trait, and a pair trait always with `toward`. It records each trait read with its slot, which the lint and "where it came from" need. A map from the rewritten text back to the source keeps the parser's positions true; this was checked on errors after a rewritten part. **Levels: `x.trait.t == 'level'` works**, because the rewrite resolves the name to its number at load ("came_back has no level named gone" otherwise). `.is('level')` is not needed. The rewrite is needed anyway: DynamicExpresso reads `'withdrawn'` as a character literal and refuses it, so the rewrite turns single quotes into double, which keeps the length. An operator `==` on a value type works without the rewrite, but an implicit conversion to int in `>` does not.
3. **Escapes: closed only with a whitelist of our own.** With the options cut to `SystemKeywords` (no type names, no `Math`, `Convert`, or `DateTime`) and assignment off, `System.IO`, `File`, `typeof`, `new`, casts, `is`, `default`, lambdas, `++`, `=`, and internal members are all refused at parse. But DynamicExpresso's own reflection guard lets through `subject.GetType()`, `GetType().Name`, `ToString()`, `Equals()`, `GetHashCode()`, `"abc".Length`, and `/ 0` (a `DivideByZeroException` at play time). With the defaults, `Math.Max`, `Convert`, `DateTime.Now`, and `typeof` all parse. A visitor added to `Interpreter.Visitors` (it runs inside `Parse`) closes every one: members and methods only on the view types and only `count`, `last`, `toward`, and the indexer; constants only int, bool, and string; no `/`, `%`, or `**`; history's kind a quoted constant and `n` a constant from 1 to `history.cycles`. Its errors carry no position (the tree has none), so `Sim.Expressions` places them at the first occurrence of the offending name in the source.
4. **Clauses: yes.** Walking `AndAlso` at the top of the parsed tree counts `subject.fit && (subject.morale < 30 || subject.loyalty < 30) && !subject.history.last('sent')` as 3.
5. **Cost: well inside the budget.** Through a compiled `Func<…, bool>`, an evaluation is about 0.2 µs: 200 storylets × 50 tries is about 2 ms a cycle. `Lambda.Invoke` (reflection) is about 1.3 µs and is not to be used. Parsing with the whitelist is about 0.14 ms an expression and compiling about 0.4 ms more, so 200 expressions cost about 30 ms to check and 100 ms to compile. The loader parses only, and `Sim.Storylets` compiles once when the module is built, so a pack of hundreds stays quick to save in the editor.

**For Task 2:** the rewrite, the whitelist visitor, and the compiled delegate with a fixed signature (three slot parameters, unused ones named so no expression can read them, then `ship` and `cycle`) are `Sim.Expressions`. The view's members are lower case, as the language is. Content can't divide or take a remainder, which none needs.

### Task 2: `Sim.Expressions` (2026-11-18)

Built as Task 1 said: the rewrite (`Rewrite.cs`), DynamicExpresso.Core 2.19.6 with `InterpreterOptions.SystemKeywords` and assignment off, the whitelist (`Whitelist.cs`, in `Interpreter.Visitors`), and a compiled delegate with the fixed signature. Differences and choices:

- **The rewrite refuses more than it rewrites.** It reads the source as tokens and allows only names, whole numbers, quoted names, `( ) , . ! < > + - *`, and `&& || == != <= >=`. So `/`, `%`, `[`, `?`, `;`, `@`, and a lone `=` or `&` are refused with their position before the parser sees them. A name where a value starts must be a slot it may read, `ship`, `cycle`, `true`, or `false`; after a slot only the words of 4.3 follow, and nothing follows a complete read ("Nothing is read from subject.morale"). So most of the escape list is refused by name and position in the rewrite, and the whitelist is the second wall: a test parses the spike's escapes past the rewrite and the whitelist refuses each on its own.
- **Quoted names hold only letters, digits, and `_`**, since they are only history kinds and level names. A quoted name anywhere else is refused ("A quoted name is a level of the trait it is compared with, or a history kind"). A level may be written on either side of `==` or `!=`.
- **`history.count`'s kind and `n` are checked in the rewrite**, with their positions, not in the whitelist (which still requires both to be constants).
- **Hints:** `and`, `or`, and `not` are refused as "and is written &&" and so on. `chronicle` is refused as waiting for part five, beside `rel` and `grade`.
- **The indexer is an `Index` node** in DynamicExpresso's tree, not a call to `get_Item`; the whitelist allows it only on the view types with a constant id.
- **Compiled on first evaluation.** `Check` parses only; a `CheckedExpression` compiles its delegate the first time it is evaluated and keeps it. The cost falls on the first cycle that reads it, once, and the editor's save, which only checks, never pays it.
- **`IStateSource` and the view objects live in `Sim.Expressions`,** not `Sim.Core`: they are the language's, and only the storylets module implements the source. The history kinds are passed to the book; Task 3 gives them their constants.
- `x == y` compares who the crew members are through the view's own operator; the view's id is internal, so no expression can read it.

### Task 3: the history (2026-11-18)

Built as 4.8 says, in `src/Sim.Storylets/History.cs`. `StoryletsState.History` is a list of cycles, oldest first, each with the kinds recorded for each crew member by id; it replaces `LastCycle`. `IHistoryView` and the kinds' constants (`HistoryKinds`) are in `Sim.Core`'s `Views.cs`; `LastCycleFacts` names the same strings until Task 4 removes it. The tuning key is `history.cycles` (20 in both packs, at least 1). No golden hash moved. Differences and choices:

- **When each kind is recorded.** `sent`, `hurt`, `lost_mission`, and `lost_crewmate` are recorded when the mission ends; the aftermath adds the cycle with nothing in it when no team was sent. `rested` is recorded when the next itinerary opens, not at the aftermath: fitness is read after the week's recovery, as part two read it, so nobody who rested changes.
- **`lost_crewmate` keeps part two's meaning:** every living crew member, sent or not, when someone died on the cycle's mission. 4.3 says "someone died on their mission and they came home", which is narrower; narrowing it would change who the game's "lost a crewmate" vignette reaches and move the hash, so it is the author's to decide.
- **`lost_mission`** is recorded for those who came home from a mission that ended lost, incomplete, or withdrawn.
- **Read in the cycle after.** `Count(crew, kind, n)` reads the `n` cycles before the current one, so in a cycle's aftermath its own record is not yet read. Cycles older than `history.cycles` fall out when the itinerary opens.
- **Before any cycle has ended,** a part-two fact (`{ last: kind }`, with or without `not`) holds for no one, as it did with no last cycle. From Task 4, `count` simply reads 0 there.
- **The dead drop out** when they die (`CrewDied`) and from the mission's record.
- **No version raise:** a store written before this task has no `History` and is refused by its strict read until Task 4 raises the versions. The store's tests gain a round trip of the history. Part two's test that the last cycle is forgotten when the orders open is replaced by `Last_reads_the_history`, which reads it there.

### Task 4: expressions and casting replace the lists and the scope (2026-11-18)

Built as 4.2 to 4.4 say, with arrival kept in part three's order until Task 5 (stakes, then the pack's order; vignettes the longest away first, then by id). `Casting.cs` in `Sim.Storylets` holds every expression, checked by `ExpressionBook` when the module is built and compiled on first use. Both packs, the fixture, and the storylet and vignette tests are migrated. The fixture's `trait_a` and `trait_c` gain minimal entries (8.4). Section 5 says what was built. Differences and choices:

- **The author decided two things during the task (section 8, items 5 and 6):** both kinds of loss (`lost_teammate` is new, and no content reads it yet), and `castTries` 100.
- **Out of tries, measured.** For a pair with one holder among 8 crew, each other subject costs 8 tries (itself and seven targets), so at 50 the search finishes only if the holder is among the first six in the seeded order, or seventh with the right target first: about 77% of cycles. The worked example's "Clear the air" and the pair head's bridge test failed on it. At 100 every pair search over 8 crew finishes, and both pass unchanged. Section 8 has the plan for 500 crew.
- **No hidden memory evaluates; it is not narrowed (4.6).** `ExpressionBook.OnTraits` reads the parsed tree with only the traits known, in three-valued logic, so a clause on a stat, a skill, the history, fitness, or who is who "may still hold", and a false trait clause makes the `&&` false. The loader takes the lowest values of the traits read that let every expression hold (lowest total first, each from 0 to one past the highest number written, or its cap, and every level of a trait with levels), then applies each option's trait changes there, clamped as play clamps them. An option that leaves no expression false is refused: "Option 'x' leaves every expression true, so this storylet would come back every cycle." When no values let every expression hold, the storylet is refused as one that can never arrive; this replaces part one's "No value of X satisfies". Nothing is compiled, so the editor's save stays quick.
- **`Needs`, for the lint.** The same lowest values give the traits a storylet needs above 0. The lint's "Nothing sets {trait} on {where}" reads those, so an expression that holds at 0 is still not warned of, as a requirement with only a max was not. Lint tests that read undefined `flag_*` traits now read the fixture's `trait_c` and `trait_f`, since 8.4 refuses an undefined one.
- **Errors** read "{id}: {key}, at character {n}: {message}", with n counted from 1, where key is `cast.{slot}`, `requires`, or `options.{id}.requires`. A reserved slot name reads "'ship' cannot name a slot; ship, crew, captain, cycle, best, or handler each mean something already." There are at most three slots, and each name follows the inner-id rule.
- **Effects:** loyalty and morale need `on:` crew or a slot. A trait with no `on:` is on the first slot, or on the ship for a storylet with no cast, so the migrated content keeps its effects as written. `toward:` names a slot other than the one the trait is on.
- **Vignettes' options take no `requires`** (my call: a vignette's choice is small and free, as 4.7 says of checks). The vignette's head keeps its words ("CREW_03, lost a crewmate"): `VignetteDef.Fact` is the first kind its first slot reads with `last()` where no `!` stands directly before it. `Sim.Expressions` records these reads as `LastRead`. The head of a cast of two keeps part three's "CREW_01 · avoids CREW_03" until Task 7 lists every cast.
- **Events at version 2** carry the cast on every storylet and vignette event, `VignetteAnswered` included. `Salience` waits for Task 5, and `HandlerId` and `Roll` for Task 6. They are added then, inside this part's one version raise.
- **The stream.** `cast:<id>:<cycle>` is asked for only by a storylet or vignette with a cast; one for the ship draws nothing. A vignette's `vignettes.cooldown` is its cast cooldown: casting passes over a binding that arrived within it, so the next crew member in the seeded order has it.
- **Arrivals are kept for storylets too** (for Task 5's cooldowns and freshness). An arrival whose cast holds someone now dead is dropped with them.
- **Before any cycle has ended,** `!x.history.last(k)` holds for everyone (count reads 0), as Task 3 said it would. Part two's test of it now gates on a trait.
- **Once a cycle in the simulator** (review focus 2). In `SimRunner cycles` (200 legacies × 200 cycles, both packs, both answer policies), the only fire count that moves under first answers is "A week aboard" (VIGNETTE_01 in both packs): 101.7 to 99.6 per 100 cycles, since it arrived for up to three rested crew in one cycle and now arrives once. One or two missions in 40,000 move from full to partial after it, through the morale it no longer gives. Under random answers, three rare counts move by 0.1 (VIGNETTE_02's leave_be, VIGNETTE_05, and the fixture's VIGNETTE_03). The random policy hashes its pick with the offer id, and offer ids are now `{cycle}:{id}`, so STORYLET_01's answer, which sets what those two vignettes read, falls differently. The storylets fire 0.0 per 100 cycles before and after. The golden hash is re-pinned ("storylets cast in expressions"). The CLI test's 2 × 3 run goes from 269 to 235 events and from 216.7 to 100.0 vignettes per 100 cycles, the same once-a-cycle, with its missions unchanged. The walkthrough's hash does not move.
- **Tests that assumed the roster's order** now assert what casting promises: one arrival a cycle, a seeded choice among those who hold, the same choice for the same seed, and no slot bound twice. `Sim.Storylets` shows its internals to `Sim.Tests` so `Out_of_tries_is_told_apart_from_no_cast` can read `CastResult`. The itinerary's web tests gain a second rested vignette, since one vignette no longer arrives twice.

### Task 5: salience and cooldowns (2026-11-18)

Built as 4.5 and 4.6 say, in `src/Sim.Storylets/Salience.cs`. Part three's order (stakes, then the pack's order) and part two's vignette order (the one away longest first, then by id) are gone. Section 5 says what was built. Differences and choices:

- **One function decides (review focus 3).** `Salience.Contend` takes the pack, the state, the state view, and a way to ask for a stream by name, and returns every storylet and vignette with its fate: arrived, lost on the budget, waiting (not cast again), cooling (in its global cooldown, with the cycle it may return), no cast, or out of tries. A contender also has its cast, its tries, and its score in parts, each already weighted: stakes, specificity, freshness, and its place in the tie draw. It changes nothing. The module withdraws the stale, runs it, and then offers the storylets and sends the vignettes it says arrived, in salience order. Task 7's dry run can call it with copies of the streams. A crew storylet whose every holding binding is in its cast cooldown reports no cast, as part four's search has always passed over a cooling binding. If "Why these storylets" needs to tell the two apart, Task 7 adds it to `CastResult`.
- **Specificity is counted where the expressions are kept.** `Casting` adds up the top-level `&&` clauses of each slot's expression and `requires` when the module is built from the pack, once. The loader does not store a count, because it would be the same number and a pack built by hand in tests would lack it.
- **Ties.** When two or more storylets contend, one seeded order of them in the pack's order (Fisher-Yates from the end) is drawn from `salience:<cycle>`, and then the same for the vignettes, from the same stream. Each contender's tie is its place in that order, 1 first, and it orders only equal totals. The places are all different, so no other order is needed after them. With fewer than two contenders nothing is drawn, so most cycles ask for no stream.
- **Budgets.** A storylet arrives if it ranks within `storylets.perCycle` less the storylets still waiting. A vignette arrives if it ranks within `vignettes.perCycle`. A storylet that loses is cast again next cycle, and a vignette that loses is dropped, as before.
- **Freshness** counts the cycles among the `salience.window` before this one in which it arrived, with any cast, and takes off `salience.repeat` for each. A storylet arrives at most once a cycle, so that is its number of arrivals.
- **What the cooldowns count.** Both count from the cycle of arrival, not of the answer. A binding or storylet is held while fewer than N cycles have passed since then, which is what `vignettes.cooldown` has meant since part two (6: arrived in cycle 2, back from cycle 8). So a cooldown of 1 holds nothing back, since nothing arrives twice in one cycle. The global cooldown is read before casting, so a storylet in it draws from no cast stream. The cast cooldown is read inside the search as before: a binding that arrived within it is passed over for the next one in the seeded order. A ship storylet's binding is the empty one, so its cast cooldown works as a global one.
- **How the fires are kept (5.3).** `Arrivals` keeps only the latest arrival of each id and cast, and loses it when a cast member dies, so it can't answer "how many times in the window, over every cast". `Fired` is a second list, one entry per id, with the last arrival and the cycles within the window. A death leaves it alone, so the global cooldown and freshness are the storylet's own, whoever has died.
- **The loader.** `cooldown: { cast, global }` takes whole numbers of 0 or more, and either may be left out ("A cooldown's cast and global are whole numbers of cycles, 0 or more."). With `cooldown.cast` above 0, the no-hidden-memory rule is not applied (4.6), and "must read a trait" is met by the cooldown, as 4.2 allows. A global cooldown alone lifts neither rule, because a storylet that comes back every M cycles with the same cast is still the hidden memory the rule refuses. Vignettes were already exempt from both.
- **A vignette's cooldown** is `null` unless written, and `ContentPack.CooldownOf` gives `{ cast: vignettes.cooldown }` for it. The default is filled in there, not by the loader, so tests that build a vignette by hand and set the pack's cooldown keep working, and the rule lives in one place.
- **Tuning:** `salience.stakes` 10, `.specificity` 1, `.repeat` 3, `.window` 20 in both packs, each a whole number of 0 or more.
- **Events:** `StoryletOffered` and `VignetteArrived` carry `Salience` (the total) inside Task 4's version 2. `SchemaVersion` and `CurrentFormatVersion` are not raised again.
- **The simulator.** In `SimRunner cycles` (200 legacies × 200 cycles, both packs, both answer policies), all 800 legacies play the same as before Task 5. No mission, death, fire, answer, or trait count moves in any legacy. Each pack has one or two storylets and few vignettes that ever hold in the same cycle, and the budgets (2 and 3) are seldom full, so ranking changes at most the order within a cycle. Every legacy's hash moves, through the new field on the events. The golden hash is re-pinned ("salience and cooldowns"). The CLI test's 2 × 3 run and the walkthrough's hash do not move.
- **Tests that assumed an order.** Part two's `Each_vignette_arrives_once_a_cycle_and_the_one_away_longest_comes_first` is replaced by `Vignettes_arrive_by_salience_and_the_one_that_just_came_falls_back`. Two equal vignettes that arrive together are now compared as a set. In the itinerary's web tests, VIGNETTE_A reads two clauses, so salience, not its id, puts it above VIGNETTE_B. Tests that run several cycles raise the fixture's `trait_b` to 5, since it falls by 1 every three weeks.

### Task 6: options with `requires`, and options settled by a check (2026-11-18)

Built as 4.7 and 4.9 say. Section 5 says what was built. Differences and choices:

- **One reader of what is open.** The storylets module serves `IStoryletView`: a waiting storylet's options whose `requires` holds, in the pack's order, and for a check option everyone who may be sent, in roster order, with their skill, score, and chance. The guard reads the same rules (`Casting.Open`, `Check.Refusal`), the bridge shows the view, and the simulator picks from it, so none of them can disagree. `BridgeReader.Read` takes the view from the live game; a reader with no game (a test) builds one from the pack.
- **Refusals.** An option whose `requires` fails: "That answer is not open to this storylet now." A check option with no handler: "That answer is settled by a check: name who goes." A handler on any other: "Only an answer settled by a check sends someone." A handler in the cast, unfit, or not living: "Crew member 'X' is in this storylet's cast, so cannot be sent.", "… is not fit to send.", "… is not on the roster." The answer is one command, so a handler cannot die between being named and the roll (review focus 5).
- **The roll.** Score is the skill level, the handler's morale modifier (`TeamMember.MoraleBonus`), and `ITraitView.Bonuses` with the team read as the cast and the handler. It adds `check.dice`, against `difficulty + 7`, and a margin of 0 or more succeeds. The dice come from `storylet-check:<offer id>`, so the same offer rolls the same dice whoever is sent. `StoryletRoll` stores the level, the morale, and each trait bonus (not 5.2's single `Score`), so the log line can say each part; `Score` is worked out from them and is not stored.
- **The chance word** is `Chances.Of(dice, score, against)` in `Sim.Core.Content`, from the exact chance the dice reach `against − score` (`DiceDef.AtLeast`): below 35% a long shot, below 65% even, below 95% likely, otherwise sure. On 2d6 that is sure when the dice need 3 or less, likely 4 to 6, even 7 or 8, and a long shot 9 or more. The bridge and the tests read the same function.
- **The log line** is the settled panel's first line: "CREW_01 (SKILL_B 5) rolled 8 against 11: success". Morale and each trait bonus are named only when they are not 0 ("…, morale +1, trait_h +1"), as a mission check's tags are.
- **Outcome text is a line,** as an option's is: `{id}.{option}.success.text` and `.failure.text`, beat `storyletAnswer`, with a new fact `outcome` (success or failure), and `handler` as a person in that text only. `StoryletOutcome` therefore holds only effects. A storylet's own text, or a plain option's, that names `#handler#` is refused as naming no person.
- **The loader.** `check: { skill, difficulty }` needs a known skill and a difficulty of 1 or more, and both `success` and `failure`. `effects` or `text` beside it are refused, and so are `success` or `failure` without it. `on: handler` or `toward: handler` outside an outcome is refused ("handler is a slot only in a check's success and failure."). Vignettes refuse `check`. Review focus 4's rule now reads "A storylet needs an option with neither requires nor check, so it can always be answered."
- **No hidden memory** (4.6) judges success and failure each as an option ("Option 'x' leaves every expression true on failure, …") when `cooldown.cast` is 0. A change on or toward the handler touches no trait the expressions read, since the handler is never in the cast. "Must read a trait" and its cooldown waiver are as Task 5 left them.
- **The lint's "Nothing sets"** and Dev Play's trait list count trait changes in success and failure, through `StoryletOption.Outcomes`.
- **The simulator** chooses by its policy's index from the open answers. An option whose `requires` fails is skipped, and so is a check option with no one fit to send. A check option sends the first who may go in roster order, until Task 8's policies.
- **Events.** `StoryletAnswered` gains `HandlerId` and `Roll` inside version 2. `SchemaVersion` and `CurrentFormatVersion` are not raised again. The handler is bound as the slot `handler` only for the outcome's effects, through the same `Apply`, and never enters the stored cast or `Arrivals`.
- **The panel** (4.9). A check option is a `<details>` whose summary is the button: it opens the list of who may be sent in place, inside the panel, with no modal. Each name is its own form ("CREW_01, SKILL_B 5" and the chance word), and all of the storylet's forms wait while any is in flight. With no one fit to send, the option is a disabled button with "No one fit to send." beside it. An option whose `requires` fails is not shown.
- **Looked at** in headless Edge at 1280 and 400 pixels, driven over the DevTools protocol, on a scratch copy of the fixture where STORYLET_04 casts at the first itinerary. Closed, open, and settled, the page does not scroll sideways at either width. Open, the list wraps under its button and the other answers move down. At 400 the names stack one to a row. Settled, the roll line stands above the outcome's text and tags. Claude in Chrome was not connected, and headless Edge was used as in part three. **Not checked in a browser:** the disabled "No one fit to send." state (checked in the bridge tests only), keyboard use of the disclosure, and a screen reader's reading of it.
- **The fixture** gains STORYLET_04 (4.13's "a check option" and "an option with requires"). It is cast on a holder of `trait_h` and its target. `option_a` is a check on SKILL_B at difficulty 4, `option_b` needs `subject.loyalty >= 60`, `option_c` is free, and every answer clears `trait_h`. Its options are written in flow style, since the page-names test adds a `name:` after every block-style `- id:`. Parity seed 3 now casts it and answers its check through the requests, with the simulator's event log.
- **The simulator** (`SimRunner cycles`, 200 legacies × 200 cycles, both packs, both answer policies). The game's pack does not move under either policy. In the fixture, STORYLET_04 fires 24.6 times per 100 cycles. Under first answers, option_a (the check) is taken 24.4 times; the other 0.2 still waited when the runs ended. Under random answers, option_a and option_c are taken 12.2 times each and option_b never, since no subject reached loyalty 60, so `requires` holds it back. `trait_h` is set from 0 24.6 times per 100 cycles, not 0.5: the answers clear it, and ENCOUNTER_07's critical raises it again. Missions move from 39077 full, 811 partial, 80 incomplete, 27 withdrawn, 5 lost to 38834, 1028, 101, 32, 5, because `trait_h`'s +1 withTarget check bonus is gone while it is clear. Checked: with answers that keep `trait_h` (and a long cast cooldown), the missions are exactly as before, and the storylet fires 0.5 times. Deaths, vignettes, and end morale do not move. The golden hash is re-pinned ("options settled by a check"). The CLI test's 2 × 3 run and the walkthrough's hash do not move. The check's success rate waits for Task 8's report.

### Task 7: the itinerary's cast, Dev Play's "Why these storylets", and the editor (2026-11-18)

Built as 4.9, 4.10, and 4.12 say. Section 5 says what was built. No golden hash moved: `SimRunner.Tests` passes unchanged, and so does the walkthrough. Differences and choices:

- **The head** lists the cast by name in slot order, joined by " · ", for storylets and vignettes alike, and a ship storylet lists none. Part three's pair head ("CREW_01 · trait_g CREW_03") is gone, and a storylet cast with one crew member now names them, which it did not before. A vignette keeps its fact words after the names ("CREW_03, rested"). A slot the definition no longer has, because the author changed it while the offer waited, follows in the order cast. "Where it came from" keeps part three's words ("From ENCOUNTER_01 · trait_a"), since 4.9 says it stays.
- **Where it came from, corrected.** The module took the encounter of the first trait read that was set, which is none when an answer last changed that trait, so a later trait an encounter had changed was missed. It now takes the first trait read, on a slot or the ship, that is set and that an encounter last changed, as 4.9 says. No hash moved, so no simulated legacy met the case.
- **The dry run** is `IStoryletDryRun`, beside `IStoryletView` in `Sim.Core`: `Contest(cycleEvents)` and `Failed(id, cycleEvents)`. It runs `Salience.Contend` on a copy of the module's state. **Streams:** it makes each stream new from the seed (`RngStream.For(seed, cycle, name)`) and never reads or writes the legacy's saved streams. Casting and ties draw only from `cast:<id>:<cycle>` and `salience:<cycle>`, which nothing draws from before the itinerary opens, so a new stream is the stream the opening found, even after the opening has advanced the saved one. A copy of a saved stream would be the wrong one in the itinerary.
  - **In the itinerary** it is the contest as it ran. The copy is put back as it stood before the opening's arrivals: this cycle's offers, arrivals, and fires are taken out, and a storylet from an earlier cycle answered since is put back as waiting. Every trait, morale, and loyalty changed since the opening reads as the first change found it, from the events after this cycle's `PhaseEntered(Itinerary)`. Those changes are an answer's, a vignette's, and the developer's. So an answer that clears the trait a storylet read leaves the explanation as it was. A cast cooldown's arrival that this cycle's arrival replaced, and a global cooldown's earlier fire, were older than their cooldowns, or the storylet could not have arrived, so taking this cycle's out misses neither.
  - **Between cycles** (any phase after the itinerary) it foresees the next one. The copy is opened as the module opens it: the last cycle's rest is marked, old history and stale offers are dropped, and fires outside the window fall out. The week's recovery and a trait's change with the weeks come as the next cycle begins, so they are not in it.
- **The contest's types moved to `Sim.Core`** so the bridge can read them: `Contest`, `Contender`, `ContestFate` (was `Fate`), and `SalienceScore` (was `Score`). `Contest` gains `Cycle` and `Open`, and `Contender` gains `Eligible`.
- **Cooling casts are told apart (Task 5's note).** `CastResult` gains `CastOutcome.Cooling`, with `Held`, the first binding that held and was cooling. The contest reports it as `ContestFate.CastCooling` with that cast, and `ReturnsAt` is its arrival plus `cooldown.cast`. The search reads exactly as before, try for try, so casting and the hash are unchanged.
- **Eligible** (the count line): not waiting, out of the global cooldown, and with a first slot that held for someone. `CastResult.FirstHeld` records the last part as the search reads. For a ship storylet, eligible means its `requires` held. A first slot cut short by the cap on tries counts as not held.
- **The count line** counts storylets and vignettes together: "4 storylets, 4 vignettes: 5 eligible, 4 cast, 3 arrived". "Cast" is the contenders, arrived or lost on the budget. A heading above it says which itinerary: "The itinerary now open, cycle 2, as it opened" or "The next itinerary, cycle 2, as things stand now".
- **The contenders** are two tables, storylets and vignettes, each in salience order. A row shows the storylet, its cast ("The ship" for none), its stakes, specificity, and freshness as weighted (freshness as taken off, "−3"), its place in the tie draw, the total, its fate ("Arrived" or "Lost on the budget"), and a link to its file in the editor. Storylets and vignettes are still edited as text, so the link goes to the file.
- **The search** posts to `/dev/legacy/{id}/why` and patches the section alone. It matches part of an id or name, ignoring case, in the pack's order, and lists at most 20 matches, saying how many more there are. Each match gets one line:
  - Arrived: "arrives (or arrived), with salience 32".
  - Lost on the budget.
  - Waiting: "waiting for an answer, so it is not cast again".
  - In its global cooldown: "in its global cooldown of 3 cycles; it may arrive again from cycle 6".
  - Every holding cast cooling: "every cast that held is in its cast cooldown of 6 cycles; CREW_01 · CREW_03 may have it again from cycle 8".
  - Out of tries: "out of tries after 100".
  - No cast: the first failing clause.
- **The first failing clause** (`Casting.Failed`). Each slot in turn is read over every crew member in roster order not already bound, beside each cast the earlier slots held for. Its clauses narrow those casts one at a time, and the first to leave none is reported. Then `requires` is read over every full cast. A first clause reads "subject: subject.trait.trait_b >= 1 held for no one". A later clause adds how many the clauses before it held for: "…; the clauses before it held for 1". `requires` reads "held for no cast", and a ship's `requires` "is false". It draws no stream and counts no try, since why nothing held does not turn on the order. Past 10,000 casts for one slot (three slots over 22 crew) it gives up and says so. The search reads the same rewound or foreseen state as the contest.
- **Clauses one at a time** (`ExpressionBook.Split`). A small split over the rewrite's tokens divides the source at each `&&` outside parentheses, reading inside parentheses that wrap a part whole. A part with `||` at its top is one clause, since `&&` binds tighter. Each part is checked again by `Check`, so the rewrite and the whitelist cover it as they do any expression. The parts must number `Clauses`, the tree's count; if they did not, the expression would stand whole, which a test shows does not happen. A clause is shown as written, without the parentheses that wrapped it.
- **The editor** (4.12). "With its position" is the place in the file. Beside the loader's words ("STORYLET_02: cast.subject, at character 45: No trait named nosuch."), a refused expression in the storylets or vignettes file shows "Line 17, column 59" and that line with a mark under the character. The mark is hidden from screen readers, since the line and column say it. The loader's message is unchanged. The editor reads the id, key, and character from it, finds the expression in the YAML as typed through YamlDotNet's node marks, and counts the character within it. That is exact when the expression stands in the file as it is read (plain, or quoted with no escapes, on one line), as every expression in both packs does. Otherwise it points at where the value starts. The marked line scrolls inside the alert at narrow widths.
- **The panel redraw.** Dev Play's panel draws itself again after any other request on the page, and that redraw shows "Why these storylets" without the last search. Searching again brings it back.
- **Looked at** in headless Edge at 1280 and 400 pixels, driven over the DevTools protocol, on a scratch copy of the fixture with one storylet a cycle. A legacy was brought to cycle 1's aftermath with STORYLET_04, STORYLET_01, VIGNETTE_04, and VIGNETTE_01 set to contend, and checked between cycles and in the open itinerary. The count line and both tables were the same in each. The heads read "CREW_02 · CREW_04" and "CREW_01 · CREW_03", and VIGNETTE_01's gave the rested crew member's id with "rested". The search for STORYLET_03 named its target clause. The page does not scroll sideways at either width; the tables scroll inside their regions at 400. Three things were found and fixed:
  - The tables' visually hidden "Edit" heading is absolutely placed, and it widened the page by 330 pixels at 400, so its scroller is now `position: relative`.
  - A cast now stays on one line.
  - The editor's marked line now scrolls inside the alert, not past it.
- **Not checked in a browser** (Claude in Chrome was not connected, so headless Edge was used, as in Task 6):
  - The game's pack, where names replace the fixture's ids ("Okafor · Vance").
  - Keyboard use and a screen reader's reading of the search and the tables.
  - The give-up message past 10,000 casts.
  - A search with more than 20 matches.
- **Tests.** The seven of the plan. `The_head_lists_the_cast` replaces part three's `A_pairs_storylet_and_vignette_name_the_holder_the_trait_and_the_target`. `The_dry_run_advances_no_stream` runs two legacies side by side, one with dry runs between its commands, and checks:
  - The streams, the state, and every later event are the same in both.
  - The foreseen contest is what arrived.
  - The contest read in the open itinerary after an answer equals the foreseen one.

  `Clauses_split_as_they_are_counted` covers `Split`. The Dev Play tests run on a scratch copy of the fixture with `storylets.perCycle` 1 and a global cooldown on STORYLET_01.

### Task 8: the simulator (2026-11-19)

Built as 4.11 says. Section 5 says what was built. No golden hash moved: not the simulation's, the game pack's, the CLI test's 2 × 3 run, the walkthrough's, or the web parity test's. Differences and choices:

- **Handlers.** The captain reads who may go from `IStoryletView.Answers`, as Task 6 left it, and works out no fitness of its own. Under `first` it sends the one with the highest level in the check's skill (`HandlerOdds.Skill`, the level alone, not the score with morale and traits), and the view lists them in roster order and the sort is stable, so a tie goes to roster order. Under `random` it makes the stream `sim-captain:<offer>` from the legacy's seed, as the option's pick did: the option is its first draw, as before, and the handler its second. So random answers pick the same options as before Task 8, and no game stream moves (`Random_handlers_are_reproducible` checks the streams).
- **The read of the contest (5.1).** The module sets `StoryletsState.Opened` to the contest it ran when the itinerary opened, and `IStoryletView.Opened` serves it. It is `[JsonIgnore]`, so the store and play are as before. The simulator steps the captain's commands one at a time, as `PlayCycle` sends them, and after a step that opened an itinerary it reads the contest. So eligible, cast, lost on the budget, and out of tries are the module's own `Contender.Fate` and `Eligible`, with no second dry run. Success comes from `StoryletAnswered.Roll`.
- **What a count covers.** Each legacy has 201 itineraries in a 200-cycle run, as before: the founding's, and the one that opens as the last cycle ends. That is the itinerary whose arrivals the fire counts have always included. Per 100 cycles still divides by the cycles asked for.
- **Eligible** is the contest's `Eligible` (4.10): out of the global cooldown, not waiting, and with a first slot that held for someone. So a storylet whose first slot held only while it waited or cooled counts as not eligible then. "Never eligible" lists those eligible in no cycle. "Never cast, though eligible" lists those eligible at least once and never arrived or lost on the budget, out of tries included. Both lists cover storylets and vignettes. "Out of tries" is always printed, "none" or each id with its cycles, because it says when to build section 8's candidate search.
- **The new lines,** after "Fired per 100 cycles":
  - "Lost on the budget per 100 cycles: STORYLET_04 0.0 of 24.6 cast": each storylet that cast at least once, with its losses and its casts.
  - "Check success: STORYLET_04 option_a 83.0% of 9750 taken": each check option that was taken, with its successes over the times taken.
  - Then the traits line as before, "Never eligible", "Never cast, though eligible", "Out of tries", and "Never fired" as before.
- **The summary** (`--out`) gains, for each legacy, `Eligible`, `Cast`, `Lost`, and `OutOfTries` (cycles, by id), and `ChecksTaken` and `ChecksSucceeded` (by "{id} {option}").
- **The fixture** gains STORYLET_05, which can't cast. Its first slot is `subject.history.last('hurt')`, which holds for whoever was hurt last cycle, now and then and on no trait that decays. Its second is `target.loyalty > 100`, which the loader lets through (a clause on a stat may still hold, Task 4) and play never meets, since loyalty stops at 100. It reads no trait, so it has `cooldown: { cast: 6 }`, as 4.2 allows. It has two plain options, since a storylet needs two. Its options are in flow style. The fixture's tests that list its storylets, and Dev Play's count line ("5 storylets, 4 vignettes: 5 eligible, 4 cast, 3 arrived"), count it; nobody is hurt in that line's legacy, so it is not eligible there.
- **The simulation's hash did not move, so it was not re-pinned.** The plan expected the handler choice to move it. Each effect separately:
  - **The new storylet** moves nothing on its own. It never casts, so it publishes no event and joins no tie draw, and its cast stream `cast:STORYLET_05:<cycle>` is its own. With STORYLET_05 in the fixture and the old captain, all 44 of the simulator's tests passed, the golden hash included, before the captain changed.
  - **The handler choice** moves no legacy under `first`. The fixture casts STORYLET_04 only from ENCOUNTER_07's critical, which sets `trait_h` on the actor toward the lead, and the lead is CREW_01. So CREW_01 is always in the cast, and CREW_02 (SKILL_B 6, the highest) is both first in roster order and the most skilled. Golden seed 1's one check (offer `10:STORYLET_04`, cast CREW_03 and CREW_01) sends CREW_02 under either rule. In the 200 × 200 run under `first`, all 200 legacies' hashes are the same before and after. `First_picks_the_most_skilled_handler` shows the rule where it matters, with STORYLET_04 cast by hand on CREW_03 and CREW_04: CREW_01 first in roster order, CREW_02 sent.
  - **Under `random`** the handler is drawn, so 199 of 200 legacies' logs move. No golden hash pins a random run.
- **Figures** (`SimRunner cycles`, 200 legacies × 200 cycles, both packs, both answer policies):
  - **The game's pack** does not move under either policy: the first line and every fire count are as before.

    | Line | First | Random |
    |---|---|---|
    | Lost on the budget | STORYLET_01 0.0 of 0.0 cast | the same |
    | Check success | no check taken | the same |
    | Never eligible | STORYLET_02, STORYLET_03, VIGNETTE_04, VIGNETTE_05 | STORYLET_02, STORYLET_03, VIGNETTE_04 |
    | Never cast, though eligible | STORYLET_04, VIGNETTE_06 | the same |
    | Out of tries | none | none |

    STORYLET_01 cast a handful of times (under 0.05 per 100 cycles) and never lost. The game's STORYLET_04 and VIGNETTE_06 have a first slot that holds and a later slot that never does. That is for the author to read in Task 9, when the pack is written.
  - **The fixture under `first`** does not move against Task 6: missions 38834 full, 1028 partial, 101 incomplete, 32 withdrawn, 5 lost; STORYLET_04 24.6 per 100 cycles, option_a 24.4, option_b and option_c 0.0. **STORYLET_04 option_a succeeds 83.0% of the time** (8,091 of 9,750). That is the rate Task 6 left for this task, and Task 6's captain had it too, since its logs are the same. STORYLET_04 cast 9,824 times and lost on the budget none. Never eligible: STORYLET_02, VIGNETTE_03. Never cast, though eligible: STORYLET_03, STORYLET_05 (eligible in 139 cycles), VIGNETTE_04. Out of tries: none.
  - **The fixture under `random`:** missions, deaths, fires, traits set, and end morale do not move against Task 6. STORYLET_04 is 24.6, option_a 12.2, option_c 12.2, option_b 0.0. Events go from 1,628,283 to 1,624,307, as different handlers roll and the outcomes move morale on different crew. **option_a succeeds 42.1% of the time** (of 4,871): a handler drawn at random is often a weak one. Never eligible: STORYLET_02 (VIGNETTE_03 is eligible under random, in 564 cycles). Never cast and out of tries: as under `first`.
- **Tests.** The plan's five are in `SimulationTests`. `Never_cast_is_told_apart_from_never_eligible` and `Check_success_rates_are_reported` read both the summary and the printed lines. The check test counts the rolls again from a separate play of the same legacies. `Losses_on_the_budget_are_reported` runs with `storylets.perCycle` 0, where every cast is lost. With the fixture's budget, it checks that each storylet's casts are its arrivals plus its losses. `NextCommand` keeps its shape, so the web parity test is unchanged.
