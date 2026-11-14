using Sim.Core.Content;
using Sim.Missions;

namespace Sim.Tests;

// One rule per test (GDD 4.2 to 4.5), on small hand-built missions.
public class MissionRulesTests
{
    private static readonly DecisionDef Stakes2 = new("DECISION_PROMPT_1", 2, new EffectDef(Progress: 1), new EffectDef());
    private static readonly DecisionDef Stakes3 = new("DECISION_PROMPT_2", 3, new EffectDef(Progress: 1), new EffectDef());

    [Fact]
    public void The_score_is_skill_plus_half_the_helper_plus_gear()
    {
        MissionStep step = MissionResolver.Advance(Start(TestContent.Team(1, 1, 3), new Parameters(), Encounter(5, gearTag: "scanner")));

        CheckBeat check = step.Beats.OfType<CheckBeat>().Single();
        Assert.Equal(("CREW_01", "CREW_03", 6, 2, 2, 10, 12), (check.ActorId, check.HelperId, check.ActorSkill, check.Assist, check.Gear, check.Score, check.Target));
        Assert.Equal(check.Score + check.Roll - check.Target, check.Margin);
    }

    [Fact]
    public void Every_check_adds_up_and_lands_in_the_tier_its_margin_gives()
    {
        MissionTuning t = TestContent.Tuning;
        for (ulong seed = 1; seed <= 300; seed++)
        {
            foreach (CheckBeat check in TestContent.Play(TestContent.Team(2, 2, 7, 8), seed).Beats.OfType<CheckBeat>())
            {
                Assert.Equal(check.ActorSkill + check.Assist + check.Gear + check.Momentum - check.Strain, check.Score);
                Assert.Equal(check.Score + check.Roll - check.Target, check.Margin);
                Tier expected = check.Margin >= t.TierCritical ? Tier.Critical
                    : check.Margin >= t.TierSuccess ? Tier.Success
                    : check.Margin >= t.TierPartial ? Tier.Partial
                    : check.Margin >= t.TierFailure ? Tier.Failure
                    : Tier.Disaster;
                Assert.Equal(expected, check.Tier);
            }
        }
    }

    // Skill A: CREW_03 has 5, CREW_06 has 3, and CREW_05 has 2.
    [Fact]
    public void Develop_juniors_puts_the_junior_forward_when_the_check_is_within_reach()
    {
        MissionStep step = MissionResolver.Advance(Start(TestContent.Team(3, 3, 5, 6), new Parameters(DevelopJuniors: true), Encounter(5)));

        CheckBeat check = step.Beats.OfType<CheckBeat>().Single();
        Assert.True(check.Supervised);
        Assert.Equal(("CREW_05", "CREW_03", 2, 4), (check.ActorId, check.HelperId, check.ActorSkill, check.Assist));
    }

    [Fact]
    public void Develop_juniors_leaves_a_check_out_of_reach_to_the_best_qualified()
    {
        MissionStep step = MissionResolver.Advance(Start(TestContent.Team(3, 3, 5, 6), new Parameters(DevelopJuniors: true), Encounter(9)));

        CheckBeat check = step.Beats.OfType<CheckBeat>().Single();
        Assert.False(check.Supervised);
        Assert.Equal(("CREW_03", "CREW_06", 5, 1), (check.ActorId, check.HelperId, check.ActorSkill, check.Assist));
    }

    [Fact]
    public void A_success_removes_one_strain()
    {
        MissionState state = Start(TestContent.Team(1, 1, 3), new Parameters(), Encounter(1));
        state.Strain = 2;

        MissionStep step = MissionResolver.Advance(state);

        Assert.Equal(1, step.State.Strain);
        Assert.Equal(MissionOutcome.FullSuccess, step.Result!.Outcome);
    }

    [Fact]
    public void The_call_threshold_weighs_the_lead_and_autonomy()
    {
        MissionTuning t = TestContent.Tuning;
        TeamMember cautious = TestContent.Member(1);
        TeamMember reckless = TestContent.Member(2);

        Assert.Equal(new[] { 0.5, 1.5, 2.5 }, new[] { -1, 0, 1 }.Select(a => MissionResolver.AskThreshold(t, cautious, new Parameters(Autonomy: a))));
        Assert.Equal(4.25, MissionResolver.AskThreshold(t, reckless, new Parameters()));
        Assert.Equal(CallAnswer.Safe, MissionResolver.Prefers(t, cautious, new Parameters(Priority: 1)));
        Assert.Equal(CallAnswer.Bold, MissionResolver.Prefers(t, reckless, new Parameters()));
        Assert.Equal(CallAnswer.Bold, MissionResolver.Prefers(t, TestContent.Member(3), new Parameters(Priority: 1)));
    }

    [Fact]
    public void A_cautious_lead_calls_where_a_reckless_one_sends_a_notice()
    {
        MissionStep cautious = MissionResolver.Advance(Start(TestContent.Team(1, 1, 3), new Parameters(), Encounter(1, Stakes2)));
        MissionStep reckless = MissionResolver.Advance(Start(TestContent.Team(2, 2, 3), new Parameters(), Encounter(1, Stakes2)));

        Assert.Equal("DECISION_PROMPT_1", cautious.Pending!.DecisionId);
        Assert.Equal(new[] { CallAnswer.Bold, CallAnswer.Safe }, cautious.Pending.Options);
        Assert.Null(reckless.Pending);
        NoticeBeat notice = reckless.Beats.OfType<NoticeBeat>().Single();
        Assert.Equal((CallAnswer.Bold, false), (notice.Chose, notice.Blocked));
        Assert.Equal((0, 1), (reckless.Result!.Calls, reckless.Result.Notices));
    }

    [Fact]
    public void Blocked_comms_force_a_notice_unless_the_team_carries_comms_gear()
    {
        EncounterDef blocked = Encounter(1, Stakes2, commsBlocked: true);

        MissionStep without = MissionResolver.Advance(Start(TestContent.Team(1, 1, 3), new Parameters(), blocked));
        MissionStep with = MissionResolver.Advance(Start(TestContent.Team(1, 1, 3), new Parameters(), ["comms"], blocked));

        Assert.True(without.Beats.OfType<NoticeBeat>().Single().Blocked);
        Assert.NotNull(with.Pending);
    }

    [Fact]
    public void Backing_the_lead_raises_their_loyalty_and_overruling_lowers_it()
    {
        MissionStep call = MissionResolver.Advance(Start(TestContent.Team(1, 1, 3), new Parameters(), Encounter(1, Stakes2)));

        MissionStep backed = MissionResolver.Advance(call.State, CallAnswer.Safe);
        MissionStep overruled = MissionResolver.Advance(call.State, CallAnswer.Bold);

        Assert.Equal(1, backed.Result!.Crew.Single(c => c.Id == "CREW_01").Loyalty);
        Assert.Equal(-1, overruled.Result!.Crew.Single(c => c.Id == "CREW_01").Loyalty);
        Assert.Equal(2, overruled.Result.Progress);
    }

    [Fact]
    public void Withdrawal_is_offered_only_at_high_stakes()
    {
        MissionStep low = MissionResolver.Advance(Start(TestContent.Team(1, 1, 3), new Parameters(), Encounter(1, Stakes2)));
        MissionStep high = MissionResolver.Advance(Start(TestContent.Team(1, 1, 3), new Parameters(), Encounter(1, Stakes3)));

        Assert.Throws<ArgumentException>(() => MissionResolver.Advance(low.State, CallAnswer.Withdraw));
        Assert.Equal(MissionOutcome.Withdrawal, MissionResolver.Advance(high.State, CallAnswer.Withdraw).Result!.Outcome);
    }

    // A reckless lead's threshold (4.25) is above the abort's stakes (4), so only the rule makes this a call.
    [Fact]
    public void At_the_abort_threshold_even_a_reckless_lead_calls()
    {
        MissionState state = Start(TestContent.Team(2, 2, 7), new Parameters(), Encounter(5));
        state.Phase = MissionPhase.Strain;
        state.Strain = 4;

        MissionStep step = MissionResolver.Advance(state);

        Assert.Equal(MissionResolver.AbortDecisionId, step.Pending!.DecisionId);
        Assert.Equal(new[] { CallAnswer.Bold, CallAnswer.Safe }, step.Pending.Options);
        Assert.Equal(MissionOutcome.Withdrawal, MissionResolver.Advance(step.State, CallAnswer.Safe).Result!.Outcome);
        Assert.Null(MissionResolver.Advance(step.State, CallAnswer.Bold).Pending);
    }

    [Fact]
    public void With_comms_blocked_the_lead_decides_the_abort_alone()
    {
        MissionState state = Start(TestContent.Team(2, 2, 7), new Parameters(), Encounter(5, commsBlocked: true));
        state.Phase = MissionPhase.Strain;
        state.Strain = 4;

        MissionStep step = MissionResolver.Advance(state);

        NoticeBeat notice = step.Beats.OfType<NoticeBeat>().Single();
        Assert.Equal((MissionResolver.AbortDecisionId, CallAnswer.Bold, true), (notice.DecisionId, notice.Chose, notice.Blocked));
        Assert.Equal((0, 1), (step.Result!.Calls, step.Result.Notices));
    }

    [Fact]
    public void At_the_strain_cap_the_mission_collapses_with_one_severe_roll_and_light_rolls_for_the_rest()
    {
        MissionState state = Start(TestContent.Team(1, 1, 3, 5), new Parameters(), Encounter(5));
        state.Phase = MissionPhase.Strain;
        state.Strain = 5;

        MissionStep step = MissionResolver.Advance(state);

        Assert.Single(step.Beats.OfType<CollapseBeat>());
        List<InjuryBeat> rolls = step.Beats.OfType<InjuryBeat>().ToList();
        Assert.Equal(1, rolls.Count(r => r.Severity == InjurySeverity.Severe));
        Assert.Equal(2, rolls.Count(r => r.Severity == InjurySeverity.Light));
        Assert.Equal(3, rolls.Select(r => r.CrewId).Distinct().Count());
        Assert.Equal(MissionOutcome.Loss, step.Result!.Outcome);
    }

    [Fact]
    public void High_momentum_skips_a_skippable_stage_at_a_cost()
    {
        MissionState state = Start(TestContent.Team(1, 1, 3), new Parameters(), [], skippable: true, Encounter(5));
        state.Momentum = 4;

        MissionStep step = MissionResolver.Advance(state);

        Assert.Single(step.Beats.OfType<SkipBeat>());
        Assert.Empty(step.Beats.OfType<CheckBeat>());
        Assert.Equal((2, 1), (step.State.Momentum, step.Result!.Progress));
    }

    // Difficulty 40 cannot be met, so every check is a disaster.
    [Fact]
    public void A_disaster_chains_one_extra_encounter_and_only_once()
    {
        EncounterDef chain = Encounter(40, id: "ENCOUNTER_CHAIN", tags: ["chained"], chain: "ENCOUNTER_CHAIN");
        MissionState state = Start(TestContent.Team(1, 1, 3, 5, 7), new Parameters(), Encounter(40, chain: "ENCOUNTER_CHAIN"), chain);

        MissionStep step = MissionResolver.Advance(state);

        Assert.Equal(2, step.Beats.OfType<CheckBeat>().Count(c => c.Tier == Tier.Disaster));
        Assert.Equal("ENCOUNTER_CHAIN", step.Beats.OfType<ChainBeat>().Single().EncounterId);
        Assert.Equal(2, step.State.Encounters.Count);
    }

    [Fact]
    public void The_lead_must_be_on_the_team()
    {
        var team = new Team([TestContent.Member(1)], "CREW_02");

        Assert.Throws<ArgumentException>(() => Start(team, new Parameters(), Encounter(5)));
    }

    private static EncounterDef Encounter(int difficulty, DecisionDef? decision = null, bool commsBlocked = false, string? gearTag = null, string id = "ENCOUNTER_01", string[]? tags = null, string? chain = null)
    {
        return new EncounterDef(id, tags ?? ["field"], commsBlocked, chain, new ApproachDef("SKILL_A", difficulty, gearTag), new ApproachDef("SKILL_A", difficulty + 1, null), decision);
    }

    private static MissionState Start(Team team, Parameters parameters, params EncounterDef[] pool)
    {
        return Start(team, parameters, TestContent.Gear, pool);
    }

    private static MissionState Start(Team team, Parameters parameters, string[] gear, params EncounterDef[] pool)
    {
        return Start(team, parameters, gear, skippable: false, pool);
    }

    // One stage that draws the single encounter tagged "field".
    private static MissionState Start(Team team, Parameters parameters, string[] gear, bool skippable, params EncounterDef[] pool)
    {
        var objective = new ObjectiveDef("OBJECTIVE_TEMPLATE_1", [], 1, 4, 1, [new StageDef("stage", ["field"], 1, 1, skippable)]);
        return MissionResolver.Start(objective, pool, team, gear, parameters, TestContent.Tuning, seed: 1);
    }
}
