using System.Text.Json.Serialization;

namespace Sim.Crew;

public enum CrewStatus
{
    Active,
    Recovering,
    Dead,
}

public sealed class SkillState
{
    public required int Level { get; set; }

    public required double Xp { get; set; }
}

public sealed class CrewMember
{
    public required string Id { get; set; }

    public required int Seniority { get; set; }

    public required Dictionary<string, SkillState> Skills { get; set; }

    public required int Caution { get; set; }

    public required int Discipline { get; set; }

    public required CrewStatus Status { get; set; }

    // One entry per unhealed light injury: the day it heals.
    public required List<int> LightInjuriesHealAtDay { get; set; }

    // The day a recovering member returns to duty.
    public required int? ReturnsAtDay { get; set; }

    public required int Loyalty { get; set; }

    public required int AwayMissions { get; set; }

    // 0 to 100 by the default tuning (1e part two). Changed only through the crew module.
    public required int Morale { get; set; }

    // The cycle of the last mission they were sent on, or null: a fit member not sent in the cycle just ended rests.
    public required int? LastSentCycle { get; set; }
}

public sealed class CrewState
{
    [JsonRequired]
    public List<CrewMember> Members { get; set; } = new();
}
