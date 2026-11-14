using Sim.Core;
using Sim.Core.Content;

namespace Sim.Crew;

[EventType("SkillRaised", 1)]
public sealed record SkillRaised(string CrewId, string SkillId, int Level) : IEvent;

[EventType("CrewInjured", 1)]
public sealed record CrewInjured(string CrewId, MemberStatus Outcome, int LightInjuries, int? ReturnsAtWeek) : IEvent;

[EventType("CrewDied", 1)]
public sealed record CrewDied(string CrewId, string MissionId) : IEvent;

[EventType("CrewRecovered", 1)]
public sealed record CrewRecovered(string CrewId) : IEvent;

public static class CrewModule
{
    private const string ModuleName = "crew";

    private const int SubscriberOrder = 100;

    public static void Register(EventBus bus, ContentPack pack)
    {
        CrewTuning tuning = pack.CrewTuning;
        bus.Module<CrewState>(ModuleName);
        // The factory reads the state on every call: the state may not exist until founding.
        bus.Provide<IRosterView>(state => new RosterView(state.Module<CrewState>(ModuleName)));
        bus.Subscribe<LegacyFounded>(SubscriberOrder, "crew-found", (_, context) => OnFounded(pack, context));
        bus.Subscribe<MissionEnded>(SubscriberOrder, "crew-mission-ended", (e, context) => OnMissionEnded(tuning, e, context));
        bus.Subscribe<WeekAdvanced>(SubscriberOrder, "crew-week", (e, context) => OnWeekAdvanced(e, context));
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
        }).ToList();
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
                continue;
            }

            GainExperience(tuning, member, result, context);
            ApplyOutcome(tuning, member, result, ended.MissionId, week, context);
            member.Loyalty = Math.Clamp(member.Loyalty + (result.Loyalty * tuning.LoyaltyPerPoint), tuning.LoyaltyMin, tuning.LoyaltyMax);
            member.AwayMissions++;
        }
    }

    private static void GainExperience(CrewTuning tuning, CrewMember member, CrewResult result, IBusContext context)
    {
        foreach ((string skillId, double amount) in result.Experience)
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

        for (int i = 0; i < result.Injury; i++)
        {
            member.LightInjuriesHealAtWeek.Add(week + tuning.LightInjuryWeeks);
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

    private static void OnWeekAdvanced(WeekAdvanced advanced, IBusContext context)
    {
        foreach (CrewMember member in context.Module<CrewState>().Members)
        {
            if (member.Status == CrewStatus.Dead)
            {
                continue;
            }

            bool healed = member.LightInjuriesHealAtWeek.RemoveAll(due => due <= advanced.Week) > 0;
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
