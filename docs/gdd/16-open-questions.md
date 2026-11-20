# 16. Open questions

A chapter of the [Starship Sim GDD](../gdd.md). Section numbers are permanent ids; cross-references such as "section 4.3" find their chapter through the index.

Cheap to settle later; none blocks slice 1e.

1. **An objective's type**
The sample objective in 3.4 has `type: survey`, "one of ~10 objective types". The built objective has only tags, and nothing reads a type.
(a) Drop it; tags already group objectives (b) Keep a fixed list of types, for something tags cannot do, such as an icon or the report's wording
Recommend (a), unless something turns up that needs exactly one type per objective.

2. **An objective's base stakes**
The sample objective has `baseStakes: 2` (1 to 5). The built game sets stakes only on each decision, and the call rule reads those.
(a) Drop it (b) Add it to the stakes of every decision on the mission (c) Use it only where a whole objective is weighed, such as what leaving it unattended costs
Recommend (a) until a rule needs a stakes figure for the whole objective.

3. **An objective's rewards**
The sample objective and assignment grant `rewards: { shipRenown, admiraltyStanding }`. Neither stat is built; both arrive with Phase 6.
(a) Fixed rewards per objective, scaled by the result (b) Effects per outcome, written beside the objective's outcome text (c) Leave it to Phase 6
Recommend (c), and write them as (b) then, so what a result says and what it does sit together.

4. **What an unattended failure does**
The sample's `ifUnattended` has `onFail` effects (lose renown, add a chronicle line). The built objective has only `ifUnattended.difficulty`, the roll.
(a) Effects per objective, as in the sample (b) One cost for every objective, in the tuning file (c) The objective's own outcome text and rewards, with no separate list
Recommend deciding with question 3, since a failed unattended roll costs the same kinds of thing.

5. **Ending a stage early**
The sample encounter's decision has an `abortStage` effect. The built effects are progress, momentum, strain, injury, withdraw (which ends the whole mission), and story flags; none ends part of one. In play a stage is only a label on its encounters (3.4), so "the rest of the stage" means the following encounters that the same stage drew.
(a) Add it: skip the rest of the stage's encounters (b) Drop it; withdraw and the momentum skip cover it
Recommend deciding when stage versus encounter is settled, that is, whether a stage becomes a unit of play.

6. **Previews on test ships for encounters and lines**
Slice 1e part six casts storylet and vignette previews on test ships (`content/testships/`). The encounter, objective, and line previews still use slice 1d's synthetic samples, built for the item from the starting crew.
(a) Move them onto test ships, with a mission started on the ship (b) Keep the synthetic samples, which can satisfy any line's criteria (c) Both: the synthetic sample by default, a test ship on request
Recommend (c) for now, and revisit once storylets have been written on test ships for a while and the author knows whether a ship's realism is worth more than a sample made to fit.
