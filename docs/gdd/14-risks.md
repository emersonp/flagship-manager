# 14. Risks

A chapter of the [Starship Sim GDD](../gdd.md). Section numbers are permanent ids; cross-references such as "section 4.3" find their chapter through the index.

| # | Risk | Mitigation |
|---|---|---|
| 1 | The mission layer feels passive | Throwaway prototype first. Calls are real choices with costs. The autonomy lever gives control over involvement. Resolution streams beat by beat, so the player watches a story unfold, not a result appear. Preparation choices (approach-opening gear, parameters) are visibly credited in the report. |
| 2 | Tone clash between spreadsheets and prose | Log-style register. Every prose line cites what changed (a small tag such as "morale −5"). Every number links to the beats that caused it. |
| 3 | Vignette fatigue and repetition | Salience budget, cooldowns, expansion memory, and the dead-content and over-fire counts in the simulator. |
| 4 | Authoring load: one writer, 500 crew | Fallback chain in the grammar so generic lines cover everything from day one. Tag-level lines. The simulator reports which lines are seen most, so writing effort goes where the player looks. |
| 5 | Balance drift as mechanics pile up | Simulator in CI with baselines and the pure-policy assertion. |
| 6 | Agents eroding module boundaries | The dependency rules in 7.1 enforced by an architecture test. Events additive only. Golden seeds. |
| 7 | Death feels arbitrary | Death needs a failed severe roll, which needs a failure or disaster, which strain and the abort rule give warning of. The report names the chain of causes. |
| 8 | Delegation hides the game from the player | Digest lines are one click from full detail. Salience promotes anything touching crew the player has invested in. |
| 9 | Expression language grows into a programming language | Keep it to boolean and arithmetic expressions over the state view, with no loops or assignment. New capability comes from new quality functions, not new syntax. |
| 10 | Save compatibility over years | State tables migrate normally. Events are never replayed to load. Content version pinned per legacy. |
