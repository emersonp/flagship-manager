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
}

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
        State = state;
    }

    public int morale => State.Morale(Id);

    public int loyalty => State.Loyalty(Id);

    public bool fit => State.IsFit(Id);

    public bool aboard => State.IsAboard(Id);

    public SkillsView skills { get; }

    public TraitsView trait { get; }

    public HistoryView history { get; }

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

// x.trait.t, rewritten to x.trait["t"]; x.trait.t.toward(y), rewritten to x.trait.toward("t", y).
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

// ship.trait.t, rewritten to ship.trait["t"].
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
