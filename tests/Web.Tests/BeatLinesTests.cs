using System.Reflection;
using Sim.Core;
using Sim.Core.Content;
using Sim.Missions;
using Web.Bridge;

namespace Web.Tests;

public class BeatLinesTests
{
    private static readonly PendingCall SampleCall = new("DECISION_1", 3, 2.5, "CREW_01", CallAnswer.Safe, [CallAnswer.Bold, CallAnswer.Safe, CallAnswer.Withdraw], "c1");

    private static readonly Beat[] Samples =
    [
        new StageBeat(0, "ENCOUNTER_03", "SLOT_1", true, 1, 2),
        new SkipBeat("ENCOUNTER_04"),
        new CheckBeat("ENCOUNTER_03", Approach.Careful, "SKILL_A", "CREW_02", null, false, 3, 0, 1, 0, 0, 3, 6, 11, -2, Tier.Partial, 0.5, false, 0),
        new EffectsBeat(new EffectDef(Momentum: 1, Strain: -1, Progress: 2, InjuryRoll: InjurySeverity.Light, Withdraw: true)),
        new InjuryBeat("CREW_02", "ENCOUNTER_03", InjurySeverity.Severe, 4, InjuryOutcome.LightInjury),
        new LeadChangeBeat("CREW_03"),
        new ChainBeat("ENCOUNTER_05"),
        new CallBeat(SampleCall),
        new NoticeBeat("DECISION_2", 1, 2.5, "CREW_01", CallAnswer.Bold, true),
        new AnswerBeat(CallAnswer.Withdraw, CallStanding.Neutral, "CREW_01"),
        new CollapseBeat(),
        new ResultBeat(MissionOutcome.PartialSuccess),
    ];

    // A new beat type with no sample here, and so no line, fails this test.
    [Fact]
    public void Every_beat_type_has_a_line()
    {
        HashSet<Type> beatTypes = typeof(Beat).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(Beat))).ToHashSet();

        Assert.Equal(beatTypes.OrderBy(t => t.Name), Samples.Select(s => s.GetType()).Distinct().OrderBy(t => t.Name));
        foreach (Beat sample in Samples)
        {
            LogLine line = BeatLines.Line(7, sample);
            Assert.Equal(7, line.Index);
            Assert.False(string.IsNullOrWhiteSpace(line.Text), sample.GetType().Name);
        }
    }

    [Fact]
    public void Each_beat_type_has_its_own_stable_lower_case_kind()
    {
        List<string> kinds = Samples.Select(s => BeatLines.Line(0, s).Kind).ToList();

        Assert.Equal(kinds.Count, kinds.Distinct().Count());
        Assert.All(kinds, k => Assert.Matches("^[a-z]+$", k));
        Assert.Equal(["stage", "skip", "check", "effects", "injury", "lead", "chain", "call", "notice", "answer", "collapse", "result"], kinds);
    }

    [Fact]
    public void A_check_reads_as_one_structural_sentence()
    {
        LogLine line = BeatLines.Line(2, Samples[2]);

        Assert.Equal("CREW_02 checks SKILL_A at ENCOUNTER_03: 9 against 11, partial.", line.Text);
    }

    [Theory]
    [InlineData(0, "Stage ENCOUNTER_03 in slot SLOT_1: momentum 1, strain 2, comms blocked.")]
    [InlineData(1, "ENCOUNTER_04 skipped.")]
    [InlineData(3, "Effects: momentum +1, strain -1, progress +2, light injury roll, withdraw.")]
    [InlineData(4, "CREW_02 injury roll at ENCOUNTER_03: severe severity, roll 4, light injury.")]
    [InlineData(5, "CREW_03 becomes lead.")]
    [InlineData(6, "Chain to ENCOUNTER_05.")]
    [InlineData(7, "Call DECISION_1: stakes 3, threshold 2.5, lead CREW_01 prefers safe, options bold, safe, withdraw.")]
    [InlineData(8, "Notice DECISION_2: stakes 1, threshold 2.5, lead CREW_01 chose bold. Comms blocked.")]
    [InlineData(9, "Answer withdraw, neutral, lead CREW_01.")]
    [InlineData(10, "Team collapse.")]
    [InlineData(11, "Result: partial success.")]
    public void The_other_beats_read_as_systems_text(int sample, string expected)
    {
        Assert.Equal(expected, BeatLines.Line(0, Samples[sample]).Text);
    }

    [Fact]
    public void A_fast_supervised_check_names_the_approach_and_the_supervisor()
    {
        var beat = new CheckBeat("ENCOUNTER_03", Approach.Fast, "SKILL_A", "CREW_02", "CREW_01", true, 3, 0, 0, 0, 0, 3, 6, 11, -2, Tier.Failure, 0, false, 0);

        Assert.Equal("CREW_02 checks SKILL_A at ENCOUNTER_03: 9 against 11, failure. Fast approach. Supervised by CREW_01.", BeatLines.Line(0, beat).Text);
    }

    [Fact]
    public void An_unknown_beat_type_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BeatLines.Line(0, new UnknownBeat()));
    }

    private sealed record UnknownBeat : Beat;
}
