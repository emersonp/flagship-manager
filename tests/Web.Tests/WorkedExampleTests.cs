using System.Net;
using System.Text.RegularExpressions;
using GameHost;
using Sim.Content;
using Sim.Core;
using Sim.Core.Content;
using Sim.Crew;
using Sim.Missions;
using Sim.Storylets;
using Sim.Traits;
using SimRunner;
using Web.Bridge;

namespace Web.Tests;

// Slice 1e, part three's worked example, played on the game's own pack with the commands Dev Play and the bridge send (design,
// "Ends with"). The game's missions rarely shake anyone in a short run, so Dev Play's SetTrait does what the Collapsed Gallery would.
public sealed class WorkedExampleTests
{
    private static readonly ContentPack Pack = LoadGame();

    private static ContentPack LoadGame()
    {
        string root = TestApp.RepositoryRoot();
        ContentLoadResult result = ContentLoader.LoadDirectory(Path.Combine(root, "content"));
        return result.Pack ?? throw new InvalidOperationException(string.Join(" | ", result.Errors.Select(e => e.Message)));
    }

    private sealed class Legacy
    {
        public Game Game { get; } = Game.Build(Pack);

        public LegacyState State { get; } = new() { LegacyId = Guid.NewGuid(), Seed = 7 };

        public List<IEvent> Events { get; } = [];

        public List<IEvent> Send(ICommand command)
        {
            List<IEvent> events = Game.Bus.Send(State, command).Select(p => p.Event).ToList();
            Events.AddRange(events);
            return events;
        }

        // The captain's moves, but stop at a phase: the test decides what happens there.
        public void To(CyclePhase phase, int cycle)
        {
            while (State.Phase != phase || State.Cycle != cycle)
            {
                ICommand next = AutoCaptain.NextCommand(Game, State, Pack);
                Send(next is AnswerStorylet || (next is AssignTeam && State.Phase == CyclePhase.Briefing) ? new AdvancePhase() : next);
            }
        }

        public CrewMember Member(string id) => State.Module<CrewState>(CrewModule.Name).Members.Single(m => m.Id == id);

        public int Trait(string trait, string? crewId, string? targetId = null) => Game.Bus.View<ITraitView>(State).Value(trait, crewId, targetId);

        public WaitingStorylet Waiting(string storyletId) => State.Module<StoryletsState>(StoryletsModule.Name).Waiting.Single(w => w.StoryletId == storyletId);
    }

    [Fact]
    public void A_shaken_crew_member_is_promised_a_rest_and_the_broken_promise_comes_back_as_a_storylet()
    {
        var legacy = new Legacy();
        legacy.Send(new FoundLegacy());
        string subject = legacy.Game.Bus.View<IRosterView>(legacy.State).Roster[0];
        legacy.Send(new SetTrait("shaken", subject, null, 2));
        legacy.To(CyclePhase.Itinerary, 2);

        // "Someone shaken": stand them down.
        legacy.Send(new AnswerStorylet(legacy.Waiting("STORYLET_02").OfferId, "stand_down"));
        Assert.Equal((1, 1), (legacy.Trait("shaken_heard", subject), legacy.Trait("stood_down", subject)));

        // The briefing says what sending them would cost; the captain sends them anyway.
        legacy.To(CyclePhase.Briefing, 2);
        BriefingView briefing = BridgeReader.Read(new Persistence.LegacyRecord(legacy.State, Pack.Id, Pack.Version, "WORKED", DateTimeOffset.UnixEpoch), Pack, legacy.Events).Briefing!;
        Assert.Contains(briefing.Chips, c => c.HolderId == subject && c.Text == "Promised a rest");
        LaunchRow row = Assert.Single(briefing.Launch);
        Assert.Equal((subject, LaunchWhen.Sent), (row.HolderId, row.When));
        Assert.EndsWith("Stood down: Stood down to broken, Loyalty −2, Morale −4", row.Text, StringComparison.Ordinal);

        var team = (AssignTeam)AutoCaptain.NextCommand(legacy.Game, legacy.State, Pack);
        Assert.DoesNotContain(subject, team.CrewIds);
        (int loyalty, int morale) = (legacy.Member(subject).Loyalty, legacy.Member(subject).Morale);
        legacy.Send(team with { CrewIds = [subject, .. team.CrewIds.Skip(1)], LeadId = subject });
        legacy.To(CyclePhase.Resolution, 2);

        Assert.Equal(2, legacy.Trait("stood_down", subject));
        Assert.Equal(loyalty - 2, legacy.Member(subject).Loyalty);
        Assert.Contains(legacy.Events, e => e is MoraleChanged { Cause: MoraleCauses.Trait, From: var from, To: var to } m && m.CrewId == subject && to - from == -4 && from == morale);

        // The next itinerary: "You gave your word".
        legacy.To(CyclePhase.Itinerary, 3);
        WaitingStorylet word = legacy.Waiting("STORYLET_03");
        Assert.Equal(subject, word.Cast["subject"]);
        legacy.Send(new AnswerStorylet(word.OfferId, "apologise"));
        Assert.Equal(0, legacy.Trait("stood_down", subject));
    }

    [Fact]
    public void A_pair_who_avoid_each_other_are_kept_apart_and_drift_back_into_each_others_way()
    {
        var legacy = new Legacy();
        legacy.Send(new FoundLegacy());
        IReadOnlyList<string> roster = legacy.Game.Bus.View<IRosterView>(legacy.State).Roster;
        (string holder, string target) = (roster[1], roster[2]);
        legacy.Send(new SetTrait("avoids", holder, target, 3));
        legacy.To(CyclePhase.Itinerary, 2);

        WaitingStorylet air = legacy.Waiting("STORYLET_04");
        Assert.Equal((holder, target), (air.Cast["subject"], air.Cast["target"]));
        legacy.Send(new AnswerStorylet(air.OfferId, "keep_apart"));
        Assert.Equal(5, legacy.Trait("avoids", holder, target));

        // The pair's vignette arrives once it has waited longest: in cycle 2 the rested crew's "A week aboard" filled the three places.
        legacy.To(CyclePhase.Itinerary, 3);
        Assert.Contains(legacy.Events, e => e is VignetteArrived { VignetteId: "VIGNETTE_06" } v && v.Cast["subject"] == holder && v.Cast["target"] == target);

        // Four weeks unchanged, and the weekly step brings it back into "Clear the air"'s range.
        legacy.To(CyclePhase.Itinerary, 6);
        Assert.Equal(4, legacy.Trait("avoids", holder, target));
        Assert.Equal((holder, target), (legacy.Waiting("STORYLET_04").Cast["subject"], legacy.Waiting("STORYLET_04").Cast["target"]));
    }

    // Slice 1e-calendar's worked example (design 4.6): "The ghosts have names now" needs three cycles and 40 days since "A quirk in
    // the sensors". Three ordinary cycles bring the cycles (15 to 21 days) but never the days; a journey of 5 weeks brings both.
    [Fact]
    public void A_storylet_that_waits_forty_days_arrives_only_after_a_journey()
    {
        var legacy = new Legacy();
        legacy.Send(new FoundLegacy());
        legacy.Send(new SetTrait("sensors_quirk", null, null, 1));
        legacy.To(CyclePhase.Itinerary, 2);
        legacy.Send(new AnswerStorylet(legacy.Waiting("STORYLET_01").OfferId, "fly_on"));
        int arrived = legacy.State.Day;

        for (int cycle = 3; cycle <= 5; cycle++)
        {
            legacy.To(CyclePhase.Itinerary, cycle);
            Assert.DoesNotContain(legacy.State.Module<StoryletsState>(StoryletsModule.Name).Waiting, w => w.StoryletId == "STORYLET_07");
        }

        Assert.InRange(legacy.State.Day - arrived, 15, 21);

        legacy.To(CyclePhase.Advance, 5);
        legacy.Send(new Travel(5));
        Assert.Equal((CyclePhase.Itinerary, 6), (legacy.State.Phase, legacy.State.Cycle));
        Assert.True(legacy.State.Day - arrived >= 40, $"{legacy.State.Day - arrived} days");

        legacy.Send(new AnswerStorylet(legacy.Waiting("STORYLET_07").OfferId, "strip_down"));
        Assert.Equal(0, legacy.Trait("sensors_quirk", null));
    }

    // 1e part six's worked example (design 4.11), from the storylets file as the editor's form saved it: "Time for it later" casts
    // someone grieving, which "Not now" in "The names on the wall" leaves them; Dev Play's SetTrait does what that answer would.
    // Giving it time lets grieving run its four weeks, and the cast cooldown keeps the storylet from coming back to them meanwhile.
    [Fact]
    public void A_grieving_crew_member_is_met_by_the_storylet_written_in_the_editor()
    {
        var legacy = new Legacy();
        legacy.Send(new FoundLegacy());
        string subject = legacy.Game.Bus.View<IRosterView>(legacy.State).Roster[4];
        legacy.Send(new SetTrait("grieving", subject, null, 1));
        legacy.To(CyclePhase.Itinerary, 2);

        WaitingStorylet later = legacy.Waiting("STORYLET_09");
        Assert.Equal(subject, later.Cast["subject"]);
        legacy.Send(new AnswerStorylet(later.OfferId, "give_it_time"));
        Assert.Equal(1, legacy.Trait("grieving", subject));

        int cycle = 2;
        while (legacy.Trait("grieving", subject) > 0)
        {
            cycle++;
            Assert.True(cycle <= 8, "grieving outlasted its four weeks.");
            legacy.To(CyclePhase.Itinerary, cycle);
            Assert.DoesNotContain(legacy.State.Module<StoryletsState>(StoryletsModule.Name).Waiting, w => w.StoryletId == "STORYLET_09");
        }

        // The other way: hearing the names clears it at once.
        var heard = new Legacy();
        heard.Send(new FoundLegacy());
        heard.Send(new SetTrait("grieving", subject, null, 1));
        heard.To(CyclePhase.Itinerary, 2);
        heard.Send(new AnswerStorylet(heard.Waiting("STORYLET_09").OfferId, "hear_names_now"));
        Assert.Equal(0, heard.Trait("grieving", subject));
    }

    // 1e part five's worked example (design 4.8), in the fixture's short version, through the web. With 1d20 dice and deadly injuries
    // the captain plays until two crew have died within 120 days and STORYLET_06 ("The names on the wall") waits; the service, its
    // first option, is held through the bridge, and the Chronicle page shows the weight-4 entry in the option's own words, set apart.
    [Fact]
    public async Task Two_deaths_within_120_days_bring_the_names_on_the_wall_and_the_service_is_remembered()
    {
        using var app = new TestApp(wideDice: true);
        HttpClient client = app.NewClient();
        Guid id = await app.CreateAtOrders("LEGACY_WALL", 3);
        var captain = new WebCaptain(app, client, id);
        AnswerStorylet? wall = null;
        for (int steps = 0; steps < 2000 && wall is null; steps++)
        {
            if (await captain.Peek() is AnswerStorylet { OfferId: var offer } answer && offer.EndsWith(":STORYLET_06", StringComparison.Ordinal))
            {
                wall = answer;
            }
            else
            {
                await captain.Step();
            }
        }

        Assert.True(wall is not null, "The names on the wall never arrived; choose another seed.");
        LoadedLegacy before = await captain.Read();
        IChronicleView chronicle = app.Live.Game.Bus.View<IChronicleView>(before.State);
        Assert.True(chronicle.Count(ChronicleKinds.Death, 120) >= 2);
        Assert.Equal("option_a", wall.OptionId);
        string voice = before.State.Module<StoryletsState>(StoryletsModule.Name).Waiting.Single(w => w.OfferId == wall.OfferId).Cast["voice"];

        await captain.Step();

        LoadedLegacy after = await captain.Read();
        ChronicleEntry entry = app.Live.Game.Bus.View<IChronicleView>(after.State).Entries[^1];
        ChronicleSubject subject = Assert.Single(entry.Subjects);
        Assert.Equal(voice, subject.Id);
        Assert.Equal((ChronicleKinds.Storylet, 4, $"{subject.Name} STORYLET_06_CHRONICLE"), (entry.Kind, entry.Weight, entry.Text));
        string page = await client.GetStringAsync($"/legacy/{id}/chronicle?show=notable");
        Assert.Matches(new Regex($@"<li class=""entry entry--apart"">(?:(?!</li>).)*<p class=""entry__text"">{Regex.Escape(WebUtility.HtmlEncode(entry.Text))}</p>", RegexOptions.Singleline), page);
    }
}
