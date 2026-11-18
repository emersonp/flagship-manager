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

History kinds are `sent`, `hurt`, `rested`, `lost_crewmate` (someone died on their mission and they came home), and `lost_mission` (on a mission whose objective failed or was abandoned; decided by the author, section 8). `n` is 1 to `history.cycles`. Anything not in the table is refused by name, for example "rel() waits for relationships (Phase 4)" and "grade waits for 1h".

How `x.trait.t` is checked (the trait id is a member name, not a declared property) is the spike's main question. Either a rewrite to `x.Trait("t")` before parsing, with the id checked by the rewrite, or a typed dynamic member with a checking binder. See section 8.

### 4.4 Casting

For each storylet and vignette out of its global cooldown:

1. If it has no `cast`, read `requires` on the ship. It holds or it doesn't.
2. Otherwise draw a seeded order of the living crew from the stream `cast:<id>:<cycle>`. Then search depth first: for slot 1, take crew in that order whose expression holds; for each, fill slot 2 likewise, and so on, then read `requires`. Every expression read counts as a try. Stop at the first full binding that holds and is out of its `cooldown.cast`, or when `storylets.castTries` (tuning, 50) is spent.
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
| `Sim.Storylets` | 300 | Casting, salience, cooldowns, check options, and the history. Reads expressions through `Sim.Expressions`. Serves `IHistoryView`, and the dry run Dev Play reads. |
| `Sim.Expressions` (new, a library, not a module) | — | Wraps DynamicExpresso: parses an expression against the state view's types, gives errors with positions, counts clauses for specificity, lists the traits read (for the lint and "where it came from"), and evaluates against a view. Referenced by `Sim.Content` (to check at load) and `Sim.Storylets` (to run). It holds no state **(my call)**. |
| `Sim.Content` | — | Reads `cast`, `requires`, `cooldown`, and `check`. Refuses `scope` and the list form. Checks expressions through `Sim.Expressions` against `TraitBook`. |
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

### 5.3 Persistence

`StoryletsState` gains the history, each storylet's and each cast's last arrival cycle, and the cast on each waiting offer. It loses `LastCycle` and the subject and target fields. `SchemaVersion` goes from 11 to 12 and `CurrentFormatVersion` from 9 to 10. Old saves are refused.

### 5.4 Rules that hold

- Feature modules never reference each other. `Sim.Expressions` is a library both the loader and the storylets module use.
- Expressions see only the view objects. DynamicExpresso's reflection stays off, and no type outside the view is registered.
- Determinism: casting draws from `cast:<id>:<cycle>`, ties from `salience:<cycle>`, and checks from `storylet-check:<offer>`. The simulator's picks draw only from `sim-captain`. Dev Play's dry run never advances a stream.
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
