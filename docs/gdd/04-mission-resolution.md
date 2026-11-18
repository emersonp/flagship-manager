# 4. Mission resolution

A chapter of the [Starship Sim GDD](../gdd.md). Section numbers are permanent ids; cross-references such as "section 4.3" find their chapter through the index.

## 4.1 Mission state

A mission in flight is a small serialisable record: the drawn encounter list, a cursor, the team's **momentum** and **strain**, objective progress, injuries so far, and the tuning and encounter definitions it started with. Dice are keyed by seed and encounter, so no stream position is stored; beats are returned by each step, not kept in the state.

## 4.2 The check

For each encounter:

1. **Pick the approach.** An approach is available when all of its requirements hold for the team as it stands at that encounter. Among the available approaches the team lead takes the one whose skill is highest for the lead, after injury; a tie goes to the approach listed first. It is the lead's own skill, not the team's best, so naming the lead changes how the team works. Personality does not weigh the choice. The best-qualified member still makes the check (step 2).
2. **Pick the actor.** The crew member with the best relevant skill acts, unless the parameters say "develop juniors". Then the lowest-skilled eligible member acts under the best member's supervision, but only when the check is within reach: an expected margin of +2 or better on an average roll, at the supervised score. Otherwise the best acts as usual.
3. **Compute the score.** `actor skill + assist + gear + traits + morale + momentum − strain − injuries`. The assist is half the second-best skill, rounded down. Morale (slice 1e, part two) is −1 for an actor at Low morale and +1 at High, fixed when the mission launches. Under "develop juniors" the senior carries the junior: the supervised check is made at the score the best-qualified member would have had, minus a penalty of 1, and minus 1 more for every four levels between senior and junior. Supervision therefore always costs something, on any team.
4. **Roll.** Score plus the check dice (2d6 today) against `difficulty + 7`. The margin gives a tier: critical (+5 or more), success (0 to +4), partial (−1 to −3), failure (−4 to −7), or disaster (−8 or less).
5. **Apply the tier's effects** and record a beat.

Two dice give a bell curve, so skill differences matter more than luck and the simulator's results are stable. The dice are expected to change: every roll goes through one seam (section 4.7).

## 4.3 Compounding

Momentum and strain are the "dungeon" memory. Both carry between encounters and both are capped at 5.

- Momentum adds to checks, and can skip an encounter that a skippable stage drew. At momentum 4 or more (`skipAtMomentum` in the tuning file) the team pays 2 momentum (`skipCost`), gains 1 progress, and moves on without a check. The test is made at each encounter, so a skippable stage that drew two encounters may skip both, one, or neither.
- Strain subtracts from checks. A success or critical removes one strain.
- At strain 4 the abort decision is always a call, whatever the lead is like, unless comms are blocked. At strain 5 the mission collapses: one member takes a severe injury roll and the rest take light rolls.
- The collapse is checked after the encounter's decision, so a call at the strain cap can still pull the team out by withdrawing. This is the order the prototype played in. Checking first would cost weak teams under a lead who calls (an average lead with a rookie team: 21% lost becomes 28%); it is one condition in the resolver.
- An approach may write its own outcomes, so a bolder approach can pay in momentum; a tier it does not write takes the tuning file's default. The prototype's rule that fast earns momentum on success is gone from the tuning file: the converted `fast` approaches carry it as written outcomes.
- Disasters can chain a follow-on encounter that was not in the original draw.

A mission ends in one of five results: full success, partial success, incomplete, withdrawal, or loss. Incomplete means the team played every encounter and achieved too little; withdrawal means only that someone chose to pull out. Withdrawing when the lead preferred the safe option does not count as overruling them. Death comes only from a failed severe injury roll: on a disaster, on a bold option that risks one, or in a collapse.

## 4.4 Decisions: call or initiative

When an encounter has a decision:

```
askThreshold = 3 − (lead.caution / 2) − (lead.discipline / 4) + parameters.autonomy
if decision.stakes >= askThreshold  → call (mission waits for the captain)
else                                → initiative (lead picks the option their personality prefers)
```

- The player names each team's lead, defaulting to the highest rank. Choosing the lead is choosing what you will be asked.
- The operational parameters are priority, autonomy, and develop juniors. Risk tolerance is not among them: it no longer chooses an approach, and returns with the overhaul of skills, attributes, and gear (slice 1h, section 11.1).
- `parameters.autonomy` runs from −1 ("check in often") to +1 ("use your judgement"). This is the player's lever on how many calls they get.
- A cautious, disciplined lead calls about most things. A reckless one mostly takes initiatives, some of which the captain would not have chosen. Lead and autonomy weigh about equally: on the prototype's bench a cautious lead makes 3.1, 2.5, or 1.2 calls per mission across the three autonomy settings, and a reckless one 0.8, 0.2, or 0.2.
- Some encounters set `commsBlocked`, forcing an initiative whatever the lead is like. Who you sent matters most then.
- Overruling a lead's stated preference on a call costs a little of their loyalty; backing them raises it.

Expected volume: most missions produce zero to two calls. A site worked by three teams produces more in total, by design.

## 4.5 Growth and harm

- **Experience** goes to the actor of each check, and half to the assist. It is scaled by how close the check was: a check won or survived at a margin within ±3 earns the most; a trivial check earns almost none.
- **Mentoring**: a junior actor with an assist four or more levels above them earns a bonus.
- **Shipboard training** (from the duty roster) earns experience every week at roughly a fifth of the mission rate, and cannot raise a skill past a cap set by rank.
- **Promotion** requires a minimum `awayMissions` count per rank.
- **The captain** may join an away team, at a cost in captain's time that cycle. It is the usual way a captain dies in the line of duty.
- **Injury rolls** are light or severe. A severe roll, when failed, is permanent injury or death.

This makes the safest team the worst one for development, which is the point.

## 4.6 The resolver contract

The rest of the game knows only this:

```
Start(objective, encounter pool, team, loadout, parameters, tuning, seed) → MissionState
Advance(MissionState, reply?) → { state, beats[], pending: Decision | null, result: MissionResult | null }
```

`Advance` runs until it needs a captain's answer or the mission ends. A reply names the call it answers; a reply to another call, or when none is waiting, is refused. `MissionResult` carries outcome, injuries, experience by skill, and resource changes. The stored mission state carries a format version and is read strictly: an unknown or missing member is an error. Everything in and out is plain data. The resolver knows nothing about assignments, sites, or other teams; the cycle orchestrator runs several missions side by side and interleaves their beats.

A tactical grid resolver would implement the same two operations. `pending` would then be "the player's turn on the grid" rather than a call, and the consumer would not change.

## 4.7 Tuning

The numbers in this section were set on the throwaway prototype's bench (2,000 missions per briefing) and are starting values, to be tuned in play.

- Every number the resolver uses (tier margins, strain and momentum effects, the reach margin, assist values, call-rule weights, experience by margin, and injury targets) lives in one tuning file in the content pack, not in code.
- In developer mode the file reloads without a restart, and a tuning page edits it and re-runs the mission bench on the same seeds, showing before and after side by side.
- A running mission keeps the tuning it started with, so a legacy stays deterministic.

Before the 1d review:

- Known and accepted: a reckless lead still loses about 13% of missions at about 3.7 deaths per 100 crew-missions. That is the price of sending them.
- Built in Phase 1 (slices 1a and 1b): the tuning file is `content/tuning/missions.yaml`, and `SimRunner bench --objective ID --compare FILE` runs the same seeds under the pack's tuning and under `FILE`. On the benchmark crew (10,000 missions per briefing) the C# resolver gives calls per mission of 3.1, 2.5, and 1.3 for a cautious lead across the three autonomy settings, 2.5, 1.3, and 0.6 for an average one, and 0.7, 0.2, and 0.2 for a reckless one. A reckless lead loses about 17% of missions at about 3.6 deaths per 100 crew-missions (the prototype gave 13% and 3.7: the skippable mark moved from the encounter to the stage that draws it, and the dice differ).
- **The dice seam** (slice 1c): the tuning file holds `check.dice` and `injury.dice`, each `{ count, sides }`, both 2 and 6 today. The resolver rolls only through one dice type, and the reach rule uses its average, so another number and size of dice is a file edit plus a bench. A different mechanic (a pool, advantage, or exploding dice) replaces that one type; the golden values are then re-pinned and the shape tests say whether the game kept its shape.
- Measured again after slice 1c on the bench's generated mixed team (10,000 missions per briefing): calls per mission are unchanged at 3.1, 2.5, and 1.3 for a cautious lead across the three autonomy settings, 2.5, 1.3, and 0.6 for an average one, and 0.7, 0.2, and 0.2 for a reckless one. A reckless lead of that team loses 15.7% of missions at 3.5 deaths per 100 crew-missions. That is a different team from the fixed benchmark crew behind the 17% above; slice 1c changed neither figure.
- "Develop juniors" under the carried rule, same team: full success 78.2% against 85.2% for best qualified. The junior's experience goes from 2.0 to 5.2 per mission and the senior's from 12.0 to 10.1. On a veteran team it costs about three points (95.4% against 98.3%); rookies never qualify for supervision, so it changes nothing for them.
- Whole cycles (slice 1c, `SimRunner cycles`, 20 legacies of 50 cycles): the simulator's captain always sends the strongest fit team and backs the lead. Of 1,000 missions, 882 were full successes, 110 partial, 5 incomplete, 1 withdrawn, and 2 lost, with no deaths and a highest skill level of 7. Over 10,000 missions that captain loses about 4 crew. That is far below the alarm band in section 10, as it should be for the safest policy; the simulator's other policies come with slice 1e.

After the 1d review (authored approaches), fixture content, 2000 runs, rookie / mixed / veteran:

- Loss %: 34.2 / 0.7 / 0.1.
- Deaths per 100 crew-missions: 3.1 / 0.1 / 0.0.
- Calls per mission: 1.36 / 1.29 / 1.22.
- Initiatives per mission: 1.89 / 2.46 / 2.44.
