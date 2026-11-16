using Sim.Core;
using Sim.Core.Content;

namespace Sim.Missions;

// The default resolver (GDD 4.6). Pure: the same inputs always give the same step.
public static class MissionResolver
{
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
            Version = MissionJson.CurrentVersion,
            Seed = seed,
            ObjectiveId = objective.Id,
            Need = objective.Need,
            Tuning = tuning,
            Encounters = [],
            Chains = [],
            Cursor = 0,
            Phase = MissionPhase.Check,
            Momentum = 0,
            Strain = 0,
            Progress = 0,
            Team = team.Members.Select(m => new MemberState { Member = m, Injury = 0, Status = MemberStatus.Active, Experience = [], Loyalty = 0 }).ToList(),
            LeadId = team.LeadId,
            GearTags = gearTags.ToList(),
            Parameters = parameters,
            Pending = null,
            Result = null,
            AbortAsked = false,
            Chained = false,
            Calls = 0,
            Initiatives = 0,
            Flags = [],
        };

        ulong draw = drawSeed ?? seed;
        var drawn = new HashSet<string>();
        var chainTargets = pool.Where(e => e.Chain is not null).Select(e => e.Chain!).ToHashSet();
        for (int i = 0; i < objective.Stages.Count; i++)
        {
            StageDef stage = objective.Stages[i];
            int count = stage.CountMin + RngStream.For(draw, 0, $"count:{i}").Next(stage.CountMax - stage.CountMin + 1);
            for (int j = 0; j < count; j++)
            {
                List<EncounterDef> candidates = pool
                    .Where(e => !drawn.Contains(e.Id) && !chainTargets.Contains(e.Id) && stage.DrawTags.All(e.Tags.Contains))
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
    public static MissionStep Advance(MissionState state, CallReply? reply = null)
    {
        var run = new Run(MissionJson.Deserialize(MissionJson.Serialize(state)));
        run.Go(reply);
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

    // What a tier does when the approach has not written it: the tuning file's numbers and nothing else. The editor shows it in words.
    public static EffectDef DefaultOutcome(MissionTuning tuning, Tier tier)
    {
        return tier switch
        {
            Tier.Critical => new EffectDef(Momentum: tuning.MomentumCritical, Strain: -tuning.StrainReliefOnSuccess, Progress: 1),
            Tier.Success => new EffectDef(Strain: -tuning.StrainReliefOnSuccess, Progress: 1),
            Tier.Partial => new EffectDef(Strain: tuning.StrainPartial),
            Tier.Failure => new EffectDef(Strain: tuning.StrainFailure, InjuryRoll: InjurySeverity.Light),
            _ => new EffectDef(Strain: tuning.StrainDisaster, InjuryRoll: InjurySeverity.Severe),
        };
    }

    // The approaches whose every requirement holds for the team as it stands, in the encounter's order.
    internal static IReadOnlyList<ApproachDef> Available(EncounterDef encounter, IReadOnlyList<MemberState> standing, IReadOnlyCollection<string> gearTags)
    {
        bool Holds(RequirementDef requirement)
        {
            return requirement.Skill is { } skill
                ? standing.Any(m => m.Member.Skills.GetValueOrDefault(skill) - m.Injury >= requirement.Level)
                : gearTags.Contains(requirement.Gear!);
        }

        return encounter.Approaches.Where(a => a.Requires.All(Holds)).ToList();
    }

    // The lead takes what they are best at, after injury; the earliest listed wins a tie.
    internal static ApproachDef Choose(IReadOnlyList<ApproachDef> available, MemberState lead)
    {
        ApproachDef best = available[0];
        int bestSkill = -1;
        foreach (ApproachDef approach in available)
        {
            int skill = Math.Max(0, lead.Member.Skills.GetValueOrDefault(approach.Skill) - lead.Injury);
            if (skill > bestSkill)
            {
                (best, bestSkill) = (approach, skill);
            }
        }

        return best;
    }

    private sealed class Run(MissionState state)
    {
        public MissionState State { get; } = state;

        public List<Beat> Beats { get; } = [];

        private MissionTuning T => State.Tuning;

        private List<MemberState> Active => State.Team.Where(m => m.Status == MemberStatus.Active).ToList();

        private MemberState Lead => State.Team.First(m => m.Member.Id == State.LeadId);

        private DrawnEncounter Current => State.Encounters[State.Cursor];

        public void Go(CallReply? reply)
        {
            if (State.Pending is { } pending)
            {
                if (reply is null)
                {
                    return;
                }

                if (reply.CallId != pending.CallId)
                {
                    throw new ArgumentException($"The reply is to '{reply.CallId}', but the pending call is '{pending.CallId}'.", nameof(reply));
                }

                Answer(pending, reply.Answer);
            }
            else if (reply is not null)
            {
                throw new ArgumentException($"There is no call pending, so '{reply.CallId}' cannot be answered.", nameof(reply));
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
                                : MissionOutcome.Incomplete);
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
                            Raise(decision.Id, decision.Stakes, decision.Bold, decision.Safe, $"dec:{Current.Encounter.Id}", alwaysCall: false);
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
                            Raise(DecisionIds.Abort, T.AbortStakes, new EffectDef(), new EffectDef(Withdraw: true), $"abort:{Current.Encounter.Id}", alwaysCall: true);
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
            IReadOnlyList<ApproachDef> available = Available(encounter, Active, State.GearTags);
            ApproachDef def = Choose(available, Lead);
            int Skill(MemberState m)
            {
                return Math.Max(0, m.Member.Skills.GetValueOrDefault(def.Skill) - m.Injury);
            }

            List<MemberState> best = Active.OrderByDescending(Skill).ToList();
            int gear = def.GearTag is not null && State.GearTags.Contains(def.GearTag) ? T.GearBonus : 0;
            int target = def.Difficulty + T.CheckTarget;

            // "Develop juniors": the senior carries the junior's check, at a cost, when the junior is within reach (GDD 4.2).
            MemberState senior = best[0];
            MemberState junior = best[^1];
            int carried = 0;
            bool supervised = p.DevelopJuniors && best.Count > 1;
            if (supervised)
            {
                carried = Math.Max(0, Skill(senior) + (Skill(best[1]) / T.AssistDivisor) - T.SupervisorPenalty - ((Skill(senior) - Skill(junior)) / T.SupervisorGapDivisor) - Skill(junior));
                double expected = Skill(junior) + carried + gear + State.Momentum - State.Strain + T.CheckDice.Average - target;
                supervised = expected >= T.ReachMargin;
            }

            MemberState actor = supervised ? junior : senior;
            MemberState? helper = supervised ? senior : best.Count > 1 ? best[1] : null;
            int assist = helper is null ? 0
                : supervised ? carried
                : Skill(helper) / T.AssistDivisor;
            int actorSkill = Skill(actor);
            int momentum = State.Momentum;
            int strain = State.Strain;
            int score = actorSkill + assist + gear + momentum - strain;
            int roll = T.CheckDice.Roll(Dice($"check:{encounter.Id}"));
            int margin = score + roll - target;
            Tier tier = margin >= T.TierCritical ? Tier.Critical
                : margin >= T.TierSuccess ? Tier.Success
                : margin >= T.TierPartial ? Tier.Partial
                : margin >= T.TierFailure ? Tier.Failure
                : Tier.Disaster;

            EffectDef outcome = def.Outcomes.TryGetValue(BeatSituations.Word(tier), out EffectDef? written) ? written : DefaultOutcome(T, tier);
            State.Momentum = Clamp(State.Momentum + outcome.Momentum);
            State.Strain = Clamp(State.Strain + outcome.Strain);
            State.Progress += outcome.Progress;

            List<MissionFlag>? flags = Flag(outcome, encounter.Id, actor);
            bool mentoring = helper is not null && Skill(helper) - actorSkill >= T.MentorGap;
            double baseExperience = margin >= T.TrivialAtMargin ? 0
                : Math.Abs(margin) <= T.CloseMargin ? T.ExperienceClose
                : Math.Abs(margin) <= T.NearMargin ? T.ExperienceNear
                : T.ExperienceFar;
            double experience = baseExperience + (mentoring ? T.MentorBonus : 0);
            double helperExperience = helper is null ? 0 : baseExperience * T.AssistShare;
            actor.Experience[def.Skill] = actor.Experience.GetValueOrDefault(def.Skill) + experience;
            if (helper is not null)
            {
                helper.Experience[def.Skill] = helper.Experience.GetValueOrDefault(def.Skill) + helperExperience;
            }

            Beats.Add(new CheckBeat(
                encounter.Id, def.Id, available.Count, def.Skill, actor.Member.Id, helper?.Member.Id, supervised,
                actorSkill, assist, gear, momentum, strain, score, roll, target, margin, tier, experience, mentoring, helperExperience, State.Momentum, State.Strain, flags));

            // The roll falls on the actor after the check's beat, under the key the default rolls always used, so its dice do not move.
            if (outcome.InjuryRoll is { } severity)
            {
                InjuryRoll(actor, severity, $"checkinj:{encounter.Id}");
            }

            // The chain is the encounter's, whatever the approach's disaster outcome says.
            if (tier == Tier.Disaster && !State.Chained && encounter.Chain is not null && State.Chains.TryGetValue(encounter.Chain, out EncounterDef? chain))
            {
                State.Chained = true;
                State.Encounters.Insert(State.Cursor + 1, new DrawnEncounter("chain", false, chain));
                Beats.Add(new ChainBeat(chain.Id));
            }
        }

        // A decision becomes a call or an initiative (GDD 4.4).
        private void Raise(string decisionId, int stakes, EffectDef bold, EffectDef safe, string callId, bool alwaysCall)
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
                State.Pending = new PendingCall(decisionId, stakes, threshold, lead.Id, prefers, options, callId);
                Beats.Add(new CallBeat(State.Pending));
            }
            else
            {
                State.Initiatives++;
                Beats.Add(new InitiativeBeat(decisionId, stakes, threshold, lead.Id, prefers, blocked));
                Apply(prefers == CallAnswer.Bold ? bold : safe, callId);
            }
        }

        private void Answer(PendingCall pending, CallAnswer answer)
        {
            if (!pending.Options.Contains(answer))
            {
                throw new ArgumentException($"'{answer}' is not an option for {pending.DecisionId}.", nameof(answer));
            }

            CallStanding standing = answer == CallAnswer.Withdraw && pending.LeadPrefers == CallAnswer.Safe ? CallStanding.Neutral
                : answer == pending.LeadPrefers ? CallStanding.Backed
                : CallStanding.Overruled;
            State.Team.First(m => m.Member.Id == pending.LeadId).Loyalty += standing switch
            {
                CallStanding.Backed => T.BackedLoyalty,
                CallStanding.Overruled => T.OverruledLoyalty,
                _ => 0,
            };
            State.Pending = null;
            Beats.Add(new AnswerBeat(answer, standing, pending.LeadId));

            DecisionDef? decision = Current.Encounter.Decision;
            bool abort = pending.DecisionId == DecisionIds.Abort;
            EffectDef effect = answer switch
            {
                CallAnswer.Withdraw => new EffectDef(Withdraw: true),
                CallAnswer.Bold => abort ? new EffectDef() : decision!.Bold,
                _ => abort ? new EffectDef(Withdraw: true) : decision!.Safe,
            };
            Apply(effect, pending.CallId);
        }

        private void Apply(EffectDef effect, string key)
        {
            State.Momentum = Clamp(State.Momentum + effect.Momentum);
            State.Strain = Clamp(State.Strain + effect.Strain);
            State.Progress += effect.Progress;
            Flag(effect, Current.Encounter.Id, null);
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

        // Records one flag for each one the change lands on, before any injury roll the same effect makes; nothing else about the
        // mission changes. actor is who made the check, and null for a decision's answer, where the loader refuses on: actor.
        private List<MissionFlag>? Flag(EffectDef effect, string encounterId, MemberState? actor)
        {
            if (effect.Flags is null)
            {
                return null;
            }

            var made = new List<MissionFlag>();
            foreach (FlagChange change in effect.Flags)
            {
                List<string?> on = change.On switch
                {
                    FlagIds.Ship => [null],
                    FlagIds.Actor => [actor?.Member.Id ?? throw new InvalidOperationException($"{encounterId}: on: actor names no one outside an approach's outcome.")],
                    FlagIds.Lead => [State.LeadId],
                    FlagIds.Team => [.. Active.Select(m => m.Member.Id)],
                    _ => throw new InvalidOperationException($"{encounterId}: a flag change on '{change.On}' names no one."),
                };
                made.AddRange(on.Select(id => new MissionFlag(change.Flag, change.Amount, change.Set, id, encounterId)));
            }

            State.Flags.AddRange(made);
            return made;
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
            int roll = T.InjuryDice.Roll(Dice(key)) + (State.GearTags.Contains("medical") ? T.MedicalBonus : 0);
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
                outcome, State.Progress, State.Need, State.Calls, State.Initiatives,
                State.Team.Select(m => new CrewResult(m.Member.Id, m.Status, m.Injury, new Dictionary<string, double>(m.Experience), m.Loyalty)).ToList(),
                State.Flags.ToList());
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
