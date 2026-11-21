using Sim.Core.Content;

namespace Web.Tuning;

// A choice in a select: its value and what it shows.
public sealed record BenchChoice(string Value, string Label);

// What the bench's inputs are drawn from (1f part one, Task 10; design 4.4): the inputs as the query has them, the page's id (the run it
// owns), the pack's objectives and gear tags (each tag with the gear that carries it), the legacies and test ships a real team can come
// from, the chosen source's roster with each member as the launch takes them, an error beside each input the bench cannot take, a
// refusal of the run as a whole (a real team the launch refuses), and whether the draft does not load (so Run is refused and "Run saved
// only" offered).
public sealed record BenchInputsView(
    TuningQuery Query,
    string Page,
    IReadOnlyList<BenchChoice> Objectives,
    IReadOnlyList<BenchChoice> Gear,
    IReadOnlyList<BenchChoice> Sources,
    BenchSource? Source,
    IReadOnlyDictionary<string, string> Errors,
    string? Refused,
    bool DraftBroken)
{
    public static async Task<BenchInputsView> Build(
        TuningQuery query,
        string page,
        LivePack? live,
        BenchTeams teams,
        GameServices game,
        TuningDrafts drafts,
        IReadOnlyDictionary<string, string>? errors,
        string? refused,
        CancellationToken cancellation)
    {
        IReadOnlyList<BenchChoice> objectives = [];
        IReadOnlyList<BenchChoice> gear = [];
        if (live is not null)
        {
            ContentPack pack = live.Pack;
            objectives = pack.Objectives.Select(o => new BenchChoice(o.Id, $"{live.Names.Objective(o.Id)} ({o.Id})")).ToList();
            gear = TuningQuery.GearTags(pack)
                .Select(t => new BenchChoice(t, string.Join(", ", pack.Gear.Where(g => g.Tag == t).Select(g => live.Names.Gear(g.Id)))))
                .ToList();
        }

        IReadOnlyList<BenchChoice> sources = query.Team switch
        {
            BenchTeams.LegacyKind => game.Store.ListLegacies().Select(l => new BenchChoice(l.LegacyId.ToString(), l.Name)).ToList(),
            BenchTeams.TestShipKind => teams.TestShips().Select(s => new BenchChoice(s.Id, s.Name)).ToList(),
            _ => [],
        };
        BenchSource? source = null;
        var all = new Dictionary<string, string>(errors ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        if (query.RealTeam && query.Source.Length > 0)
        {
            source = query.Team == BenchTeams.LegacyKind
                ? Guid.TryParse(query.Source, out Guid id) ? await teams.Legacy(id, cancellation) : null
                : teams.TestShip(query.Source);
            if (source is null)
            {
                all.TryAdd("source", query.Team == BenchTeams.LegacyKind ? $"No legacy '{query.Source}'." : $"No test ship '{query.Source}'.");
            }
        }

        bool broken = drafts.Current(TuningDrafts.Missions) is not null && drafts.Load(TuningDrafts.Missions).Tuning is null;
        return new BenchInputsView(query, page, objectives, gear, sources, source, all, refused, broken);
    }
}
