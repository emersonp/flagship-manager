# 5. Storylet system

A chapter of the [Starship Sim GDD](../gdd.md). Section numbers are permanent ids; cross-references such as "section 4.3" find their chapter through the index.

The game uses two narrative structures, named as in Emily Short's survey ("Beyond Branching: Quality-Based and Salience-Based Narrative Structures", 2016), each where it fits:

| Structure | How the next piece is chosen | Used for |
|---|---|---|
| Quality-based | A piece becomes available when the qualities it requires hold | Which storylets are eligible, who can be cast (5.1), and which options a storylet offers |
| Salience-based | Of everything eligible, the piece that fits the situation most specifically wins; ties break by seed | Report and vignette lines (section 6), and which storylets reach the inbox (5.1) |

One selection engine serves both: requirements gate (quality-based), and a score ranks what is left (salience). Each structure's known weakness has a counter here. A thin early library is covered by generic fallbacks. Content that sequences itself at a bad moment is checked by stakes, cooldowns, and the simulator's fire counts.

Short's survey names a third structure, waypoint narrative, in which the engine steers content towards an authored target by measuring how far each piece is from it. The game does not use it. Storylines (5.3) and conversations (5.4) get the same result from qualities, and an author can read any storylet and know when it fires.

## 5.1 Selection each cycle

**As built in slice 1e, part one,** and moved in part two from the aftermath to the captain's itinerary, the first phase of each cycle. When the itinerary opens, the storylets module finds every storylet whose requirements all hold, on the ship for a storylet for the ship and on each living crew member for a storylet for the crew, and which is not already waiting for that scope. It offers them highest stakes first, then by id, then by the subject's place on the roster, until `storylets.perCycle` (tuning, 2) are waiting; the rest arrive in a later cycle, and nothing is lost. There is no other memory: a storylet comes again whenever its requirements hold, and its own answers are what stop it (3.7). A storylet whose subject dies is withdrawn, and the traits on them are dropped. The itinerary cannot be left while one waits. Nothing draws randomness.

**As built in part three:** storylets and vignettes read and change traits (story flags until then) through the traits module, and may be for a **pair**: they arrive once for each holder and target whose requirements hold, by the holder's place on the roster and then the target's, and are withdrawn if either dies. Traits change each week by their definitions (a step, an expiry), so a storylet can come back, or stop, without anyone answering anything; and a trait can bite on missions (an approach's requirement, a check bonus, a rule at launch), which the briefing shows before the captain launches (section 7.5).

**Vignettes, as built in part two,** arrive in the same itinerary, after the storylets: every vignette whose requirements hold on a scope and which is out of its cooldown there is due, and up to `vignettes.perCycle` (tuning, 3) arrive, the one longest since it last arrived anywhere first, then by id, then by roster order. The rest are dropped. One with effects applies them as it arrives; one with a choice waits, may be answered in any order, and takes its default when the captain moves on to the orders. The captain's time (3) is shown on the itinerary and not yet spent. The steps below are the engine in full, built in part four (renumbered on 2026-11-17 from part three; see section 15, item 40).

1. **Index.** Storylets are indexed by the tags and quality kinds they test, so only plausible candidates are evaluated.
2. **Cast.** For each candidate, find crew bindings that satisfy the cast slots. Bindings are sampled, not enumerated, with a hard cap per storylet.
3. **Score salience.** `stakes × recency of the underlying change × how much the player has invested in the cast (time spent, missions sent on) − repetition penalty`.
4. **Route.**
   - Vignettes: the top few by salience go to the inbox (a per-cycle budget, starting at five). The rest are dropped or folded into the digest.
   - Captain storylets with stakes below the relevant department head's threshold: the head resolves them by personality, and the result goes in the digest.
   - Captain storylets above it: the inbox, with a cost and an expiry.

The head's threshold uses the same formula as the away-team call rule. One rule, two places.

## 5.2 Captain storylets and vignettes

| | Captain storylet | Vignette |
|---|---|---|
| Choice | Yes, two to four options | Usually none; sometimes one small one |
| Cost | One to three captain's time | Free |
| Length | A short paragraph plus options | One to three sentences |
| Purpose | A ruling with lasting effects | A signal about who needs attention |
| Mechanical effect | Always | Always, but small (a relationship or morale nudge, or a trait) |

A vignette always changes something or reveals something true about the state. If it does neither, it is cut. As built in part two, a vignette with no choice must have at least one effect, and its text is one to three script entries.

A vignette may offer one small, free choice where a moment of attention should be possible: in a ride in the turbolift, say something or say nothing. Its scope stays small (a nudge, a trait, a little insight), and it never becomes a ruling. If the cycle advances without an answer, the vignette's default option applies, usually the quiet one.

## 5.3 Storylines

A storyline is storylets chained by traits (story flags until slice 1e's part three renamed them). Storylets are events, as in Crusader Kings III: one presented between missions is always answered, and there is no `ifIgnored` (section 15, item 40; this paragraph described one until 2026-11-17). What the captain can leave alone is a trait, and a trait left alone can get worse: a trait with a rising weekly step (a festering grudge) grows until a storylet that requires its higher values arrives. Ignoring a problem is still a choice with an outcome, made by not acting on what the vignettes and the roster show.

A longer storyline tracks its progress in a trait used as a counter, for example `feud` from 0 to 5. Its storylets each require a range of that trait and raise or lower it. The storylets the storyline is heading for (a confrontation, a reconciliation, a departure) are ordinary storylets that require a high value, so each fires when the state reaches it, by whichever storylets got it there. The captain's choices change the flag or set another, which makes different storylets eligible: that is how a storyline is diverted or ended. Salience picks among the eligible storylets and freshness disfavours those already used, so one storyline plays out differently each time. Nothing steers a storyline: it reaches its confrontation only through storylets written to raise the flag. A chain of flags, as above, is the simplest case: a storyline with one path. Built in Phase 4.

## 5.4 Captain's time

- Budget per cycle: three to six, set by the captain's traits.
- Spent on inbox captain storylets and on **discretionary actions**: player-initiated storylets such as a ready-room talk with a chosen crew member, a promotion review, or a social evening. These are ordinary storylets whose cast the player picks. In a ready-room talk (Phase 4) the crew member has something they mean to raise. That is a quality the talk's options test, so the talk comes round to it unless the captain's choices change the subject.
- Does not carry over.
- Transit cycles get a larger fixed budget. It does not scale with the number of weeks.
- The inbox should usually hold more than the budget can cover. If the simulator shows the budget routinely covering everything, the economy is broken.

## 5.5 Delegation

Department heads fill the duty roster for their department, resolve low-stakes storylets, and nominate people for missions. Each does it in character: an ambitious head hoards the good assignments, and a low-empathy head lets morale problems run. The digest reports what they did, in one line each, and the player can open any line to see the detail or reverse a roster choice.

A department that looks quiet may be well run or badly reported. Appointing heads is therefore one of the weightiest captain storylets.

## 5.6 Insight and the org chart

The org chart is always visible: who reports to whom, by department and post. What the player knows about each person is not.

- Personality, morale, and relationship values are shown as bands in words, never as numbers.
- Each crew member has an insight level: hidden, rough, clear, or keen. It controls how many bands are shown and how fine they are.
- The captain's direct reports are always clear.
- For everyone else, insight comes from the people responsible for them. Department heads and trainers give insight into skills and potential. Counsellors give insight into personality, morale, and relationships. The better they are at it, the clearer the picture and the sooner it arrives.
- Skill grades (3.10) are seen through the department head or trainer responsible for the person. At rough insight a grade is a word ("High"); at clear it is a letter ("B+"); at keen it is the letter and its shape ("B+, excelling in SKILL_A"). The shape is the hard part to see. Someone strong across a whole attribute and someone average with one exceptional skill look alike to anyone short of a keen observer.
- Vignettes and missions raise insight for the crew they feature.
- Insight is never wrong at first, only vague: poor staff mean wide bands. Later, traits can distort it, both in the person observed (someone hiding something) and in the observer (a counsellor biased against `SPECIES_A` reads them badly).

Staffing the insight roles well is therefore how a captain of 500 sees the ship at all. A vignette is often the first sign of something the bands have not yet shown.
