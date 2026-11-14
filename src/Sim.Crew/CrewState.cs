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

    // One entry per unhealed light injury: the week it heals.
    public required List<int> LightInjuriesHealAtWeek { get; set; }

    public required int? ReturnsAtWeek { get; set; }

    public required int Loyalty { get; set; }

    public required int AwayMissions { get; set; }
}

public sealed class CrewState
{
    [JsonRequired]
    public List<CrewMember> Members { get; set; } = new();
}
