using Sim.Content;
using Sim.Core;
using Sim.Core.Content;
using Sim.Missions;
using Web.Forms;

namespace Web.Tuning;

// One member a real team can be picked from, with why the launch would refuse them, or null.
public sealed record BenchCandidate(string Id, string Name, string? Refused);

// Where a real team comes from: a legacy (Kind "legacy", Id its id) or a test ship (Kind "testship", Id the ship's id), and who is on
// its roster, in roster order.
public sealed record BenchSource(string Kind, string Id, string Name, IReadOnlyList<BenchCandidate> Candidates);

// The bench's real teams (1f part one, Task 6; design 4.5): a legacy's roster or a test ship's crew, each member built by the launch's
// own code (LaunchTeam) on the crew and traits modules' own views. A legacy is read through LegacySession.Read, which holds its lock
// only while it loads, and is never written: nothing is sent, published, or stored. A test ship is read as the editor's previews read
// it and founded in memory (TestShipLegacy).
public sealed class BenchTeams(LegacySession sessions, ContentHost host, ContentEditor editor)
{
    public const string LegacyKind = "legacy";
    public const string TestShipKind = "testship";

    // The legacy's roster, or null when there is no such legacy (or no live pack).
    public async Task<BenchSource?> Legacy(Guid legacyId, CancellationToken cancellation)
    {
        if (host.Current is not { } live || await sessions.Read(live, legacyId, cancellation) is not { } legacy)
        {
            return null;
        }

        return Source(live, LegacyKind, legacyId.ToString(), legacy.Record.Name, legacy.State);
    }

    // The ship's crew, or null when there is no such ship (or no live pack).
    public BenchSource? TestShip(string shipId)
    {
        if (host.Current is not { } live || Ship(live, shipId) is not { } ship)
        {
            return null;
        }

        return Source(live, TestShipKind, ship.Id, ship.Name, TestShipLegacy.Found(live, ship));
    }

    // The test ships a team can come from, as the editor's previews read them: each one's id and name.
    public IReadOnlyList<(string Id, string Name)> TestShips()
    {
        return host.Current is not { } live ? [] : ContentLoader.LoadTestShips(editor.Read().Files, live.Pack).Ships.Select(s => (s.Id, s.Name)).ToList();
    }

    // The team the launch would send for these picks, built once for a run (every seed uses the same team). Null with the refusal
    // when the source is not found, the team is empty, or the launch would refuse the picks (a member listed twice, not on the roster,
    // or not fit, or a lead not picked), in the launch's words.
    public async Task<(Team? Team, string? Refused)> Build(string kind, string id, IReadOnlyList<string> crewIds, string leadId, CancellationToken cancellation)
    {
        if (host.Current is not { } live)
        {
            return (null, "No content pack is live.");
        }

        LegacyState? state = kind switch
        {
            LegacyKind => Guid.TryParse(id, out Guid legacyId) && await sessions.Read(live, legacyId, cancellation) is { } read ? read.State : null,
            TestShipKind => Ship(live, id) is { } ship ? TestShipLegacy.Found(live, ship) : null,
            _ => null,
        };
        if (state is null)
        {
            return (null, kind == TestShipKind ? $"No test ship '{id}'." : kind == LegacyKind ? $"No legacy '{id}'." : $"No team source of kind '{kind}'.");
        }

        if (crewIds.Count == 0)
        {
            return (null, "Nobody is picked.");
        }

        IRosterView roster = live.Game.Bus.View<IRosterView>(state);
        if (LaunchTeam.PicksRefusal(roster, crewIds, leadId) is { } refused)
        {
            return (null, refused);
        }

        return (LaunchTeam.Build(roster, live.Game.Bus.View<ITraitView>(state), crewIds, leadId), null);
    }

    private static BenchSource Source(LivePack live, string kind, string id, string name, LegacyState state)
    {
        IRosterView roster = live.Game.Bus.View<IRosterView>(state);
        List<BenchCandidate> candidates = roster.Roster.Select(c => new BenchCandidate(c, live.Names.Crew(c), LaunchTeam.Refusal(roster, c))).ToList();
        return new BenchSource(kind, id, name, candidates);
    }

    // The ships as the editor's previews read them: the files on disk, read against the live pack.
    private TestShipDef? Ship(LivePack live, string shipId)
    {
        return ContentLoader.LoadTestShips(editor.Read().Files, live.Pack).Ships.FirstOrDefault(s => s.Id == shipId);
    }
}
