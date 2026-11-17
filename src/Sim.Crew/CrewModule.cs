using Sim.Core;
using Sim.Core.Content;

namespace Sim.Crew;

[EventType("SkillRaised", 1)]
public sealed record SkillRaised(string CrewId, string SkillId, int Level) : IEvent;

[EventType("CrewInjured", 1)]
public sealed record CrewInjured(string CrewId, MemberStatus Outcome, int LightInjuries, int? ReturnsAtWeek) : IEvent;

[EventType("CrewRecovered", 1)]
public sealed record CrewRecovered(string CrewId) : IEvent;

public static class CrewModule
{
    public const string Name = "crew";

    private const int SubscriberOrder = 100;

    public static void Register(EventBus bus, ContentPack pack)
    {
        CrewTuning tuning = pack.CrewTuning;
        bus.Module<CrewState>(Name);
        // The factory reads the state on every call: the state may not exist until founding.
        bus.Provide<IRosterView>(state => new RosterView(state.Module<CrewState>(Name), tuning));
        bus.Guard<AdjustLoyalty>(SubscriberOrder, Name, (command, context) => IsLiving(context, command.CrewId) ? null : $"Crew member '{command.CrewId}' is not on the roster.");
        bus.Handle<AdjustLoyalty>((command, context) => OnAdjustLoyalty(tuning, command, context));
        bus.Guard<AdjustMorale>(SubscriberOrder, Name, (command, context) => IsLiving(context, command.CrewId) ? null : $"Crew member '{command.CrewId}' is not on the roster.");
        bus.Handle<AdjustMorale>((command, context) => ChangeMorale(tuning, Find(context, command.CrewId), command.Delta, MoraleCauses.Answer, context));
        bus.Subscribe<LegacyFounded>(SubscriberOrder, "crew-found", (_, context) => OnFounded(pack, context));
        bus.Subscribe<MissionEnded>(SubscriberOrder, "crew-mission-ended", (e, context) => OnMissionEnded(tuning, e, context));
        bus.Subscribe<WeekAdvanced>(SubscriberOrder, "crew-week", (e, context) => OnWeekAdvanced(tuning, e, context));
    }

    private static void OnFounded(ContentPack pack, IBusContext context)
    {
        CrewState state = context.Module<CrewState>();
        state.Members = pack.Crew.Select(def => new CrewMember
        {
            Id = def.Id,
            Seniority = def.Seniority,
            Skills = def.Skills.ToDictionary(s => s.Key, s => new SkillState { Level = s.Value, Xp = 0 }),
            Caution = def.Caution,
            Discipline = def.Discipline,
            Status = CrewStatus.Active,
            LightInjuriesHealAtWeek = new List<int>(),
            ReturnsAtWeek = null,
            Loyalty = pack.CrewTuning.StartingLoyalty,
            AwayMissions = 0,
            Morale = pack.CrewTuning.MoraleResting,
            LastSentCycle = null,
        }).ToList();
    }

    private static bool IsLiving(IBusContext context, string crewId)
    {
        return context.Module<CrewState>().Members.Any(m => m.Id == crewId && m.Status != CrewStatus.Dead);
    }

    // Clamped as a mission's loyalty is, but taken as written: an answer's loyalty is not counted in points.
    private static void OnAdjustLoyalty(CrewTuning tuning, AdjustLoyalty command, IBusContext context)
    {
        CrewMember member = context.Module<CrewState>().Members.Single(m => m.Id == command.CrewId);
        int from = member.Loyalty;
        member.Loyalty = Math.Clamp(from + command.Delta, tuning.LoyaltyMin, tuning.LoyaltyMax);
        if (member.Loyalty != from)
        {
            context.Publish(new LoyaltyChanged(member.Id, from, member.Loyalty));
        }
    }

    private static void OnMissionEnded(CrewTuning tuning, MissionEnded ended, IBusContext context)
    {
        CrewState state = context.Module<CrewState>();
        int week = context.State.Week;
        foreach (CrewResult result in ended.Result.Crew)
        {
            CrewMember? member = state.Members.FirstOrDefault(m => m.Id == result.Id);
            if (member is null)
            {
                throw new InvalidOperationException($"Mission {ended.MissionId} reported crew member '{result.Id}', who is not on the roster.");
            }

            GainExperience(tuning, member, result, context);
            ApplyOutcome(tuning, member, result, ended.MissionId, week, context);
            member.Loyalty = Math.Clamp(member.Loyalty + (result.Loyalty * tuning.LoyaltyPerPoint), tuning.LoyaltyMin, tuning.LoyaltyMax);
            member.AwayMissions++;
            member.LastSentCycle = context.State.Cycle;
        }

        // Morale after everything else (design 4.1): each who came home takes the result, then their hurt; then each death
        // reaches every living member, the team included.
        foreach (CrewResult result in ended.Result.Crew.Where(r => r.Status != MemberStatus.Dead))
        {
            CrewMember member = Find(context, result.Id);
            ChangeMorale(tuning, member, tuning.MoraleResult[ended.Result.Outcome], MoraleCauses.Mission, context);
            int hurt = result.Status == MemberStatus.Incapacitated ? tuning.MoraleIncapacitated : result.Injury * tuning.MoraleHurt;
            ChangeMorale(tuning, member, hurt, MoraleCauses.Hurt, context);
        }

        int deaths = ended.Result.Crew.Count(r => r.Status == MemberStatus.Dead);
        for (int i = 0; i < deaths; i++)
        {
            foreach (CrewMember living in state.Members.Where(m => m.Status != CrewStatus.Dead))
            {
                ChangeMorale(tuning, living, tuning.MoraleCrewmateDied, MoraleCauses.Death, context);
            }
        }
    }

    private static CrewMember Find(IBusContext context, string crewId)
    {
        return context.Module<CrewState>().Members.Single(m => m.Id == crewId);
    }

    // Every change of morale: clamped to the tuning, and published only when it moved.
    private static void ChangeMorale(CrewTuning tuning, CrewMember member, int delta, string cause, IBusContext context)
    {
        int from = member.Morale;
        member.Morale = Math.Clamp(from + delta, tuning.MoraleMin, tuning.MoraleMax);
        if (member.Morale != from)
        {
            context.Publish(new MoraleChanged(member.Id, from, member.Morale, cause));
        }
    }

    // Rest lifts a fit member who was not sent up towards resting; drift then moves everyone towards it. Neither passes it.
    private static void RestAndDrift(CrewTuning tuning, CrewMember member, int endedCycle, IBusContext context)
    {
        int resting = tuning.MoraleResting;
        if (member.Status == CrewStatus.Active && member.LastSentCycle != endedCycle && member.Morale < resting)
        {
            ChangeMorale(tuning, member, Math.Min(tuning.MoraleRested, resting - member.Morale), MoraleCauses.Rest, context);
        }

        int gap = resting - member.Morale;
        ChangeMorale(tuning, member, Math.Sign(gap) * Math.Min(tuning.MoraleDrift, Math.Abs(gap)), MoraleCauses.Drift, context);
    }

    private static void GainExperience(CrewTuning tuning, CrewMember member, CrewResult result, IBusContext context)
    {
        foreach ((string skillId, double amount) in result.Experience.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            if (!member.Skills.TryGetValue(skillId, out SkillState? skill))
            {
                continue;
            }

            skill.Xp += amount;
            while (skill.Level < tuning.MaxLevel && skill.Xp >= tuning.XpBase * (skill.Level + 1))
            {
                skill.Xp -= tuning.XpBase * (skill.Level + 1);
                skill.Level++;
                context.Publish(new SkillRaised(member.Id, skillId, skill.Level));
            }

            if (skill.Level >= tuning.MaxLevel)
            {
                skill.Xp = 0;
            }
        }
    }

    private static void ApplyOutcome(CrewTuning tuning, CrewMember member, CrewResult result, string missionId, int week, IBusContext context)
    {
        if (member.Status == CrewStatus.Dead)
        {
            return;
        }

        if (result.Status != MemberStatus.Dead)
        {
            for (int i = 0; i < result.Injury; i++)
            {
                member.LightInjuriesHealAtWeek.Add(week + tuning.LightInjuryWeeks);
            }
        }

        switch (result.Status)
        {
            case MemberStatus.Dead:
                member.Status = CrewStatus.Dead;
                context.Publish(new CrewDied(member.Id, missionId));
                return;
            case MemberStatus.Incapacitated:
                member.Status = CrewStatus.Recovering;
                member.ReturnsAtWeek = week + tuning.IncapacitatedWeeks;
                context.Publish(new CrewInjured(member.Id, MemberStatus.Incapacitated, result.Injury, member.ReturnsAtWeek));
                return;
        }

        if (result.Injury > 0)
        {
            context.Publish(new CrewInjured(member.Id, MemberStatus.Active, result.Injury, null));
        }
    }

    private static void OnWeekAdvanced(CrewTuning tuning, WeekAdvanced advanced, IBusContext context)
    {
        foreach (CrewMember member in context.Module<CrewState>().Members)
        {
            if (member.Status == CrewStatus.Dead)
            {
                continue;
            }

            // Before healing: a member who comes back this week was not resting through it. The cycle has already turned.
            RestAndDrift(tuning, member, context.State.Cycle - 1, context);
            bool healed =member.LightInjuriesHealAtWeek.RemoveAll(due => due <= advanced.Week) > 0;
            if (member.Status == CrewStatus.Recovering && member.ReturnsAtWeek <= advanced.Week)
            {
                member.Status = CrewStatus.Active;
                member.ReturnsAtWeek = null;
                healed = true;
            }

            if (healed)
            {
                context.Publish(new CrewRecovered(member.Id));
            }
        }
    }
}
