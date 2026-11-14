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
        MissionView? mission = showMission ? Mission(site!.Mission!, site.Briefing, cycleEvents) : null;
        return new BridgeView(
            TopBar(record, pack),
            mission is null ? roster : MarkAway(roster, mission.Team, mission.LeadId),
            showSite ? new SiteView(site!.AssignmentId, objectives) : null,
            showBriefing ? Briefing(pack, site!, objectives, roster) : null,
            mission,
            showAftermath ? Aftermath(site!, state, cycleEvents) : null);
    }

    // The away team's rows, with the lead flagged; everyone else unmarked. Used for a lead other than the stored one too.
    public static IReadOnlyList<CrewRow> MarkAway(IReadOnlyList<CrewRow> roster, IReadOnlyList<string> team, string leadId)
    {
        return roster.Select(r => r with { IsAway = team.Contains(r.Id), IsLead = team.Contains(r.Id) && r.Id == leadId }).ToList();
    }

    private static TopBarView TopBar(LegacyRecord record, ContentPack pack)
    {
        string? warning = record.ContentPackId == pack.Id && record.ContentPackVersion == pack.Version
            ? null
            : Inv($"Legacy uses pack {record.ContentPackId} version {record.ContentPackVersion}, and pack {pack.Id} version {pack.Version} is loaded.");
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
        // A stored legacy may name a template the loaded pack lacks: the pack warning explains it, so a miss reads as empty.
        ObjectiveDef? template = pack.Objectives.FirstOrDefault(o => o.Id == objective.TemplateId);
        // Every skill any encounter the template can draw is checked with, either approach.
        List<string> skills = template is null ? [] : pack.Encounters
            .Where(e => template.Stages.Any(s => s.DrawTags.All(e.Tags.Contains)))
            .SelectMany(e => new[] { e.Careful.Skill, e.Fast.Skill })
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();
        return new ObjectiveView(
            objective.Id,
            objective.TemplateId,
            skills,
            template?.TeamSizeMin ?? 0,
            template?.TeamSizeMax ?? 0,
            template?.UnattendedDifficulty,
            site.Briefing?.ObjectiveId == objective.Id);
    }

    private static BriefingView Briefing(ContentPack pack, SiteState site, IReadOnlyList<ObjectiveView> objectives, IReadOnlyList<CrewRow> roster)
    {
        AssignmentDef? assignment = pack.Assignments.FirstOrDefault(a => a.Id == site.AssignmentId);
        return new BriefingView(
            objectives,
            roster.Where(c => c.Status != nameof(CrewStatus.Dead)).ToList(),
            pack.Gear.Select(g => new GearChoice(g.Id, g.Tag)).ToList(),
            assignment?.LoadoutLimit ?? 0);
    }

    private static MissionView Mission(MissionInFlight mission, Briefing? briefing, IReadOnlyList<IEvent> cycleEvents)
    {
        MissionState state = mission.State;
        List<MissionBeat> beats = cycleEvents.OfType<MissionBeat>().Where(b => b.MissionId == mission.Id).OrderBy(b => b.Index).ToList();
        CallView? call = state.Pending is { } pending
            ? new CallView(pending.CallId, pending.DecisionId, pending.Stakes, pending.Options.Select(o => o.ToString()).ToList(), pending.LeadPrefers.ToString(), pending.LeadId)
            : null;
        return new MissionView(
            mission.Id,
            mission.ObjectiveId,
            state.Team.Select(m => m.Member.Id).ToList(),
            state.LeadId,
            state.Momentum,
            state.Strain,
            Lines(beats),
            call,
            state.Result?.Outcome.ToString(),
            Gear(mission, briefing, cycleEvents),
            state.Tuning.Cap,
            StartLead(mission, briefing, cycleEvents) ?? state.LeadId);
    }

    // The log's stage beats are numbered from 1 in order; the beat's own cursor also counts skipped slots.
    private static List<LogLine> Lines(IReadOnlyList<MissionBeat> beats)
    {
        int stage = 0;
        var lines = new List<LogLine>();
        foreach (MissionBeat b in beats)
        {
            stage += b.Beat is StageBeat ? 1 : 0;
            lines.Add(BeatLines.Line(b.Index, b.Beat, stage));
        }

        return lines;
    }

    // The lead the team was sent with: a later lead change is in the log, not here.
    private static string? StartLead(MissionInFlight mission, Briefing? briefing, IReadOnlyList<IEvent> cycleEvents)
    {
        return briefing is not null && briefing.ObjectiveId == mission.ObjectiveId
            ? briefing.LeadId
            : cycleEvents.OfType<TeamAssigned>().LastOrDefault(a => a.ObjectiveId == mission.ObjectiveId)?.LeadId;
    }

    // The stored briefing while it still names this mission's objective, else the cycle's TeamAssigned event.
    private static IReadOnlyList<string> Gear(MissionInFlight mission, Briefing? briefing, IReadOnlyList<IEvent> cycleEvents)
    {
        if (briefing is not null && briefing.ObjectiveId == mission.ObjectiveId)
        {
            return briefing.GearIds.ToList();
        }

        TeamAssigned? assigned = cycleEvents.OfType<TeamAssigned>().LastOrDefault(a => a.ObjectiveId == mission.ObjectiveId);
        return assigned?.GearIds.ToList() ?? [];
    }

    private static string Inv(FormattableString text)
    {
        return text.ToString(CultureInfo.InvariantCulture);
    }

    private static AftermathView Aftermath(SiteState site, LegacyState state, IReadOnlyList<IEvent> cycleEvents)
    {
        CrewState? crew = state.TryModule(CrewModule.Name, out CrewState? found) ? found : null;
        string? outcome = null;
        var changes = new List<CrewChange>();
        var unattended = new List<UnattendedView>();
        foreach (IEvent e in cycleEvents)
        {
            switch (e)
            {
                case MissionEnded ended:
                    outcome = ended.Result.Outcome.ToString();
                    // The result holds mission points, not the applied change: show the stored loyalty, never a delta.
                    foreach (CrewResult result in ended.Result.Crew.Where(c => c.Loyalty != 0))
                    {
                        if (crew?.Members.FirstOrDefault(m => m.Id == result.Id) is { } member)
                        {
                            changes.Add(new CrewChange(member.Id, Inv($"{member.Id} loyalty now {member.Loyalty}.")));
                        }
                    }


                    break;
                case SkillRaised raised:
                    changes.Add(new CrewChange(raised.CrewId, Inv($"{raised.CrewId} raises {raised.SkillId} to level {raised.Level}.")));
                    break;
                case CrewInjured injured:
                    string returns = injured.ReturnsAtWeek is { } week ? Inv($", returns at week {week}") : string.Empty;
                    changes.Add(new CrewChange(injured.CrewId, Inv($"{injured.CrewId} injured: {BeatLines.Words(injured.Outcome)}, light injuries {injured.LightInjuries}{returns}.")));
                    break;
                case CrewDied died:
                    changes.Add(new CrewChange(died.CrewId, Inv($"{died.CrewId} died on {died.MissionId}.")));
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
