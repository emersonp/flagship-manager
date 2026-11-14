using System.Text.Json;
using System.Text.Json.Serialization;
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

    public int Injury { get; set; }

    public MemberStatus Status { get; set; }

    public double Experience { get; set; }

    public int Loyalty { get; set; }
}

// A mission in flight (GDD 4.1). Plain data: it carries its own tuning and encounters,
// so it replays the same whatever the content pack says later.
public sealed class MissionState
{
    public ulong Seed { get; set; }

    public string ObjectiveId { get; set; } = "";

    public int Need { get; set; }

    public required MissionTuning Tuning { get; set; }

    public List<DrawnEncounter> Encounters { get; set; } = [];

    public Dictionary<string, EncounterDef> Chains { get; set; } = [];

    public int Cursor { get; set; }

    public MissionPhase Phase { get; set; }

    public int Momentum { get; set; }

    public int Strain { get; set; }

    public int Progress { get; set; }

    public List<MemberState> Team { get; set; } = [];

    public string LeadId { get; set; } = "";

    public List<string> GearTags { get; set; } = [];

    public Parameters Parameters { get; set; } = new();

    public PendingCall? Pending { get; set; }

    public MissionResult? Result { get; set; }

    public bool AbortAsked { get; set; }

    public bool Chained { get; set; }

    public int Calls { get; set; }

    public int Notices { get; set; }
}

public static class MissionJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };

    public static string Serialize(MissionState state)
    {
        return JsonSerializer.Serialize(state, Options);
    }

    public static MissionState Deserialize(string json)
    {
        return JsonSerializer.Deserialize<MissionState>(json, Options) ?? throw new InvalidOperationException("The mission state is empty.");
    }
}
