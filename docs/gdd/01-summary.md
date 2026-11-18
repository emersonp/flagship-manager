# 1. Summary

A chapter of the [Starship Sim GDD](../gdd.md). Section numbers are permanent ids; cross-references such as "section 4.3" find their chapter through the index.

The player holds the **chair** of a ship. Each **cycle** they choose missions, pick **away teams**, and then live with the consequences aboard. One player's whole game is a **legacy**. There is no ending and no game over: hulls are lost, captains retire, crew die or leave, and the **chronicle** keeps growing.

The game is staffing decisions whose consequences stay. Choosing an away team is the one made most often, and several things pull on it at once:

- The people most likely to succeed, or the people who most need the experience.
- A broad team that can attempt anything, or a deep one that does one thing well.
- Who leads, which decides how the team goes about its work and what the captain is asked.
- Who is loyal, who is close to whom, and whose qualities may set off events of their own, on the mission or aboard afterwards.

None of these is the game by itself. Developing juniors is one consideration among them.

## Settled decisions

| Area | Decision |
|---|---|
| Structure | Assignment, then objectives at a site, then one mission per away team |
| Stack | C# backend, Postgres (SQLite locally), Datastar front end |
| Resolver | Step-wise, resumable, seeded, serialisable |
| State | Current-state tables plus append-only event log |
| Qualities | Typed model behind a read-only state view |
| Scale | Every crew member simulated identically, up to roughly 500 |
| Permanence | Permadeath, single autosave, no game over |
| Online | Isolated legacies per player; shared universe later |
| Time | Cycles of variable length, one week by default; crew and captains age and turn over |
| Lineage | One name; every new hull takes the next suffix |
