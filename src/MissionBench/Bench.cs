using Sim.Core;
using Sim.Core.Content;
using Sim.Missions;

namespace MissionBench;

// Runs one mission many times in memory. It holds no legacy and writes nothing.
public static class Bench
{
    // How often Run reports progress and checks for cancellation, in runs.
    public const int ProgressEvery = 100;

    // Runs options.Runs missions from options.Seed through a tally. Progress is the runs done, every ProgressEvery runs and at the end;
    // a cancellation is honoured within ProgressEvery runs, by throwing OperationCanceledException.
    public static BenchFigures Run(ContentPack pack, MissionTuning tuning, BenchOptions options,
        IProgress<int>? progress = null, CancellationToken cancellation = default)
    {
        var tally = new BenchTally(pack);
        for (int i = 0; i < options.Runs; i++)
        {
            if (i % ProgressEvery == 0)
            {
                if (i > 0)
                {
                    progress?.Report(i);
                }

                cancellation.ThrowIfCancellationRequested();
            }

            tally.Add(Replay(pack, tuning, options, options.Seed + (ulong)i));
        }

        progress?.Report(options.Runs);
        return tally.Figures();
    }

    // Every objective in the pack, in the pack's order, options.Runs runs a cell (design 4.8, "Every objective"): one cell for each
    // strength asked for, or, for a real team, one cell with Strength null, refused when the team's size is outside what the objective
    // takes. Progress is the runs done across every cell.
    public static IReadOnlyList<ObjectiveCell> Objectives(ContentPack pack, MissionTuning tuning, BenchOptions options,
        IReadOnlyList<TeamStrength> strengths, IProgress<int>? progress = null, CancellationToken cancellation = default)
    {
        var cells = new List<ObjectiveCell>();
        int done = 0;
        foreach (ObjectiveDef objective in pack.Objectives)
        {
            if (options.Team is { } team)
            {
                int size = team.Members.Count;
                if (size < objective.TeamSizeMin || size > objective.TeamSizeMax)
                {
                    // The words MissionsModule's launch check uses.
                    cells.Add(new ObjectiveCell(objective.Id, null, null, $"A team of {size} is outside the {objective.TeamSizeMin} to {objective.TeamSizeMax} this objective takes."));
                    continue;
                }

                cells.Add(new ObjectiveCell(objective.Id, null, Run(pack, tuning, options with { ObjectiveId = objective.Id }, Offset(progress, done), cancellation), null));
                done += options.Runs;
                continue;
            }

            foreach (TeamStrength strength in strengths)
            {
                BenchOptions cell = options with { ObjectiveId = objective.Id, Strength = strength };
                cells.Add(new ObjectiveCell(objective.Id, strength, Run(pack, tuning, cell, Offset(progress, done), cancellation), null));
                done += options.Runs;
            }
        }

        return cells;
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

        return new MissionTrace(seed, team, beats, step.Result!, step.State);
    }

    public static CallAnswer Answer(CallPolicy policy, PendingCall call)
    {
        return policy switch
        {
            CallPolicy.Bold => CallAnswer.Bold,
            CallPolicy.Safe => CallAnswer.Safe,
            CallPolicy.Withdraw => call.Options.Contains(CallAnswer.Withdraw) ? CallAnswer.Withdraw : CallAnswer.Safe,
            _ => call.DecisionId == DecisionIds.Abort ? CallAnswer.Safe : call.LeadPrefers,
        };
    }

    private static IProgress<int>? Offset(IProgress<int>? progress, int done)
    {
        return progress is null ? null : new OffsetProgress(progress, done);
    }

    private sealed class OffsetProgress(IProgress<int> inner, int done) : IProgress<int>
    {
        public void Report(int value)
        {
            inner.Report(done + value);
        }
    }
}
