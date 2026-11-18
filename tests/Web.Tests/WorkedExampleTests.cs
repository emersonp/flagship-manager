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

        public int Trait(string trait, string crewId, string? targetId = null) => Game.Bus.View<ITraitView>(State).Value(trait, crewId, targetId);

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
        Assert.Equal(subject, word.SubjectId);
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
        Assert.Equal((holder, target), (air.SubjectId, air.TargetId));
        legacy.Send(new AnswerStorylet(air.OfferId, "keep_apart"));
        Assert.Equal(5, legacy.Trait("avoids", holder, target));

        // The pair's vignette arrives once it has waited longest: in cycle 2 the rested crew's "A week aboard" filled the three places.
        legacy.To(CyclePhase.Itinerary, 3);
        Assert.Contains(legacy.Events, e => e is VignetteArrived { VignetteId: "VIGNETTE_06" } v && v.SubjectId == holder && v.TargetId == target);

        // Four weeks unchanged, and the weekly step brings it back into "Clear the air"'s range.
        legacy.To(CyclePhase.Itinerary, 6);
        Assert.Equal(4, legacy.Trait("avoids", holder, target));
        Assert.Equal((holder, target), (legacy.Waiting("STORYLET_04").SubjectId, legacy.Waiting("STORYLET_04").TargetId));
    }
}
