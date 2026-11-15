using System.Text.Json;
using Sim.Core;
using Sim.Core.Content;

namespace Sim.Missions;

public enum MissionPhase
{
    Check,
    Decision,
    Strain,
    Advance,
}

public sealed class MemberState
{
    public required TeamMember Member { get; set; }

    public required int Injury { get; set; }

    public required MemberStatus Status { get; set; }

    public required Dictionary<string, double> Experience { get; set; }

    public required int Loyalty { get; set; }
}

// A mission in flight (GDD 4.1). Plain data: it carries its own tuning and encounters,
// so it replays the same whatever the content pack says later.
public sealed class MissionState
{
    public required int Version { get; set; }

    public required ulong Seed { get; set; }

    public required string ObjectiveId { get; set; }

    public required int Need { get; set; }

    public required MissionTuning Tuning { get; set; }

    public required List<DrawnEncounter> Encounters { get; set; }

    public required Dictionary<string, EncounterDef> Chains { get; set; }

    public required int Cursor { get; set; }

    public required MissionPhase Phase { get; set; }

    public required int Momentum { get; set; }

    public required int Strain { get; set; }

    public required int Progress { get; set; }

    public required List<MemberState> Team { get; set; }

    public required string LeadId { get; set; }

    public required List<string> GearTags { get; set; }

    public required Parameters Parameters { get; set; }

    public required PendingCall? Pending { get; set; }

    public required MissionResult? Result { get; set; }

    public required bool AbortAsked { get; set; }

    public required bool Chained { get; set; }

    public required int Calls { get; set; }

    public required int Initiatives { get; set; }
}

public static class MissionJson
{
    public const int CurrentVersion = 1;

    public static readonly JsonSerializerOptions Options = SimJson.Options;

    public static string Serialize(MissionState state)
    {
        return JsonSerializer.Serialize(state, Options);
    }

    public static MissionState Deserialize(string json)
    {
        MissionState state = JsonSerializer.Deserialize<MissionState>(json, Options) ?? throw new InvalidOperationException("The mission state is empty.");
        return state.Version == CurrentVersion ? state : throw new InvalidOperationException($"The mission state is version {state.Version}, and this build reads version {CurrentVersion}.");
    }
}
