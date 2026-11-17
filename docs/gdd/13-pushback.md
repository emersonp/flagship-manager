# 13. Pushback

A chapter of the [Starship Sim GDD](../gdd.md). Section numbers are permanent ids; cross-references such as "section 4.3" find their chapter through the index.

Points where the brief was changed or should be watched:

1. **Calls per mission.** "Two to five per mission" across three teams would stall the loop. Now: calls scale with mission size, and the player has an autonomy lever.
2. **Ironman while building.** Single autosave and a game under construction do not mix. Developer mode exists from Phase 0.
3. **500 fully simulated crew.** Accepted, and cheap to compute. The cost moves to authoring and to salience; both are flagged as risks, and scale is deliberately the last phase.
4. **"Sometimes only one mission is possible."** Withdrawn after review. The author's point was that the choice is in how an assignment is carried out, which led to the assignment, site, and objective structure in section 2. What should be rare is a site with only one objective, and the simulator reports how often that happens.
5. **Vignettes "should be many".** Many are generated, few are shown. An inbox of thirty vignettes is noise.
6. **Online in one to three months.** Achievable because the server is authoritative from the start, but it should not be allowed to jump ahead of Phase 2. A hosted game without the central trade-off is not worth hosting.
7. **Datastar and a future tactical grid.** Datastar is the wrong tool for a grid. The plan is a separate client behind the resolver contract, not stretching hypermedia to do it.
