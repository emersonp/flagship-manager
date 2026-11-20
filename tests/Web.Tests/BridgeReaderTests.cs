using System.Text.RegularExpressions;
using Persistence;
using Sim.Core;
using Sim.Core.Content;
using Sim.Crew;
using Sim.Missions;
using Web.Bridge;

namespace Web.Tests;

public class BridgeReaderTests
{
    private static readonly ContentPack Pack = BridgePack.Load();

    private static readonly ContentPack Wide = BridgePack.Wide(Pack);

    private static BridgeView Read(Played played)
    {
        return BridgeReader.Read(played.Record(), played.Pack, played.CycleEvents());
    }

    private static Played Until(ContentPack pack, Func<Played, bool> condition, ulong seeds = 40)
    {
        for (ulong seed = 1; seed <= seeds; seed++)
        {
            var played = new Played(pack, seed);
            if (played.StepUntil(() => condition(played)))
            {
                return played;
            }
        }

        throw new InvalidOperationException("No seed reached the wanted state.");
    }

    private static bool Founded(Played p)
    {
        return p.State.Modules.ContainsKey(MissionsModule.Name);
    }

    [Fact]
    public void A_legacy_not_yet_founded_reads_as_an_empty_bridge()
    {
        var played = new Played(Pack, 1);

        BridgeView view = Read(played);

        Assert.Empty(view.Roster);
        Assert.Null(view.Site);
        Assert.Null(view.Briefing);
        Assert.Null(view.Mission);
        Assert.Null(view.Aftermath);
        Assert.Equal("LEGACY", view.TopBar.LegacyName);
        Assert.Empty(played.State.Modules);
    }

    [Fact]
    public void Orders_show_the_site_and_the_roster_and_nothing_else()
    {
        Played played = Until(Pack, p => Founded(p) && p.State.Phase == CyclePhase.Orders && p.State.TryModule(MissionsModule.Name, out MissionsState? m) && m.Site is not null);

        BridgeView view = Read(played);

        Assert.Equal(CyclePhase.Orders, view.TopBar.Phase);
        Assert.Equal(1, view.TopBar.Cycle);
        Assert.Equal(8, view.Roster.Count);
        Assert.NotNull(view.Site);
        Assert.NotEmpty(view.Site.Objectives);
        Assert.All(view.Site.Objectives, o =>
        {
            Assert.False(o.HasTeam);
            Assert.NotEmpty(o.PossibleSkills);
            Assert.True(o.TeamSizeMin <= o.TeamSizeMax);
        });
        Assert.Null(view.Briefing);
        Assert.Null(view.Mission);
        Assert.Null(view.Aftermath);
        Assert.Null(view.TopBar.PackWarning);
    }

    [Fact]
    public void The_briefing_adds_the_crew_and_the_gear_choices()
    {
        Played played = Until(Pack, p => p.State.Phase == CyclePhase.Briefing);

        BridgeView view = Read(played);

        Assert.NotNull(view.Briefing);
        Assert.NotNull(view.Site);
        Assert.Equal(view.Site.Objectives, view.Briefing.Objectives);
        Assert.Equal(Pack.Gear.Select(g => g.Id), view.Briefing.Gear.Select(g => g.Id));
        Assert.Equal(Pack.Assignments.Single(a => a.Id == view.Site.AssignmentId).LoadoutLimit, view.Briefing.LoadoutLimit);
        Assert.Equal(8, view.Briefing.Crew.Count);
        Assert.Null(view.Mission);
        Assert.Null(view.Aftermath);
    }

    [Fact]
    public void The_assigned_objective_is_marked_as_having_a_team()
    {
        Played played = Until(Pack, p => p.State.Phase == CyclePhase.Briefing && p.State.Module<MissionsState>(MissionsModule.Name).Site!.Briefing is not null || p.State.Phase == CyclePhase.Resolution);

        BridgeView view = Read(played);

        Assert.Single(view.Site!.Objectives, o => o.HasTeam);
    }

    [Fact]
    public void Resolution_with_a_call_waiting_shows_the_call_and_a_log_as_long_as_the_beat_count()
    {
        Played played = Until(Pack, p => p.State.Phase == CyclePhase.Resolution && p.Mission?.State.Pending is not null);

        BridgeView view = Read(played);

        MissionView mission = Assert.IsType<MissionView>(view.Mission);
        PendingCall pending = played.Mission!.State.Pending!;
        Assert.NotNull(mission.Call);
        Assert.Equal(pending.CallId, mission.Call.CallId);
        Assert.Equal(pending.DecisionId, mission.Call.DecisionId);
        Assert.Equal(pending.Stakes, mission.Call.Stakes);
        Assert.Equal(pending.Options.Select(o => o.ToString()), mission.Call.Options);
        Assert.Equal(pending.LeadPrefers.ToString(), mission.Call.LeadPrefers);
        Assert.Null(mission.Outcome);
        Assert.Equal(played.Mission.BeatCount, mission.Log.Count);
        Assert.Equal(Enumerable.Range(0, mission.Log.Count), mission.Log.Select(l => l.Index));
        Assert.Equal("call", mission.Log[^1].Kind);
        Assert.Null(view.Aftermath);
        Assert.Contains(mission.LeadId, mission.Team);
    }

    [Fact]
    public void A_waiting_call_names_its_lead()
    {
        Played played = Until(Pack, p => p.State.Phase == CyclePhase.Resolution && p.Mission?.State.Pending is not null);

        MissionView mission = Assert.IsType<MissionView>(Read(played).Mission);

        Assert.Equal(played.Mission!.State.Pending!.LeadId, mission.Call!.LeadId);
        Assert.Contains(mission.Call.LeadId, mission.Team);
    }

    [Fact]
    public void The_mission_carries_the_gear_the_team_was_sent_with()
    {
        Played played = Until(Pack, p => p.State.Phase == CyclePhase.Resolution && p.Mission is not null);
        string[] sent = played.CycleEvents().OfType<TeamAssigned>().Single().GearIds.ToArray();

        MissionView mission = Assert.IsType<MissionView>(Read(played).Mission);

        Assert.NotEmpty(sent);
        Assert.Equal(sent, mission.GearIds);
    }

    [Fact]
    public void A_mission_sent_with_no_gear_has_an_empty_gear_list()
    {
        ContentPack none = Pack with { Assignments = Pack.Assignments.Select(a => a with { LoadoutLimit = 0 }).ToList() };
        Played played = Until(none, p => p.State.Phase == CyclePhase.Resolution && p.Mission is not null);

        MissionView mission = Assert.IsType<MissionView>(Read(played).Mission);

        Assert.Empty(mission.GearIds);
    }

    [Fact]
    public void The_meter_cap_is_the_missions_stored_tuning_cap()
    {
        ContentPack capped = Pack with { Tuning = Pack.Tuning with { Cap = 7 } };
        Played played = Until(capped, p => p.State.Phase == CyclePhase.Resolution && p.Mission is not null);

        MissionView mission = Assert.IsType<MissionView>(Read(played).Mission);

        Assert.Equal(7, played.Mission!.State.Tuning.Cap);
        Assert.Equal(7, mission.Cap);
    }

    [Fact]
    public void Stage_lines_number_the_stages_from_one_in_log_order()
    {
        Played played = Until(Pack, p => p.State.Phase == CyclePhase.Resolution && p.Mission?.State.Result is not null);

        MissionView mission = Assert.IsType<MissionView>(Read(played).Mission);

        List<string> stages = mission.Log.Where(l => l.Kind == "stage").Select(l => l.Text).ToList();
        Assert.NotEmpty(stages);
        for (int i = 0; i < stages.Count; i++)
        {
            Assert.StartsWith($"Stage {i + 1}: ", stages[i]);
        }
    }

    [Fact]
    public void The_roster_marks_exactly_the_living_team_in_resolution_with_the_lead_flagged()
    {
        Played played = Until(Pack, p => p.State.Phase == CyclePhase.Resolution && p.Mission is not null);

        BridgeView view = Read(played);

        MissionView mission = view.Mission!;
        Assert.Equal(mission.Team.Order(), view.Roster.Where(r => r.IsAway).Select(r => r.Id).Order());
        Assert.Equal([mission.LeadId], view.Roster.Where(r => r.IsLead).Select(r => r.Id));
        Assert.All(view.Roster.Where(r => r.IsLead), r => Assert.True(r.IsAway));
    }

    // The aftermath names the team in its head; the roster no longer tags it.
    [Fact]
    public void The_roster_marks_nobody_in_aftermath()
    {
        Played played = Until(Pack, p => p.State.Phase == CyclePhase.Aftermath && p.Mission is not null);

        BridgeView view = Read(played);

        Assert.NotNull(view.Mission);
        Assert.All(view.Roster, r => Assert.False(r.IsAway || r.IsLead));
    }

    [Fact]
    public void A_member_who_died_on_the_mission_is_never_tagged_away()
    {
        Played played = Until(Wide, p => p.State.Phase == CyclePhase.Resolution && p.CycleEvents().OfType<CrewDied>().Any(), 200);
        string dead = played.CycleEvents().OfType<CrewDied>().First().CrewId;

        CrewRow row = Read(played).Roster.Single(r => r.Id == dead);

        Assert.Equal("Dead", row.Status);
        Assert.False(row.IsAway || row.IsLead);
    }

    [Fact]
    public void The_roster_marks_nobody_in_orders_and_briefing()
    {
        foreach (CyclePhase phase in new[] { CyclePhase.Orders, CyclePhase.Briefing })
        {
            Played played = Until(Pack, p => Founded(p) && p.State.Phase == phase);

            BridgeView view = Read(played);

            Assert.NotEmpty(view.Roster);
            Assert.All(view.Roster, r => Assert.False(r.IsAway || r.IsLead));
        }
    }

    [Fact]
    public void A_mission_remembers_the_lead_it_started_with()
    {
        Played played = Until(Pack, p => p.State.Phase == CyclePhase.Resolution && p.Mission is not null);

        MissionView mission = Assert.IsType<MissionView>(Read(played).Mission);

        Assert.Equal(played.CycleEvents().OfType<TeamAssigned>().Single().LeadId, mission.StartLeadId);
    }

    [Fact]
    public void Resolution_that_has_ended_shows_the_outcome_and_no_call()
    {
        Played played = Until(Pack, p => p.State.Phase == CyclePhase.Resolution && p.Mission?.State.Result is not null);

        MissionView mission = Assert.IsType<MissionView>(Read(played).Mission);

        Assert.Null(mission.Call);
        Assert.Equal(played.Mission!.State.Result!.Outcome.ToString(), mission.Outcome);
        Assert.Equal(played.Mission.BeatCount, mission.Log.Count);
        Assert.Equal("result", mission.Log[^1].Kind);
    }

    [Fact]
    public void The_log_keeps_index_order_even_when_the_events_arrive_shuffled()
    {
        Played played = Until(Pack, p => p.State.Phase == CyclePhase.Resolution && p.Mission?.State.Result is not null);
        List<IEvent> shuffled = played.CycleEvents().Reverse().ToList();

        MissionView mission = Assert.IsType<MissionView>(BridgeReader.Read(played.Record(), Pack, shuffled).Mission);

        Assert.Equal(Enumerable.Range(0, played.Mission!.BeatCount), mission.Log.Select(l => l.Index));
    }

    [Fact]
    public void Momentum_and_strain_equal_the_stored_mission_state_while_waiting_after_the_end_and_in_aftermath()
    {
        foreach (Func<Played, bool> reached in new Func<Played, bool>[]
        {
            p => p.State.Phase == CyclePhase.Resolution && p.Mission?.State.Pending is not null,
            p => p.State.Phase == CyclePhase.Resolution && p.Mission?.State.Result is not null && LastBeatDisagrees(p),
            p => p.State.Phase == CyclePhase.Aftermath && p.Mission is not null,
        })
        {
            Played played = Until(Pack, reached);

            MissionView mission = Assert.IsType<MissionView>(Read(played).Mission);

            Assert.Equal(played.Mission!.State.Momentum, mission.Momentum);
            Assert.Equal(played.Mission.State.Strain, mission.Strain);
        }
    }

    // A mission whose last reporting beat predates its own effects: the case a beat scan gets wrong.
    private static bool LastBeatDisagrees(Played p)
    {
        (int Momentum, int Strain)? last = p.CycleEvents().OfType<MissionBeat>()
            .Select(b => b.Beat switch
            {
                StageBeat s => ((int, int)?)(s.Momentum, s.Strain),
                CheckBeat c => (c.Momentum, c.Strain),
                _ => null,
            })
            .LastOrDefault(x => x is not null);
        return last is { } l && l != (p.Mission!.State.Momentum, p.Mission.State.Strain);
    }

    [Fact]
    public void Aftermath_carries_the_outcome_the_changes_and_the_mission()
    {
        Played played = Until(Pack, p => p.State.Phase == CyclePhase.Aftermath && p.Mission is not null);

        BridgeView view = Read(played);

        AftermathView aftermath = Assert.IsType<AftermathView>(view.Aftermath);
        Assert.NotNull(view.Mission);
        Assert.NotNull(view.Site);
        Assert.Null(view.Briefing);
        Assert.Equal(played.Mission!.State.Result!.Outcome.ToString(), aftermath.Outcome);
        Assert.Equal(view.Mission.Outcome, aftermath.Outcome);
        int raised = played.CycleEvents().OfType<SkillRaised>().Count();
        Assert.Equal(raised, aftermath.Changes.Count(c => c.Kind == "skill"));
    }

    [Fact]
    public void Aftermath_changes_follow_event_order_and_include_the_loyalty_change()
    {
        Played played = Until(Pack, p => p.State.Phase == CyclePhase.Aftermath && p.CycleEvents().OfType<MissionEnded>().Any(e => e.Result.Crew.Any(c => c.Loyalty != 0)));
        MissionEnded ended = played.CycleEvents().OfType<MissionEnded>().Single();
        CrewResult changed = ended.Result.Crew.First(c => c.Loyalty != 0);

        List<CrewChange> changes = Assert.IsType<AftermathView>(Read(played).Aftermath).Changes.ToList();

        int stored = played.State.Module<CrewState>(CrewModule.Name).Members.Single(m => m.Id == changed.Id).Loyalty;
        Assert.Equal(stored, Read(played).Roster.Single(r => r.Id == changed.Id).Loyalty);
        Assert.Contains(changes, c => c.CrewId == changed.Id && c.Kind == "loyalty" && c.Text == $"{changed.Id} loyalty now {stored}.");
        (string, string)[] expected = played.CycleEvents()
            .Where(e => e is MissionEnded or SkillRaised or CrewInjured or CrewDied or CrewRecovered)
            .SelectMany(e => e switch
            {
                MissionEnded m => m.Result.Crew.Where(c => c.Loyalty != 0).Select(c => (c.Id, "loyalty")),
                SkillRaised s => [(s.CrewId, "skill")],
                CrewInjured i => Enumerable.Repeat((i.CrewId, "injury"), i.LightInjuries + (i.Outcome == MemberStatus.Incapacitated ? 1 : 0)),
                CrewDied d => [(d.CrewId, "death")],
                CrewRecovered r => [(r.CrewId, "recovery")],
                _ => [],
            })
            .ToArray();
        Assert.Equal(expected, changes.TakeWhile(c => c.Kind != "morale").Select(c => (c.CrewId, c.Kind)));

        // 1e part two: then each member's net morale change from the mission, in the order their changes began.
        string[] moved = played.CycleEvents().OfType<MoraleChanged>().GroupBy(m => m.CrewId).Where(g => g.Last().To != g.First().From).Select(g => g.Key).ToArray();
        Assert.NotEmpty(moved);
        Assert.Equal(moved, changes.SkipWhile(c => c.Kind != "morale").Select(c => c.CrewId));
        Assert.All(changes.SkipWhile(c => c.Kind != "morale"), c => Assert.Equal("morale", c.Kind));
    }

    [Fact]
    public void A_morale_change_line_gives_the_net_change_and_the_word_play_shows()
    {
        Played played = Until(Pack, p => p.State.Phase == CyclePhase.Aftermath && p.CycleEvents().OfType<MoraleChanged>().Any());
        MoraleChanged first = played.CycleEvents().OfType<MoraleChanged>().First();
        List<MoraleChanged> theirs = played.CycleEvents().OfType<MoraleChanged>().Where(m => m.CrewId == first.CrewId).ToList();
        int to = theirs[^1].To;

        CrewChange line = Assert.IsType<AftermathView>(Read(played).Aftermath).Changes.Single(c => c.Kind == "morale" && c.CrewId == first.CrewId);

        string word = BridgeReader.MoraleWord(Pack.CrewTuning, to).ToLowerInvariant();
        Assert.Equal($"{first.CrewId} morale {BeatLines.Signed(to - theirs[0].From)}, now {word}.", line.Text);
        Assert.Equal(BridgeReader.MoraleWord(Pack.CrewTuning, to), Read(played).Roster.Single(r => r.Id == first.CrewId).Morale);
    }

    // Injury and death lines in the design's voice, built from the events and the stored heal days.
    [Fact]
    public void Injury_death_and_recovery_lines_read_in_the_designs_voice()
    {
        Played light = Until(Wide, p => p.State.Phase == CyclePhase.Aftermath && p.CycleEvents().OfType<CrewInjured>().Any(i => i.Outcome == MemberStatus.Active), 200);
        CrewInjured hurt = light.CycleEvents().OfType<CrewInjured>().First(i => i.Outcome == MemberStatus.Active);
        List<int> heals = light.State.Module<CrewState>(CrewModule.Name).Members.Single(m => m.Id == hurt.CrewId).LightInjuriesHealAtDay.TakeLast(hurt.LightInjuries).ToList();
        List<CrewChange> lines = Read(light).Aftermath!.Changes.Where(c => c.CrewId == hurt.CrewId && c.Kind == "injury").ToList();
        Assert.Equal(heals.Select(d => $"{hurt.CrewId} lightly injured, heals in {Dates.Weeks(Wide.Calendar, d - light.State.Day)}."), lines.Select(c => c.Text));
        Assert.All(lines, c => Assert.Matches(@"heals in (under a week|1 week|\d+ weeks)\.$", c.Text));

        Played incapacitated = Until(Wide, p => p.State.Phase == CyclePhase.Aftermath && p.CycleEvents().OfType<CrewInjured>().Any(i => i.Outcome == MemberStatus.Incapacitated), 400);
        CrewInjured down = incapacitated.CycleEvents().OfType<CrewInjured>().First(i => i.Outcome == MemberStatus.Incapacitated);
        Assert.Contains(Read(incapacitated).Aftermath!.Changes, c => c.Kind == "injury" && c.Text == $"{down.CrewId} incapacitated, returns in {Dates.Weeks(Wide.Calendar, down.ReturnsAtDay!.Value - incapacitated.State.Day)}.");

        Played died = Until(Wide, p => p.State.Phase == CyclePhase.Aftermath && p.CycleEvents().OfType<CrewDied>().Any(), 200);
        string gone = died.CycleEvents().OfType<CrewDied>().First().CrewId;
        Assert.Contains(Read(died).Aftermath!.Changes, c => c.Kind == "death" && c.Text == $"{gone} died.");

        Played healed = Until(Wide, p => p.State.Phase == CyclePhase.Aftermath && p.CycleEvents().OfType<CrewRecovered>().Any(), 200);
        string back = healed.CycleEvents().OfType<CrewRecovered>().First().CrewId;
        Assert.Contains(Read(healed).Aftermath!.Changes, c => c.Kind == "recovery" && c.Text == $"{back} recovered.");
    }

    [Fact]
    public void Aftermath_lists_the_objectives_that_went_unattended()
    {
        Played played = Until(Pack, p => p.State.Phase == CyclePhase.Aftermath && p.CycleEvents().OfType<ObjectiveUnattended>().Any());
        List<ObjectiveUnattended> events = played.CycleEvents().OfType<ObjectiveUnattended>().ToList();

        IReadOnlyList<UnattendedView> unattended = Assert.IsType<AftermathView>(Read(played).Aftermath).Unattended;

        Assert.Equal(events.Select(e => (e.ObjectiveId, e.TemplateId, e.Roll, e.Target, e.Succeeded)), unattended.Select(u => (u.ObjectiveId, u.TemplateId, u.Roll, u.Target, u.Succeeded)));
    }

    [Fact]
    public void Advance_keeps_the_aftermath_and_drops_the_mission()
    {
        Played played = Until(Pack, p => p.State.Phase == CyclePhase.Advance);

        BridgeView view = Read(played);

        Assert.NotNull(view.Aftermath);
        Assert.NotNull(view.Site);
        Assert.Null(view.Mission);
        Assert.Null(view.Briefing);
    }

    [Fact]
    public void The_next_cycles_orders_start_clean()
    {
        Played played = Until(Pack, p => p.State.Cycle == 2 && p.State.Phase == CyclePhase.Orders && p.State.Module<MissionsState>(MissionsModule.Name).Site?.Cycle == 2);

        BridgeView view = Read(played);

        Assert.Equal(2, view.TopBar.Cycle);
        Assert.NotNull(view.Site);
        Assert.All(view.Site.Objectives, o => Assert.False(o.HasTeam));
        Assert.Null(view.Mission);
        Assert.Null(view.Aftermath);
        Assert.Null(view.Briefing);
    }

    [Fact]
    public void A_recovering_and_a_dead_crew_member_are_listed_with_the_dead_last()
    {
        ContentPack wide = BridgePack.Wide(Pack);
        Played played = Until(wide, p =>
        {
            CrewState? crew = p.State.TryModule(CrewModule.Name, out CrewState? c) ? c : null;
            return crew is not null
                && crew.Members.Any(m => m.Status == CrewStatus.Recovering)
                && crew.Members.Any(m => m.Status == CrewStatus.Dead)
                && crew.Members.Take(crew.Members.Count - 1).Any(m => m.Status == CrewStatus.Dead);
        }, 20);

        IReadOnlyList<CrewRow> roster = Read(played).Roster;

        CrewRow recovering = roster.First(r => r.Status == "Recovering");
        Assert.False(recovering.Fit);
        Assert.NotNull(recovering.ReturnsAtDay);
        Assert.Contains(roster, r => r.Status == "Dead" && !r.Fit);
        int firstDead = roster.ToList().FindIndex(r => r.Status == "Dead");
        Assert.All(roster.Skip(firstDead), r => Assert.Equal("Dead", r.Status));
        Assert.Equal(8, roster.Count);
    }

    [Fact]
    public void Effective_level_is_the_level_less_unhealed_light_injuries_with_a_floor_of_zero()
    {
        Played played = Until(Pack, p => p.State.Phase == CyclePhase.Orders && Founded(p));
        CrewMember member = played.State.Module<CrewState>(CrewModule.Name).Members[0];
        member.LightInjuriesHealAtDay.AddRange([5, 6]);
        member.Skills.Values.First().Level = 1;

        CrewRow row = Read(played).Roster.Single(r => r.Id == member.Id);

        Assert.Equal([5, 6], row.LightInjuryHealDays);
        Assert.All(row.Skills, s => Assert.Equal(Math.Max(0, s.Level - 2), s.EffectiveLevel));
        Assert.Contains(row.Skills, s => s.Level == 1 && s.EffectiveLevel == 0);
        Assert.Equal(member.Seniority, row.Seniority);
        Assert.Equal(member.Loyalty, row.Loyalty);
        Assert.Equal(member.AwayMissions, row.AwayMissions);
    }

    // Played under the repository pack, read under one with another version and without the site's template and assignment.
    [Fact]
    public void A_pack_mismatch_sets_the_warning_and_never_throws_in_orders_briefing_and_resolution()
    {
        foreach (CyclePhase phase in new[] { CyclePhase.Orders, CyclePhase.Briefing, CyclePhase.Resolution })
        {
            Played played = Until(Pack, p => p.State.Phase == phase && p.State.Module<MissionsState>(MissionsModule.Name).Site is not null && p.Mission is null == (phase != CyclePhase.Resolution) || p.State.Phase == phase && p.Mission is not null);
            SiteState site = played.State.Module<MissionsState>(MissionsModule.Name).Site!;
            var templates = site.Objectives.Select(o => o.TemplateId).ToHashSet();
            ContentPack other = Pack with
            {
                Version = Pack.Version + 1,
                Objectives = Pack.Objectives.Where(o => !templates.Contains(o.Id)).ToList(),
                Assignments = Pack.Assignments.Where(a => a.Id != site.AssignmentId).ToList(),
                Gear = [],
            };

            BridgeView view = BridgeReader.Read(played.Record(), other, played.CycleEvents());

            Assert.False(string.IsNullOrWhiteSpace(view.TopBar.PackWarning));
            Assert.NotNull(view.Site);
            Assert.All(view.Site.Objectives, o =>
            {
                Assert.True(o.Missing);
                Assert.Empty(o.PossibleSkills);
                Assert.Equal(0, o.TeamSizeMin);
                Assert.Equal(0, o.TeamSizeMax);
                Assert.Null(o.UnattendedDifficulty);
            });
            if (phase == CyclePhase.Briefing)
            {
                Assert.Equal(0, view.Briefing!.LoadoutLimit);
            }
        }
    }

    [Fact]
    public void Read_changes_nothing_in_the_legacy_in_any_phase()
    {
        var played = new Played(BridgePack.Wide(Pack), 7);
        for (int i = 0; i < 120; i++)
        {
            played.Step();
            string before = Snapshot(played.State);

            Read(played);

            Assert.Equal(before, Snapshot(played.State));
        }
    }

    private static string Snapshot(LegacyState state)
    {
        return System.Text.Json.JsonSerializer.Serialize(new { state.Modules, state.RngStates }, SimJson.Options);
    }

    [Fact]
    public void Aftermath_with_no_team_sent_has_no_outcome_no_mission_and_lists_the_unattended_rolls()
    {
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var played = new Played(Pack, seed);
            played.StepUntil(() => played.State.Phase == CyclePhase.Briefing);
            foreach (CrewMember member in played.State.Module<CrewState>(CrewModule.Name).Members.Skip(1))
            {
                member.Status = CrewStatus.Recovering;
            }

            played.StepUntil(() => played.State.Phase == CyclePhase.Aftermath);
            List<ObjectiveUnattended> rolled = played.CycleEvents().OfType<ObjectiveUnattended>().ToList();
            if (rolled.Count == 0)
            {
                continue;
            }

            BridgeView view = Read(played);

            Assert.Null(view.Mission);
            Assert.Null(view.Aftermath!.Outcome);
            Assert.Equal(rolled.Select(r => r.ObjectiveId), view.Aftermath.Unattended.Select(u => u.ObjectiveId));
            return;
        }

        Assert.Fail("No seed rolled an unattended objective.");
    }

    [Fact]
    public void The_briefing_after_the_team_is_assigned_still_shows_with_that_objective_marked()
    {
        Played played = Until(Pack, p => p.State.Phase == CyclePhase.Briefing);
        played.Step();

        BridgeView view = Read(played);

        Assert.Equal(CyclePhase.Briefing, played.State.Phase);
        Assert.NotNull(view.Briefing);
        Assert.Single(view.Briefing.Objectives, o => o.HasTeam);
    }

    [Theory]
    [InlineData("OTHER_PACK", 1)]
    [InlineData(null, 99)]
    public void A_different_pack_id_or_version_sets_the_pack_warning(string? id, int version)
    {
        var played = new Played(Pack, 1);
        LegacyRecord record = new(played.State, id ?? Pack.Id, version == 99 ? Pack.Version + version : version, "LEGACY", DateTimeOffset.UnixEpoch);

        BridgeView view = BridgeReader.Read(record, Pack, []);

        Assert.False(string.IsNullOrWhiteSpace(view.TopBar.PackWarning));
    }

    [Fact]
    public void No_line_or_change_holds_a_seed_or_a_number_beyond_32_bits()
    {
        ContentPack wide = BridgePack.Wide(Pack);
        var texts = new List<string>();
        // Large seeds, so a leaked one cannot hide inside a small number.
        foreach (ulong seed in new[] { 12345678901234567890UL, 9876543210987654321UL, 5555555555555555555UL })
        {
            var played = new Played(wide, seed);
            for (int step = 0; step < 400 && played.State.Cycle <= 8; step++)
            {
                played.Step();
                BridgeView view = Read(played);
                texts.AddRange(view.Mission?.Log.Select(l => l.Text) ?? []);
                texts.AddRange(view.Aftermath?.Changes.Select(c => c.Text) ?? []);
                texts.Add(view.TopBar.PackWarning ?? string.Empty);
                Assert.DoesNotContain(texts, t => t.Contains(played.State.Seed.ToString()));
            }
        }

        Assert.NotEmpty(texts);
        foreach (string text in texts)
        {
            foreach (Match number in Regex.Matches(text, @"\d+"))
            {
                Assert.True(number.Length < 10 && long.Parse(number.Value) <= int.MaxValue, text);
            }
        }
    }
}
