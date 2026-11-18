using GameHost;
using Sim.Content;
using Sim.Core;
using Sim.Core.Content;
using Sim.Crew;
using Sim.Missions;
using Sim.Storylets;
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

    // 30 cycles since the lead chooses by skill (approaches plan, Task 4): experience goes to members' best skills, which take longer to raise.
    [Fact]
    public void A_cycle_run_plays_missions_and_keeps_the_books()
    {
        SimSummary summary = Simulation.Run(new SimOptions(Legacies: 3, Cycles: 30, Seed: 1), Pack);

        Assert.Equal(new ulong[] { 1, 2, 3 }, summary.Legacies.Select(l => l.Seed));
        int levelsGained = 0;
        Assert.All(summary.Legacies, l =>
        {
            Assert.True(l.Missions.Values.Sum() > 0);
            Assert.Equal(8, l.Alive + l.Deaths);
            List<PublishedEvent> events = Play(Pack, l.Seed, 30).Events;
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

    // The captain answers every storylet with its first option, so none is left waiting and none is lost. The fixture's flags are
    // set only by a partial at ENCOUNTER_01 and a disaster at ENCOUNTER_04, which the captain's teams rarely meet: these are the
    // legacies among the first 400 that see a storylet within 50 cycles. Morale (1e part two) made them rarer: a
    // winning team rises to high morale and rolls fewer partials.
    [Theory]
    [InlineData(69ul)]
    [InlineData(83ul)]
    [InlineData(104ul)]
    public void Fifty_cycles_of_the_fixture_end_with_nothing_waiting_and_every_offer_answered_or_withdrawn(ulong seed)
    {
        (_, LegacyState state, List<PublishedEvent> events) = Play(Pack, seed, 50);
        List<IEvent> log = events.Select(e => e.Event).ToList();

        Assert.True(log.OfType<StoryletOffered>().Any(), "No storylet was offered; choose other legacies.");
        Assert.True(!state.TryModule(StoryletsModule.Name, out StoryletsState? storylets) || storylets.Waiting.Count == 0);
        Assert.Equal(log.OfType<StoryletOffered>().Count(), log.OfType<StoryletAnswered>().Count() + log.OfType<StoryletWithdrawn>().Count());
        Assert.All(log.OfType<StoryletAnswered>(), a => Assert.Equal("option_a", a.OptionId));
    }

    // 1e part two: vignettes arrive in the itinerary; the captain leaves every one with a choice to its default when it closes.
    [Fact]
    public void Vignettes_arrive_and_each_is_settled_once_by_its_effects_or_its_default()
    {
        (_, LegacyState state, List<PublishedEvent> events) = Play(Pack, 1, 30);
        List<IEvent> log = events.Select(e => e.Event).ToList();
        int waiting = state.TryModule(StoryletsModule.Name, out StoryletsState? storylets) ? storylets.Vignettes.Count : 0;

        Assert.True(log.OfType<VignetteArrived>().Count() > 10);
        Assert.Equal(log.OfType<VignetteArrived>().Count(), log.OfType<VignetteAnswered>().Count() + waiting);
        Assert.Equal(log.OfType<VignetteAnswered>().Count(), log.OfType<VignetteAnswered>().Select(a => a.OfferId).Distinct().Count());
        Assert.All(log.OfType<VignetteAnswered>().Where(a => a.OptionId is not null), a => Assert.True(a.Defaulted));
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
            Storylets = Pack.Storylets.Select(s => s with { Name = "NAME_PLACEHOLDER_1" }).ToList(),
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
    // Re-pinned: approaches are chosen by the lead's skill and outcomes are authored (approaches plan, Task 4).
    // Re-pinned: a mission's result and a check's beat carry the flags they changed (1e part one, Task 1).
    // Re-pinned: morale changes after missions and each week, and moves checks at its thresholds (1e part two, Task 1).
    // Re-pinned: the cycle opens on the captain's itinerary, where storylets and vignettes arrive (1e part two, Task 3).
    // Re-pinned: flags renamed traits, in their own module (1e part three, Task 1); SimRunner's figures for 20 x 50 are unchanged.
    // Re-pinned: pair traits (1e part three, Task 3); every offer event carries a target, null but for a pair's.
    [Fact]
    public void Golden_seed_1_matches_the_accepted_log()
    {
        SimSummary summary = Simulation.Run(new SimOptions(Legacies: 1, Cycles: 10, Seed: 1), Pack);

        Assert.Equal("1AB196F04561E4476EC51EEAAB90670E8E6106259A647856F4909C0FB619D062", summary.Legacies[0].LogHash);
    }
}
