using GameHost;
using Persistence;
using Sim.Content;
using Sim.Core;
using Sim.Core.Content;
using Sim.Missions;
using SimRunner;

namespace Web.Tests;

internal static class BridgePack
{
    public static ContentPack Load()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        string root = directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        return ContentLoader.LoadDirectory(Path.Combine(root, "content")).Pack ?? throw new InvalidOperationException("The content pack did not load.");
    }

    // A 1d20 check makes injuries and deaths common inside a short run.
    public static ContentPack Wide(ContentPack pack)
    {
        return pack with { Tuning = pack.Tuning with { CheckDice = new DiceDef(1, 20) } };
    }
}

// A legacy played one automatic-captain command at a time, keeping every published event.
internal sealed class Played(ContentPack pack, ulong seed)
{
    public Game Game { get; } = Game.Build(pack);

    public LegacyState State { get; } = new() { LegacyId = new Guid((int)(seed >> 32), 0, 0, BitConverter.GetBytes(seed)), Seed = seed };

    public List<PublishedEvent> Events { get; } = [];

    public ContentPack Pack { get; } = pack;

    public void Step()
    {
        Events.AddRange(AutoCaptain.Step(Game, State, Pack));
    }

    // Steps until the condition holds, within a bound so a bad search fails instead of hanging.
    public bool StepUntil(Func<bool> condition, int limit = 4000)
    {
        for (int i = 0; i < limit && !condition(); i++)
        {
            Step();
        }

        return condition();
    }

    public MissionInFlight? Mission => State.TryModule(MissionsModule.Name, out MissionsState? m) ? m.Site?.Mission : null;

    public IReadOnlyList<IEvent> CycleEvents()
    {
        return Events.Where(e => e.Cycle == State.Cycle).Select(e => e.Event).ToList();
    }

    public LegacyRecord Record(string name = "LEGACY")
    {
        return new LegacyRecord(State, Pack.Id, Pack.Version, name, DateTimeOffset.UnixEpoch);
    }
}

// The pack with every authored item named NAME_PLACEHOLDER_ plus its id, so a test can tell a name from an id.
internal static class NamedPack
{
    public const string Prefix = "NAME_PLACEHOLDER_";

    public static ContentPack Of(ContentPack pack)
    {
        return pack with
        {
            Skills = pack.Skills.Select(s => s with { Name = Prefix + s.Id }).ToList(),
            Crew = pack.Crew.Select(c => c with { Name = Prefix + c.Id }).ToList(),
            Gear = pack.Gear.Select(g => g with { Name = Prefix + g.Id }).ToList(),
            Objectives = pack.Objectives.Select(o => o with { Name = Prefix + o.Id }).ToList(),
            Assignments = pack.Assignments.Select(a => a with { Name = Prefix + a.Id }).ToList(),
            Encounters = pack.Encounters.Select(e => e with { Name = Prefix + e.Id, Decision = e.Decision is null ? null : e.Decision with { Name = Prefix + e.Decision.Id } }).ToList(),
        };
    }
}
