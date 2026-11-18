using System.Globalization;
using Persistence;
using Sim.Core;
using Sim.Core.Content;
using Sim.Core.Narrative;
using Sim.Crew;
using Sim.Missions;
using Sim.Storylets;
using Sim.Traits;

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
        IReadOnlyList<CrewRow> roster = Roster(state, pack, names);
        SiteState? site = state.TryModule(MissionsModule.Name, out MissionsState? missions) ? missions.Site : null;
        CyclePhase phase = state.Phase;

        bool showSite = site is not null;
        bool showBriefing = site is not null && phase == CyclePhase.Briefing;
        bool showMission = site?.Mission is not null && phase is CyclePhase.Resolution or CyclePhase.Aftermath;
        bool afterwards = phase is CyclePhase.Aftermath or CyclePhase.Advance;
        bool showAftermath = afterwards && site is not null;
        ItineraryView? itinerary = phase == CyclePhase.Itinerary ? Itinerary(state, pack, cycleEvents, names) : null;

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
            showAftermath ? Aftermath(site, state, pack, cycleEvents, names) with { Report = prose is null ? null : Report.Build(beats, prose.Log(_ => []), names) } : null,
            itinerary);
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

    private static IReadOnlyList<CrewRow> Roster(LegacyState state, ContentPack pack, Names names)
    {
        if (!state.TryModule(CrewModule.Name, out CrewState? crew))
        {
            return [];
        }

        // Stable sort: the dead go last, the rest keep roster order.
        return MarkShared(crew.Members.OrderBy(m => m.Status == CrewStatus.Dead).Select(m => Row(m, pack.CrewTuning, names)).ToList());
    }

    private static CrewRow Row(CrewMember member, CrewTuning tuning, Names names)
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
            Name: names.Crew(member.Id),
            Morale: MoraleWord(tuning, member.Morale));
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

    private static AftermathView Aftermath(SiteState? site, LegacyState state, ContentPack pack, IReadOnlyList<IEvent> cycleEvents, Names names)
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

        // Each member's morale change from the mission, net of its result, hurt, and deaths, after the rest (1e part two).
        IEnumerable<IGrouping<string, MoraleChanged>> morale = cycleEvents.OfType<MoraleChanged>()
            .Where(m => m.Cause is MoraleCauses.Mission or MoraleCauses.Hurt or MoraleCauses.Death)
            .GroupBy(m => m.CrewId);
        foreach (IGrouping<string, MoraleChanged> moved in morale)
        {
            int from = moved.First().From;
            int to = moved.Last().To;
            if (to != from)
            {
                string word = MoraleWord(pack.CrewTuning, to).ToLowerInvariant();
                changes.Add(Change(moved.Key, "morale", names, c => BeatLines.Compose($"{c} morale {BeatLines.Signed(to - from)}, now {word}.")));
            }
        }

        return new AftermathView(outcome ?? site?.Mission?.State.Result?.Outcome.ToString(), changes, unattended);
    }

    // The captain's itinerary (1e part two): the storylets, the vignettes that arrived this cycle in arrival order, and the
    // captain's time.
    private static ItineraryView Itinerary(LegacyState state, ContentPack pack, IReadOnlyList<IEvent> cycleEvents, Names names)
    {
        (List<StoryletView> waiting, List<SettledStoryletView> settled) = Storylets(state, pack, cycleEvents, names);
        return new ItineraryView(pack.CaptainsTimePerCycle, waiting, settled, Vignettes(state, pack, cycleEvents, names));
    }

    // Words for the fact that brought a vignette, in its head.
    private static readonly Dictionary<string, string> FactWords = new()
    {
        [LastCycleFacts.Sent] = "back from the mission",
        [LastCycleFacts.Hurt] = "hurt on the mission",
        [LastCycleFacts.Rested] = "rested",
        [LastCycleFacts.LostCrewmate] = "lost a crewmate",
    };

    private static List<VignetteView> Vignettes(LegacyState state, ContentPack pack, IReadOnlyList<IEvent> cycleEvents, Names names)
    {
        StoryletsState? storylets = state.TryModule(StoryletsModule.Name, out StoryletsState? found) ? found : null;
        List<CrewMember> crew = state.TryModule(CrewModule.Name, out CrewState? members) ? members.Members : [];
        (HashSet<string> living, Func<string, string> shown) = Living(crew, names);
        string seed = state.Seed.ToString(CultureInfo.InvariantCulture);
        var views = new List<VignetteView>();
        foreach (VignetteArrived arrived in cycleEvents.OfType<VignetteArrived>())
        {
            VignetteDef? vignette = pack.Vignettes.FirstOrDefault(v => v.Id == arrived.VignetteId);
            bool waiting = storylets?.Vignettes.Any(v => v.OfferId == arrived.OfferId) == true;
            int at = Index(cycleEvents, e => e is VignetteAnswered a && a.OfferId == arrived.OfferId);
            if (vignette is null || (!waiting && at < 0))
            {
                // One the pack has lost, or one dropped unanswered: nothing to show or answer.
                continue;
            }

            string? about = Join(arrived.SubjectId is null ? null : shown(arrived.SubjectId), arrived.Fact is null ? null : FactWords.GetValueOrDefault(arrived.Fact));
            Situation situation = Prose.StoryletSituation(pack, names, crew, vignette.Id, arrived.SubjectId);
            IReadOnlyList<ProseEntry> script = Prose.Storylet(pack, situation, $"{seed}|{arrived.OfferId}")?.Entries ?? [Systems(vignette.Id)];
            List<StoryletOptionView> options = vignette.Options.Select(o => new StoryletOptionView(o.Id, o.Label.Length > 0 ? o.Label : o.Id)).ToList();
            string? defaultLabel = options.FirstOrDefault(o => o.Id == vignette.Default)?.Label;
            if (waiting)
            {
                views.Add(new VignetteView(arrived.OfferId, names.Storylet(vignette.Id), about, script, options, defaultLabel, false, [], []));
                continue;
            }

            var answered = (VignetteAnswered)cycleEvents[at];
            IReadOnlyList<ProseEntry> said = [];
            if (answered.OptionId is { } optionId)
            {
                Situation saying = Prose.StoryletSituation(pack, names, crew, vignette.Id, arrived.SubjectId, optionId);
                said = Prose.Storylet(pack, saying, $"{seed}|{arrived.OfferId}|{optionId}")?.Entries ?? [Systems(optionId)];
            }

            views.Add(new VignetteView(arrived.OfferId, names.Storylet(vignette.Id), about, script, options, defaultLabel, true, said, StoryletTags(ChangesBefore(cycleEvents, at), living, shown), answered.Defaulted));
        }

        return views;
    }

    private static string? Join(string? a, string? b)
    {
        return a is null ? b : b is null ? a : $"{a}, {b}";
    }

    private static int Index(IReadOnlyList<IEvent> events, Func<IEvent, bool> match)
    {
        for (int i = 0; i < events.Count; i++)
        {
            if (match(events[i]))
            {
                return i;
            }
        }

        return -1;
    }

    // An answer's own changes are published just before it: loyalty, morale it caused, and traits it changed.
    private static List<IEvent> ChangesBefore(IReadOnlyList<IEvent> events, int answer)
    {
        int start = answer;
        while (start > 0 && events[start - 1] is LoyaltyChanged or MoraleChanged { Cause: MoraleCauses.Answer } or TraitChanged { Cause: TraitCauses.Answer or TraitCauses.Vignette })
        {
            start--;
        }

        return events.Skip(start).Take(answer - start).ToList();
    }

    // The living, and how a crew member is named in a tag: by name, with the id when another shares it.
    private static (HashSet<string> Living, Func<string, string> Shown) Living(List<CrewMember> crew, Names names)
    {
        HashSet<string> living = crew.Where(m => m.Status != CrewStatus.Dead).Select(m => m.Id).ToHashSet();
        HashSet<string> shared = Shared(crew.Select(m => names.Crew(m.Id)));
        return (living, id =>
        {
            string name = names.Crew(id);
            return name != id && shared.Contains(name) ? $"{name} ({id})" : name;
        });
    }

    // The words for a morale figure: the band that changes a check.
    public static string MoraleWord(CrewTuning tuning, int morale)
    {
        int bonus = tuning.MoraleBonus(morale);
        return bonus < 0 ? "Low" : bonus > 0 ? "High" : "Steady";
    }

    // The storylets waiting (those the pack still has, in arrival order) and those answered this cycle (in the order answered).
    private static (List<StoryletView> Waiting, List<SettledStoryletView> Settled) Storylets(LegacyState state, ContentPack pack, IReadOnlyList<IEvent> cycleEvents, Names names)
    {
        StoryletsState? storylets = state.TryModule(StoryletsModule.Name, out StoryletsState? found) ? found : null;
        List<StoredTrait> traits = state.TryModule(TraitsModule.Name, out TraitsState? held) ? held.Traits : [];
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
                From(traits, storylet, w, names),
                script,
                storylet.Options.Select(o => new StoryletOptionView(o.Id, o.Label.Length > 0 ? o.Label : o.Id)).ToList()));
        }

        var settled = new List<SettledStoryletView>();
        (HashSet<string> living, Func<string, string> Crew) = Living(crew, names);

        for (int i = 0; i < cycleEvents.Count; i++)
        {
            if (cycleEvents[i] is not StoryletAnswered answered)
            {
                continue;
            }

            List<IEvent> changes = ChangesBefore(cycleEvents, i);
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

    // The encounter that set the trait that brought it, and that trait: the first required trait it last changed, else the first set.
    private static string? From(List<StoredTrait> traits, StoryletDef storylet, WaitingStorylet waiting, Names names)
    {
        if (waiting.EncounterId is not { } encounter)
        {
            return null;
        }

        List<StoredTrait> set = storylet.Traits
            .Select(r => traits.FirstOrDefault(t => t.Trait == r.Trait && t.CrewId == waiting.SubjectId && t.TargetId is null))
            .OfType<StoredTrait>()
            .ToList();
        string trait = (set.FirstOrDefault(t => t.EncounterId == encounter) ?? set.FirstOrDefault())?.Trait ?? storylet.Traits.First().Trait;
        return $"From {names.Encounter(encounter)} · {trait}";
    }

    // Tags in the log's figure style. A loyalty or morale change shared alike by every living crew member is one tag for all crew.
    private static List<string> StoryletTags(IReadOnlyList<IEvent> changes, IReadOnlySet<string> living, Func<string, string> crew)
    {
        var tags = new List<string>();
        void Stat(string word, IEnumerable<(string CrewId, int Delta)> moved)
        {
            foreach (IGrouping<int, (string CrewId, int Delta)> delta in moved.GroupBy(m => m.Delta))
            {
                bool everyone = living.Count > 1 && delta.Select(m => m.CrewId).ToHashSet().SetEquals(living);
                tags.AddRange(everyone ? [$"{word} {BeatLines.Signed(delta.Key)} all crew"] : delta.Select(m => $"{word} {BeatLines.Signed(delta.Key)} {crew(m.CrewId)}"));
            }
        }

        Stat("Loyalty", changes.OfType<LoyaltyChanged>().Select(l => (l.CrewId, l.To - l.From)));
        Stat("Morale", changes.OfType<MoraleChanged>().Select(m => (m.CrewId, m.To - m.From)));

        foreach (TraitChanged trait in changes.OfType<TraitChanged>())
        {
            string on = trait.CrewId is null ? "" : $", {crew(trait.CrewId)}";
            tags.Add(Inv($"{trait.Trait} {trait.From} to {trait.To}{on}"));
        }

        return tags;
    }
}
