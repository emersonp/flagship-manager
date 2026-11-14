using System.Globalization;
using Persistence;
using Sim.Core;
using Sim.Core.Content;
using Sim.Crew;
using Sim.Missions;

namespace Web.Bridge;

public static class BridgeReader
{
    // Pure: no store, no clock. cycleEvents are this cycle's decoded events in sequence order.
    public static BridgeView Read(LegacyRecord record, ContentPack pack, IReadOnlyList<IEvent> cycleEvents)
    {
        LegacyState state = record.State;
        IReadOnlyList<CrewRow> roster = Roster(state);
        SiteState? site = state.TryModule(MissionsModule.Name, out MissionsState? missions) ? missions.Site : null;
        CyclePhase phase = state.Phase;

        bool showSite = site is not null;
        bool showBriefing = site is not null && phase == CyclePhase.Briefing;
        bool showMission = site?.Mission is not null && phase is CyclePhase.Resolution or CyclePhase.Aftermath;
        bool showAftermath = site is not null && phase is CyclePhase.Aftermath or CyclePhase.Advance;

        List<ObjectiveView> objectives = site is null ? [] : site.Objectives.Select(o => Objective(pack, site, o)).ToList();
        return new BridgeView(
            TopBar(record, pack),
            roster,
            showSite ? new SiteView(site!.AssignmentId, objectives) : null,
            showBriefing ? Briefing(pack, site!, objectives, roster) : null,
            showMission ? Mission(site!.Mission!, cycleEvents) : null,
            showAftermath ? Aftermath(site!, cycleEvents) : null);
    }

    private static TopBarView TopBar(LegacyRecord record, ContentPack pack)
    {
        string? warning = record.ContentPackId == pack.Id && record.ContentPackVersion == pack.Version
            ? null
            : $"Legacy uses pack {record.ContentPackId} version {record.ContentPackVersion}, and pack {pack.Id} version {pack.Version} is loaded.";
        return new TopBarView(record.Name, record.State.Week, record.State.Cycle, record.State.Phase, warning);
    }

    private static IReadOnlyList<CrewRow> Roster(LegacyState state)
    {
        if (!state.TryModule(CrewModule.Name, out CrewState? crew))
        {
            return [];
        }

        // Stable sort: the dead go last, the rest keep roster order.
        return crew.Members.OrderBy(m => m.Status == CrewStatus.Dead).Select(Row).ToList();
    }

    private static CrewRow Row(CrewMember member)
    {
        int injuries = member.LightInjuriesHealAtWeek.Count;
        List<SkillCell> skills = member.Skills
            .OrderBy(s => s.Key, StringComparer.Ordinal)
            .Select(s => new SkillCell(s.Key, s.Value.Level, Math.Max(0, s.Value.Level - injuries)))
            .ToList();
        return new CrewRow(
            member.Id,
            member.Seniority,
            member.Status.ToString(),
            member.Status == CrewStatus.Active,
            skills,
            member.LightInjuriesHealAtWeek.ToList(),
            member.ReturnsAtWeek,
            member.Loyalty,
            member.AwayMissions);
    }

    private static ObjectiveView Objective(ContentPack pack, SiteState site, SiteObjective objective)
    {
        ObjectiveDef template = pack.Objectives.Single(o => o.Id == objective.TemplateId);
        // Every skill an encounter the template can draw is checked with, either approach.
        List<string> skills = pack.Encounters
            .Where(e => template.Stages.Any(s => s.DrawTags.All(e.Tags.Contains)))
            .SelectMany(e => new[] { e.Careful.Skill, e.Fast.Skill })
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();
        return new ObjectiveView(
            objective.Id,
            objective.TemplateId,
            skills,
            template.TeamSizeMin,
            template.TeamSizeMax,
            template.UnattendedDifficulty,
            site.Briefing?.ObjectiveId == objective.Id);
    }

    private static BriefingView Briefing(ContentPack pack, SiteState site, IReadOnlyList<ObjectiveView> objectives, IReadOnlyList<CrewRow> roster)
    {
        AssignmentDef assignment = pack.Assignments.Single(a => a.Id == site.AssignmentId);
        return new BriefingView(
            objectives,
            roster.Where(c => c.Status != nameof(CrewStatus.Dead)).ToList(),
            pack.Gear.Select(g => new GearChoice(g.Id, g.Tag)).ToList(),
            assignment.LoadoutLimit);
    }

    private static MissionView Mission(MissionInFlight mission, IReadOnlyList<IEvent> cycleEvents)
    {
        MissionState state = mission.State;
        List<MissionBeat> beats = cycleEvents.OfType<MissionBeat>().Where(b => b.MissionId == mission.Id).OrderBy(b => b.Index).ToList();
        int momentum = state.Momentum;
        int strain = state.Strain;
        foreach (Beat beat in beats.Select(b => b.Beat))
        {
            // The latest beat that reports the pair wins; the state's own values stand when none does.
            switch (beat)
            {
                case StageBeat s:
                    (momentum, strain) = (s.Momentum, s.Strain);
                    break;
                case CheckBeat c:
                    (momentum, strain) = (c.Momentum, c.Strain);
                    break;
            }
        }

        if (beats.Count == 0)
        {
            (momentum, strain) = (state.Momentum, state.Strain);
        }

        CallView? call = state.Pending is { } pending
            ? new CallView(pending.CallId, pending.DecisionId, pending.Stakes, pending.Options.Select(o => o.ToString()).ToList(), pending.LeadPrefers.ToString())
            : null;
        return new MissionView(
            mission.Id,
            mission.ObjectiveId,
            state.Team.Select(m => m.Member.Id).ToList(),
            state.LeadId,
            momentum,
            strain,
            beats.Select(b => BeatLines.Line(b.Index, b.Beat)).ToList(),
            call,
            state.Result?.Outcome.ToString());
    }

    private static AftermathView Aftermath(SiteState site, IReadOnlyList<IEvent> cycleEvents)
    {
        string? outcome = null;
        var changes = new List<CrewChange>();
        var unattended = new List<UnattendedView>();
        foreach (IEvent e in cycleEvents)
        {
            switch (e)
            {
                case MissionEnded ended:
                    outcome = ended.Result.Outcome.ToString();
                    changes.AddRange(ended.Result.Crew.Where(c => c.Loyalty != 0).Select(c => new CrewChange(c.Id, $"{c.Id} loyalty {c.Loyalty.ToString("+0;-0", CultureInfo.InvariantCulture)}.")));
                    break;
                case SkillRaised raised:
                    changes.Add(new CrewChange(raised.CrewId, $"{raised.CrewId} raises {raised.SkillId} to level {raised.Level}."));
                    break;
                case CrewInjured injured:
                    string returns = injured.ReturnsAtWeek is { } week ? $", returns at week {week}" : string.Empty;
                    changes.Add(new CrewChange(injured.CrewId, $"{injured.CrewId} injured: {BeatLines.Words(injured.Outcome)}, light injuries {injured.LightInjuries}{returns}."));
                    break;
                case CrewDied died:
                    changes.Add(new CrewChange(died.CrewId, $"{died.CrewId} died on {died.MissionId}."));
                    break;
                case CrewRecovered recovered:
                    changes.Add(new CrewChange(recovered.CrewId, $"{recovered.CrewId} recovered."));
                    break;
                case ObjectiveUnattended u:
                    unattended.Add(new UnattendedView(u.ObjectiveId, u.TemplateId, u.Roll, u.Target, u.Succeeded));
                    break;
            }
        }

        return new AftermathView(outcome ?? site.Mission?.State.Result?.Outcome.ToString(), changes, unattended);
    }
}
