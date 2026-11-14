using Sim.Core;
using Sim.Core.Content;

namespace Sim.Missions;

// The default resolver (GDD 4.6). Pure: the same inputs always give the same step.
public static class MissionResolver
{
    public const string AbortDecisionId = "DECISION_ABORT";
    private const int AverageRoll = 7;

    // drawSeed lets the bench hold the encounters fixed while the dice vary.
    public static MissionState Start(
        ObjectiveDef objective,
        IReadOnlyList<EncounterDef> pool,
        Team team,
        IReadOnlyList<string> gearTags,
        Parameters parameters,
        MissionTuning tuning,
        ulong seed,
        ulong? drawSeed = null)
    {
        if (team.Members.Count == 0 || team.Members.All(m => m.Id != team.LeadId))
        {
            throw new ArgumentException("The team needs at least one member, and its lead must be one of them.", nameof(team));
        }

        var state = new MissionState
        {
            Seed = seed,
            ObjectiveId = objective.Id,
            Need = objective.Need,
            Tuning = tuning,
            Team = team.Members.Select(m => new MemberState { Member = m }).ToList(),
            LeadId = team.LeadId,
            GearTags = gearTags.ToList(),
            Parameters = parameters,
        };

        ulong draw = drawSeed ?? seed;
        var drawn = new HashSet<string>();
        for (int i = 0; i < objective.Stages.Count; i++)
        {
            StageDef stage = objective.Stages[i];
            int count = stage.CountMin + RngStream.For(draw, 0, $"count:{i}").Next(stage.CountMax - stage.CountMin + 1);
            for (int j = 0; j < count; j++)
            {
                List<EncounterDef> candidates = pool
                    .Where(e => !drawn.Contains(e.Id) && stage.DrawTags.All(e.Tags.Contains))
                    .OrderBy(e => e.Id, StringComparer.Ordinal)
                    .ToList();
                if (candidates.Count == 0)
                {
                    break;
                }

                EncounterDef encounter = candidates[RngStream.For(draw, 0, $"draw:{i}:{j}").Next(candidates.Count)];
                drawn.Add(encounter.Id);
                state.Encounters.Add(new DrawnEncounter(stage.Slot, stage.Skippable, encounter));
                if (encounter.Chain is not null && pool.FirstOrDefault(e => e.Id == encounter.Chain) is { } chain)
                {
                    state.Chains[chain.Id] = chain;
                }
            }
        }

        return state;
    }

    // Runs until the mission needs the captain's answer or ends. Never changes the state it is given.
    public static MissionStep Advance(MissionState state, CallAnswer? answer = null)
    {
        var run = new Run(MissionJson.Deserialize(MissionJson.Serialize(state)));
        run.Go(answer);
        return new MissionStep(run.State, run.Beats, run.State.Pending, run.State.Result);
    }

    public static double AskThreshold(MissionTuning tuning, TeamMember lead, Parameters parameters)
    {
        return tuning.CallBase - (tuning.CautionWeight * lead.Caution) - (tuning.DisciplineWeight * lead.Discipline) + (tuning.AutonomyWeight * parameters.Autonomy);
    }

    public static CallAnswer Prefers(MissionTuning tuning, TeamMember lead, Parameters parameters)
    {
        return (tuning.BoldPriorityWeight * parameters.Priority) - (tuning.BoldCautionWeight * lead.Caution) > 0 ? CallAnswer.Bold : CallAnswer.Safe;
    }

    private sealed class Run(MissionState state)
    {
        public MissionState State { get; } = state;

        public List<Beat> Beats { get; } = [];

        private MissionTuning T => State.Tuning;

        private List<MemberState> Active => State.Team.Where(m => m.Status == MemberStatus.Active).ToList();

        private MemberState Lead => State.Team.First(m => m.Member.Id == State.LeadId);

        private DrawnEncounter Current => State.Encounters[State.Cursor];

        public void Go(CallAnswer? answer)
        {
            if (State.Pending is { } pending)
            {
                if (answer is not { } given)
                {
                    return;
                }

                Answer(pending, given);
            }

            while (State.Result is null && State.Pending is null)
            {
                if (Active.Count == 0)
                {
                    Finish(MissionOutcome.Loss);
                    break;
                }

                switch (State.Phase)
                {
                    case MissionPhase.Check:
                        if (State.Cursor >= State.Encounters.Count)
                        {
                            Finish(State.Progress >= State.Need ? MissionOutcome.FullSuccess
                                : State.Progress >= T.PartialAtProgress ? MissionOutcome.PartialSuccess
                                : MissionOutcome.Withdrawal);
                            break;
                        }

                        Beats.Add(new StageBeat(State.Cursor, Current.Encounter.Id, Current.Slot, Current.Encounter.CommsBlocked, State.Momentum, State.Strain));
                        if (Current.Skippable && State.Momentum >= T.SkipAtMomentum)
                        {
                            State.Momentum = Clamp(State.Momentum - T.SkipCost);
                            State.Progress++;
                            Beats.Add(new SkipBeat(Current.Encounter.Id));
                            State.Phase = MissionPhase.Strain;
                        }
                        else
                        {
                            Check(Current.Encounter);
                            State.Phase = MissionPhase.Decision;
                        }

                        break;
                    case MissionPhase.Decision:
                        State.Phase = MissionPhase.Strain;
                        if (Current.Encounter.Decision is { } decision && Active.Count > 0)
                        {
                            Raise(decision.Id, decision.Stakes, decision.Bold, decision.Safe, $"dec:{State.Cursor}", alwaysCall: false);
                        }

                        break;
                    case MissionPhase.Strain:
                        State.Phase = MissionPhase.Advance;
                        if (State.Strain >= T.CollapseAt)
                        {
                            Collapse();
                        }
                        else if (State.Strain >= T.AbortCallAt && !State.AbortAsked)
                        {
                            State.AbortAsked = true;
                            Raise(AbortDecisionId, T.AbortStakes, new EffectDef(), new EffectDef(Withdraw: true), $"abort:{State.Cursor}", alwaysCall: true);
                        }

                        break;
                    default:
                        State.Cursor++;
                        State.Phase = MissionPhase.Check;
                        break;
                }
            }
        }

        private void Check(EncounterDef encounter)
        {
            Parameters p = State.Parameters;
            Approach approach = Lead.Member.Caution + p.Risk >= 0 ? Approach.Careful : Approach.Fast;
            ApproachDef def = approach == Approach.Careful ? encounter.Careful : encounter.Fast;
            int Skill(MemberState m)
            {
                return Math.Max(0, m.Member.Skills.GetValueOrDefault(def.Skill) - m.Injury);
            }

            List<MemberState> best = Active.OrderByDescending(Skill).ToList();
            int gear = def.GearTag is not null && State.GearTags.Contains(def.GearTag) ? T.GearBonus : 0;
            int target = def.Difficulty + T.CheckTarget;

            // "Develop juniors" only when the junior is within reach under supervision (GDD 4.2).
            bool supervised = p.DevelopJuniors && best.Count > 1;
            if (supervised)
            {
                int expected = Skill(best[^1]) + Math.Max(0, Skill(best[0]) - T.SupervisorPenalty) + gear + State.Momentum - State.Strain + AverageRoll - target;
                supervised = expected >= T.ReachMargin;
            }

            MemberState actor = supervised ? best[^1] : best[0];
            MemberState? helper = supervised ? best[0] : best.Count > 1 ? best[1] : null;
            int assist = helper is null ? 0
                : supervised ? Math.Max(0, Skill(helper) - T.SupervisorPenalty)
                : Skill(helper) / T.AssistDivisor;
            int actorSkill = Skill(actor);
            int momentum = State.Momentum;
            int strain = State.Strain;
            int score = actorSkill + assist + gear + momentum - strain;
            int roll = Dice($"check:{State.Cursor}").TwoD6();
            int margin = score + roll - target;
            Tier tier = margin >= T.TierCritical ? Tier.Critical
                : margin >= T.TierSuccess ? Tier.Success
                : margin >= T.TierPartial ? Tier.Partial
                : margin >= T.TierFailure ? Tier.Failure
                : Tier.Disaster;

            switch (tier)
            {
                case Tier.Critical or Tier.Success:
                    State.Strain = Clamp(State.Strain - T.StrainReliefOnSuccess);
                    State.Progress++;
                    State.Momentum = Clamp(State.Momentum + (tier == Tier.Critical ? T.MomentumCritical : 0) + (approach == Approach.Fast ? T.MomentumFastSuccess : 0));
                    break;
                case Tier.Partial:
                    State.Strain = Clamp(State.Strain + T.StrainPartial);
                    break;
                case Tier.Failure:
                    State.Strain = Clamp(State.Strain + T.StrainFailure);
                    break;
                default:
                    State.Strain = Clamp(State.Strain + T.StrainDisaster);
                    break;
            }

            bool mentoring = helper is not null && Skill(helper) - actorSkill >= T.MentorGap;
            double experience = (margin >= T.TrivialAtMargin ? 0
                : Math.Abs(margin) <= T.CloseMargin ? T.ExperienceClose
                : Math.Abs(margin) <= T.NearMargin ? T.ExperienceNear
                : T.ExperienceFar) + (mentoring ? T.MentorBonus : 0);
            actor.Experience += experience;
            if (helper is not null)
            {
                helper.Experience += experience * T.AssistShare;
            }

            Beats.Add(new CheckBeat(
                encounter.Id, approach, def.Skill, actor.Member.Id, helper?.Member.Id, supervised,
                actorSkill, assist, gear, momentum, strain, score, roll, target, margin, tier, experience, mentoring));

            if (tier == Tier.Failure)
            {
                InjuryRoll(actor, InjurySeverity.Light, $"checkinj:{State.Cursor}");
            }
            else if (tier == Tier.Disaster)
            {
                InjuryRoll(actor, InjurySeverity.Severe, $"checkinj:{State.Cursor}");
                if (!State.Chained && encounter.Chain is not null && State.Chains.TryGetValue(encounter.Chain, out EncounterDef? chain))
                {
                    State.Chained = true;
                    State.Encounters.Insert(State.Cursor + 1, new DrawnEncounter("chain", false, chain));
                    Beats.Add(new ChainBeat(chain.Id));
                }
            }
        }

        // A decision becomes a call or a notice (GDD 4.4).
        private void Raise(string decisionId, int stakes, EffectDef bold, EffectDef safe, string key, bool alwaysCall)
        {
            TeamMember lead = Lead.Member;
            double threshold = AskThreshold(T, lead, State.Parameters);
            CallAnswer prefers = Prefers(T, lead, State.Parameters);
            bool blocked = Current.Encounter.CommsBlocked && !State.GearTags.Contains("comms");
            if (!blocked && (alwaysCall || stakes >= threshold))
            {
                var options = new List<CallAnswer> { CallAnswer.Bold, CallAnswer.Safe };
                if (stakes >= T.WithdrawOfferedAtStakes && !safe.Withdraw)
                {
                    options.Add(CallAnswer.Withdraw);
                }

                State.Calls++;
                State.Pending = new PendingCall(decisionId, stakes, threshold, lead.Id, prefers, options, key);
                Beats.Add(new CallBeat(State.Pending));
            }
            else
            {
                State.Notices++;
                Beats.Add(new NoticeBeat(decisionId, stakes, threshold, lead.Id, prefers, blocked));
                Apply(prefers == CallAnswer.Bold ? bold : safe, key);
            }
        }

        private void Answer(PendingCall pending, CallAnswer answer)
        {
            if (!pending.Options.Contains(answer))
            {
                throw new ArgumentException($"'{answer}' is not an option for {pending.DecisionId}.", nameof(answer));
            }

            bool backed = answer == pending.LeadPrefers;
            State.Team.First(m => m.Member.Id == pending.LeadId).Loyalty += backed ? T.BackedLoyalty : T.OverruledLoyalty;
            State.Pending = null;
            Beats.Add(new AnswerBeat(answer, backed, pending.LeadId));

            DecisionDef? decision = Current.Encounter.Decision;
            bool abort = pending.DecisionId == AbortDecisionId;
            EffectDef effect = answer switch
            {
                CallAnswer.Withdraw => new EffectDef(Withdraw: true),
                CallAnswer.Bold => abort ? new EffectDef() : decision!.Bold,
                _ => abort ? new EffectDef(Withdraw: true) : decision!.Safe,
            };
            Apply(effect, pending.Key);
        }

        private void Apply(EffectDef effect, string key)
        {
            State.Momentum = Clamp(State.Momentum + effect.Momentum);
            State.Strain = Clamp(State.Strain + effect.Strain);
            State.Progress += effect.Progress;
            Beats.Add(new EffectsBeat(effect));
            if (effect.InjuryRoll is { } severity && Active.Count > 0)
            {
                InjuryRoll(Active[Dice($"{key}:who").Next(Active.Count)], severity, $"{key}:inj");
            }

            if (effect.Withdraw)
            {
                Finish(MissionOutcome.Withdrawal);
            }
        }

        // At the strain cap the mission is lost: one severe roll, and light rolls for the rest.
        private void Collapse()
        {
            Beats.Add(new CollapseBeat());
            List<MemberState> team = Active;
            MemberState worst = team[Dice("collapse:who").Next(team.Count)];
            InjuryRoll(worst, InjurySeverity.Severe, "collapse:severe");
            foreach (MemberState member in team.Where(m => m != worst))
            {
                InjuryRoll(member, InjurySeverity.Light, $"collapse:{member.Member.Id}");
            }

            Finish(MissionOutcome.Loss);
        }

        private void InjuryRoll(MemberState member, InjurySeverity severity, string key)
        {
            int roll = Dice(key).TwoD6() + (State.GearTags.Contains("medical") ? T.MedicalBonus : 0);
            InjuryOutcome outcome = InjuryOutcome.Unharmed;
            if (severity == InjurySeverity.Light)
            {
                if (roll < T.LightBelow)
                {
                    outcome = InjuryOutcome.LightInjury;
                    member.Injury = Math.Min(T.MaxLight, member.Injury + 1);
                }
            }
            else if (roll < T.DeadBelow)
            {
                outcome = InjuryOutcome.Dead;
                member.Status = MemberStatus.Dead;
            }
            else if (roll < T.IncapacitatedBelow)
            {
                outcome = InjuryOutcome.Incapacitated;
                member.Status = MemberStatus.Incapacitated;
            }

            Beats.Add(new InjuryBeat(member.Member.Id, Current.Encounter.Id, severity, roll, outcome));
            if (member.Status != MemberStatus.Active && member.Member.Id == State.LeadId && Active.OrderByDescending(m => m.Member.Seniority).FirstOrDefault() is { } next)
            {
                State.LeadId = next.Member.Id;
                Beats.Add(new LeadChangeBeat(next.Member.Id));
            }
        }

        private void Finish(MissionOutcome outcome)
        {
            State.Result = new MissionResult(
                outcome, State.Progress, State.Need, State.Calls, State.Notices,
                State.Team.Select(m => new CrewResult(m.Member.Id, m.Status, m.Injury, m.Experience, m.Loyalty)).ToList());
            Beats.Add(new ResultBeat(outcome));
        }

        // Dice are keyed, not sequential, so the same seed rolls the same whatever team is sent.
        private RngStream Dice(string key)
        {
            return RngStream.For(State.Seed, 0, key);
        }

        private int Clamp(int value)
        {
            return Math.Clamp(value, 0, T.Cap);
        }
    }
}
