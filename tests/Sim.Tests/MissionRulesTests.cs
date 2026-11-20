using Sim.Core;
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
                Assert.Equal(check.ActorSkill + check.Assist + check.Gear + check.MoraleBonus + check.Momentum - check.Strain, check.Score);
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

    // 1e part two: the actor's morale bonus, fixed when the mission launched, is part of every check they make.
    [Fact]
    public void Morale_adds_to_every_check_the_member_makes_and_the_beat_says_so()
    {
        Team plain = TestContent.Team(2, 2, 7, 8);
        var bonus = new Dictionary<string, int> { ["CREW_02"] = 1, ["CREW_07"] = -1, ["CREW_08"] = 0 };
        Team team = plain with { Members = plain.Members.Select(m => m with { MoraleBonus = bonus[m.Id] }).ToList() };
        int seen = 0;
        for (ulong seed = 1; seed <= 100; seed++)
        {
            foreach (CheckBeat check in TestContent.Play(team, seed).Beats.OfType<CheckBeat>())
            {
                Assert.Equal(bonus[check.ActorId], check.MoraleBonus);
                Assert.Equal(check.ActorSkill + check.Assist + check.Gear + check.MoraleBonus + check.Momentum - check.Strain, check.Score);
                seen += check.MoraleBonus != 0 ? 1 : 0;
            }
        }

        Assert.True(seen > 0);
    }

    [Fact]
    public void Morale_does_not_change_the_leads_choice_of_approach()
    {
        Team plain = TestContent.Team(2, 2, 7, 8);
        Team low = plain with { Members = plain.Members.Select(m => m with { MoraleBonus = -1 }).ToList() };
        for (ulong seed = 1; seed <= 50; seed++)
        {
            CheckBeat first = TestContent.Play(plain, seed).Beats.OfType<CheckBeat>().First();
            CheckBeat lowered = TestContent.Play(low, seed).Beats.OfType<CheckBeat>().First();

            Assert.Equal((first.Approach, first.ActorId, first.Roll), (lowered.Approach, lowered.ActorId, lowered.Roll));
            Assert.Equal(first.Score - 1, lowered.Score);
        }
    }

    // A check states the momentum and strain its tier left, so a report can cite them without a copy of the rules.
    [Fact]
    public void Every_check_states_the_momentum_and_strain_its_tier_left()
    {
        MissionTuning t = TestContent.Tuning;
        int Clamp(int value) => Math.Clamp(value, 0, t.Cap);
        var seen = new HashSet<Tier>();
        for (ulong seed = 1; seed <= 300; seed++)
        {
            foreach (CheckBeat check in TestContent.Play(TestContent.Team(2, 2, 7, 8), seed).Beats.OfType<CheckBeat>())
            {
                seen.Add(check.Tier);
                ApproachDef approach = TestContent.Pack.Encounters.Single(e => e.Id == check.EncounterId).Approaches.Single(a => a.Id == check.Approach);
                string word = check.Tier.ToString().ToLowerInvariant();
                EffectDef effect = approach.Outcomes.TryGetValue(word, out EffectDef? written) ? written : MissionResolver.DefaultOutcome(t, check.Tier);
                Assert.Equal((Clamp(check.Momentum + effect.Momentum), Clamp(check.Strain + effect.Strain)), (check.MomentumAfter, check.StrainAfter));
            }
        }

        Assert.Equal(Enum.GetValues<Tier>().Length, seen.Count);
    }

    // Beats stored before slice 1d have no after-values; they must still read, with none.
    [Fact]
    public void A_check_stored_without_after_values_reads_with_none()
    {
        var registry = EventTypeRegistry.FromAssemblies(typeof(MissionBeat).Assembly);
        CheckBeat check = TestContent.Play(TestContent.Team(1, 1, 3), seed: 1).Beats.OfType<CheckBeat>().First();
        EventEnvelope envelope = registry.ToEnvelope(new PublishedEvent(1, 1, new MissionBeat("MISSION_1", 0, check)));
        string old = System.Text.RegularExpressions.Regex.Replace(envelope.Payload, @",""(MomentumAfter|StrainAfter)"":\d+", "");
        Assert.DoesNotContain("After", old, StringComparison.Ordinal);

        var read = (MissionBeat)registry.FromEnvelope(envelope with { Payload = old });

        Assert.Equal(check with { MomentumAfter = null, StrainAfter = null }, read.Beat);
    }

    // Skill A: CREW_03 has 5, CREW_06 has 3, and CREW_05 has 2.
    [Fact]
    public void Develop_juniors_puts_the_junior_forward_when_the_check_is_within_reach()
    {
        MissionStep step = MissionResolver.Advance(Start(TestContent.Team(3, 3, 5, 6), new Parameters(DevelopJuniors: true), Encounter(3)));

        CheckBeat check = step.Beats.OfType<CheckBeat>().Single();
        Assert.True(check.Supervised);
        Assert.Equal(("CREW_05", "CREW_03", 2, 3), (check.ActorId, check.HelperId, check.ActorSkill, check.Assist));
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
    public void An_encounter_with_one_approach_takes_it()
    {
        EncounterDef encounter = new("ENCOUNTER_01", ["field"], false, null, [Approach("only", "SKILL_A")], null);

        CheckBeat check = MissionResolver.Advance(Start(TestContent.Team(2, 2, 3), new Parameters(), encounter)).Beats.OfType<CheckBeat>().Single();

        Assert.Equal(("only", 1), (check.Approach, check.Offered));
    }

    [Fact]
    public void The_mentoring_bonus_goes_to_the_junior_alone_and_the_helper_gets_a_share_of_the_base()
    {
        MissionStep step = MissionResolver.Advance(Start(TestContent.Team(1, 1, 7), new Parameters(DevelopJuniors: true), Encounter(1)));

        CheckBeat check = step.Beats.OfType<CheckBeat>().Single();
        MissionTuning t = TestContent.Tuning;
        Assert.True(check.Mentoring);
        Assert.Equal(("CREW_07", "CREW_01"), (check.ActorId, check.HelperId));
        double baseExperience = check.Experience - t.MentorBonus;
        Assert.Equal(check.Experience, step.Result!.Crew.Single(c => c.Id == "CREW_07").Experience["SKILL_A"]);
        Assert.Equal(baseExperience * t.AssistShare, check.HelperExperience);
        Assert.Equal(check.HelperExperience, step.Result.Crew.Single(c => c.Id == "CREW_01").Experience["SKILL_A"]);
    }

    [Fact]
    public void A_chain_target_that_carries_the_drawn_tag_is_never_drawn()
    {
        EncounterDef chain = Encounter(5, id: "ENCOUNTER_CHAIN");
        EncounterDef source = Encounter(5, chain: "ENCOUNTER_CHAIN");
        for (ulong seed = 1; seed <= 50; seed++)
        {
            var objective = new ObjectiveDef("OBJECTIVE_TEMPLATE_1", [], 1, 4, 1, [new StageDef("stage", ["field"], 1, 1, false)]);
            MissionState state = MissionResolver.Start(objective, [chain, source], TestContent.Team(1, 1, 3), TestContent.Gear, new Parameters(), TestContent.Tuning, seed);

            Assert.Equal("ENCOUNTER_01", state.Encounters.Single().Encounter.Id);
        }
    }

    // The pool's other encounter keeps its dice whether or not a disaster chained an extra one in before it.
    [Fact]
    public void A_chain_does_not_move_the_dice_of_later_encounters()
    {
        EncounterDef later = Encounter(5, id: "ENCOUNTER_02");
        EncounterDef chain = Encounter(5, id: "ENCOUNTER_CHAIN", tags: ["chained"]);
        var objective = new ObjectiveDef("OBJECTIVE_TEMPLATE_1", [], 1, 4, 1, [new StageDef("stage", ["field"], 2, 2, false)]);
        int compared = 0;
        for (ulong seed = 1; seed <= 20; seed++)
        {
            int? Roll(params EncounterDef[] pool)
            {
                MissionState state = MissionResolver.Start(objective, pool, TestContent.Team(1, 1, 3, 5, 7), TestContent.Gear, new Parameters(), TestContent.Tuning, seed);
                MissionStep step = MissionResolver.Advance(state);
                var beats = new List<Beat>(step.Beats);
                while (step.Pending is not null)
                {
                    step = MissionResolver.Advance(step.State, Reply(step, CallAnswer.Bold));
                    beats.AddRange(step.Beats);
                }

                return beats.OfType<CheckBeat>().FirstOrDefault(c => c.EncounterId == "ENCOUNTER_02")?.Roll;
            }

            int? chained = Roll(Encounter(40, chain: "ENCOUNTER_CHAIN"), later, chain);
            int? plain = Roll(Encounter(40), later, chain);
            Assert.Equal(plain, chained);
            compared += chained is null ? 0 : 1;
        }

        Assert.True(compared > 0);
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
    public void A_cautious_lead_calls_where_a_reckless_one_takes_the_initiative()
    {
        MissionStep cautious = MissionResolver.Advance(Start(TestContent.Team(1, 1, 3), new Parameters(), Encounter(1, Stakes2)));
        MissionStep reckless = MissionResolver.Advance(Start(TestContent.Team(2, 2, 3), new Parameters(), Encounter(1, Stakes2)));

        Assert.Equal("DECISION_PROMPT_1", cautious.Pending!.DecisionId);
        Assert.Equal(new[] { CallAnswer.Bold, CallAnswer.Safe }, cautious.Pending.Options);
        Assert.Null(reckless.Pending);
        InitiativeBeat initiative = reckless.Beats.OfType<InitiativeBeat>().Single();
        Assert.Equal((CallAnswer.Bold, false), (initiative.Chose, initiative.Blocked));
        Assert.Equal((0, 1), (reckless.Result!.Calls, reckless.Result.Initiatives));
    }

    [Fact]
    public void Blocked_comms_force_an_initiative_unless_the_team_carries_comms_gear()
    {
        EncounterDef blocked = Encounter(1, Stakes2, commsBlocked: true);

        MissionStep without = MissionResolver.Advance(Start(TestContent.Team(1, 1, 3), new Parameters(), blocked));
        MissionStep with = MissionResolver.Advance(Start(TestContent.Team(1, 1, 3), new Parameters(), ["comms"], blocked));

        Assert.True(without.Beats.OfType<InitiativeBeat>().Single().Blocked);
        Assert.NotNull(with.Pending);
    }

    [Fact]
    public void Backing_the_lead_raises_their_loyalty_and_overruling_lowers_it()
    {
        MissionStep call = MissionResolver.Advance(Start(TestContent.Team(1, 1, 3), new Parameters(), Encounter(1, Stakes2)));

        MissionStep backed = MissionResolver.Advance(call.State, Reply(call, CallAnswer.Safe));
        MissionStep overruled = MissionResolver.Advance(call.State, Reply(call, CallAnswer.Bold));

        Assert.Equal(1, backed.Result!.Crew.Single(c => c.Id == "CREW_01").Loyalty);
        Assert.Equal(-1, overruled.Result!.Crew.Single(c => c.Id == "CREW_01").Loyalty);
        Assert.Equal(2, overruled.Result.Progress);
    }

    [Fact]
    public void Withdrawal_is_offered_only_at_high_stakes()
    {
        MissionStep low = MissionResolver.Advance(Start(TestContent.Team(1, 1, 3), new Parameters(), Encounter(1, Stakes2)));
        MissionStep high = MissionResolver.Advance(Start(TestContent.Team(1, 1, 3), new Parameters(), Encounter(1, Stakes3)));

        Assert.Throws<ArgumentException>(() => MissionResolver.Advance(low.State, Reply(low, CallAnswer.Withdraw)));
        Assert.Equal(MissionOutcome.Withdrawal, MissionResolver.Advance(high.State, Reply(high, CallAnswer.Withdraw)).Result!.Outcome);
    }

    // A reckless lead's threshold (4.25) is above the abort's stakes (4), so only the rule makes this a call.
    [Fact]
    public void At_the_abort_threshold_even_a_reckless_lead_calls()
    {
        MissionState state = Start(TestContent.Team(2, 2, 7), new Parameters(), Encounter(5));
        state.Phase = MissionPhase.Strain;
        state.Strain = 4;

        MissionStep step = MissionResolver.Advance(state);

        Assert.Equal(DecisionIds.Abort, step.Pending!.DecisionId);
        Assert.Equal(new[] { CallAnswer.Bold, CallAnswer.Safe }, step.Pending.Options);
        Assert.Equal(MissionOutcome.Withdrawal, MissionResolver.Advance(step.State, Reply(step, CallAnswer.Safe)).Result!.Outcome);
        Assert.Null(MissionResolver.Advance(step.State, Reply(step, CallAnswer.Bold)).Pending);
    }

    [Fact]
    public void With_comms_blocked_the_lead_decides_the_abort_alone()
    {
        MissionState state = Start(TestContent.Team(2, 2, 7), new Parameters(), Encounter(5, commsBlocked: true));
        state.Phase = MissionPhase.Strain;
        state.Strain = 4;

        MissionStep step = MissionResolver.Advance(state);

        InitiativeBeat initiative = step.Beats.OfType<InitiativeBeat>().Single();
        Assert.Equal((DecisionIds.Abort, CallAnswer.Bold, true), (initiative.DecisionId, initiative.Chose, initiative.Blocked));
        Assert.Equal((0, 1), (step.Result!.Calls, step.Result.Initiatives));
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

    [Fact]
    public void A_check_gives_its_experience_to_the_actor_and_helper_under_the_approachs_skill()
    {
        EncounterDef encounter = new("ENCOUNTER_01", ["field"], false, null, [Approach("careful", "SKILL_A", 7), Approach("fast", "SKILL_B", 8)], null);

        MissionStep step = MissionResolver.Advance(Start(TestContent.Team(2, 2, 3), new Parameters(), encounter));

        CheckBeat check = step.Beats.OfType<CheckBeat>().Single();
        Assert.Equal(("SKILL_B", "CREW_02", "CREW_03"), (check.Skill, check.ActorId, check.HelperId));
        Assert.True(check.Experience > 0 && check.HelperExperience > 0);
        Assert.Equal(new Dictionary<string, double> { ["SKILL_B"] = check.Experience }, step.Result!.Crew.Single(c => c.Id == "CREW_02").Experience);
        Assert.Equal(new Dictionary<string, double> { ["SKILL_B"] = check.HelperExperience }, step.Result.Crew.Single(c => c.Id == "CREW_03").Experience);
    }

    [Fact]
    public void A_reply_to_the_wrong_call_or_with_nothing_pending_is_refused_and_changes_nothing()
    {
        MissionState fresh = Start(TestContent.Team(1, 1, 3), new Parameters(), Encounter(1, Stakes2));
        MissionStep call = MissionResolver.Advance(fresh);
        MissionState done = MissionResolver.Advance(call.State, Reply(call, CallAnswer.Safe)).State;
        string freshJson = MissionJson.Serialize(fresh);
        string callJson = MissionJson.Serialize(call.State);
        string doneJson = MissionJson.Serialize(done);

        Assert.Throws<ArgumentException>(() => MissionResolver.Advance(call.State, new CallReply("dec:wrong", CallAnswer.Safe)));
        Assert.Throws<ArgumentException>(() => MissionResolver.Advance(fresh, new CallReply(call.Pending!.CallId, CallAnswer.Safe)));
        Assert.Throws<ArgumentException>(() => MissionResolver.Advance(done, new CallReply(call.Pending!.CallId, CallAnswer.Safe)));
        Assert.Equal((freshJson, callJson, doneJson), (MissionJson.Serialize(fresh), MissionJson.Serialize(call.State), MissionJson.Serialize(done)));
        Assert.NotNull(MissionResolver.Advance(call.State).Pending);
    }

    // One unmeetable check leaves the team at no progress, with nobody choosing to leave.
    [Fact]
    public void A_mission_that_plays_every_stage_with_too_little_progress_is_incomplete()
    {
        MissionStep step = MissionResolver.Advance(Start(TestContent.Team(1, 1, 3), new Parameters(), Encounter(40)));

        Assert.Equal(MissionOutcome.Incomplete, step.Result!.Outcome);
        Assert.True(step.Result.Progress < TestContent.Tuning.PartialAtProgress);
    }

    [Fact]
    public void Withdrawing_when_the_lead_preferred_safe_leaves_loyalty_alone_but_overruling_a_bold_lead_costs_it()
    {
        MissionStep safeLead = MissionResolver.Advance(Start(TestContent.Team(1, 1, 3), new Parameters(), Encounter(1, Stakes3)));
        MissionStep boldLead = MissionResolver.Advance(Start(TestContent.Team(3, 3, 5), new Parameters(Priority: 1, Autonomy: -1), Encounter(1, Stakes3)));

        MissionStep neutral = MissionResolver.Advance(safeLead.State, Reply(safeLead, CallAnswer.Withdraw));
        MissionStep overruled = MissionResolver.Advance(boldLead.State, Reply(boldLead, CallAnswer.Withdraw));

        Assert.Equal(CallAnswer.Safe, safeLead.Pending!.LeadPrefers);
        Assert.Equal(CallAnswer.Bold, boldLead.Pending!.LeadPrefers);
        Assert.Equal(0, neutral.Result!.Crew.Single(c => c.Id == "CREW_01").Loyalty);
        Assert.Equal(CallStanding.Neutral, neutral.Beats.OfType<AnswerBeat>().Single().Standing);
        Assert.Equal(-1, overruled.Result!.Crew.Single(c => c.Id == "CREW_03").Loyalty);
        Assert.Equal(CallStanding.Overruled, overruled.Beats.OfType<AnswerBeat>().Single().Standing);
        Assert.Equal(MissionOutcome.Withdrawal, neutral.Result.Outcome);
    }

    // Slice 1e part five: the dead carry the encounter of the roll that killed them, and the approach when a check's outcome rolled it.
    // A collapse or a decision's effect names the encounter and no approach; the living name neither.
    [Fact]
    public void A_check_that_kills_names_its_encounter_and_approach()
    {
        EncounterDef deadly = AOrC(difficulty: 1, outcomes: Every(new EffectDef(InjuryRoll: InjurySeverity.Severe)));

        MissionStep step = Fatal(TestContent.Team(1, 1, 3), deadly);

        CheckBeat check = step.Beats.OfType<CheckBeat>().Single();
        CrewResult dead = step.Result!.Crew.Single(c => c.Id == check.ActorId);
        CrewResult living = step.Result.Crew.Single(c => c.Id != check.ActorId);
        Assert.Equal((MemberStatus.Dead, "ENCOUNTER_01", check.Approach), (dead.Status, dead.DiedAtEncounter, dead.DiedAtApproach));
        Assert.Equal((MemberStatus.Active, null, null), (living.Status, living.DiedAtEncounter, living.DiedAtApproach));
    }

    [Fact]
    public void A_collapse_that_kills_names_its_encounter_and_no_approach()
    {
        EncounterDef crushing = AOrC(difficulty: 1, outcomes: Every(new EffectDef(Strain: TestContent.Tuning.Cap)));

        MissionStep step = Fatal(TestContent.Team(1, 1), crushing);

        Assert.Single(step.Beats.OfType<CollapseBeat>());
        CrewResult dead = step.Result!.Crew.Single();
        Assert.Equal((MemberStatus.Dead, "ENCOUNTER_01", null), (dead.Status, dead.DiedAtEncounter, dead.DiedAtApproach));
    }

    [Fact]
    public void A_decision_that_kills_names_its_encounter_and_no_approach()
    {
        var severe = new EffectDef(InjuryRoll: InjurySeverity.Severe);
        EncounterDef deciding = Encounter(1, new DecisionDef("DECISION_PROMPT_1", 1, severe, severe));

        MissionStep step = Fatal(TestContent.Team(1, 1), deciding);

        CrewResult dead = step.Result!.Crew.Single();
        Assert.Equal((MemberStatus.Dead, "ENCOUNTER_01", null), (dead.Status, dead.DiedAtEncounter, dead.DiedAtApproach));
    }

    // Plays the one encounter to the end with every severe roll fatal, answering any call bold.
    private static MissionStep Fatal(Team team, EncounterDef encounter)
    {
        var objective = new ObjectiveDef("OBJECTIVE_TEMPLATE_1", [], 1, 4, 1, [new StageDef("stage", ["field"], 1, 1, false)]);
        MissionTuning deadly = TestContent.Tuning with { DeadBelow = 1000, IncapacitatedBelow = 1000 };
        MissionStep step = MissionResolver.Advance(MissionResolver.Start(objective, [encounter], team, TestContent.Gear, new Parameters(), deadly, 1));
        while (step.Pending is not null)
        {
            step = MissionResolver.Advance(step.State, Reply(step, CallAnswer.Bold));
        }

        return step;
    }

    private static Dictionary<string, EffectDef> Every(EffectDef effect) =>
        new() { ["critical"] = effect, ["success"] = effect, ["partial"] = effect, ["failure"] = effect, ["disaster"] = effect };

    [Fact]
    public void Dice_of_other_counts_and_sizes_resolve_and_roll_within_their_range()
    {
        MissionTuning tuning = TestContent.Tuning with { CheckDice = new DiceDef(3, 4), InjuryDice = new DiceDef(1, 8) };
        var objective = new ObjectiveDef("OBJECTIVE_TEMPLATE_1", [], 1, 4, 1, [new StageDef("stage", ["field"], 1, 1, false)]);
        int checks = 0;

        for (ulong seed = 1; seed <= 60; seed++)
        {
            MissionState start = MissionResolver.Start(objective, [Encounter(9, Stakes2)], TestContent.Team(1, 1, 3, 5), TestContent.Gear, new Parameters(), tuning, seed);
            MissionStep step = MissionResolver.Advance(start);
            var beats = new List<Beat>(step.Beats);
            while (step.Pending is not null)
            {
                step = MissionResolver.Advance(step.State, Reply(step, CallAnswer.Bold));
                beats.AddRange(step.Beats);
            }

            Assert.NotNull(step.Result);
            Assert.All(beats.OfType<CheckBeat>(), c => Assert.InRange(c.Roll, 3, 12));
            Assert.All(beats.OfType<InjuryBeat>(), i => Assert.InRange(i.Roll, 1, 8 + tuning.MedicalBonus));
            checks += beats.OfType<CheckBeat>().Count();
        }

        Assert.True(checks > 0);
        Assert.Equal(7.5, tuning.CheckDice.Average);
        Assert.Equal(7.0, TestContent.Tuning.CheckDice.Average);
    }

    [Fact]
    public void The_reach_rule_follows_the_dice_average()
    {
        EncounterDef within = Encounter(3);
        var parameters = new Parameters(DevelopJuniors: true);
        MissionTuning small = TestContent.Tuning with { CheckDice = new DiceDef(1, 2) };
        var objective = new ObjectiveDef("OBJECTIVE_TEMPLATE_1", [], 1, 4, 1, [new StageDef("stage", ["field"], 1, 1, false)]);

        MissionStep normal = MissionResolver.Advance(Start(TestContent.Team(3, 3, 5, 6), parameters, within));
        MissionStep fewer = MissionResolver.Advance(MissionResolver.Start(objective, [within], TestContent.Team(3, 3, 5, 6), TestContent.Gear, parameters, small, seed: 1));

        Assert.True(normal.Beats.OfType<CheckBeat>().Single().Supervised);
        Assert.False(fewer.Beats.OfType<CheckBeat>().Single().Supervised);
    }

    // Senior 8, second 4, junior 2 on skill A: 8 + 4 / 2 - 1 - (8 - 2) / 4 - 2 = 6.
    [Fact]
    public void A_supervised_check_is_carried_by_the_senior_by_the_documented_formula()
    {
        var team = new Team([Flat("CREW_01", 3, 8), Flat("CREW_02", 2, 4), Flat("CREW_03", 1, 2)], "CREW_01");

        MissionStep step = MissionResolver.Advance(Start(team, new Parameters(DevelopJuniors: true), Encounter(5)));

        CheckBeat check = step.Beats.OfType<CheckBeat>().Single();
        Assert.True(check.Supervised);
        Assert.Equal(("CREW_03", "CREW_01", 2, 6), (check.ActorId, check.HelperId, check.ActorSkill, check.Assist));
    }

    [Fact]
    public void On_a_flat_team_the_supervised_score_is_lower_than_the_best_qualified_score()
    {
        var team = new Team([Flat("CREW_01", 3, 4), Flat("CREW_02", 2, 4), Flat("CREW_03", 1, 4)], "CREW_01");

        CheckBeat best = MissionResolver.Advance(Start(team, new Parameters(), Encounter(3))).Beats.OfType<CheckBeat>().Single();
        CheckBeat supervised = MissionResolver.Advance(Start(team, new Parameters(DevelopJuniors: true), Encounter(3))).Beats.OfType<CheckBeat>().Single();

        Assert.True(supervised.Supervised);
        Assert.False(best.Supervised);
        Assert.True(supervised.Score < best.Score, $"supervised {supervised.Score}, best qualified {best.Score}");
    }

    [Fact]
    public void A_stored_state_is_read_strictly()
    {
        string json = MissionJson.Serialize(Start(TestContent.Team(1, 1, 3), new Parameters(), Encounter(5)));

        Assert.Equal(1, MissionJson.Deserialize(json).Version);
        Assert.Throws<System.Text.Json.JsonException>(() => MissionJson.Deserialize(json.Insert(1, "\"Bogus\":1,")));
        Assert.Throws<System.Text.Json.JsonException>(() => MissionJson.Deserialize(json.Replace("\"Seniority\":5,", "\"Seniority\":5,\"Rank\":1,", StringComparison.Ordinal)));
        Assert.Throws<InvalidOperationException>(() => MissionJson.Deserialize(json.Replace("\"Version\":1", "\"Version\":2", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_stored_state_with_a_missing_member_is_an_error()
    {
        MissionState waiting = MissionResolver.Advance(Start(TestContent.Team(1, 1, 3), new Parameters(), Encounter(1, Stakes2))).State;
        string json = MissionJson.Serialize(waiting);

        Assert.Throws<System.Text.Json.JsonException>(() => MissionJson.Deserialize(json.Replace("\"Seed\":1,", "", StringComparison.Ordinal)));
        Assert.Throws<System.Text.Json.JsonException>(() => MissionJson.Deserialize(json.Replace("\"Version\":1,", "", StringComparison.Ordinal)));
        Assert.Throws<System.Text.Json.JsonException>(() => MissionJson.Deserialize(json.Replace("\"Stakes\":2,", "", StringComparison.Ordinal)));
        Assert.Throws<System.Text.Json.JsonException>(() => MissionJson.Deserialize(json.Replace("\"Skippable\":false,", "", StringComparison.Ordinal)));
    }

    // --- The lead's choice, availability, and outcomes (approaches plan, Task 4) ---

    [Fact]
    public void The_lead_takes_the_approach_they_are_best_at()
    {
        var team = new Team([Skilled("CREW_01", 5, ("SKILL_A", 2), ("SKILL_C", 5))], "CREW_01");

        Assert.Equal("c", CheckOf(team, AOrC()).Approach);
    }

    [Fact]
    public void Equal_or_absent_skill_takes_the_first_listed()
    {
        var none = new Team([Skilled("CREW_01", 5)], "CREW_01");
        var equal = new Team([Skilled("CREW_01", 5, ("SKILL_A", 3), ("SKILL_C", 3))], "CREW_01");

        Assert.Equal("a", CheckOf(none, AOrC()).Approach);
        Assert.Equal("a", CheckOf(equal, AOrC()).Approach);
    }

    // Injury lowers every skill by the same amount, so it never changes which is highest until it floors them all at 0.
    [Fact]
    public void The_leads_injury_counts_against_every_skill_alike_so_the_choice_holds()
    {
        EncounterDef encounter = AOrC();
        TeamMember lead = Skilled("CREW_01", 5, ("SKILL_A", 2), ("SKILL_C", 5));

        Assert.Equal(
            new[] { "c", "c", "c", "a" },
            new[] { 0, 1, 4, 5 }.Select(injury => MissionResolver.Choose(encounter.Approaches, Standing(lead, injury)).Id));
    }

    [Fact]
    public void A_new_lead_chooses_by_their_own_skills()
    {
        // The first lead is best at A and the next most senior at C. The first encounter cannot be met, so its disaster's severe roll
        // falls on the first lead; at the seeds where it downs them, the second encounter is the new lead's choice.
        var team = new Team([Skilled("CREW_01", 5, ("SKILL_A", 6), ("SKILL_C", 1)), Skilled("CREW_02", 4, ("SKILL_A", 1), ("SKILL_C", 6))], "CREW_01");
        EncounterDef first = AOrC(difficulty: 40, tags: ["first"]);
        EncounterDef second = AOrC(id: "ENCOUNTER_02", tags: ["second"]);
        var objective = new ObjectiveDef("OBJECTIVE_TEMPLATE_1", [], 1, 4, 1, [new StageDef("one", ["first"], 1, 1, false), new StageDef("two", ["second"], 1, 1, false)]);
        int changed = 0;
        for (ulong seed = 1; seed <= 60; seed++)
        {
            MissionStep step = MissionResolver.Advance(MissionResolver.Start(objective, [first, second], team, TestContent.Gear, new Parameters(), TestContent.Tuning, seed));
            // Only what happens before the second check counts: a change after it cannot steer it.
            List<Beat> beats = step.Beats.ToList();
            List<CheckBeat> checks = beats.OfType<CheckBeat>().ToList();
            Assert.Equal(("a", "CREW_01"), (checks[0].Approach, checks[0].ActorId));
            if (checks.Count < 2)
            {
                continue;
            }

            bool before = beats.Take(beats.IndexOf(checks[1])).OfType<LeadChangeBeat>().Any(b => b.CrewId == "CREW_02");
            Assert.Equal(before ? "c" : "a", checks[1].Approach);
            changed += before ? 1 : 0;
        }

        Assert.True(changed > 0);
    }

    // Skill C: the lead has 3 and the other member 2, so nobody standing reaches 4.
    [Fact]
    public void An_approach_is_unavailable_below_its_skill_level()
    {
        var team = new Team([Skilled("CREW_01", 5, ("SKILL_A", 1), ("SKILL_C", 3)), Skilled("CREW_02", 4, ("SKILL_A", 2), ("SKILL_C", 2))], "CREW_01");

        CheckBeat below = CheckOf(team, AOrC(requiresC: [new RequirementDef("SKILL_C", 4, null)]));
        CheckBeat at = CheckOf(team, AOrC(requiresC: [new RequirementDef("SKILL_C", 3, null)]));

        Assert.Equal(("a", 1), (below.Approach, below.Offered));
        Assert.Equal(("c", 2), (at.Approach, at.Offered));
    }

    // A level is held after injury: 4 with one injury is 3.
    [Fact]
    public void A_skill_requirement_counts_injury()
    {
        EncounterDef encounter = AOrC(requiresC: [new RequirementDef("SKILL_C", 4, null)]);
        TeamMember member = Skilled("CREW_01", 5, ("SKILL_C", 4));

        Assert.Equal(["a", "c"], MissionResolver.Available(encounter, [Standing(member)], []).Select(a => a.Id));
        Assert.Equal(["a"], MissionResolver.Available(encounter, [Standing(member, 1)], []).Select(a => a.Id));
    }

    [Fact]
    public void A_gear_requirement_holds_when_the_loadout_carries_the_tag()
    {
        var team = new Team([Skilled("CREW_01", 5, ("SKILL_A", 1), ("SKILL_C", 5))], "CREW_01");
        EncounterDef encounter = AOrC(requiresC: [new RequirementDef(null, 0, "scanner")]);

        CheckBeat with = CheckOf(team, encounter, ["scanner"]);
        CheckBeat without = CheckOf(team, encounter, ["tools"]);

        Assert.Equal(("c", 2), (with.Approach, with.Offered));
        Assert.Equal(("a", 1), (without.Approach, without.Offered));
    }

    [Fact]
    public void An_approach_with_five_requirements_needs_all_five()
    {
        RequirementDef[] five = [new("SKILL_A", 2, null), new("SKILL_B", 2, null), new("SKILL_C", 2, null), new("SKILL_D", 2, null), new(null, 0, "scanner")];
        var encounter = new EncounterDef("ENCOUNTER_01", ["field"], false, null, [Approach("a", "SKILL_A"), Approach("all", "SKILL_C", requires: five)], null);
        MemberState[] team = [Standing(Skilled("CREW_01", 5, ("SKILL_A", 2), ("SKILL_B", 2))), Standing(Skilled("CREW_02", 4, ("SKILL_C", 2), ("SKILL_D", 2)))];

        Assert.Equal(["a", "all"], MissionResolver.Available(encounter, team, ["scanner"]).Select(a => a.Id));
        Assert.Equal(["a"], MissionResolver.Available(encounter, team, ["tools"]).Select(a => a.Id));
        Assert.Equal(["a"], MissionResolver.Available(encounter, [team[0]], ["scanner"]).Select(a => a.Id));
        Assert.Equal(["a"], MissionResolver.Available(encounter, [team[0], Standing(team[1].Member, 1)], ["scanner"]).Select(a => a.Id));
    }

    [Fact]
    public void The_best_at_the_chosen_skill_makes_the_check_not_the_lead()
    {
        var team = new Team([Skilled("CREW_01", 5, ("SKILL_A", 1), ("SKILL_C", 5)), Skilled("CREW_02", 4, ("SKILL_A", 2), ("SKILL_C", 7))], "CREW_01");

        CheckBeat check = CheckOf(team, AOrC());

        Assert.Equal(("c", "CREW_02", "CREW_01"), (check.Approach, check.ActorId, check.HelperId));
    }

    // Each tuning value is set apart from the others, so a value read from the wrong place shows.
    [Fact]
    public void An_unwritten_outcome_is_the_default_for_every_tier()
    {
        MissionTuning t = TestContent.Tuning with { StrainReliefOnSuccess = 2, MomentumCritical = 3, StrainPartial = 4, StrainFailure = 5, StrainDisaster = 6 };

        Assert.Equal(new EffectDef(Momentum: 3, Strain: -2, Progress: 1), MissionResolver.DefaultOutcome(t, Tier.Critical));
        Assert.Equal(new EffectDef(Strain: -2, Progress: 1), MissionResolver.DefaultOutcome(t, Tier.Success));
        Assert.Equal(new EffectDef(Strain: 4), MissionResolver.DefaultOutcome(t, Tier.Partial));
        Assert.Equal(new EffectDef(Strain: 5, InjuryRoll: InjurySeverity.Light), MissionResolver.DefaultOutcome(t, Tier.Failure));
        Assert.Equal(new EffectDef(Strain: 6, InjuryRoll: InjurySeverity.Severe), MissionResolver.DefaultOutcome(t, Tier.Disaster));
    }

    // Difficulty 1 is always met, so the check is a success or a critical, and both are written.
    [Fact]
    public void A_written_outcome_replaces_the_default_whole()
    {
        var outcomes = new Dictionary<string, EffectDef> { ["critical"] = new(Momentum: 2), ["success"] = new(Momentum: 2) };
        MissionState state = Start(TestContent.Team(1, 1, 3), new Parameters(), AOrC(difficulty: 1, outcomes: outcomes));
        state.Strain = 2;

        MissionStep step = MissionResolver.Advance(state);

        CheckBeat check = step.Beats.OfType<CheckBeat>().Single();
        Assert.True(check.Tier is Tier.Critical or Tier.Success);
        Assert.Equal((0, 2, 2), (step.Result!.Progress, step.State.Strain, step.State.Momentum));
    }

    [Fact]
    public void An_empty_outcome_does_nothing()
    {
        var outcomes = new Dictionary<string, EffectDef> { ["failure"] = new() };
        int failures = 0;
        for (int difficulty = 1; difficulty <= 16; difficulty++)
        {
            for (ulong seed = 1; seed <= 10; seed++)
            {
                MissionStep step = MissionResolver.Advance(Start(TestContent.Team(1, 1, 3), new Parameters(), TestContent.Gear, false, seed, AOrC(difficulty, outcomes: outcomes)));
                CheckBeat check = step.Beats.OfType<CheckBeat>().Single();
                if (check.Tier == Tier.Failure)
                {
                    failures++;
                    Assert.Equal((0, 0), (check.StrainAfter, step.State.Strain));
                    Assert.Empty(step.Beats.OfType<InjuryBeat>());
                }
            }
        }

        Assert.True(failures > 0);
    }

    // An injury roll in an outcome falls on the actor, after the check's beat, as the default's does.
    [Fact]
    public void An_injury_roll_in_a_written_outcome_falls_on_the_actor_after_the_check()
    {
        var outcomes = new Dictionary<string, EffectDef> { ["critical"] = new(InjuryRoll: InjurySeverity.Light), ["success"] = new(InjuryRoll: InjurySeverity.Light) };
        var team = new Team([Skilled("CREW_01", 5, ("SKILL_A", 1)), Skilled("CREW_02", 4, ("SKILL_A", 6))], "CREW_01");

        MissionStep step = MissionResolver.Advance(Start(team, new Parameters(), AOrC(difficulty: 1, outcomes: outcomes)));

        List<Beat> beats = step.Beats.ToList();
        int check = beats.FindIndex(b => b is CheckBeat);
        Assert.Equal("CREW_02", ((CheckBeat)beats[check]).ActorId);
        Assert.Equal("CREW_02", Assert.IsType<InjuryBeat>(beats[check + 1]).CrewId);
    }

    // Difficulty 40 cannot be met, so the check is a disaster; its written outcome adds no strain and rolls no injury.
    [Fact]
    public void A_disaster_still_chains_when_its_outcome_is_written()
    {
        var outcomes = new Dictionary<string, EffectDef> { ["disaster"] = new(Momentum: 1) };
        EncounterDef chain = AOrC(5, id: "ENCOUNTER_CHAIN", tags: ["chained"]);
        MissionState state = Start(TestContent.Team(1, 1, 3, 5, 7), new Parameters(), AOrC(40, outcomes: outcomes, chain: "ENCOUNTER_CHAIN"), chain);

        MissionStep step = MissionResolver.Advance(state);

        CheckBeat disaster = step.Beats.OfType<CheckBeat>().First();
        Assert.Equal((Tier.Disaster, 1, 0), (disaster.Tier, disaster.MomentumAfter, disaster.StrainAfter));
        Assert.Equal("ENCOUNTER_CHAIN", step.Beats.OfType<ChainBeat>().Single().EncounterId);
        Assert.Equal(2, step.Beats.OfType<CheckBeat>().Count());
    }

    // --- Traits (flags in 1e part one, Task 1) ---

    private static EffectDef Trait(string on, int amount = 1, bool set = false, string trait = "trait_a") => new(Traits: [new TraitChange(trait, amount, set, on)]);

    // Difficulty 1 is always met, so a trait written on critical and success always lands.
    private static Dictionary<string, EffectDef> OnAnySuccess(EffectDef effect) => new() { ["critical"] = effect, ["success"] = effect };

    [Fact]
    public void A_ship_trait_from_an_outcome_is_in_the_result_with_no_crew()
    {
        MissionStep step = MissionResolver.Advance(Start(TestContent.Team(1, 1, 3), new Parameters(), AOrC(difficulty: 1, outcomes: OnAnySuccess(Trait("ship")))));

        MissionTrait expected = new("trait_a", 1, false, null, "ENCOUNTER_01");
        Assert.Equal([expected], step.Result!.Traits);
        Assert.Equal([expected], step.Beats.OfType<CheckBeat>().Single().Traits!);
    }

    [Fact]
    public void An_actor_trait_names_the_one_who_made_the_check()
    {
        var team = new Team([Skilled("CREW_01", 5, ("SKILL_A", 1)), Skilled("CREW_02", 4, ("SKILL_A", 6))], "CREW_01");

        MissionStep step = MissionResolver.Advance(Start(team, new Parameters(), AOrC(difficulty: 1, outcomes: OnAnySuccess(Trait("actor", 2, set: true)))));

        Assert.Equal("CREW_02", step.Beats.OfType<CheckBeat>().Single().ActorId);
        Assert.Equal([new MissionTrait("trait_a", 2, true, "CREW_02", "ENCOUNTER_01")], step.Result!.Traits);
    }

    [Fact]
    public void A_team_trait_names_each_member_still_standing()
    {
        MissionState state = Start(TestContent.Team(1, 1, 3, 5), new Parameters(), AOrC(difficulty: 1, outcomes: OnAnySuccess(Trait("team"))));
        state.Team.Single(m => m.Member.Id == "CREW_03").Status = MemberStatus.Incapacitated;

        MissionStep step = MissionResolver.Advance(state);

        Assert.Equal(["CREW_01", "CREW_05"], step.Result!.Traits.Select(f => f.CrewId));
        Assert.All(step.Result.Traits, f => Assert.Equal(("trait_a", 1, false, "ENCOUNTER_01"), (f.Trait, f.Amount, f.Set, f.EncounterId)));
    }

    // 1e part three (design 4.5): toward makes a pair, the one it lands on holding it toward the other.
    [Fact]
    public void Toward_makes_a_pair_from_actor_to_lead()
    {
        var team = new Team([Skilled("CREW_01", 5, ("SKILL_A", 1)), Skilled("CREW_02", 4, ("SKILL_A", 6))], "CREW_01");
        EffectDef pair = new(Traits: [new TraitChange("trait_g", 1, false, "actor", "lead")]);

        MissionStep step = MissionResolver.Advance(Start(team, new Parameters(), AOrC(difficulty: 1, outcomes: OnAnySuccess(pair))));

        Assert.Equal([new MissionTrait("trait_g", 1, false, "CREW_02", "ENCOUNTER_01", "CREW_01")], step.Result!.Traits);
        Assert.Equal(step.Result.Traits, step.Beats.OfType<CheckBeat>().Single().Traits!);
    }

    [Fact]
    public void No_pair_when_actor_is_lead()
    {
        var team = new Team([Skilled("CREW_01", 5, ("SKILL_A", 6)), Skilled("CREW_02", 4, ("SKILL_A", 1))], "CREW_01");
        EffectDef pair = new(Traits: [new TraitChange("trait_g", 1, false, "actor", "lead")]);

        MissionStep step = MissionResolver.Advance(Start(team, new Parameters(), AOrC(difficulty: 1, outcomes: OnAnySuccess(pair))));

        Assert.Equal("CREW_01", step.Beats.OfType<CheckBeat>().Single().ActorId);
        Assert.Empty(step.Result!.Traits);
        Assert.Empty(step.Beats.OfType<CheckBeat>().Single().Traits!);
    }

    // --- Traits on missions (1e part three, Task 4) ---

    private static RequirementDef TraitAt(string on, int min, int? max) => new(null, 0, null, "trait_x", min, max, on);

    [Fact]
    public void A_trait_requirement_on_the_ship_closes_an_approach()
    {
        EncounterDef encounter = AOrC(requiresC: [TraitAt("ship", 0, 1)]);
        MemberState member = Standing(Skilled("CREW_01", 5, ("SKILL_A", 3), ("SKILL_C", 3)));

        Assert.Equal(["a", "c"], MissionResolver.Available(encounter, [member], [], null, "CREW_01").Select(a => a.Id));
        Assert.Equal(["a", "c"], MissionResolver.Available(encounter, [member], [], new Dictionary<string, int> { ["trait_x"] = 1 }, "CREW_01").Select(a => a.Id));
        Assert.Equal(["a"], MissionResolver.Available(encounter, [member], [], new Dictionary<string, int> { ["trait_x"] = 2 }, "CREW_01").Select(a => a.Id));
    }

    [Fact]
    public void Anyone_reads_members_standing_and_lead_reads_the_lead()
    {
        TeamMember marked = Skilled("CREW_02", 4, ("SKILL_C", 3)) with { Traits = new Dictionary<string, int> { ["trait_x"] = 2 } };
        MemberState lead = Standing(Skilled("CREW_01", 5, ("SKILL_A", 3)));
        MemberState other = Standing(marked);
        EncounterDef anyone = AOrC(requiresC: [TraitAt("anyone", 2, null)]);
        EncounterDef byLead = AOrC(requiresC: [TraitAt("lead", 2, null)]);

        Assert.Equal(["a", "c"], MissionResolver.Available(anyone, [lead, other], [], null, "CREW_01").Select(a => a.Id));
        Assert.Equal(["a"], MissionResolver.Available(anyone, [lead], [], null, "CREW_01").Select(a => a.Id));
        Assert.Equal(["a"], MissionResolver.Available(byLead, [lead, other], [], null, "CREW_01").Select(a => a.Id));
        Assert.Equal(["a", "c"], MissionResolver.Available(byLead, [lead, other], [], null, "CREW_02").Select(a => a.Id));
    }

    [Fact]
    public void A_trait_bonus_is_added_to_every_check_and_the_beat_names_it()
    {
        TeamMember plain = Skilled("CREW_01", 5, ("SKILL_A", 4));
        TeamMember marked = plain with { TraitBonuses = [new TraitBonus("trait_g", -1), new TraitBonus("trait_h", 3)] };

        CheckBeat without = CheckOf(new Team([plain], "CREW_01"), AOrC());
        CheckBeat with = CheckOf(new Team([marked], "CREW_01"), AOrC());

        Assert.Null(without.TraitBonuses);
        Assert.Equal(without.Score + 2, with.Score);
        Assert.Equal([new TraitBonus("trait_g", -1), new TraitBonus("trait_h", 3)], with.TraitBonuses!);
    }

    // The first lead alone has the skill, so they make the impossible check and take its disaster's severe roll. At the seeds where it
    // downs them, the decision that follows lands on the new lead.
    [Fact]
    public void A_lead_trait_from_a_decision_names_the_lead_at_that_moment()
    {
        var team = new Team([Skilled("CREW_01", 5, ("SKILL_A", 6)), Skilled("CREW_02", 4)], "CREW_01");
        EffectDef answer = Trait("lead", trait: "trait_b");
        var decision = new DecisionDef("DECISION_PROMPT_1", 1, answer, answer);
        int changed = 0;
        for (ulong seed = 1; seed <= 60; seed++)
        {
            MissionStep step = MissionResolver.Advance(Start(team, new Parameters(), TestContent.Gear, false, seed, Encounter(40, decision)));
            if (step.Pending is { } pending)
            {
                step = MissionResolver.Advance(step.State, new CallReply(pending.CallId, CallAnswer.Bold));
            }

            List<Beat> beats = step.Beats.ToList();
            if (!beats.OfType<EffectsBeat>().Any())
            {
                continue;
            }

            string lead = beats.OfType<LeadChangeBeat>().Select(b => b.CrewId).LastOrDefault() ?? "CREW_01";
            Assert.Equal([new MissionTrait("trait_b", 1, false, lead, "ENCOUNTER_01")], step.Result!.Traits);
            changed += lead == "CREW_02" ? 1 : 0;
        }

        Assert.True(changed > 0);
    }

    // Every tier's outcome is its default plus a trait on the team, and both answers carry one on the lead.
    [Fact]
    public void A_trait_change_alters_nothing_else_about_the_mission()
    {
        MissionTuning t = TestContent.Tuning;
        Dictionary<string, EffectDef> Outcomes(bool flagged) => Enum.GetValues<Tier>().ToDictionary(
            tier => tier.ToString().ToLowerInvariant(),
            tier => MissionResolver.DefaultOutcome(t, tier) with { Traits = flagged ? [new TraitChange("trait_a", 1, false, "team")] : null });
        EncounterDef Make(bool flagged, int difficulty, string id, string tag)
        {
            EffectDef bold = new(Progress: 1, Traits: flagged ? [new TraitChange("trait_b", 2, true, "lead")] : null);
            EffectDef safe = new(Strain: -1, Traits: flagged ? [new TraitChange("trait_c", -1, false, "ship")] : null);
            return new EncounterDef(id, [tag], false, null, [Approach("a", "SKILL_A", difficulty, outcomes: Outcomes(flagged))], new DecisionDef($"DECISION_{id}", 2, bold, safe));
        }

        var objective = new ObjectiveDef("OBJECTIVE_TEMPLATE_1", [], 1, 4, 3, [new StageDef("one", ["one"], 1, 1, false), new StageDef("two", ["two"], 1, 1, false), new StageDef("three", ["three"], 1, 1, false)]);
        int traits = 0;
        for (ulong seed = 1; seed <= 40; seed++)
        {
            string Play(bool flagged)
            {
                EncounterDef[] pool = [Make(flagged, 3, "ENCOUNTER_01", "one"), Make(flagged, 8, "ENCOUNTER_02", "two"), Make(flagged, 13, "ENCOUNTER_03", "three")];
                MissionStep step = MissionResolver.Advance(MissionResolver.Start(objective, pool, TestContent.Team(1, 1, 3), TestContent.Gear, new Parameters(), t, seed));
                var beats = new List<Beat>(step.Beats);
                while (step.Pending is { } pending)
                {
                    step = MissionResolver.Advance(step.State, new CallReply(pending.CallId, seed % 2 == 0 ? CallAnswer.Bold : CallAnswer.Safe));
                    beats.AddRange(step.Beats);
                }

                traits += flagged ? step.Result!.Traits.Count : 0;
                IEnumerable<Beat> plain = beats.Select(b => b switch
                {
                    CheckBeat c => c with { Traits = null },
                    EffectsBeat e => e with { Effects = e.Effects with { Traits = null } },
                    _ => b,
                });
                return System.Text.Json.JsonSerializer.Serialize(plain, MissionJson.Options) + System.Text.Json.JsonSerializer.Serialize(step.Result! with { Traits = [] }, MissionJson.Options);
            }

            Assert.Equal(Play(false), Play(true));
        }

        Assert.True(traits > 0);
    }

    private static TeamMember Flat(string id, int seniority, int skill)
    {
        return new TeamMember(id, seniority, new Dictionary<string, int> { ["SKILL_A"] = skill }, 0, 0);
    }

    private static CallReply Reply(MissionStep step, CallAnswer answer)
    {
        return new CallReply(step.Pending!.CallId, answer);
    }

    private static EncounterDef Encounter(int difficulty, DecisionDef? decision = null, bool commsBlocked = false, string? gearTag = null, string id = "ENCOUNTER_01", string[]? tags = null, string? chain = null)
    {
        return new EncounterDef(id, tags ?? ["field"], commsBlocked, chain, [Approach("careful", "SKILL_A", difficulty, gearTag), Approach("fast", "SKILL_A", difficulty + 1)], decision);
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
        return Start(team, parameters, gear, skippable, 1, pool);
    }

    private static MissionState Start(Team team, Parameters parameters, string[] gear, bool skippable, ulong seed, params EncounterDef[] pool)
    {
        var objective = new ObjectiveDef("OBJECTIVE_TEMPLATE_1", [], 1, 4, 1, [new StageDef("stage", ["field"], 1, 1, skippable)]);
        return MissionResolver.Start(objective, pool, team, gear, parameters, TestContent.Tuning, seed);
    }

    private static ApproachDef Approach(string id, string skill, int difficulty = 5, string? gearTag = null, RequirementDef[]? requires = null, Dictionary<string, EffectDef>? outcomes = null)
    {
        return new ApproachDef(id, skill, difficulty, gearTag, requires ?? [], outcomes ?? []);
    }

    // A member with only the skills given, so which skill is highest is plain.
    private static TeamMember Skilled(string id, int seniority, params (string Skill, int Level)[] skills)
    {
        return new TeamMember(id, seniority, skills.ToDictionary(s => s.Skill, s => s.Level), 0, 0);
    }

    private static MemberState Standing(TeamMember member, int injury = 0)
    {
        return new MemberState { Member = member, Injury = injury, Status = MemberStatus.Active, Experience = [], Loyalty = 0 };
    }

    // Two approaches, a on skill A and c on skill C, either of which may carry requirements or outcomes.
    private static EncounterDef AOrC(int difficulty = 5, RequirementDef[]? requiresC = null, Dictionary<string, EffectDef>? outcomes = null, string id = "ENCOUNTER_01", string[]? tags = null, string? chain = null)
    {
        return new EncounterDef(id, tags ?? ["field"], false, chain, [Approach("a", "SKILL_A", difficulty, outcomes: outcomes), Approach("c", "SKILL_C", difficulty, requires: requiresC, outcomes: outcomes)], null);
    }

    private static CheckBeat CheckOf(Team team, EncounterDef encounter, string[]? gear = null)
    {
        return MissionResolver.Advance(Start(team, new Parameters(), gear ?? TestContent.Gear, encounter)).Beats.OfType<CheckBeat>().Single();
    }
}
