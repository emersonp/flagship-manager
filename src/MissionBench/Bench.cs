using Sim.Core;
using Sim.Core.Content;
using Sim.Missions;

namespace MissionBench;

// Runs one mission many times in memory. It holds no legacy and writes nothing.
public static class Bench
{
    public static BenchFigures Run(ContentPack pack, MissionTuning tuning, BenchOptions options)
    {
        var outcomes = Enum.GetValues<MissionOutcome>().ToDictionary(o => o, _ => 0);
        var experience = new SortedDictionary<string, double>(StringComparer.Ordinal);
        var encounters = new SortedDictionary<string, EncounterTally>(StringComparer.Ordinal);
        var stages = new List<StageTally>();
        int crewMissions = 0, deaths = 0, incapacitated = 0, calls = 0, notices = 0, worst = -1;
        ulong worstSeed = options.Seed;

        for (int i = 0; i < options.Runs; i++)
        {
            MissionTrace trace = Replay(pack, tuning, options, options.Seed + (ulong)i);
            outcomes[trace.Result.Outcome]++;
            calls += trace.Result.Calls;
            notices += trace.Result.Notices;
            crewMissions += trace.Result.Crew.Count;
            int harm = 0;
            foreach (CrewResult crew in trace.Result.Crew)
            {
                experience[crew.Id] = experience.GetValueOrDefault(crew.Id) + crew.Experience.Values.Sum();
                deaths += crew.Status == MemberStatus.Dead ? 1 : 0;
                incapacitated += crew.Status == MemberStatus.Incapacitated ? 1 : 0;
                harm += crew.Status == MemberStatus.Dead ? 10 : crew.Status == MemberStatus.Incapacitated ? 1 : 0;
            }

            if (harm > worst)
            {
                worst = harm;
                worstSeed = trace.Seed;
            }

            foreach (Beat beat in trace.Beats)
            {
                switch (beat)
                {
                    case StageBeat stage:
                        while (stages.Count <= stage.Cursor)
                        {
                            stages.Add(new StageTally());
                        }

                        stages[stage.Cursor].Reached++;
                        stages[stage.Cursor].Momentum += stage.Momentum;
                        stages[stage.Cursor].Strain += stage.Strain;
                        break;
                    case CheckBeat check:
                        EncounterTally tally = Tally(encounters, check.EncounterId);
                        tally.Checks++;
                        tally.Careful += check.Approach == Approach.Careful ? 1 : 0;
                        tally.Tiers[check.Tier]++;
                        break;
                    case InjuryBeat { Outcome: InjuryOutcome.Dead } injury:
                        Tally(encounters, injury.EncounterId).Deaths++;
                        break;
                    case InjuryBeat { Outcome: InjuryOutcome.Incapacitated } injury:
                        Tally(encounters, injury.EncounterId).Incapacitated++;
                        break;
                    default:
                        break;
                }
            }
        }

        return new BenchFigures(
            options.Runs,
            crewMissions,
            outcomes,
            deaths,
            incapacitated,
            calls,
            notices,
            experience.ToDictionary(e => e.Key, e => e.Value / options.Runs),
            encounters.Select(e => new EncounterFigures(e.Key, e.Value.Checks, e.Value.Careful, e.Value.Checks - e.Value.Careful, e.Value.Tiers, e.Value.Deaths, e.Value.Incapacitated)).ToList(),
            stages.Select((s, i) => new StageFigures(i + 1, s.Reached, s.Momentum / s.Reached, s.Strain / s.Reached)).ToList(),
            worstSeed);
    }

    // One run, named by its seed, with every beat kept.
    public static MissionTrace Replay(ContentPack pack, MissionTuning tuning, BenchOptions options, ulong seed)
    {
        ObjectiveDef objective = pack.Objectives.FirstOrDefault(o => o.Id == options.ObjectiveId)
            ?? throw new ArgumentException($"Unknown objective '{options.ObjectiveId}'.", nameof(options));
        Team team = options.Team ?? TeamGenerator.Generate(pack.Bench, options.Strength, options.Lead, pack.Skills, seed);
        var beats = new List<Beat>();
        MissionStep step = MissionResolver.Advance(MissionResolver.Start(
            objective, pack.Encounters, team, options.GearTags, options.Parameters, tuning, seed, options.FixedDraw ? options.Seed : null));
        beats.AddRange(step.Beats);
        while (step.Pending is { } call)
        {
            step = MissionResolver.Advance(step.State, new CallReply(call.CallId, Answer(options.Policy, call)));
            beats.AddRange(step.Beats);
        }

        return new MissionTrace(seed, team, beats, step.Result!);
    }

    public static CallAnswer Answer(CallPolicy policy, PendingCall call)
    {
        return policy switch
        {
            CallPolicy.Bold => CallAnswer.Bold,
            CallPolicy.Safe => CallAnswer.Safe,
            CallPolicy.Withdraw => call.Options.Contains(CallAnswer.Withdraw) ? CallAnswer.Withdraw : CallAnswer.Safe,
            _ => call.DecisionId == MissionResolver.AbortDecisionId ? CallAnswer.Safe : call.LeadPrefers,
        };
    }

    private static EncounterTally Tally(SortedDictionary<string, EncounterTally> encounters, string id)
    {
        if (!encounters.TryGetValue(id, out EncounterTally? tally))
        {
            tally = new EncounterTally();
            encounters[id] = tally;
        }

        return tally;
    }

    private sealed class EncounterTally
    {
        public int Checks { get; set; }

        public int Careful { get; set; }

        public int Deaths { get; set; }

        public int Incapacitated { get; set; }

        public Dictionary<Tier, int> Tiers { get; } = Enum.GetValues<Tier>().ToDictionary(t => t, _ => 0);
    }

    private sealed class StageTally
    {
        public int Reached { get; set; }

        public double Momentum { get; set; }

        public double Strain { get; set; }
    }
}
