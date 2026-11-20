namespace Sim.Expressions;

// What the view objects read (design 4.3). The storylets module adapts its roster, trait, and history views to it.
public interface IStateSource
{
    // The cycle now starting.
    int Cycle { get; }

    int Morale(string crewId);

    int Loyalty(string crewId);

    int Skill(string crewId, string skill);

    // Not injured or incapacitated.
    bool IsFit(string crewId);

    // Alive and not on a mission.
    bool IsAboard(string crewId);

    // CrewId null: the ship. TargetId: a pair trait the crew member holds toward them.
    int Trait(string trait, string? crewId, string? targetId = null);

    // Cycles among the last `cycles` in which `kind` happened to them.
    int History(string crewId, string kind, int cycles);

    // How long ago storylet `storylet` last arrived, anywhere on the ship (slice 1e-calendar, design 4.6); null if it never has.
    Since? StoryletSince(string storylet);

    // How long ago a trait last changed on a crew member, or on the ship (crewId null); null when it is not held (at 0).
    Since? TraitSince(string trait, string? crewId);

    // Chronicle entries of `kind` on days within the last `days` days, today included (1e part five, design 4.6); and among
    // those, the ones with this crew member as a subject.
    int ChronicleCount(string kind, int days);

    int ChronicleCount(string crewId, string kind, int days);
}

// Whole cycles and whole days since something happened, counted from the cycle and the day it happened on.
public readonly record struct Since(int Cycles, int Days);

// The objects an expression reads, and nothing else: every public member here is a word of the language, so each is lower case,
// as the language is. The whitelist (Whitelist.cs) lets an expression touch only these types. Ids are internal, so no expression
// can read one.

// A slot: one crew member.
public sealed class CrewView
{
    internal CrewView(IStateSource state, string id)
    {
        Id = id;
        skills = new SkillsView(state, id);
        trait = new TraitsView(state, id);
        history = new HistoryView(state, id);
        chronicle = new ChronicleView(state, id);
        State = state;
    }

    public int morale => State.Morale(Id);

    public int loyalty => State.Loyalty(Id);

    public bool fit => State.IsFit(Id);

    public bool aboard => State.IsAboard(Id);

    public SkillsView skills { get; }

    public TraitsView trait { get; }

    public HistoryView history { get; }

    public ChronicleView chronicle { get; }

    internal string Id { get; }

    private IStateSource State { get; }

    // The same crew member.
    public static bool operator ==(CrewView? a, CrewView? b)
    {
        return a?.Id == b?.Id;
    }

    public static bool operator !=(CrewView? a, CrewView? b)
    {
        return !(a == b);
    }

    public override bool Equals(object? obj)
    {
        return obj is CrewView other && other.Id == Id;
    }

    public override int GetHashCode()
    {
        return Id.GetHashCode(StringComparison.Ordinal);
    }
}

// x.skills.S, rewritten to x.skills["S"].
public sealed class SkillsView
{
    private readonly IStateSource state;
    private readonly string id;

    internal SkillsView(IStateSource state, string id)
    {
        this.state = state;
        this.id = id;
    }

    public int this[string skill] => state.Skill(id, skill);
}

// x.trait.t, rewritten to x.trait["t"]; x.trait.t.toward(y), rewritten to x.trait.toward("t", y); x.trait.t.cyclesSince and
// x.trait.t.daysSince, rewritten to x.trait.cyclesSince("t") and x.trait.daysSince("t"). The two readings of time are null when the
// trait is not held, and C#'s lifted comparisons make every <, <=, >, >=, and == with null false.
public sealed class TraitsView
{
    private readonly IStateSource state;
    private readonly string id;

    internal TraitsView(IStateSource state, string id)
    {
        this.state = state;
        this.id = id;
    }

    public int this[string trait] => state.Trait(trait, id);

    public int toward(string trait, CrewView target)
    {
        return state.Trait(trait, id, target.Id);
    }

    public int? cyclesSince(string trait)
    {
        return state.TraitSince(trait, id)?.Cycles;
    }

    public int? daysSince(string trait)
    {
        return state.TraitSince(trait, id)?.Days;
    }
}

// x.history.count('kind', n) and x.history.last('kind').
public sealed class HistoryView
{
    private readonly IStateSource state;
    private readonly string id;

    internal HistoryView(IStateSource state, string id)
    {
        this.state = state;
        this.id = id;
    }

    public int count(string kind, int cycles)
    {
        return state.History(id, kind, cycles);
    }

    public bool last(string kind)
    {
        return state.History(id, kind, 1) > 0;
    }
}

// chronicle.count('kind', n) and x.chronicle.count('kind', n) (1e part five, design 4.6): entries in the last n days, today
// included; on a slot, only those with that crew member as a subject.
public sealed class ChronicleView
{
    private readonly IStateSource state;
    private readonly string? id;

    internal ChronicleView(IStateSource state, string? id)
    {
        this.state = state;
        this.id = id;
    }

    public int count(string kind, int days)
    {
        return id is null ? state.ChronicleCount(kind, days) : state.ChronicleCount(id, kind, days);
    }
}

// ship.trait.t, rewritten to ship.trait["t"]; its readings of time as a crew member's.
public sealed class ShipView
{
    internal ShipView(IStateSource state)
    {
        trait = new ShipTraitsView(state);
    }

    public ShipTraitsView trait { get; }
}

public sealed class ShipTraitsView
{
    private readonly IStateSource state;

    internal ShipTraitsView(IStateSource state)
    {
        this.state = state;
    }

    public int this[string trait] => state.Trait(trait, null);

    public int? cyclesSince(string trait)
    {
        return state.TraitSince(trait, null)?.Cycles;
    }

    public int? daysSince(string trait)
    {
        return state.TraitSince(trait, null)?.Days;
    }
}

// cycle.number.
public sealed class CycleView
{
    private readonly IStateSource state;

    internal CycleView(IStateSource state)
    {
        this.state = state;
    }

    public int number => state.Cycle;
}

// storylet.s.cyclesSince and storylet.s.daysSince (slice 1e-calendar, design 4.6), rewritten to storylet.cyclesSince("s") and
// storylet.daysSince("s"): how long since storylet s last arrived, or null if it never has.
public sealed class StoryletsView
{
    private readonly IStateSource state;

    internal StoryletsView(IStateSource state)
    {
        this.state = state;
    }

    public int? cyclesSince(string storylet)
    {
        return state.StoryletSince(storylet)?.Cycles;
    }

    public int? daysSince(string storylet)
    {
        return state.StoryletSince(storylet)?.Days;
    }
}
