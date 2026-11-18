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
}

public sealed record DevMoraleRow(string Shown, int Morale, string Word);

// On: the crew member's shown name, or "Ship". From: the encounter that last changed it in play, shown; null when only answers or the developer have.
public sealed record DevTraitRow(string Trait, string On, int Value, int Week, string? From);

// Id: "ship" or a crew id, as the form posts it.
public sealed record DevTarget(string Id, string Shown);

public static class DevReader
{
    public const string Ship = "ship";

    public static DevView Read(LegacyState state, Game game, ContentPack pack, Names names)
    {
        IReadOnlyList<string> roster = game.Bus.View<IRosterView>(state).Roster;
        List<StoredTrait> stored = state.TryModule(TraitsModule.Name, out TraitsState? traits) ? traits.Traits : [];
        List<DevTraitRow> rows = stored
            .OrderBy(t => t.CrewId is null ? -1 : IndexOf(roster, t.CrewId))
            .ThenBy(t => t.Trait, StringComparer.Ordinal)
            .Select(t => new DevTraitRow(t.Trait, t.CrewId is null ? "Ship" : names.Crew(t.CrewId), t.Value, t.ChangedAtWeek, t.EncounterId is null ? null : names.Encounter(t.EncounterId)))
            .ToList();
        List<DevTarget> targets = [new(Ship, "Ship"), .. roster.Select(id => new DevTarget(id, names.Crew(id)))];
        IRosterView view = game.Bus.View<IRosterView>(state);
        List<DevMoraleRow> morale = roster.Select(id => new DevMoraleRow(names.Crew(id), view.Morale(id), BridgeReader.MoraleWord(pack.CrewTuning, view.Morale(id)))).ToList();
        return new DevView(rows, targets, Known(pack), SetTrait.Most) { Morale = morale };
    }

    // Every trait the pack reads or changes, for the form's suggestions.
    public static IReadOnlyList<string> Known(ContentPack pack)
    {
        IEnumerable<string> read = pack.Storylets.SelectMany(s => s.Traits.Select(r => r.Trait));
        IEnumerable<string> answered = pack.Storylets.SelectMany(s => s.Options).SelectMany(o => o.Effects).Select(e => e.Trait?.Trait).OfType<string>();
        IEnumerable<EffectDef> effects = pack.Encounters.SelectMany(e => e.Approaches.SelectMany(a => a.Outcomes.Values)
            .Concat(e.Decision is { } d ? [d.Bold, d.Safe] : []));
        IEnumerable<string> set = effects.SelectMany(e => e.Traits ?? []).Select(t => t.Trait);
        return read.Concat(answered).Concat(set).Distinct().Order(StringComparer.Ordinal).ToList();
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
