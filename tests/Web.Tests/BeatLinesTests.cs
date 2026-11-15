using System.Globalization;
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
        new CheckBeat("ENCOUNTER_03", "careful", "SKILL_A", "CREW_02", null, false, 3, 0, 1, 0, 0, 3, 6, 11, -2, Tier.Partial, 0.5, false, 0),
        new EffectsBeat(new EffectDef(Momentum: 1, Strain: -1, Progress: 2, InjuryRoll: InjurySeverity.Light, Withdraw: true)),
        new InjuryBeat("CREW_02", "ENCOUNTER_03", InjurySeverity.Severe, 4, InjuryOutcome.LightInjury),
        new LeadChangeBeat("CREW_03"),
        new ChainBeat("ENCOUNTER_05"),
        new CallBeat(SampleCall),
        new InitiativeBeat("DECISION_2", 1, 2.5, "CREW_01", CallAnswer.Bold, true),
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
        Assert.Equal(["stage", "skip", "check", "effects", "injury", "lead", "chain", "call", "initiative", "answer", "collapse", "result"], kinds);
    }

    private static CheckBeat Check(string? helper, bool supervised, string approach, int margin, Tier tier)
    {
        return new CheckBeat("ENCOUNTER_03", approach, "SKILL_A", "CREW_02", helper, supervised, 3, 0, 0, 0, 0, 3, 6, 11, margin, tier, 0, false, 0);
    }

    [Fact]
    public void A_check_with_no_helper_has_no_helper_clause_and_a_true_minus_margin()
    {
        Assert.Equal("CREW_02 checks SKILL_A at ENCOUNTER_03: 3 + 6 against 11, partial (\u22122). careful.", BeatLines.Line(2, Samples[2]).Text);
    }

    [Fact]
    public void A_helped_check_names_the_helper_and_a_positive_margin_carries_a_plus()
    {
        CheckBeat beat = Check("CREW_01", false, "careful", 4, Tier.Success);

        Assert.Equal("CREW_02 checks SKILL_A at ENCOUNTER_03, helped by CREW_01: 3 + 6 against 11, success (+4). careful.", BeatLines.Line(0, beat).Text);
    }

    [Fact]
    public void A_supervised_fast_check_says_supervised_by_and_ends_with_the_approach()
    {
        CheckBeat beat = Check("CREW_01", true, "fast", -2, Tier.Failure);

        Assert.Equal("CREW_02 checks SKILL_A at ENCOUNTER_03, supervised by CREW_01: 3 + 6 against 11, failure (\u22122). fast.", BeatLines.Line(0, beat).Text);
    }

    // Every check ends with the approach taken: its name, or its id when it has none. Ids are unique only within an encounter.
    [Fact]
    public void The_roll_detail_ends_with_the_approachs_name_or_its_id()
    {
        ContentPack pack = BridgePack.Load() with
        {
            Encounters =
            [
                new EncounterDef("ENCOUNTER_03", [], false, null, [new ApproachDef("careful", "SKILL_A", 1, null, "APPROACH_A_NAME"), new ApproachDef("fast", "SKILL_A", 2, null)], null),
                new EncounterDef("ENCOUNTER_04", [], false, null, [new ApproachDef("careful", "SKILL_A", 1, null, "APPROACH_C_NAME")], null),
            ],
        };
        var names = new Names(pack);

        Assert.EndsWith("success (+4). APPROACH_A_NAME.", BeatLines.Line(0, Check(null, false, "careful", 4, Tier.Success), 0, names).Text);
        Assert.EndsWith("success (+4). fast.", BeatLines.Line(0, Check(null, false, "fast", 4, Tier.Success), 0, names).Text);
        Assert.Equal("APPROACH_C_NAME", names.Approach("ENCOUNTER_04", "careful"));
        Assert.Equal("slow", names.Approach("ENCOUNTER_03", "slow"));
    }

    [Fact]
    public void A_zero_margin_has_no_sign()
    {
        Assert.Equal("CREW_02 checks SKILL_A at ENCOUNTER_03: 3 + 6 against 11, critical (0). careful.", BeatLines.Line(0, Check(null, false, "careful", 0, Tier.Critical)).Text);
    }

    [Theory]
    [InlineData(StageIndex, "Stage 3: ENCOUNTER_03. Comms blocked. Momentum 1, strain 2.")]
    [InlineData(1, "ENCOUNTER_04 skipped.")]
    [InlineData(3, "Progress +2. Momentum +1. Strain \u22121. Light injury roll. Withdraw.")]
    [InlineData(4, "CREW_02 takes a severe injury roll at ENCOUNTER_03: 4, light injury.")]
    [InlineData(5, "CREW_03 becomes lead.")]
    [InlineData(6, "Chain to ENCOUNTER_05.")]
    [InlineData(7, "CREW_01 calls: DECISION_1. Stakes 3.")]
    [InlineData(8, "CREW_01 decided DECISION_2 without calling: chose Bold. Stakes 1. Comms blocked.")]
    [InlineData(9, "Captain answers Withdraw: neutral.")]
    [InlineData(10, "Team collapse.")]
    [InlineData(11, "Result: partial success.")]
    public void The_other_beats_read_in_the_designs_voice(int sample, string expected)
    {
        Assert.Equal(expected, BeatLines.Line(0, Samples[sample], 3).Text);
    }

    private const int StageIndex = 0;

    // The route and the meter read a stage line's encounter, blocked flag, and figures as data, never from its text.
    [Fact]
    public void A_stage_line_carries_its_encounter_blocked_flag_momentum_and_strain_as_data_and_other_lines_carry_none()
    {
        LogLine stage = BeatLines.Line(0, new StageBeat(5, "ENCOUNTER_02", "SLOT_9", true, 3, 4), 1);

        Assert.Equal(("ENCOUNTER_02", true, 3, 4), (stage.EncounterId, stage.CommsBlocked, stage.Momentum, stage.Strain));
        Assert.All(Samples.Where(s => s is not StageBeat), s =>
        {
            LogLine line = BeatLines.Line(0, s);
            Assert.Equal((null, null, null, null), (line.EncounterId, line.CommsBlocked, line.Momentum, line.Strain));
        });
    }

    [Fact]
    public void An_unblocked_stage_has_no_blocked_sentence_and_drops_the_slot_name()
    {
        string text = BeatLines.Line(0, new StageBeat(5, "ENCOUNTER_02", "SLOT_9", false, 0, 0), 1).Text;

        Assert.Equal("Stage 1: ENCOUNTER_02. Momentum 0, strain 0.", text);
    }

    [Fact]
    public void An_initiative_that_was_not_blocked_ends_after_the_stakes()
    {
        Assert.Equal("CREW_01 decided DECISION_2 without calling: chose Safe. Stakes 2.", BeatLines.Line(0, new InitiativeBeat("DECISION_2", 2, 1.5, "CREW_01", CallAnswer.Safe, false)).Text);
    }

    [Fact]
    public void An_effect_of_nothing_reads_no_effect()
    {
        Assert.Equal("No effect.", BeatLines.Line(0, new EffectsBeat(new EffectDef())).Text);
    }

    [Theory]
    [InlineData(MissionOutcome.FullSuccess, "Result: full success.")]
    [InlineData(MissionOutcome.PartialSuccess, "Result: partial success.")]
    [InlineData(MissionOutcome.Withdrawal, "Result: withdrawal.")]
    [InlineData(MissionOutcome.Loss, "Result: loss.")]
    [InlineData(MissionOutcome.Incomplete, "Result: incomplete.")]
    public void A_result_names_its_outcome_in_words(MissionOutcome outcome, string expected)
    {
        Assert.Equal(expected, BeatLines.Line(0, new ResultBeat(outcome)).Text);
    }

    [Theory]
    [InlineData(CallStanding.Backed, "Captain answers Safe: backed.")]
    [InlineData(CallStanding.Overruled, "Captain answers Safe: overruled.")]
    public void An_answer_names_the_standing(CallStanding standing, string expected)
    {
        Assert.Equal(expected, BeatLines.Line(0, new AnswerBeat(CallAnswer.Safe, standing, "CREW_01")).Text);
    }

    [Fact]
    public void An_unknown_beat_type_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BeatLines.Line(0, new UnknownBeat()));
    }

    private sealed record UnknownBeat : Beat;

    // sv-SE writes its own minus; lines must carry the true minus (U+2212) whatever the culture.
    [Fact]
    public void Numbers_use_the_invariant_culture()
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
            Assert.NotEqual("-", (-1).ToString());
            Assert.DoesNotContain('-', string.Concat(BeatLines.Line(0, new EffectsBeat(new EffectDef(Momentum: -1))).Text));
            var check = new CheckBeat("ENCOUNTER_03", "careful", "SKILL_A", "CREW_02", null, false, 0, 0, 0, 0, 0, -5, 1, 11, -15, Tier.Disaster, 0, false, 0);
            var stage = new StageBeat(0, "ENCOUNTER_03", "SLOT_1", false, -1, 2);
            var effects = new EffectsBeat(new EffectDef(Momentum: -1, Strain: -2, Progress: -3));

            string[] lines = [BeatLines.Line(0, check).Text, BeatLines.Line(0, stage, 2).Text, BeatLines.Line(0, effects).Text];

            Assert.Equal("CREW_02 checks SKILL_A at ENCOUNTER_03: \u22125 + 1 against 11, disaster (\u221215). careful.", lines[0]);
            Assert.Equal("Stage 2: ENCOUNTER_03. Momentum \u22121, strain 2.", lines[1]);
            Assert.Equal("Progress \u22123. Momentum \u22121. Strain \u22122.", lines[2]);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    private static readonly string[] SampleCrew = ["CREW_01", "CREW_02", "CREW_03"];

    // Names for every id the samples use: crew, skill, encounters, and the two decisions (carried on encounters).
    private static Names SampleNames(string crew02 = "NAME_PLACEHOLDER_CREW_02")
    {
        ApproachDef[] approach = [new ApproachDef("careful", "SKILL_A", 1, null), new ApproachDef("fast", "SKILL_A", 2, null)];
        ContentPack pack = BridgePack.Load() with
        {
            Crew = SampleCrew.Select(id => new CrewDef(id, 1, new Dictionary<string, int>(), 0, 0, id == "CREW_02" ? crew02 : "NAME_PLACEHOLDER_" + id)).ToList(),
            Skills = [new SkillDef("SKILL_A", [], "NAME_PLACEHOLDER_SKILL_A")],
            Encounters =
            [
                new EncounterDef("ENCOUNTER_02", [], false, null, approach, null, "NAME_PLACEHOLDER_ENCOUNTER_02"),
                new EncounterDef("ENCOUNTER_03", [], false, null, approach, null, "NAME_PLACEHOLDER_ENCOUNTER_03"),
                new EncounterDef("ENCOUNTER_04", [], false, null, approach, null, "NAME_PLACEHOLDER_ENCOUNTER_04"),
                new EncounterDef("ENCOUNTER_05", [], false, null, approach, new DecisionDef("DECISION_1", 1, new EffectDef(), new EffectDef(), "NAME_PLACEHOLDER_DECISION_1"), "NAME_PLACEHOLDER_ENCOUNTER_05"),
                new EncounterDef("ENCOUNTER_06", [], false, null, approach, new DecisionDef("DECISION_2", 1, new EffectDef(), new EffectDef(), "NAME_PLACEHOLDER_DECISION_2"), "NAME_PLACEHOLDER_ENCOUNTER_06"),
            ],
        };
        return new Names(pack);
    }

    private static string WithNames(string text)
    {
        return text.Replace("CREW_0", "NAME_PLACEHOLDER_CREW_0", StringComparison.Ordinal)
            .Replace("SKILL_A", "NAME_PLACEHOLDER_SKILL_A", StringComparison.Ordinal)
            .Replace("ENCOUNTER_0", "NAME_PLACEHOLDER_ENCOUNTER_0", StringComparison.Ordinal)
            .Replace("DECISION_", "NAME_PLACEHOLDER_DECISION_", StringComparison.Ordinal);
    }

    // The shapes stay; only the ids inside them become names.
    [Fact]
    public void With_names_every_line_keeps_its_shape_and_swaps_ids_for_names()
    {
        Names names = SampleNames();

        for (int i = 0; i < Samples.Length; i++)
        {
            string plain = BeatLines.Line(0, Samples[i], 3).Text;
            string named = BeatLines.Line(0, Samples[i], 3, names).Text;
            Assert.Equal(WithNames(plain), named);
        }

        Assert.Contains("NAME_PLACEHOLDER_CREW_02", BeatLines.Line(0, Samples[2], 3, names).Text);
    }

    [Fact]
    public void A_line_without_names_has_the_same_text_and_parts_whose_names_are_the_ids()
    {
        LogLine line = BeatLines.Line(0, Samples[2]);

        Assert.Equal("CREW_02 checks SKILL_A at ENCOUNTER_03: 3 + 6 against 11, partial (\u22122). careful.", line.Text);
        Assert.Equal(["CREW_02", "SKILL_A", "ENCOUNTER_03", "careful"], line.Segments.Where(p => p.IsName).Select(p => p.Text));
        Assert.Equal(line.Text, string.Concat(line.Segments.Select(p => p.Text)));
    }

    [Fact]
    public void The_parts_hold_each_name_whole_and_join_to_the_text()
    {
        Names names = SampleNames("NAME 7, <b> & -3");
        var beat = new CheckBeat("ENCOUNTER_03", "careful", "SKILL_A", "CREW_02", "CREW_01", false, 3, 0, 0, 0, 0, 3, 6, 11, 4, Tier.Success, 0, false, 0);

        LogLine line = BeatLines.Line(0, beat, 0, names);

        Assert.Equal(["NAME 7, <b> & -3", "NAME_PLACEHOLDER_SKILL_A", "NAME_PLACEHOLDER_ENCOUNTER_03", "NAME_PLACEHOLDER_CREW_01", "careful"], line.Segments.Where(p => p.IsName).Select(p => p.Text));
        Assert.Equal(line.Text, string.Concat(line.Segments.Select(p => p.Text)));
    }

    [Fact]
    public void A_stage_line_carries_the_encounters_name_beside_its_id()
    {
        LogLine stage = BeatLines.Line(0, new StageBeat(5, "ENCOUNTER_02", "SLOT_9", true, 3, 4), 1, SampleNames());

        Assert.Equal(("ENCOUNTER_02", "NAME_PLACEHOLDER_ENCOUNTER_02"), (stage.EncounterId, stage.EncounterName));
    }
}
