namespace Sim.Core.Content;

// Every number the mission resolver uses (GDD 4.7). A mission copies this at its start.
public sealed record MissionTuning
{
    public required int CheckTarget { get; init; }

    public required int TierCritical { get; init; }

    public required int TierSuccess { get; init; }

    public required int TierPartial { get; init; }

    public required int TierFailure { get; init; }

    public required int GearBonus { get; init; }

    public required int AssistDivisor { get; init; }

    public required int Cap { get; init; }

    public required int MomentumCritical { get; init; }

    public required int MomentumFastSuccess { get; init; }

    public required int StrainPartial { get; init; }

    public required int StrainFailure { get; init; }

    public required int StrainDisaster { get; init; }

    public required int StrainReliefOnSuccess { get; init; }

    public required int SkipAtMomentum { get; init; }

    public required int SkipCost { get; init; }

    public required int AbortCallAt { get; init; }

    public required int CollapseAt { get; init; }

    public required int ReachMargin { get; init; }

    public required int SupervisorPenalty { get; init; }

    public required double CallBase { get; init; }

    public required double CautionWeight { get; init; }

    public required double DisciplineWeight { get; init; }

    public required double AutonomyWeight { get; init; }

    public required double BoldCautionWeight { get; init; }

    public required double BoldPriorityWeight { get; init; }

    public required int WithdrawOfferedAtStakes { get; init; }

    public required int AbortStakes { get; init; }

    public required int BackedLoyalty { get; init; }

    public required int OverruledLoyalty { get; init; }

    public required double ExperienceClose { get; init; }

    public required double ExperienceNear { get; init; }

    public required double ExperienceFar { get; init; }

    public required int CloseMargin { get; init; }

    public required int NearMargin { get; init; }

    public required int TrivialAtMargin { get; init; }

    public required double AssistShare { get; init; }

    public required int MentorGap { get; init; }

    public required double MentorBonus { get; init; }

    public required int LightBelow { get; init; }

    public required int DeadBelow { get; init; }

    public required int IncapacitatedBelow { get; init; }

    public required int MedicalBonus { get; init; }

    public required int MaxLight { get; init; }

    public required int PartialAtProgress { get; init; }
}

public sealed record BenchTeamDef(int Size, int SkillMin, int SkillMax, int Seniors, int SeniorSkillMin, int SeniorSkillMax);

// Shapes the bench's generated teams. Never copied into a mission.
public sealed record BenchTuning(BenchTeamDef Rookie, BenchTeamDef Mixed, BenchTeamDef Veteran);
