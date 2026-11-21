using Sim.Core;
using Sim.Core.Content;
using Sim.Crew;
using Sim.Traits;

namespace Web.Forms;

// A test ship as a legacy in memory (1f part one, Task 5; design 4.5): founded on the live pack's game as LegacySession.CreateLegacy
// founds one, but never stored, then its crew and traits set from the ship. Its views are then the crew and traits modules' own,
// through game.Bus.View<T>(state), as TestShipCapture reads a legacy, so the bench (and slice 1f-previews) build a test ship's team
// through LaunchTeam without a second copy of the roster or the trait bonuses. Only what the launch reads is set: the roster, each
// member's morale, loyalty, skill levels, and fitness, the traits, and the day and cycle. History, the chronicle, and storylet
// arrivals are as founding leaves them, not the ship's: nothing the launch reads uses them.
internal static class TestShipLegacy
{
    // Fixed so that the same ship founds the same state; nothing rolled at founding reaches what is set from the ship.
    private const ulong Seed = 1;

    public static LegacyState Found(LivePack live, TestShipDef ship)
    {
        var state = new LegacyState { LegacyId = Guid.Empty, Seed = Seed };
        // The events are dropped: nothing is published anywhere a legacy is kept.
        _ = live.Game.Bus.Send(state, new FoundLegacy());
        state.Day = ship.Date;
        state.Cycle = ship.Cycle;

        Dictionary<string, TestShipCrew> changes = ship.Crew.ToDictionary(c => c.Id, StringComparer.Ordinal);
        foreach (CrewMember member in state.Module<CrewState>(CrewModule.Name).Members)
        {
            if (!changes.TryGetValue(member.Id, out TestShipCrew? change))
            {
                continue;
            }

            member.Morale = change.Morale ?? member.Morale;
            member.Loyalty = change.Loyalty ?? member.Loyalty;
            foreach ((string skill, int level) in change.Skills ?? new Dictionary<string, int>())
            {
                member.Skills[skill] = new SkillState { Level = level, Xp = member.Skills.GetValueOrDefault(skill)?.Xp ?? 0 };
            }

            // Play has no "away": someone not aboard cannot be sent, as someone unfit cannot, so both are Recovering, with no day
            // they return on (the ship does not say one). Dead is off the roster.
            member.Status = change.Alive == false ? CrewStatus.Dead
                : change.Fit == false || change.Aboard == false ? CrewStatus.Recovering
                : CrewStatus.Active;
        }

        // The ship's traits are the whole of them, in its order: each changed as many days and cycles ago as the ship says.
        TraitsState traits = state.Module<TraitsState>(TraitsModule.Name);
        traits.Traits = ship.Traits.Where(t => t.Value > 0).Select(t => new StoredTrait
        {
            Trait = t.Trait,
            CrewId = t.On,
            TargetId = t.Toward,
            Value = t.Value,
            ChangedAtDay = ship.Date - t.DaysAgo,
            ChangedAtCycle = ship.Cycle - t.CyclesAgo,
            EncounterId = null,
        }).ToList();
        return state;
    }
}
