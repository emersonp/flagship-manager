using GameHost;
using Sim.Core;
using Sim.Core.Content;
using Sim.Traits;

namespace Web.Bridge;

// Dev Play's panel: every stored trait, and what the set-a-trait form offers.
public sealed record DevView(IReadOnlyList<DevTraitRow> Traits, IReadOnlyList<DevTarget> Targets, IReadOnlyList<string> KnownTraits, int Most)
{
    // Each living crew member's morale figure, which play shows only as a word (1e part two).
    public IReadOnlyList<DevMoraleRow> Morale { get; init; } = [];

    // "Why these storylets" (1e part four, Task 7); null in a view built without this cycle's events.
    public WhyView? Why { get; init; }
}

public sealed record DevMoraleRow(string Shown, int Morale, string Word);

// On: the crew member's shown name, or "Ship"; Toward: a pair's target, shown (1e part three). Level: the value's level name, if it has
// one. Hidden: a trait never shown in play. From: the encounter that last changed it in play, shown; null when only answers or the
// developer have.
public sealed record DevTraitRow(string Trait, string On, int Value, int Day, string? From, string? Toward = null, string? Level = null, bool Hidden = false);

// Id: "ship" or a crew id, as the form posts it.
public sealed record DevTarget(string Id, string Shown);

public static class DevReader
{
    public const string Ship = "ship";

    // cycleEvents: this cycle's, for "Why these storylets".
    public static DevView Read(LegacyState state, Game game, ContentPack pack, Names names, IReadOnlyList<IEvent> cycleEvents)
    {
        IReadOnlyList<string> roster = game.Bus.View<IRosterView>(state).Roster;
        List<StoredTrait> stored = state.TryModule(TraitsModule.Name, out TraitsState? traits) ? traits.Traits : [];
        List<DevTraitRow> rows = stored
            .OrderBy(t => t.CrewId is null ? -1 : IndexOf(roster, t.CrewId))
            .ThenBy(t => t.Trait, StringComparer.Ordinal)
            .ThenBy(t => t.TargetId is null ? -1 : IndexOf(roster, t.TargetId))
            .Select(t => new DevTraitRow(
                t.Trait, t.CrewId is null ? "Ship" : names.Crew(t.CrewId), t.Value, t.ChangedAtDay, t.EncounterId is null ? null : names.Encounter(t.EncounterId),
                t.TargetId is null ? null : names.Crew(t.TargetId),
                pack.Traits.FirstOrDefault(d => d.Id == t.Trait)?.LevelName(t.Value),
                pack.Traits.FirstOrDefault(d => d.Id == t.Trait) is { Shown: false }))
            .ToList();
        List<DevTarget> targets = [new(Ship, "Ship"), .. roster.Select(id => new DevTarget(id, names.Crew(id)))];
        IRosterView view = game.Bus.View<IRosterView>(state);
        List<DevMoraleRow> morale = roster.Select(id => new DevMoraleRow(names.Crew(id), view.Morale(id), BridgeReader.MoraleWord(pack.CrewTuning, view.Morale(id)))).ToList();
        return new DevView(rows, targets, Known(pack), SetTrait.Most) { Morale = morale, Why = WhyReader.Read(state, game, pack, names, cycleEvents) };
    }

    // Every trait the pack defines, reads, or changes, for the form's suggestions.
    public static IReadOnlyList<string> Known(ContentPack pack)
    {
        IEnumerable<string> defined = pack.Traits.Select(t => t.Id);
        IEnumerable<string> read = pack.Storylets.SelectMany(s => s.Reads.Select(r => r.Trait)).Concat(pack.Vignettes.SelectMany(v => v.Reads.Select(r => r.Trait)));
        IEnumerable<string> answered = pack.Storylets.SelectMany(s => s.Options).SelectMany(o => o.Outcomes.SelectMany(e => e)).Select(e => e.Trait?.Trait).OfType<string>();
        IEnumerable<EffectDef> effects = pack.Encounters.SelectMany(e => e.Approaches.SelectMany(a => a.Outcomes.Values)
            .Concat(e.Decision is { } d ? [d.Bold, d.Safe] : []));
        IEnumerable<string> set = effects.SelectMany(e => e.Traits ?? []).Select(t => t.Trait);
        return defined.Concat(read).Concat(answered).Concat(set).Distinct().Order(StringComparer.Ordinal).ToList();
    }

    private static int IndexOf(IReadOnlyList<string> roster, string id)
    {
        for (int i = 0; i < roster.Count; i++)
        {
            if (roster[i] == id)
            {
                return i;
            }
        }

        return roster.Count;
    }
}
