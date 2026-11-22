# 8. Engine recommendation

A chapter of the [Flagship: Ready Room GDD](../gdd.md). Section numbers are permanent ids; cross-references such as "section 4.3" find their chapter through the index.

Stay with C# and Datastar.

| | C# + Datastar | Godot |
|---|---|---|
| Inbox, tables, and forms | Native strength | Laborious; UI toolkit is weaker than HTML |
| Online, accounts, and persistence | Native strength | Needs a backend anyway |
| Headless simulator | Same code as the game | Possible, with more friction |
| Agent-built iterations | Plain C# with fast tests | Scene files are harder for agents to edit safely |
| Paper-doll portraits | Layered images composited in code | Better tooling (cutout and shaders) |
| Tactical grid | Weak fit for Datastar | Strong fit |

Seams for later migration:

- `Sim.*` has no web or database dependencies. It can be hosted in Godot's .NET runtime as it stands, or ported module by module to Rust behind the same data contracts.
- The resolver contract is plain data in and out. A tactical grid can be a separate client (Godot web export, or canvas) that plays the grid and returns a `MissionResult`.
- Portraits are a spec (`{ face, hair, tints, … }`) rendered by a replaceable component.
