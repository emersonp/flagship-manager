using System.Text.Json.Serialization;
using Sim.Core;

namespace Sim.Missions;

public sealed record Briefing(string ObjectiveId, IReadOnlyList<string> CrewIds, string LeadId, IReadOnlyList<string> GearIds, Parameters Parameters);

// The mission stays here, finished, until the next cycle's site replaces it.
public sealed class MissionInFlight
{
    public required string Id { get; set; }

    public required string ObjectiveId { get; set; }

    public required MissionState State { get; set; }

    public required int BeatCount { get; set; }
}

public sealed class SiteState
{
    public required int Cycle { get; set; }

    public required string AssignmentId { get; set; }

    public required List<SiteObjective> Objectives { get; set; }

    public required Briefing? Briefing { get; set; }

    public required MissionInFlight? Mission { get; set; }
}

public sealed class MissionsState
{
    [JsonRequired]
    public SiteState? Site { get; set; }
}
