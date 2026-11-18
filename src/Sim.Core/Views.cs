namespace Sim.Core;

// Read-only crew roster for modules that must not reference the crew module.
public interface IRosterView
{
    // Living crew ids in roster order.
    IReadOnlyList<string> Roster { get; }

    bool IsFit(string crewId);

    // Null for an unknown or dead crew member.
    TeamMember? Member(string crewId);

    // A living crew member's stats, read by storylet requirements (1e part two); 0 for an unknown or dead one.
    int Morale(string crewId);

    int Loyalty(string crewId);
}

// Read-only traits (slice 1e, part three) for modules that must not reference the traits module. CrewId null: the ship.
public interface ITraitView
{
    // 0 when it is not held.
    int Value(string trait, string? crewId, string? targetId = null);

    // The ship's traits (crewId null), or a crew member's, pairs they hold included, in the order first set.
    IReadOnlyList<TraitHeld> Held(string? crewId);

    // The encounter that last changed it, or null.
    string? EncounterId(string trait, string? crewId, string? targetId = null);

    // The check bonuses this crew member's traits give on this team (design 4.6): one entry for each trait, a pair's withTarget
    // bonus only when its target is on the team.
    IReadOnlyList<TraitBonus> Bonuses(string crewId, IReadOnlyCollection<string> team);

    // Every launch rule that could fire for these crew, with the condition it needs, in the order the rules run at launch. The
    // traits module applies exactly the entries whose condition the team meets, so the briefing and the launch never disagree.
    IReadOnlyList<LaunchPreview> Preview(IReadOnlyList<string> roster);
}

// Each living crew member's recent cycles (1e part four, design 4.8), served by the storylets module so part five's chronicle can
// read it. Read in the cycle after: the cycle whose aftermath last ended is the last one.
public interface IHistoryView
{
    // How many of the last `cycles` cycles this kind happened to this crew member in; 0 for an unknown or dead one.
    int Count(string crewId, string kind, int cycles);
}

// What the history records of a crew member in one cycle (design 4.3).
public static class HistoryKinds
{
    // On the mission and came home.
    public const string Sent = "sent";

    // Injured or incapacitated on it.
    public const string Hurt = "hurt";

    // Not sent, and fit when the next itinerary opened.
    public const string Rested = "rested";

    // Someone died on the cycle's mission, and this one is alive, sent or not: part two's meaning, kept by the author (2026-11-18).
    public const string LostCrewmate = "lost_crewmate";

    // On a mission where someone died, and came home (design 4.3's narrower meaning, kept beside lost_crewmate by the author).
    public const string LostTeammate = "lost_teammate";

    // On a mission whose objective failed or was abandoned, and came home. A death alone does not lose a mission.
    public const string LostMission = "lost_mission";

    public static readonly IReadOnlyList<string> All = [Sent, Hurt, Rested, LostCrewmate, LostTeammate, LostMission];
}
