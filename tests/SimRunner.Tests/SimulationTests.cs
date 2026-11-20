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
        return ContentLoader.LoadDirectory(Path.Combine(Root(), "tests", "fixtures", "content")).Pack ?? throw new InvalidOperationException("The content pack did not load.");
    }

    public static string Root()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
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
    // Re-pinned: launch rules and trait bonuses (1e part three, Task 4).
    // Re-pinned: storylets cast in expressions (1e part four, Task 4). Casting draws from cast:<id>:<cycle>, offers are {cycle}:{id}
    // and arrive once a cycle, and the offer events are version 2 with the cast. In SimRunner's 200 x 200 only "A week aboard" moves
    // (101.7 to 99.6 per 100 cycles, once a cycle), with one or two missions of 40,000 after it and, under random answers, picks
    // hashed with the new offer ids.
    // Re-pinned: salience and cooldowns (1e part four, Task 5). StoryletOffered and VignetteArrived carry their Salience, and what
    // arrives together comes in salience order, its ties drawn from salience:<cycle>. In SimRunner's 200 x 200, both packs and both
    // answer policies, every legacy plays the same: no count of missions, deaths, fires, answers, or traits moves. Both packs'
    // storylets and vignettes seldom hold together, so salience reorders little; the new field moves every hash.
    // Re-pinned: options settled by a check (1e part four, Task 6). The fixture gains STORYLET_04, cast from trait_h, whose first
    // option is a check (stream storylet-check:<offer>) and whose answers clear trait_h. In SimRunner's 200 x 200 the game's pack
    // does not move under either policy. In the fixture STORYLET_04 fires 24.6 per 100 cycles, trait_h is set from 0 24.6 times
    // instead of 0.5 (the encounter raises it again once it is cleared), and missions move from 39077 full, 811 partial, 80
    // incomplete, 27 withdrawn to 38834, 1028, 101, 32, since trait_h's +1 withTarget bonus is lost while it is clear: with answers
    // that keep trait_h, the missions are exactly as before. Under random answers option_b (requires loyalty 60) never fires and
    // option_a and option_c split evenly.
    // Not moved by the simulator naming handlers (1e part four, Task 8): this run's one check is cast on CREW_03 and CREW_01, so
    // CREW_02 is both first in roster order and the most skilled, and the fixture's STORYLET_05, which never casts, publishes nothing.
    // Re-pinned: time kept in days (slice 1e-calendar, Task 2). Each turn rolls 5 to 7 days on the calendar stream and publishes
    // CycleLengthRolled and one DayReached per day where WeekAdvanced was, so heals, returns, the drift, and trait steps fall on
    // days. With the roll pinned at 6, SimRunner's 200 x 200 is exactly as before under both policies but for 6 more events a
    // turn; with the roll, the fixture moves from 38834 full, 1028 partial, 101 incomplete, 32 withdrawn, 5 lost to 38826, 1033,
    // 103, 32, 6, as injuries heal and the incapacitated return between cycles.
    [Fact]
    public void Golden_seed_1_matches_the_accepted_log()
    {
        SimSummary summary = Simulation.Run(new SimOptions(Legacies: 1, Cycles: 10, Seed: 1), Pack);

        Assert.Equal("15E7E2C9712A840B81CD02FFD22C7EE40CA4BD4089AE1A6B5CEA43E71D72C910", summary.Legacies[0].LogHash);
    }

    // --- The captain's policies and promises (1e part three, Task 6) ---

    [Fact]
    public void Random_answers_are_reproducible()
    {
        var options = new SimOptions(Legacies: 2, Cycles: 15, Seed: 3, Answers: AnswerPolicy.Random);

        Assert.Equal(Simulation.Run(options, Pack).Legacies.Select(l => l.LogHash), Simulation.Run(options, Pack).Legacies.Select(l => l.LogHash));
    }

    // Focus 5: the policy draws only from its own stream, hashed with the offer, so choosing an answer moves no game stream and the
    // same offer always gets the same answer. A waiting storylet is set up by hand: the fixture's storylets do not come up in play.
    [Fact]
    public void The_random_policy_draws_from_no_game_stream()
    {
        Game game = Game.Build(Pack);
        LegacyState state = TestPack.NewLegacy(1);
        AutoCaptain.Step(game, state, Pack);
        game.Bus.Send(state, new SetTrait("trait_a", null, null, 1));
        while (state.Phase != CyclePhase.Itinerary || state.Cycle != 2)
        {
            AutoCaptain.Step(game, state, Pack);
        }

        string streams = string.Join(",", state.RngStates.Select(r => $"{r.Key}={r.Value}"));
        var first = (AnswerStorylet)AutoCaptain.NextCommand(game, state, Pack, AnswerPolicy.First);
        var random = (AnswerStorylet)AutoCaptain.NextCommand(game, state, Pack, AnswerPolicy.Random);

        Assert.Equal(("2:STORYLET_01", "option_a"), (first.OfferId, first.OptionId));
        Assert.Equal(random, AutoCaptain.NextCommand(game, state, Pack, AnswerPolicy.Random));
        Assert.Equal(streams, string.Join(",", state.RngStates.Select(r => $"{r.Key}={r.Value}")));
    }

    [Fact]
    public void The_captain_keeps_a_promise_when_it_can()
    {
        (Game game, LegacyState state) = AtBriefing();
        var before = (AssignTeam)AutoCaptain.NextCommand(game, state, Pack);
        game.Bus.Send(state, new SetTrait("trait_e", before.CrewIds[0], null, 1));

        var after = (AssignTeam)AutoCaptain.NextCommand(game, state, Pack);

        Assert.DoesNotContain(before.CrewIds[0], after.CrewIds);
        Assert.Equal(before.CrewIds.Count, after.CrewIds.Count);
    }

    [Fact]
    public void The_captain_breaks_it_rather_than_send_nobody()
    {
        (Game game, LegacyState state) = AtBriefing();
        IReadOnlyList<string> roster = game.Bus.View<IRosterView>(state).Roster;
        foreach (string crewId in roster)
        {
            game.Bus.Send(state, new SetTrait("trait_e", crewId, null, 1));
        }

        var team = (AssignTeam)AutoCaptain.NextCommand(game, state, Pack);
        ObjectiveDef template = Pack.Objectives.Single(o => o.Id == state.Module<MissionsState>("missions").Site!.Objectives[0].TemplateId);

        Assert.Equal(template.TeamSizeMin, team.CrewIds.Count);
    }

    // --- Handlers and the reports beside the fire counts (1e part four, Task 8) ---

    // STORYLET_04 cast on CREW_03 toward CREW_04 and waiting in the second itinerary, so everyone else fit may be sent.
    private static (Game Game, LegacyState State) WaitingCheck(ContentPack pack, ulong seed)
    {
        Game game = Game.Build(pack);
        LegacyState state = TestPack.NewLegacy(seed);
        AutoCaptain.Step(game, state, pack);
        game.Bus.Send(state, new SetTrait("trait_h", "CREW_03", "CREW_04", 1));
        while (state.Phase != CyclePhase.Itinerary || state.Cycle != 2)
        {
            AutoCaptain.Step(game, state, pack);
        }

        Assert.Contains(state.Module<StoryletsState>(StoryletsModule.Name).Waiting, w => w.OfferId == "2:STORYLET_04" && w.Cast["subject"] == "CREW_03");
        return (game, state);
    }

    private static IReadOnlyList<HandlerOdds> Handlers(Game game, LegacyState state)
    {
        return game.Bus.View<IStoryletView>(state).Answers("2:STORYLET_04").Single(a => a.OptionId == "option_a").Handlers!;
    }

    private static string Streams(LegacyState state)
    {
        return string.Join(",", state.RngStates.Select(r => $"{r.Key}={r.Value}"));
    }

    private static int Sum(SimSummary summary, Func<LegacySummary, IReadOnlyDictionary<string, int>> of, string key)
    {
        return summary.Legacies.Sum(l => of(l).GetValueOrDefault(key));
    }

    private static string Report(int legacies, int cycles, string answers = "first")
    {
        var output = new StringWriter();
        string content = Path.Combine(TestPack.Root(), "tests", "fixtures", "content");
        Assert.Equal(0, Cli.Run(["cycles", "--legacies", $"{legacies}", "--cycles", $"{cycles}", "--answers", answers, "--content", content], output, new StringWriter()));
        return output.ToString();
    }

    private static string? Line(string report, string start)
    {
        return report.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith(start, StringComparison.Ordinal));
    }

    // Under first, the most skilled in the check's skill goes, ties to roster order. On SKILL_B CREW_01 has 5 and CREW_02 6, and the
    // cast is CREW_03 and CREW_04, so CREW_01 is first in roster order and CREW_02 the most skilled.
    [Fact]
    public void First_picks_the_most_skilled_handler()
    {
        (Game game, LegacyState state) = WaitingCheck(Pack, 1);
        var answer = (AnswerStorylet)AutoCaptain.NextCommand(game, state, Pack);

        Assert.Equal("CREW_01", Handlers(game, state)[0].CrewId);
        Assert.Equal(("option_a", "CREW_02"), (answer.OptionId, answer.HandlerId));

        // With CREW_01 raised to 6, the two tie and roster order gives it to CREW_01.
        ContentPack tied = Pack with
        {
            Crew = Pack.Crew.Select(c => c.Id == "CREW_01" ? c with { Skills = new Dictionary<string, int>(c.Skills) { ["SKILL_B"] = 6 } } : c).ToList(),
        };
        (game, state) = WaitingCheck(tied, 1);
        Assert.Equal(6, Handlers(game, state).Single(h => h.CrewId == "CREW_02").Skill);
        Assert.Equal("CREW_01", ((AnswerStorylet)AutoCaptain.NextCommand(game, state, tied)).HandlerId);
    }

    // Under random, the handler comes from sim-captain alone: the same offer always sends the same one, no game stream moves, and
    // over several legacies more than one is sent.
    [Fact]
    public void Random_handlers_are_reproducible()
    {
        var sent = new HashSet<string>();
        for (ulong seed = 1; seed <= 12; seed++)
        {
            (Game game, LegacyState state) = WaitingCheck(Pack, seed);
            string streams = Streams(state);
            var answer = (AnswerStorylet)AutoCaptain.NextCommand(game, state, Pack, AnswerPolicy.Random);

            Assert.Equal(answer, AutoCaptain.NextCommand(game, state, Pack, AnswerPolicy.Random));
            Assert.Equal(streams, Streams(state));
            if (answer.HandlerId is { } handler)
            {
                Assert.Contains(handler, Handlers(game, state).Select(h => h.CrewId));
                sent.Add(handler);
            }
        }

        Assert.True(sent.Count > 1, $"Only {string.Join(", ", sent)} was sent.");
        var options = new SimOptions(Legacies: 3, Cycles: 30, Seed: 60, Answers: AnswerPolicy.Random);
        Assert.Equal(Simulation.Run(options, Pack).Legacies.Select(l => l.LogHash), Simulation.Run(options, Pack).Legacies.Select(l => l.LogHash));
    }

    // STORYLET_05's first slot holds for whoever was hurt last cycle, and its second for no one, so it is eligible and never cast.
    [Fact]
    public void Never_cast_is_told_apart_from_never_eligible()
    {
        SimSummary summary = Simulation.Run(new SimOptions(Legacies: 10, Cycles: 30, Seed: 1), Pack);

        Assert.True(Sum(summary, l => l.Eligible, "STORYLET_05") > 0);
        Assert.Equal(0, Sum(summary, l => l.Cast, "STORYLET_05"));
        Assert.True(Sum(summary, l => l.Cast, "STORYLET_04") > 0);
        Assert.All(summary.Legacies, l => Assert.All(l.Cast, c => Assert.True(c.Value <= l.Eligible.GetValueOrDefault(c.Key), c.Key)));

        string report = Report(10, 30);
        Assert.Matches(@"^Never cast, though eligible: (.*, )?STORYLET_05(, .*)?\.$", Line(report, "Never cast")!);
        Assert.DoesNotContain("STORYLET_05", Line(report, "Never eligible") ?? "", StringComparison.Ordinal);
        Assert.Equal("Out of tries: none.", Line(report, "Out of tries"));
    }

    // Each check option's successes over the times it was taken, read from the rolls.
    [Fact]
    public void Check_success_rates_are_reported()
    {
        SimSummary summary = Simulation.Run(new SimOptions(Legacies: 4, Cycles: 40, Seed: 1), Pack);
        List<StoryletAnswered> rolled = summary.Legacies
            .SelectMany(l => Play(Pack, l.Seed, 40).Events.Select(e => e.Event).OfType<StoryletAnswered>())
            .Where(a => a.Roll is not null)
            .ToList();
        int succeeded = rolled.Count(a => a.Roll!.Success);

        Assert.True(succeeded > 0 && succeeded < rolled.Count, $"{succeeded} of {rolled.Count}");
        Assert.Equal(rolled.Count, Sum(summary, l => l.ChecksTaken, "STORYLET_04 option_a"));
        Assert.Equal(succeeded, Sum(summary, l => l.ChecksSucceeded, "STORYLET_04 option_a"));

        string percent = (succeeded * 100.0 / rolled.Count).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal($"Check success: STORYLET_04 option_a {percent}% of {rolled.Count} taken.", Line(Report(4, 40), "Check success"));
    }

    // A storylet that casts but finds the budget full is lost on it. With no room at all, every cast is lost and none arrives.
    [Fact]
    public void Losses_on_the_budget_are_reported()
    {
        SimSummary none = Simulation.Run(new SimOptions(Legacies: 4, Cycles: 40, Seed: 1), Pack with { StoryletsPerCycle = 0 });

        Assert.True(Sum(none, l => l.Lost, "STORYLET_04") > 0);
        Assert.Equal(Sum(none, l => l.Cast, "STORYLET_04"), Sum(none, l => l.Lost, "STORYLET_04"));
        Assert.Equal(0, Sum(none, l => l.Fired, "STORYLET_04"));

        // With room, what was cast either arrived or was lost.
        SimSummary room = Simulation.Run(new SimOptions(Legacies: 4, Cycles: 40, Seed: 1), Pack);
        foreach (StoryletDef storylet in Pack.Storylets)
        {
            Assert.Equal(Sum(room, l => l.Cast, storylet.Id), Sum(room, l => l.Fired, storylet.Id) + Sum(room, l => l.Lost, storylet.Id));
        }

        Assert.Matches(@"^Lost on the budget per 100 cycles: STORYLET_04 \d+\.\d of \d+\.\d cast", Line(Report(4, 40), "Lost on the budget")!);
    }
}
