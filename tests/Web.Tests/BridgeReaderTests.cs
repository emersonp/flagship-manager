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
            Assert.NotEmpty(o.Skills);
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
    public void Momentum_and_strain_are_the_latest_a_beat_reports()
    {
        Played played = Until(Pack, p => p.State.Phase == CyclePhase.Resolution && p.Mission?.State.Result is not null);
        (int momentum, int strain) = played.CycleEvents().OfType<MissionBeat>()
            .Select(b => b.Beat switch
            {
                StageBeat s => ((int, int)?)(s.Momentum, s.Strain),
                CheckBeat c => (c.Momentum, c.Strain),
                _ => null,
            })
            .Where(x => x is not null)
            .Last()!.Value;

        MissionView mission = Assert.IsType<MissionView>(Read(played).Mission);

        Assert.Equal(momentum, mission.Momentum);
        Assert.Equal(strain, mission.Strain);
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
        Assert.Equal(raised, aftermath.Changes.Count(c => c.Text.Contains(" raises ")));
    }

    [Fact]
    public void Aftermath_changes_follow_event_order_and_include_the_loyalty_change()
    {
        Played played = Until(Pack, p => p.State.Phase == CyclePhase.Aftermath && p.CycleEvents().OfType<MissionEnded>().Any(e => e.Result.Crew.Any(c => c.Loyalty != 0)));
        MissionEnded ended = played.CycleEvents().OfType<MissionEnded>().Single();
        CrewResult changed = ended.Result.Crew.First(c => c.Loyalty != 0);

        List<CrewChange> changes = Assert.IsType<AftermathView>(Read(played).Aftermath).Changes.ToList();

        Assert.Contains(changes, c => c.CrewId == changed.Id && c.Text == $"{changed.Id} loyalty {changed.Loyalty:+0;-0}.");
        string[] expected = played.CycleEvents()
            .Where(e => e is MissionEnded or SkillRaised or CrewInjured or CrewDied or CrewRecovered)
            .SelectMany(e => e switch
            {
                MissionEnded m => m.Result.Crew.Where(c => c.Loyalty != 0).Select(c => c.Id),
                SkillRaised s => [s.CrewId],
                CrewInjured i => [i.CrewId],
                CrewDied d => [d.CrewId],
                CrewRecovered r => [r.CrewId],
                _ => [],
            })
            .ToArray();
        Assert.Equal(expected, changes.Select(c => c.CrewId));
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
        Assert.NotNull(recovering.ReturnsAtWeek);
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
        member.LightInjuriesHealAtWeek.AddRange([5, 6]);
        member.Skills.Values.First().Level = 1;

        CrewRow row = Read(played).Roster.Single(r => r.Id == member.Id);

        Assert.Equal([5, 6], row.LightInjuryHealWeeks);
        Assert.All(row.Skills, s => Assert.Equal(Math.Max(0, s.Level - 2), s.EffectiveLevel));
        Assert.Contains(row.Skills, s => s.Level == 1 && s.EffectiveLevel == 0);
        Assert.Equal(member.Seniority, row.Seniority);
        Assert.Equal(member.Loyalty, row.Loyalty);
        Assert.Equal(member.AwayMissions, row.AwayMissions);
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
