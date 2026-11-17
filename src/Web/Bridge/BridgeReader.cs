using System.Globalization;
using Persistence;
using Sim.Core;
using Sim.Core.Content;
using Sim.Core.Narrative;
using Sim.Crew;
using Sim.Missions;
using Sim.Storylets;

namespace Web.Bridge;

public static class BridgeReader
{
    // Pure: no store, no clock. cycleEvents are this cycle's decoded events in sequence order.
    public static BridgeView Read(LegacyRecord record, ContentPack pack, IReadOnlyList<IEvent> cycleEvents)
    {
        return Read(record, pack, cycleEvents, new Names(pack));
    }

    // The names are the snapshot's own, so a page never mixes one pack's tables with another's names.
    public static BridgeView Read(LegacyRecord record, ContentPack pack, IReadOnlyList<IEvent> cycleEvents, Names names)
    {
        LegacyState state = record.State;
        IReadOnlyList<CrewRow> roster = Roster(state, names);
        SiteState? site = state.TryModule(MissionsModule.Name, out MissionsState? missions) ? missions.Site : null;
        CyclePhase phase = state.Phase;

        bool showSite = site is not null;
        bool showBriefing = site is not null && phase == CyclePhase.Briefing;
        bool showMission = site?.Mission is not null && phase is CyclePhase.Resolution or CyclePhase.Aftermath;
        bool afterwards = phase is CyclePhase.Aftermath or CyclePhase.Advance;
        (List<StoryletView> waiting, List<SettledStoryletView> settled) = afterwards ? Storylets(state, pack, cycleEvents, names) : ([], []);
        // Storylets can wait on a legacy with no site, so they show the aftermath too.
        bool showAftermath = afterwards && (site is not null || waiting.Count > 0 || settled.Count > 0);

        List<ObjectiveView> objectives = site is null ? [] : site.Objectives.Select(o => Objective(pack, names, site, o)).ToList();
        MissionInFlight? inFlight = site?.Mission;
        List<MissionBeat> beats = inFlight is null ? [] : cycleEvents.OfType<MissionBeat>().Where(b => b.MissionId == inFlight.Id).OrderBy(b => b.Index).ToList();
        Prose? prose = inFlight is not null && (showMission || showAftermath)
            ? new Prose(pack, names, state.Seed, inFlight, beats, StartLead(inFlight, site!.Briefing, cycleEvents) ?? inFlight.State.LeadId)
            : null;
        MissionView? mission = showMission ? Mission(inFlight!, site!.Briefing, cycleEvents, names, beats, prose!) : null;
        return new BridgeView(
            TopBar(record, pack),
            // Only while the mission plays: the aftermath names the team in its head instead.
            mission is null || phase != CyclePhase.Resolution ? roster : MarkAway(roster, mission.Team, mission.LeadId),
            showSite ? new SiteView(site!.AssignmentId, objectives, names.Assignment(site.AssignmentId)) : null,
            showBriefing ? Briefing(pack, names, site!, objectives, roster) : null,
            mission,
            showAftermath ? Aftermath(site, state, cycleEvents, names) with { Report = prose is null ? null : Report.Build(beats, prose.Log(_ => []), names), Waiting = waiting, Settled = settled } : null);
    }

    // Rows in view that share a shown name also show their ids; a row shown by its id never repeats it.
    private static List<CrewRow> MarkShared(List<CrewRow> rows)
    {
        HashSet<string> shared = Shared(rows.Select(r => r.Shown));
        return rows.Select(r => r with { Disambiguate = r.Name is not null && r.Name != r.Id && shared.Contains(r.Shown) }).ToList();
    }

    private static List<GearChoice> MarkShared(List<GearChoice> gear)
    {
        HashSet<string> shared = Shared(gear.Select(g => g.Shown));
        return gear.Select(g => g with { Disambiguate = g.Name is not null && g.Name != g.Id && shared.Contains(g.Shown) }).ToList();
    }

    private static HashSet<string> Shared(IEnumerable<string> shown)
    {
        return shown.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
    }

    // The away team's living rows, with the lead flagged; everyone else unmarked. Used for a lead other than the stored one too.
    public static IReadOnlyList<CrewRow> MarkAway(IReadOnlyList<CrewRow> roster, IReadOnlyList<string> team, string leadId)
    {
        return roster.Select(r =>
        {
            bool away = team.Contains(r.Id) && r.Status != nameof(CrewStatus.Dead);
            return r with { IsAway = away, IsLead = away && r.Id == leadId };
        }).ToList();
    }

    private static TopBarView TopBar(LegacyRecord record, ContentPack pack)
    {
        string? warning = record.ContentPackId == pack.Id && record.ContentPackVersion == pack.Version
            ? null
            : Inv($"Legacy uses pack {record.ContentPackId} version {record.ContentPackVersion}, and pack {pack.Id} version {pack.Version} is loaded.");
        return new TopBarView(record.Name, record.State.Week, record.State.Cycle, record.State.Phase, warning);
    }

    private static IReadOnlyList<CrewRow> Roster(LegacyState state, Names names)
    {
        if (!state.TryModule(CrewModule.Name, out CrewState? crew))
        {
            return [];
        }

        // Stable sort: the dead go last, the rest keep roster order.
        return MarkShared(crew.Members.OrderBy(m => m.Status == CrewStatus.Dead).Select(m => Row(m, names)).ToList());
    }

    private static CrewRow Row(CrewMember member, Names names)
    {
        int injuries = member.LightInjuriesHealAtWeek.Count;
        List<SkillCell> skills = member.Skills
            .OrderBy(s => s.Key, StringComparer.Ordinal)
            .Select(s => new SkillCell(s.Key, s.Value.Level, Math.Max(0, s.Value.Level - injuries), names.Skill(s.Key)))
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
            member.AwayMissions,
            Name: names.Crew(member.Id));
    }

    private static ObjectiveView Objective(ContentPack pack, Names names, SiteState site, SiteObjective objective)
    {
        // A stored legacy may name a template the loaded pack lacks: the pack warning explains it, so a miss reads as empty.
        ObjectiveDef? template = pack.Objectives.FirstOrDefault(o => o.Id == objective.TemplateId);
        // Every skill any encounter the template can draw is checked with, by any of its approaches.
        List<string> skills = template is null ? [] : pack.Encounters
            .Where(e => template.Stages.Any(s => s.DrawTags.All(e.Tags.Contains)))
            .SelectMany(e => e.Approaches.Select(a => a.Skill))
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
            site.Briefing?.ObjectiveId == objective.Id,
            names.Objective(objective.TemplateId),
            skills.Select(names.Skill).ToList(),
            template is null);
    }

    private static BriefingView Briefing(ContentPack pack, Names names, SiteState site, IReadOnlyList<ObjectiveView> objectives, IReadOnlyList<CrewRow> roster)
    {
        AssignmentDef? assignment = pack.Assignments.FirstOrDefault(a => a.Id == site.AssignmentId);
        return new BriefingView(
            objectives,
            MarkShared(roster.Where(c => c.Status != nameof(CrewStatus.Dead)).ToList()),
            MarkShared(pack.Gear.Select(g => new GearChoice(g.Id, g.Tag, names.Gear(g.Id))).ToList()),
            assignment?.LoadoutLimit ?? 0);
    }

    private static MissionView Mission(MissionInFlight mission, Briefing? briefing, IReadOnlyList<IEvent> cycleEvents, Names names, List<MissionBeat> beats, Prose prose)
    {
        MissionState state = mission.State;
        List<LogLine> log = Lines(beats, names, prose);
        CallView? call = null;
        if (state.Pending is { } pending)
        {
            // The call's own words are its beat's script, shown once: in the log, just below the call.
            call = new CallView(pending.CallId, pending.DecisionId, pending.Stakes, pending.Options.Select(o => o.ToString()).ToList(), pending.LeadPrefers.ToString(), pending.LeadId, names.Decision(pending.DecisionId), names.Crew(pending.LeadId))
            {
                Labels = prose.ChoiceLabels(pending.DecisionId),
            };
        }

        List<string> team = state.Team.Select(m => m.Member.Id).ToList();
        IReadOnlyList<string> gear = Gear(mission, briefing, cycleEvents);
        return new MissionView(
            mission.Id,
            mission.ObjectiveId,
            team,
            state.LeadId,
            state.Momentum,
            state.Strain,
            log,
            call,
            state.Result?.Outcome.ToString(),
            gear,
            state.Tuning.Cap,
            StartLead(mission, briefing, cycleEvents) ?? state.LeadId,
            team.Select(names.Crew).ToList(),
            gear.Select(names.Gear).ToList());
    }

    // The log's stage beats are numbered from 1 in order; the beat's own cursor also counts skipped slots. Each line carries its
    // prose and tags beside the structural text, which stays the line's detail.
    private static List<LogLine> Lines(IReadOnlyList<MissionBeat> beats, Names names, Prose prose)
    {
        int stage = 0;
        var lines = new List<LogLine>();
        foreach (MissionBeat b in beats)
        {
            stage += b.Beat is StageBeat ? 1 : 0;
            lines.Add(BeatLines.Line(b.Index, b.Beat, stage, names));
        }

        IReadOnlyList<BeatProse> texts = prose.Log(i => lines[i].Segments);
        return lines.Select((line, i) => line with { Prose = texts[i].Parts, Tags = BeatLines.Tags(beats[i].Beat, names), Authored = texts[i].Authored, Script = texts[i].Entries }).ToList();
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

    // A crew change whose line names the member; the id stays in CrewId.
    private static CrewChange Change(string crewId, string kind, Names names, Func<BeatLines.Nm, List<LinePart>> line)
    {
        List<LinePart> parts = line(new BeatLines.Nm(names.Crew(crewId)));
        return new CrewChange(crewId, kind, string.Concat(parts.Select(p => p.Text)), parts);
    }

    private static AftermathView Aftermath(SiteState? site, LegacyState state, IReadOnlyList<IEvent> cycleEvents, Names names)
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
                    // The result holds mission points, not the applied change: show the stored loyalty, never a delta.
                    foreach (CrewResult result in ended.Result.Crew.Where(c => c.Loyalty != 0))
                    {
                        if (crew?.Members.FirstOrDefault(m => m.Id == result.Id) is { } member)
                        {
                            changes.Add(Change(member.Id, "loyalty", names, c => BeatLines.Compose($"{c} loyalty now {member.Loyalty}.")));
                        }
                    }

                    break;
                case SkillRaised raised:
                    changes.Add(Change(raised.CrewId, "skill", names, c => BeatLines.Compose($"{c} raises {new BeatLines.Nm(names.Skill(raised.SkillId))} to level {raised.Level}.")));
                    break;
                case CrewInjured injured:
                    // One line per light injury with its heal week: this mission's are the last ones the crew state added.
                    IEnumerable<int> heals = crew?.Members.FirstOrDefault(m => m.Id == injured.CrewId)?.LightInjuriesHealAtWeek.TakeLast(injured.LightInjuries) ?? [];
                    foreach (int week in heals)
                    {
                        changes.Add(Change(injured.CrewId, "injury", names, c => BeatLines.Compose($"{c} lightly injured, heals week {week}.")));
                    }

                    if (injured.Outcome == MemberStatus.Incapacitated)
                    {
                        changes.Add(Change(injured.CrewId, "injury", names, c => BeatLines.Compose($"{c} incapacitated, returns week {injured.ReturnsAtWeek}.")));
                    }

                    break;
                case CrewDied died:
                    changes.Add(Change(died.CrewId, "death", names, c => BeatLines.Compose($"{c} died.")));
                    break;
                case CrewRecovered recovered:
                    changes.Add(Change(recovered.CrewId, "recovery", names, c => BeatLines.Compose($"{c} recovered.")));
                    break;
                case ObjectiveUnattended u:
                    unattended.Add(new UnattendedView(u.ObjectiveId, u.TemplateId, u.Roll, u.Target, u.Succeeded, names.Objective(u.TemplateId)));
                    break;
            }
        }

        return new AftermathView(outcome ?? site?.Mission?.State.Result?.Outcome.ToString(), changes, unattended);
    }

    // The storylets waiting (those the pack still has, in arrival order) and those answered this cycle (in the order answered).
    private static (List<StoryletView> Waiting, List<SettledStoryletView> Settled) Storylets(LegacyState state, ContentPack pack, IReadOnlyList<IEvent> cycleEvents, Names names)
    {
        StoryletsState? storylets = state.TryModule(StoryletsModule.Name, out StoryletsState? found) ? found : null;
        List<CrewMember> crew = state.TryModule(CrewModule.Name, out CrewState? members) ? members.Members : [];
        string seed = state.Seed.ToString(CultureInfo.InvariantCulture);
        var waiting = new List<StoryletView>();
        foreach (WaitingStorylet w in storylets?.Waiting ?? [])
        {
            // One the pack has lost blocks nothing and is withdrawn when the next aftermath begins.
            if (pack.Storylets.FirstOrDefault(d => d.Id == w.StoryletId) is not { } storylet)
            {
                continue;
            }

            Situation situation = Prose.StoryletSituation(pack, names, crew, storylet.Id, w.SubjectId);
            IReadOnlyList<ProseEntry> script = Prose.Storylet(pack, situation, $"{seed}|{w.OfferId}")?.Entries
                ?? [Systems($"{storylet.Id}: {string.Join(", ", storylet.Options.Select(o => o.Id))}.")];
            waiting.Add(new StoryletView(
                w.OfferId,
                names.Storylet(storylet.Id),
                From(storylets!, storylet, w, names),
                script,
                storylet.Options.Select(o => new StoryletOptionView(o.Id, o.Label.Length > 0 ? o.Label : o.Id)).ToList()));
        }

        var settled = new List<SettledStoryletView>();
        HashSet<string> living = crew.Where(m => m.Status != CrewStatus.Dead).Select(m => m.Id).ToHashSet();
        HashSet<string> shared = Shared(crew.Select(m => names.Crew(m.Id)));
        string Crew(string id)
        {
            string name = names.Crew(id);
            return name != id && shared.Contains(name) ? $"{name} ({id})" : name;
        }

        for (int i = 0; i < cycleEvents.Count; i++)
        {
            if (cycleEvents[i] is not StoryletAnswered answered)
            {
                continue;
            }

            // An answer's own changes are published just before it: loyalty, and flags with no encounter.
            int start = i;
            while (start > 0 && cycleEvents[start - 1] is LoyaltyChanged or FlagChanged { EncounterId: null })
            {
                start--;
            }

            List<IEvent> changes = cycleEvents.Skip(start).Take(i - start).ToList();
            Situation situation = Prose.StoryletSituation(pack, names, crew, answered.StoryletId, answered.SubjectId, answered.OptionId);
            IReadOnlyList<ProseEntry> said = Prose.Storylet(pack, situation, $"{seed}|{answered.OfferId}|{answered.OptionId}")?.Entries ?? [Systems(answered.OptionId)];
            settled.Add(new SettledStoryletView(names.Storylet(answered.StoryletId), said, StoryletTags(changes, living, Crew)));
        }

        return (waiting, settled);
    }

    private static ProseEntry Systems(string text)
    {
        return new ProseEntry(EntryKind.Narration, null, [new LinePart(false, text)]);
    }

    // The encounter that set the flag that brought it, and that flag: the first required flag it last changed, else the first set.
    private static string? From(StoryletsState storylets, StoryletDef storylet, WaitingStorylet waiting, Names names)
    {
        if (waiting.EncounterId is not { } encounter)
        {
            return null;
        }

        List<StoredFlag> set = storylet.Flags
            .Select(r => storylets.Flags.FirstOrDefault(f => f.Flag == r.Flag && f.CrewId == waiting.SubjectId))
            .OfType<StoredFlag>()
            .ToList();
        string flag = (set.FirstOrDefault(f => f.EncounterId == encounter) ?? set.FirstOrDefault())?.Flag ?? storylet.Flags.First().Flag;
        return $"From {names.Encounter(encounter)} · {flag}";
    }

    // Tags in the log's figure style. A loyalty change shared alike by every living crew member is one tag for all crew.
    private static List<string> StoryletTags(IReadOnlyList<IEvent> changes, IReadOnlySet<string> living, Func<string, string> crew)
    {
        var tags = new List<string>();
        List<LoyaltyChanged> loyalty = changes.OfType<LoyaltyChanged>().ToList();
        foreach (IGrouping<int, LoyaltyChanged> delta in loyalty.GroupBy(l => l.To - l.From))
        {
            bool everyone = living.Count > 1 && delta.Select(l => l.CrewId).ToHashSet().SetEquals(living);
            tags.AddRange(everyone ? [$"Loyalty {BeatLines.Signed(delta.Key)} all crew"] : delta.Select(l => $"Loyalty {BeatLines.Signed(delta.Key)} {crew(l.CrewId)}"));
        }

        foreach (FlagChanged flag in changes.OfType<FlagChanged>())
        {
            string on = flag.CrewId is null ? "" : $", {crew(flag.CrewId)}";
            tags.Add(Inv($"{flag.Flag} {flag.From} to {flag.To}{on}"));
        }

        return tags;
    }
}
