using GameHost;
using Sim.Content;
using Sim.Core;
using Sim.Core.Content;
using Sim.Crew;
using Sim.Missions;
using SimRunner;

namespace SimRunner.Tests;

internal static class TestPack
{
    public static ContentPack Load()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        string root = directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        return ContentLoader.LoadDirectory(Path.Combine(root, "tests", "fixtures", "content")).Pack ?? throw new InvalidOperationException("The content pack did not load.");
    }

    public static LegacyState NewLegacy(ulong seed)
    {
        return new LegacyState { LegacyId = new Guid((int)(seed >> 32), 0, 0, BitConverter.GetBytes(seed)), Seed = seed };
    }
}

public class SimulationTests
{
    private static readonly ContentPack Pack = TestPack.Load();

    // Step until the briefing, with the site arrived and nothing assigned.
    private static (Game Game, LegacyState State) AtBriefing()
    {
        Game game = Game.Build(Pack);
        LegacyState state = TestPack.NewLegacy(1);
        while (state.Phase != CyclePhase.Briefing || state.Cycle != 1)
        {
            AutoCaptain.Step(game, state, Pack);
        }

        return (game, state);
    }

    [Fact]
    public void The_captain_sends_the_best_team_the_objective_takes_led_by_the_most_senior()
    {
        (Game game, LegacyState state) = AtBriefing();
        ObjectiveDef template = Pack.Objectives.Single(o => o.Id == state.Module<MissionsState>("missions").Site!.Objectives[0].TemplateId);

        TeamAssigned assigned = Assert.IsType<TeamAssigned>(Assert.Single(AutoCaptain.Step(game, state, Pack), e => e.Event is TeamAssigned).Event);

        // By skill sums: CREW_03 19, CREW_01 18, CREW_02 17, CREW_04 16, then the rest.
        string[] byStrength = ["CREW_03", "CREW_01", "CREW_02", "CREW_04"];
        Assert.Equal(byStrength.Take(template.TeamSizeMin), assigned.CrewIds);
        Assert.Equal("CREW_01", assigned.LeadId);
        Assert.Equal(["GEAR_01", "GEAR_02"], assigned.GearIds);
        Assert.Equal(new Parameters(), assigned.Parameters);
        Assert.Equal(state.Module<MissionsState>("missions").Site!.Objectives[0].Id, assigned.ObjectiveId);
    }

    [Fact]
    public void The_captain_sends_nobody_when_too_few_are_fit()
    {
        (Game game, LegacyState state) = AtBriefing();
        foreach (CrewMember member in state.Module<CrewState>("crew").Members.Skip(1))
        {
            member.Status = CrewStatus.Recovering;
        }

        IReadOnlyList<PublishedEvent> events = AutoCaptain.Step(game, state, Pack);

        Assert.DoesNotContain(events, e => e.Event is TeamAssigned);
        Assert.Equal(CyclePhase.Resolution, state.Phase);
    }

    [Fact]
    public void A_cycle_run_plays_missions_and_keeps_the_books()
    {
        SimSummary summary = Simulation.Run(new SimOptions(Legacies: 3, Cycles: 20, Seed: 1), Pack);

        Assert.Equal(new ulong[] { 1, 2, 3 }, summary.Legacies.Select(l => l.Seed));
        int levelsGained = 0;
        Assert.All(summary.Legacies, l =>
        {
            Assert.True(l.Missions.Values.Sum() > 0);
            Assert.Equal(8, l.Alive + l.Deaths);
            List<PublishedEvent> events = Play(Pack, l.Seed, 20).Events;
            Assert.Equal(events.Count(e => e.Event is MissionStarted), l.Missions.Values.Sum());
            levelsGained += events.Count(e => e.Event is SkillRaised);
        });
        Assert.True(levelsGained > 0, "No legacy gained a level.");
    }

    private static (Game Game, LegacyState State, List<PublishedEvent> Events) Play(ContentPack pack, ulong seed, int cycles)
    {
        Game game = Game.Build(pack);
        LegacyState state = TestPack.NewLegacy(seed);
        var events = new List<PublishedEvent>();
        while (state.Cycle <= cycles)
        {
            events.AddRange(AutoCaptain.PlayCycle(game, state, pack));
        }

        return (game, state, events);
    }

    // With a 1d20 check, disasters are common enough to see a death inside a short run.
    [Fact]
    public void A_death_in_a_cycle_run_is_published_once_and_ends_that_crew_members_missions()
    {
        ContentPack wide = Pack with { Tuning = Pack.Tuning with { CheckDice = new DiceDef(1, 20) } };
        int deaths = 0;
        for (ulong seed = 1; seed <= 4; seed++)
        {
            (Game game, LegacyState state, List<PublishedEvent> events) = Play(wide, seed, 15);

            var diedInResults = events.Select(e => e.Event).OfType<MissionEnded>()
                .SelectMany(m => m.Result.Crew.Where(c => c.Status == MemberStatus.Dead).Select(c => (c.Id, m.MissionId)))
                .ToList();
            var died = events.Select(e => e.Event).OfType<CrewDied>().Select(d => (d.CrewId, d.MissionId)).ToList();
            Assert.Equal(diedInResults, died);
            Assert.Equal(8, game.Bus.View<IRosterView>(state).Roster.Count + died.Count);
            var dead = new HashSet<string>();
            foreach (IEvent e in events.Select(e => e.Event))
            {
                if (e is CrewDied d)
                {
                    dead.Add(d.CrewId);
                }

                if (e is TeamAssigned assigned)
                {
                    Assert.DoesNotContain(assigned.CrewIds, dead.Contains);
                }
            }

            deaths += died.Count;
        }

        Assert.True(deaths > 0, "No death in the run; choose other legacies.");
    }

    // Names are display only: naming every item leaves the log hash where it was.
    [Fact]
    public void Naming_every_item_does_not_move_the_log_hash()
    {
        ContentPack named = Pack with
        {
            Skills = Pack.Skills.Select(s => s with { Name = "NAME_PLACEHOLDER_1" }).ToList(),
            Crew = Pack.Crew.Select(c => c with { Name = "NAME_PLACEHOLDER_1" }).ToList(),
            Gear = Pack.Gear.Select(g => g with { Name = "NAME_PLACEHOLDER_1" }).ToList(),
            Objectives = Pack.Objectives.Select(o => o with { Name = "NAME_PLACEHOLDER_1" }).ToList(),
            Assignments = Pack.Assignments.Select(a => a with { Name = "NAME_PLACEHOLDER_1" }).ToList(),
            Encounters = Pack.Encounters.Select(e => e with { Name = "NAME_PLACEHOLDER_1", Decision = e.Decision is null ? null : e.Decision with { Name = "NAME_PLACEHOLDER_1" } }).ToList(),
        };

        SimSummary plain = Simulation.Run(new SimOptions(Legacies: 1, Cycles: 10, Seed: 1), Pack);
        SimSummary withNames = Simulation.Run(new SimOptions(Legacies: 1, Cycles: 10, Seed: 1), named);

        Assert.Equal(plain.Legacies[0].LogHash, withNames.Legacies[0].LogHash);
    }

    // Text is never stored and draws no randomness (slice 1d), so a pack's lines cannot change what a legacy does.
    [Fact]
    public void Lines_and_symbols_never_change_the_event_log()
    {
        var options = new SimOptions(Legacies: 2, Cycles: 6, Seed: 3);

        SimSummary with_ = Simulation.Run(options, Pack);
        SimSummary without = Simulation.Run(options, Pack with { Lines = [], Symbols = [] });

        Assert.NotEmpty(Pack.Lines);
        Assert.Equal(with_.Legacies.Select(l => l.LogHash), without.Legacies.Select(l => l.LogHash));
    }

    // Golden seed: this hash changes only when behaviour changes. If the change is intended, update it in the same commit and say why. Re-pinned: the notice beat is named initiative.
    // Re-pinned in slice 1d: check beats gained MomentumAfter and StrainAfter (additive; no rule changed).
    // Re-pinned for authored approaches: a check's approach is written as its id ("careful") where the enum wrote "Careful"; every other field is unchanged.
    [Fact]
    public void Golden_seed_1_matches_the_accepted_log()
    {
        SimSummary summary = Simulation.Run(new SimOptions(Legacies: 1, Cycles: 10, Seed: 1), Pack);

        Assert.Equal("62963D7EE85A33E8CD4514720BEBC6491D76353DE772F43E65CD4F38B6039484", summary.Legacies[0].LogHash);
    }
}
