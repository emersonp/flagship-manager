# Slice 1e, part four (the storylet engine in full): implementation plan

> **For agentic workers:** steps use checkbox (`- [ ]`) syntax for tracking. Do the tasks in order; each ends with `./check.sh` and a commit. Keep the design's section 5 (architecture) current as you go: when the build differs, change section 5 in the same commit, and say so in the design's "As built" section (section 9).

**Goal:** storylets and vignettes are written in one expression language over a read-only state view, cast with up to three named slots, ranked by salience against their budgets, held back by cooldowns per cast and per storylet, and answered by options that may carry `requires` or be settled by a check with a handler the captain names. A rolling history of each crew member's recent cycles is read by expressions. Dev Play explains "Why these storylets", the simulator reports what never cast, and the game's pack plays "The one who came back".

**Architecture:** a new library `Sim.Expressions` (not a module, no state) wraps the evaluator: it parses against the state view's types, reports errors with positions, counts top-level `&&` clauses, lists the traits read, and evaluates. `Sim.Content` uses it to check at load, `Sim.Storylets` to run. `Sim.Storylets` gains casting, salience, cooldowns, check options, and the history (`IHistoryView`). Contracts live in `Sim.Core`.

**Spec:** `docs/plans/2026-11-18-phase-1e-part-four-design.md`. Read it with this plan. **Design system:** `docs/design/bridge-design.md`.

## Global constraints

Part one's (`2026-11-16-phase-1e-part-one-plan.md`, "Global constraints") hold, with these changes:

- `SchemaVersion` is 11 and `CurrentFormatVersion` is 9 today. Task 4 raises them to 12 and 10, once for the whole part; later tasks that change `StoryletsState` do so inside that one raise (the store is refused, not migrated).
- This part draws randomness, only from the named streams of design 5.4: `cast:<id>:<cycle>`, `salience:<cycle>`, `storylet-check:<offer id>`, and the simulator's `sim-captain`. Dev Play's dry run never advances a stream.
- `Sim.Expressions` references only `Sim.Core` and the evaluator's package. It is pure (the banned-API analyzer covers it, as a `Sim.*` project). The architecture test learns that it is a library, not a module, and that `Sim.Content` and `Sim.Storylets` may reference it.
- `content/` may gain generated words for `came_back`, STORYLET_05, and STORYLET_06 (design 4.13), and the migration of the existing storylets and vignettes, and nothing else.

## Review focus

1. **Nothing reaches past the state view.** No expression can call `GetType()`, name a type, call a static method, or have a side effect; each is refused at load with a position. (Tasks 1 and 2)
2. **The migration changes nothing but once-a-cycle.** After Task 4 the fixture and the game's pack play the same choices and outcomes, apart from a storylet arriving at most once a cycle. (Task 4)
3. **Replay is exact.** Casting, ties, and checks draw only from their named streams; the dry run leaves every stream where it was; a reload mid-itinerary plays on identically. (Tasks 4 to 7)
4. **A storylet can always be answered.** The loader requires an option with neither `requires` nor `check`; a check option with no one fit to send is disabled with its reason. (Tasks 4 and 6)
5. **A dead cast member withdraws the offer,** whichever slot they held, and a handler who dies before the answer is impossible (the answer is one command). (Tasks 4 and 6)

---

### Task 1: the spike (report to the author before Task 2)

**Files:** none in the repository. A throwaway console project in the scratchpad referencing `DynamicExpresso.Core` (and `NCalc` only if a question fails), with stub view types shaped as design 4.3. The findings go in the design's section 9 and to the author.

Answer design section 6's five questions:

1. Parse `subject.trait.shaken > 0 && subject.history.count('hurt', 5) >= 2` against declared types; refuse `subject.trait.nosuch` and `subject.rel` at parse time with a position.
2. How `x.trait.t` is made checkable (a rewrite to a method call before parsing, or a typed dynamic member with a checking binder); whether `x.trait.t == 'level'` works or levels need `x.trait.t.is('level')`.
3. With reflection off and only the view types registered: can an expression reach `GetType()`, `System.IO`, a static method, or anything else?
4. Counting the top-level `&&` clauses from the parsed tree.
5. Cost: 200 storylets × 50 tries, compiled once at load.

- [ ] **Step 1:** Build the spike and run each question as a printed case.
- [ ] **Step 2:** If any of 1 to 3 fail, run NCalc against the same list.
- [ ] **Step 3:** Record the answers in the design's section 9 and report them. **Stop. Go no further until the author has read them.** Commit the design change alone: "Record the expression spike's findings for slice 1e's fourth part".

### Task 2: `Sim.Expressions`

**Files:** a new project `src/Sim.Expressions/` (`ExpressionBook.cs`, `StateView.cs` with the view types, `ExpressionError.cs`), added to `StarshipSim.slnx`; `tests/Sim.Tests/ExpressionsTests.cs`; `tests/Sim.Tests/ArchitectureTests.cs`.

**Produces** (shape; the spike may change the internals):

```csharp
public sealed record ExpressionError(string Message, int Position);
public sealed record CheckedExpression(string Source, int Clauses, IReadOnlyList<TraitRead> Traits);   // TraitRead(string Trait, string Slot, string? Toward)
public sealed class ExpressionBook   // built once per pack: the trait definitions, skill ids, history kinds, history.cycles
{
    public CheckedExpression? Check(string source, IReadOnlyList<string> slots, out ExpressionError? error);
    public bool Evaluate(CheckedExpression expression, IStateSource state, IReadOnlyDictionary<string, string> cast);
}
public interface IStateSource { /* reads roster, traits, history, cycle: what the view objects wrap */ }
```

- Every read of design 4.3, and nothing else. A name outside the table is refused by name, with the messages of 4.3 ("rel() waits for relationships (Phase 4)", "grade waits for 1h").
- Trait ids, levels, skill ids, history kinds, and `n` are checked here, against the book; `toward` only on a pair trait; a slot read only if bound before (the caller passes the slots in reading order).
- Compiled once; evaluation allocates nothing per call beyond the view.

- [ ] **Step 1:** Tests: one per read of 4.3 holding and not holding; one per refusal with its position; `Clauses_count_the_top_level_ands`; `Traits_read_lists_each_trait_with_its_slot`; `A_later_slot_cannot_be_read`; the spike's escape list (focus 1); a timing test that 10,000 evaluations take well under a second.
- [ ] **Step 2:** Run them. Expected: compile failures.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** `./check.sh`, then commit: "Add the expression language for storylets, checked against the state view".

### Task 3: the history

**Files:** `src/Sim.Core/Views.cs` (`IHistoryView`); `src/Sim.Storylets/StoryletsModule.cs`, `StoryletsState.cs`, a new `History.cs`; `GameHost/Game.cs` if the view is registered there; the tuning loader and both packs' `tuning/missions.yaml` (`history.cycles: 20`); tests beside each.

- Each living crew member's kinds per cycle for the last `history.cycles` cycles, built at the aftermath from the events `LastCycleRecord` reads today, plus `lost_mission` (objective failed or abandoned; design 8.3). Dead crew drop out.
- `LastCycleRecord` is folded in: the list form's `{ last: kind }` reads `Count(kind, 1) > 0` until Task 4 replaces it.
- The state grows; no version raise here (Task 4 raises once), but the store's round-trip test covers the new field.

- [ ] **Step 1:** Tests: `History_keeps_the_last_N_cycles`, `Lost_mission_means_failed_or_abandoned_not_a_death`, `The_dead_drop_out`, `Last_reads_the_history` (the part-two last-cycle tests still pass unchanged).
- [ ] **Step 2:** Run them. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** `dotnet test`; no golden hash moves (the history records, nothing reads more than before).
- [ ] **Step 5:** `./check.sh`, then commit: "Keep a rolling history of each crew member's recent cycles".

### Task 4: expressions and casting replace the lists and the scope

**Files:** `src/Sim.Core/Content/StoryletContent.cs`, `StoryletContracts.cs` (events version 2, design 5.2); `src/Sim.Content/ContentLoader.Storylets.cs`, `ContentWriter.cs` if it writes storylets, `Sim.Content.csproj`; `src/Sim.Storylets/*`; `src/Web/Bridge/*`, `StoryletPanel.razor`, `VignettePanel.razor`, `src/Web/Forms/TextFiles.cs` (lint reads traits from expressions); `src/Sim.Narrative` and `Prose.cs` where slot names become text tokens; both packs' `storylets` and `vignettes`; both persistence version files; tests listed by `grep -rlE "Scope|LastCycle|TraitRange|StatRange" tests --include=*.cs`.

- `cast` (up to three ordered slots, lower-case names, not reserved), `requires` as one expression, read after the cast. The loader refuses `scope` ("scope was replaced by cast in part four; a storylet for crew casts subject.") and the list form, as it refuses `flag:`. Every error names the storylet, the key, and the position.
- `on:`, `toward:`, and speakers take any slot name; `toward:` may not name `on:`'s slot.
- "Must read a trait" over `cast` and `requires`. "No hidden memory" is re-checked by evaluating each option's trait results against the expressions (design 4.6), or narrowed and said in section 9.
- The loader requires an option with neither `requires` nor `check` (focus 4); option `requires` is parsed here but read in Task 6.
- Casting as design 4.4: a seeded order from `cast:<id>:<cycle>`, depth first, `storylets.castTries` (50) in tuning; one cast per storylet per cycle; "no cast" and "out of tries" told apart in the result. No slot bound twice. A waiting storylet is not cast again; any cast member's death withdraws it (focus 5).
- Arrival keeps today's order (stakes, then file order) until Task 5.
- Offer ids become `{cycle}:{id}` (one cast per cycle). Events: `StoryletOffered`, `StoryletAnswered`, `StoryletWithdrawn`, `VignetteArrived` at version 2 with `Cast`. Raise `SchemaVersion` to 12 and `CurrentFormatVersion` to 10.
- Migrate STORYLET_01 to 04, the six vignettes, and the fixture, keeping `subject` and `target` so scripts don't change.

- [ ] **Step 1:** Tests. Content: each new loader message (scope, list form, a reserved slot name, a later slot read, `toward` equal to `on`, no free option, a bad trait with its position). Module: `Casting_is_the_same_for_the_same_seed`, `A_slot_is_never_bound_twice`, `Out_of_tries_is_told_apart_from_no_cast`, `A_storylet_arrives_at_most_once_a_cycle`, `Any_cast_members_death_withdraws_it`, `A_ship_storylet_casts_no_one`. Both packs load. The old storylet and vignette tests, migrated, still pass (focus 2).
- [ ] **Step 2:** Run them. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** `dotnet test`. The simulation's hash moves (casting draws from a stream; once a cycle; events version 2): compare `SimRunner cycles` fire counts before and after, explain each difference by once-a-cycle, then re-pin with "storylets cast in expressions (1e part four, Task 4)". The walkthrough's moves only if its events change.
- [ ] **Step 5:** `./check.sh`, then commit: "Write storylets and vignettes in the expression language, cast with named slots".

### Task 5: salience and cooldowns

**Files:** `src/Sim.Core/Content/StoryletContent.cs` (`Cooldown`); `src/Sim.Content/ContentLoader.Storylets.cs`, `ContentLoader.Tuning.cs`; both packs' tuning (`salience.stakes` 10, `.specificity` 1, `.repeat` 3, `.window` 20); `src/Sim.Storylets/*` (a `Salience.cs`); tests beside each.

- `cooldown: { cast, global }`; a vignette with none takes `{ cast: vignettes.cooldown }`. Each storylet's and each cast's last arrival are stored.
- Salience as design 4.5, specificity counted once at load; ties from `salience:<cycle>`. Storylets and vignettes arrive highest first up to their budgets; part two's vignette order is replaced.
- "No hidden memory" applies only when `cooldown.cast` is 0.
- The arrival code is one function that returns every contender with its score in parts and its fate, so Task 7's dry run reads the same thing (focus 3).

- [ ] **Step 1:** Tests: `Higher_stakes_wins`, `More_clauses_wins_at_equal_stakes`, `A_storylet_that_just_fired_loses_ground`, `Ties_are_drawn_from_the_salience_stream`, `Cast_cooldown_holds_the_same_binding_only`, `Global_cooldown_holds_every_cast`, `A_recurring_storylet_with_cast_cooldown_passes_the_hidden_memory_rule`, `Losers_on_the_budget_arrive_later`.
- [ ] **Step 2:** Run them. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** `dotnet test`. The simulation's hash moves (salience order): re-pin with "salience and cooldowns (1e part four, Task 5)".
- [ ] **Step 5:** `./check.sh`, then commit: "Rank storylets and vignettes by salience, with cooldowns per cast and per storylet".

### Task 6: options with `requires`, and options settled by a check

**Files:** `src/Sim.Core/Content/StoryletContent.cs` (`StoryletCheck`, `StoryletOutcome`), `StoryletContracts.cs` (`AnswerStorylet.HandlerId`, `StoryletRoll`); `src/Sim.Content/ContentLoader.Storylets.cs`; `src/Sim.Storylets/*` (a `Check.cs`); `src/Web/Bridge/*`, `StoryletPanel.razor`, `src/Web/Endpoints/BridgeEndpoints.cs`; tests beside each.

- An option whose `requires` is false is refused and not shown.
- `check: { skill, difficulty }` with `success` and `failure`; no `effects` or `text` beside them; `handler` is a slot only inside them. Vignettes refuse `check`.
- Handler: living, fit, aboard, not in the cast. Score = skill + morale modifier + trait check bonuses (with `withTarget` read against the cast) + `check.dice` against `difficulty + 7`; margin 0 or more succeeds. Stream `storylet-check:<offer id>`. The chance word (long shot, even, likely, sure) is worked from the dice by one function the bridge and tests share.
- `HandlerId` required for a check option, refused on any other. The log line: "Okafor (SKILL_B 6, morale +1) rolled 7 against 11: success".
- Panel: the check option's button opens the handler list in place, no modal; "No one fit to send." disables it (focus 4); the settled panel shows the roll line.

- [ ] **Step 1:** Tests. Content: each check message. Module: `A_check_option_needs_a_handler`, `A_handler_is_refused_on_a_plain_option`, `A_cast_member_cannot_handle`, `The_roll_is_the_same_for_the_same_offer`, `Success_and_failure_apply_their_own_effects`, `Handler_is_readable_in_the_outcome_text_and_effects`, `An_option_whose_requires_fails_is_refused`. Bridge: the handler list, the chance words, the disabled reason, the roll line.
- [ ] **Step 2:** Run them. Expected: FAIL.
- [ ] **Step 3:** Implement. Look at 1280 and 400 pixels, as in part two, and note what could not be checked.
- [ ] **Step 4:** `dotnet test`. The simulation's hash moves only if the fixture gains a check option here (it does: the fixture's check storylet): re-pin with "options settled by a check (1e part four, Task 6)". The simulator picks the first fit handler in roster order until Task 8.
- [ ] **Step 5:** `./check.sh`, then commit: "Let a storylet's options carry requires, or be settled by a check with a handler the captain names".

### Task 7: the itinerary's cast, Dev Play's "Why these storylets", and the editor

**Files:** `src/Web/Bridge/BridgeView.cs`, `BridgeReader.cs`, `DevView.cs`, `EditorLinks.cs`; `src/Web/Components/StoryletPanel.razor`, `VignettePanel.razor`, `DevPanel.razor` (or a new component); `src/Web/Endpoints/DevPlayEndpoints.cs` (search); `src/Sim.Storylets/*` (the dry run, served through a view in `Sim.Core`); `src/Web/Forms/TextFiles.cs` (expression errors with positions); tests beside each.

- The head lists the cast by name ("Okafor · Vance"); "where it came from" reads the first trait an expression reads on a slot or the ship.
- Dev Play: the count line, the contenders in salience order with score parts, fate, and an editor link; a search by id or name that says why one isn't here (the first failing clause and its slot, out of tries, or the cooldown and when it ends). It reads Task 5's function through a dry run that draws from copies of the streams (focus 3).
- The editor's storylets and vignettes files show expression errors with their positions.

- [ ] **Step 1:** Tests: `The_head_lists_the_cast`, `The_count_line_counts_eligible_cast_and_arrived`, `Contenders_are_in_salience_order_with_their_parts`, `Search_names_the_failing_clause_and_slot`, `Search_names_the_cooldown_and_when_it_ends`, `The_dry_run_advances_no_stream` (focus 3), `An_expression_error_shows_its_position_in_the_editor`.
- [ ] **Step 2:** Run them. Expected: FAIL.
- [ ] **Step 3:** Implement. Look at 1280 and 400 pixels.
- [ ] **Step 4:** `dotnet test`; no golden hash moves.
- [ ] **Step 5:** `./check.sh`, then commit: "Show a storylet's cast, and explain in Dev Play why these storylets arrived".

### Task 8: the simulator

**Files:** `src/SimRunner/AutoCaptain.cs`, `Simulation.cs`, `Cli.cs`; `tests/SimRunner.Tests/*`; the web parity test if `NextCommand` changes.

- Handlers: under `first` the highest skill, ties in roster order; under `random` from `sim-captain`.
- Reports beside the fire counts: "Never cast" apart from "Never eligible"; each check option's success rate; each storylet's losses on the budget.

- [ ] **Step 1:** Tests: `First_picks_the_most_skilled_handler`, `Random_handlers_are_reproducible`, `Never_cast_is_told_apart_from_never_eligible` (the fixture's storylet that can't cast), `Check_success_rates_are_reported`, `Losses_on_the_budget_are_reported`. The walkthrough's hash is still the same under either policy.
- [ ] **Step 2:** Run them. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** `dotnet test`. The simulation's hash moves (handler choice): re-pin with "the simulator names handlers (1e part four, Task 8)".
- [ ] **Step 5:** `./check.sh`, then commit: "Let the simulator name handlers, and report what never cast, check success, and losses on the budget".

### Task 9: the game's pack, and the record

**Files:** `content/traits/base.yaml`, `content/storylets/base.yaml`, `content/vignettes/base.yaml`; the docs below.

- [ ] **Step 1:** Content, in generated words (design 4.13): `came_back`; STORYLET_05 "The one who came back"; STORYLET_06 "Still not talking". Check with the numbers that STORYLET_05 outranks "Someone shaken" when both hold; change clauses, not tuning, if it doesn't. Run `SimRunner cycles` (20 legacies × 50 cycles, seed 1) under `first` and `random`, and read the reports. Do not change difficulty to force a storylet to fire; say what did not.
- [ ] **Step 2:** `./check.sh`; play the design's "Ends with" in the browser through Dev Play: lose a mission, see STORYLET_05 cast and ranked, send a handler, fail, see STORYLET_06 come and come back once, and read "Why these storylets".
- [ ] **Step 3:** The record.
  - GDD: status line to v1.22. 3.7 and 3.8 as built (`cast`, expression `requires`, `cooldown`, `check`; the state view's table); 5.1 steps 1 to 3 as built, step 4 waiting for department heads; 7.5 (the itinerary's cast and handler list, Dev Play's "Why these storylets"); 11.1 (part four done with the date; part five next); 15, item 41 (design section 1).
  - Glossary: expression, slot, cast, contender, salience, handler, history.
  - Conventions: slot names and the reserved words.
  - Design: section 9 with differences, the simulator's figures, and what the author should judge in play. Keep section 5 true.
  - Outline's slice 1e paragraph; README and map, one line each if they mention `scope` or requirements.
  - Rebuild and republish the GDD page (`tools/gdd-page/README.md`) to `https://claude.ai/artifact/9CKdbu7bhV7UvriU9jzWHW`.
- [ ] **Step 4:** Commit: "Give the game the one who came back, and record slice 1e's fourth part".
