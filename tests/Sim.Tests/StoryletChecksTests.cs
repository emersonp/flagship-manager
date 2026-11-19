using System.Text.Json;
using Sim.Core;
using Sim.Core.Content;
using Sim.Cycle;
using Sim.Storylets;
using static Sim.Tests.StoryletsModuleTests;

namespace Sim.Tests;

// 1e part four, Task 6 (design 4.7): options with requires, and options settled by a check with a handler the captain names.
public class StoryletChecksTests
{
    private const string Offer = "2:STORYLET_TALK";

    private static StoryletOption Check(string id, string skill, int difficulty, StoryletEffect[] success, StoryletEffect[] failure, string? requires = null)
    {
        return new StoryletOption(id, id.ToUpperInvariant(), [], requires is null ? null : new Expr(requires),
            new StoryletCheck(skill, difficulty, new StoryletOutcome(success), new StoryletOutcome(failure)));
    }

    private static StoryletEffect Morale(int amount, string on)
    {
        return new StoryletEffect(Morale: amount, MoraleOn: on);
    }

    // Cast on whoever holds trait_b. talk is a check on SKILL_A; leave is plain; rank is open only to a loyal subject.
    private static StoryletDef Talk(int difficulty = 4, StoryletEffect[]? success = null, StoryletEffect[]? failure = null)
    {
        return StoryletsModuleTests.Crew(
            "STORYLET_TALK",
            3,
            "subject.trait.trait_b >= 1",
            Check("talk", "SKILL_A", difficulty, success ?? [Set("trait_b", 0), Morale(3, "subject")], failure ?? [Set("trait_b", 0), Loyalty(-1, "subject")]),
            Option("leave", Set("trait_b", 0)),
            new StoryletOption("rank", "RANK", [Set("trait_b", 0)], new Expr("subject.loyalty >= 60")));
    }

    // CREW_02 holds trait_b, so the storylet is cast on them and waits in the second cycle's itinerary.
    private static Harness Waiting(StoryletDef storylet, ulong seed = 1)
    {
        var h = new Harness(Pack(2, storylet), seed);
        h.Mission(Raise("trait_b", 1, "CREW_02"));
        Assert.Equal([Offer], h.Waiting);
        return h;
    }

    private static string Snapshot(Harness h)
    {
        return JsonSerializer.Serialize(h.State, SimJson.Options);
    }

    private static IReadOnlyList<OpenAnswer> Answers(Harness h)
    {
        return h.Bus.View<IStoryletView>(h.State).Answers(Offer);
    }

    [Fact]
    public void A_check_option_needs_a_handler()
    {
        Harness h = Waiting(Talk());
        string before = Snapshot(h);

        Assert.Equal("That answer is settled by a check: name who goes.", h.Refused(new AnswerStorylet(Offer, "talk")));
        Assert.Equal(before, Snapshot(h));

        List<IEvent> events = h.Send(new AnswerStorylet(Offer, "talk", "CREW_01"));

        StoryletAnswered answered = events.OfType<StoryletAnswered>().Single();
        Assert.Equal(("talk", "CREW_01", "subject=CREW_02"), (answered.OptionId, answered.HandlerId, Show(answered.Cast)));
        Assert.NotNull(answered.Roll);
        Assert.Empty(h.Waiting);
    }

    [Fact]
    public void A_handler_is_refused_on_a_plain_option()
    {
        Harness h = Waiting(Talk());

        Assert.Equal("Only an answer settled by a check sends someone.", h.Refused(new AnswerStorylet(Offer, "leave", "CREW_01")));

        StoryletAnswered answered = h.Send(new AnswerStorylet(Offer, "leave")).OfType<StoryletAnswered>().Single();
        Assert.Equal(((string?)null, (StoryletRoll?)null), (answered.HandlerId, answered.Roll));
    }

    // Living, fit, aboard, and not in the cast (design 4.7). The view lists exactly those the guard accepts, in roster order.
    [Fact]
    public void A_cast_member_cannot_handle()
    {
        var h = new Harness(Pack(2, Talk()));
        h.To(CyclePhase.Resolution);
        h.End([Raise("trait_b", 1, "CREW_02")], Died("CREW_08"), new CrewResult("CREW_04", MemberStatus.Incapacitated, 0, new Dictionary<string, double>(), 0));
        h.To(CyclePhase.Itinerary);
        string before = Snapshot(h);

        Assert.Equal("Crew member 'CREW_02' is in this storylet's cast, so cannot be sent.", h.Refused(new AnswerStorylet(Offer, "talk", "CREW_02")));
        Assert.Equal("Crew member 'CREW_04' is not fit to send.", h.Refused(new AnswerStorylet(Offer, "talk", "CREW_04")));
        Assert.Equal("Crew member 'CREW_08' is not on the roster.", h.Refused(new AnswerStorylet(Offer, "talk", "CREW_08")));
        Assert.Equal("Crew member 'CREW_99' is not on the roster.", h.Refused(new AnswerStorylet(Offer, "talk", "CREW_99")));
        Assert.Equal(before, Snapshot(h));

        OpenAnswer talk = Answers(h).Single(a => a.OptionId == "talk");
        Assert.Equal(["CREW_01", "CREW_03", "CREW_05", "CREW_06", "CREW_07"], talk.Handlers!.Select(o => o.CrewId));
        Assert.Null(Answers(h).Single(a => a.OptionId == "leave").Handlers);
    }

    // The roll draws from storylet-check:<offer id>, so the same offer rolls the same dice whoever is sent, and the same seed the same
    // offer.
    [Fact]
    public void The_roll_is_the_same_for_the_same_offer()
    {
        StoryletRoll Roll(ulong seed, string handler)
        {
            Harness h = Waiting(Talk(), seed);
            return h.Send(new AnswerStorylet(Offer, "talk", handler)).OfType<StoryletAnswered>().Single().Roll!;
        }

        int dice = TestContent.Pack.Tuning.CheckDice.Roll(RngStream.For(1, 2, $"storylet-check:{Offer}"));
        StoryletRoll first = Roll(1, "CREW_01");
        StoryletRoll again = Roll(1, "CREW_01");
        StoryletRoll other = Roll(1, "CREW_03");

        Assert.Equal(dice, first.Dice);
        Assert.Equal((first.Dice, first.Success), (again.Dice, again.Success));
        Assert.Equal(dice, other.Dice);
        Assert.Equal(("SKILL_A", 6, 0, 11), (first.Skill, first.Level, first.Morale, first.Against));
        Assert.Equal(("SKILL_A", 5), (other.Skill, other.Level));
        Assert.Equal(first.Level + first.Dice - first.Against >= 0, first.Success);
        Assert.Contains(Enumerable.Range(1, 40).Select(s => Roll((ulong)s, "CREW_01").Dice), d => d != dice);
    }

    // Difficulty 1 against SKILL_A 6: at least 8 against 8, always. Difficulty 20: at most 18 against 27, never.
    [Fact]
    public void Success_and_failure_apply_their_own_effects()
    {
        Harness sure = Waiting(Talk(difficulty: 1));
        List<IEvent> won = sure.Send(new AnswerStorylet(Offer, "talk", "CREW_01"));

        Assert.Equal([new TraitChanged("trait_b", "CREW_02", null, 1, 0, TraitCauses.Answer, null)], won.OfType<TraitChanged>());
        Assert.Equal(["CREW_02"], won.OfType<MoraleChanged>().Select(m => m.CrewId));
        Assert.Empty(won.OfType<LoyaltyChanged>());
        Assert.True(won.OfType<StoryletAnswered>().Single().Roll!.Success);

        Harness hopeless = Waiting(Talk(difficulty: 20));
        List<IEvent> lost = hopeless.Send(new AnswerStorylet(Offer, "talk", "CREW_01"));

        Assert.Equal([new TraitChanged("trait_b", "CREW_02", null, 1, 0, TraitCauses.Answer, null)], lost.OfType<TraitChanged>());
        Assert.Equal([new LoyaltyChanged("CREW_02", 50, 49)], lost.OfType<LoyaltyChanged>());
        Assert.Empty(lost.OfType<MoraleChanged>());
        Assert.False(lost.OfType<StoryletAnswered>().Single().Roll!.Success);
        Assert.IsType<StoryletAnswered>(lost[^1]);
    }

    // The handler is the slot "handler" in the outcome's effects, for loyalty, morale, on, and toward, and never joins the stored cast.
    [Fact]
    public void Handler_is_readable_in_the_outcome_text_and_effects()
    {
        StoryletEffect[] effects =
        [
            Set("trait_b", 0),
            Loyalty(2, StoryletScopes.Handler),
            Morale(-3, StoryletScopes.Handler),
            new StoryletEffect(Trait: new TraitChange("trait_h", 1, false, "", StoryletScopes.Handler)),
            new StoryletEffect(Trait: new TraitChange("trait_j", 1, false, StoryletScopes.Handler)),
        ];
        Harness h = Waiting(Talk(difficulty: 1, success: effects));

        List<IEvent> events = h.Send(new AnswerStorylet(Offer, "talk", "CREW_03"));

        Assert.Equal([new LoyaltyChanged("CREW_03", 50, 52)], events.OfType<LoyaltyChanged>());
        Assert.Equal(["CREW_03"], events.OfType<MoraleChanged>().Select(m => m.CrewId));
        Assert.Equal(
            [
                new TraitChanged("trait_b", "CREW_02", null, 1, 0, TraitCauses.Answer, null),
                new TraitChanged("trait_h", "CREW_02", "CREW_03", 0, 1, TraitCauses.Answer, null),
                new TraitChanged("trait_j", "CREW_03", null, 0, 1, TraitCauses.Answer, null),
            ],
            events.OfType<TraitChanged>());
        Assert.Equal("subject=CREW_02", Show(events.OfType<StoryletAnswered>().Single().Cast));
        Assert.All(h.Storylets.Arrivals, a => Assert.DoesNotContain("CREW_03", a.Cast.Values));
    }

    [Fact]
    public void An_option_whose_requires_fails_is_refused()
    {
        Harness h = Waiting(Talk());

        Assert.Equal(["talk", "leave"], Answers(h).Select(a => a.OptionId));
        Assert.Equal("That answer is not open to this storylet now.", h.Refused(new AnswerStorylet(Offer, "rank")));

        h.Crew.Members.Single(m => m.Id == "CREW_02").Loyalty = 60;

        Assert.Equal(["talk", "leave", "rank"], Answers(h).Select(a => a.OptionId));
        Assert.Equal("rank", h.Send(new AnswerStorylet(Offer, "rank")).OfType<StoryletAnswered>().Single().OptionId);
    }

    // Score = skill + morale modifier + trait check bonuses, with withTarget read against the cast and the handler (design 4.7).
    [Fact]
    public void The_score_adds_morale_and_the_trait_bonuses_the_cast_gives()
    {
        var h = new Harness(Pack(2, Talk()));
        h.To(CyclePhase.Resolution);
        h.End(
            [
                Raise("trait_b", 1, "CREW_02"),
                new MissionTrait("trait_h", 1, false, "CREW_01", Encounter, "CREW_02"),
                new MissionTrait("trait_g", 1, false, "CREW_01", Encounter, "CREW_05"),
            ]);
        h.To(CyclePhase.Itinerary);
        h.Crew.Members.Single(m => m.Id == "CREW_01").Morale = 80;
        h.Crew.Members.Single(m => m.Id == "CREW_03").Morale = 10;

        IReadOnlyList<HandlerOdds> odds = Answers(h).Single(a => a.OptionId == "talk").Handlers!;

        // CREW_01: SKILL_A 6, morale +1, trait_h +1 toward the subject; trait_g toward CREW_05, who is not in the cast, gives nothing.
        Assert.Equal(new HandlerOdds("CREW_01", 6, 8, 11, Chances.Of(TestContent.Pack.Tuning.CheckDice, 8, 11)), odds[0]);
        Assert.Equal(new HandlerOdds("CREW_03", 5, 4, 11, Chances.Of(TestContent.Pack.Tuning.CheckDice, 4, 11)), odds[1]);

        StoryletRoll roll = h.Send(new AnswerStorylet(Offer, "talk", "CREW_01")).OfType<StoryletAnswered>().Single().Roll!;

        Assert.Equal((6, 1, 8), (roll.Level, roll.Morale, roll.Score));
        Assert.Equal([new TraitBonus("trait_h", 1)], roll.Traits);
    }

    // On 2d6: sure when the dice need 3 or less, likely 4 to 6, even 7 or 8, a long shot 9 or more.
    [Theory]
    [InlineData(8, Chance.Sure)]
    [InlineData(7, Chance.Likely)]
    [InlineData(5, Chance.Likely)]
    [InlineData(4, Chance.Even)]
    [InlineData(3, Chance.Even)]
    [InlineData(2, Chance.LongShot)]
    [InlineData(-5, Chance.LongShot)]
    public void The_chance_word_comes_from_the_dice(int score, Chance chance)
    {
        Assert.Equal(chance, Chances.Of(new DiceDef(2, 6), score, 11));
    }

    [Fact]
    public void The_chance_words()
    {
        Assert.Equal(["long shot", "even", "likely", "sure"], Enum.GetValues<Chance>().Select(Chances.Word));
        Assert.Equal(1.0, new DiceDef(2, 6).AtLeast(2));
        Assert.Equal(1 / 36.0, new DiceDef(2, 6).AtLeast(12), 9);
        Assert.Equal(0.0, new DiceDef(2, 6).AtLeast(13));
    }
}
