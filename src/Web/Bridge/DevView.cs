using GameHost;
using Sim.Core;
using Sim.Core.Content;
using Sim.Storylets;

namespace Web.Bridge;

// Dev Play's panel: every stored flag, and what the set-a-flag form offers.
public sealed record DevView(IReadOnlyList<DevFlagRow> Flags, IReadOnlyList<DevTarget> Targets, IReadOnlyList<string> KnownFlags, int Most)
{
    // Each living crew member's morale figure, which play shows only as a word (1e part two).
    public IReadOnlyList<DevMoraleRow> Morale { get; init; } = [];
}

public sealed record DevMoraleRow(string Shown, int Morale, string Word);

// On: the crew member's shown name, or "Ship". From: the encounter that last changed it in play, shown; null when only answers or the developer have.
public sealed record DevFlagRow(string Flag, string On, int Value, int Week, string? From);

// Id: "ship" or a crew id, as the form posts it.
public sealed record DevTarget(string Id, string Shown);

public static class DevReader
{
    public const string Ship = "ship";

    public static DevView Read(LegacyState state, Game game, ContentPack pack, Names names)
    {
        IReadOnlyList<string> roster = game.Bus.View<IRosterView>(state).Roster;
        List<StoredFlag> stored = state.TryModule(StoryletsModule.Name, out StoryletsState? storylets) ? storylets.Flags : [];
        List<DevFlagRow> flags = stored
            .OrderBy(f => f.CrewId is null ? -1 : IndexOf(roster, f.CrewId))
            .ThenBy(f => f.Flag, StringComparer.Ordinal)
            .Select(f => new DevFlagRow(f.Flag, f.CrewId is null ? "Ship" : names.Crew(f.CrewId), f.Value, f.SetAtWeek, f.EncounterId is null ? null : names.Encounter(f.EncounterId)))
            .ToList();
        List<DevTarget> targets = [new(Ship, "Ship"), .. roster.Select(id => new DevTarget(id, names.Crew(id)))];
        IRosterView view = game.Bus.View<IRosterView>(state);
        List<DevMoraleRow> morale = roster.Select(id => new DevMoraleRow(names.Crew(id), view.Morale(id), BridgeReader.MoraleWord(pack.CrewTuning, view.Morale(id)))).ToList();
        return new DevView(flags, targets, Known(pack), SetFlag.Most) { Morale = morale };
    }

    // Every flag the pack reads or changes, for the form's suggestions.
    public static IReadOnlyList<string> Known(ContentPack pack)
    {
        IEnumerable<string> read = pack.Storylets.SelectMany(s => s.Flags.Select(r => r.Flag));
        IEnumerable<string> answered = pack.Storylets.SelectMany(s => s.Options).SelectMany(o => o.Effects).Select(e => e.Flag?.Flag).OfType<string>();
        IEnumerable<EffectDef> effects = pack.Encounters.SelectMany(e => e.Approaches.SelectMany(a => a.Outcomes.Values)
            .Concat(e.Decision is { } d ? [d.Bold, d.Safe] : []));
        IEnumerable<string> set = effects.SelectMany(e => e.Flags ?? []).Select(f => f.Flag);
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
