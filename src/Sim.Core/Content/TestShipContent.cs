namespace Sim.Core.Content;

// A test ship (1e part six, design 4.6): a ship described in content, which the editor casts storylets and vignettes on for real.
// Dev-only: ContentPack never carries one, and play never reads one. Every time is written as days and cycles ago from the ship's
// today, so a ship never goes stale as the calendar changes.

// A starting-crew member with changes; a null is as the starting crew file has them (morale and loyalty at the tuning file's
// starting values). Skills override one skill at a time. Alive false: dead, so neither aboard nor on the roster.
public sealed record TestShipCrew(string Id, int? Morale, int? Loyalty, IReadOnlyDictionary<string, int>? Skills, bool? Fit, bool? Aboard, bool? Alive);

// A trait held: On is the crew member (null for the ship), Toward a pair's target. Value is resolved from a level name to its number.
public sealed record TestShipTrait(string Trait, string? On, string? Toward, int Value, int DaysAgo, int CyclesAgo);

public sealed record TestShipEntry(string Kind, int DaysAgo, IReadOnlyList<string> Subjects);

// A storylet's (or a vignette's) last arrival.
public sealed record TestShipArrival(string Storylet, int DaysAgo, int CyclesAgo);

// Date: the day number of the ship's today. Cycle: the cycle now starting. History: by crew member, then kind, the cycles ago it
// happened (1 is last cycle).
public sealed record TestShipDef(
    string Id,
    string Name,
    int Date,
    int Cycle,
    IReadOnlyList<TestShipCrew> Crew,
    IReadOnlyList<TestShipTrait> Traits,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<int>>> History,
    IReadOnlyList<TestShipEntry> Chronicle,
    IReadOnlyList<TestShipArrival> Storylets);

// Ships: every ship that loaded, in file order; Errors: what the loader refused, each naming the file, the ship, and the field.
public sealed record TestShipsLoadResult(IReadOnlyList<TestShipDef> Ships, IReadOnlyList<ContentError> Errors);
