using System.Globalization;
using System.Text.Json;
using MissionBench;
using Sim.Content;
using Sim.Core;
using Sim.Core.Content;
using Sim.Missions;

namespace SimRunner;

// The command line. Exit codes: 0 done, 1 content error, and 2 bad usage.
public static class Cli
{
    public const string Usage = """
        Usage:
          SimRunner cycles [--legacies N] [--cycles N] [--seed N] [--answers first|random] [--content DIR] [--out FILE]
          SimRunner bench --objective ID [--runs N] [--seed N]
                          [--team rookie|mixed|veteran|sweep] [--lead cautious|average|reckless]
                          [--autonomy -1|0|1] [--priority -1|0|1] [--develop]
                          [--gear TAG,TAG] [--policy back-lead|bold|safe|withdraw]
                          [--fixed-draw] [--compare TUNING_FILE] [--replay SEED]
                          [--content DIR] [--out FILE]
        """;

    private static readonly JsonSerializerOptions Json = new(SimJson.Options) { WriteIndented = true };

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            return args.FirstOrDefault() switch
            {
                "cycles" => Cycles(args[1..], output, error),
                "bench" => BenchCommand(args[1..], output, error),
                _ => throw new UsageException(args.Length == 0 ? "No command given." : $"Unknown command '{args[0]}'."),
            };
        }
        catch (UsageException e)
        {
            error.WriteLine(e.Message);
            error.WriteLine(Usage);
            return 2;
        }
    }

    private static int Cycles(string[] args, TextWriter output, TextWriter error)
    {
        var line = new CommandLine(args, ["--legacies", "--cycles", "--seed", "--answers", "--content", "--out"], []);
        var options = new SimOptions(line.Int("--legacies", 200, 1, 1_000_000), line.Int("--cycles", 500, 1, 1_000_000), line.Seed("--seed", 1), line.Choice("--answers", AnswerPolicy.First));
        if (LoadContent(line.Text("--content", "content"), error) is not { } pack)
        {
            return 1;
        }

        SimSummary summary = Simulation.Run(options, pack);
        if (line.Has("--out"))
        {
            File.WriteAllText(line.Text("--out", ""), JsonSerializer.Serialize(summary, Json));
        }

        int Total(MissionOutcome outcome)
        {
            return summary.Legacies.Sum(l => l.Missions[outcome]);
        }

        // Per 100 cycles of one legacy, so runs of different sizes compare.
        string PerHundred(Func<LegacySummary, int> count)
        {
            return (summary.Legacies.Sum(count) * 100.0 / (summary.Legacies.Count * (double)options.Cycles)).ToString("0.0", CultureInfo.InvariantCulture);
        }

        output.WriteLine(
            $"Content {pack.Id} v{pack.Version}: {summary.Legacies.Count} legacies x {options.Cycles} cycles, {summary.Legacies.Sum(l => l.Events)} events. "
            + $"Missions: {Total(MissionOutcome.FullSuccess)} full, {Total(MissionOutcome.PartialSuccess)} partial, {Total(MissionOutcome.Incomplete)} incomplete, {Total(MissionOutcome.Withdrawal)} withdrawn, {Total(MissionOutcome.Loss)} lost. "
            + $"Deaths: {summary.Legacies.Sum(l => l.Deaths)}. "
            + $"Storylets per 100 cycles: {PerHundred(l => l.StoryletsOffered)} offered, {PerHundred(l => l.StoryletsAnswered)} answered. "
            + $"Vignettes per 100 cycles: {PerHundred(l => l.VignettesArrived)} arrived, {PerHundred(l => l.VignettesDefaulted)} left to their default. "
            + $"Morale at the end: {summary.Legacies.Sum(l => l.MoraleLow)} low, {summary.Legacies.Sum(l => l.MoraleSteady)} steady, {summary.Legacies.Sum(l => l.MoraleHigh)} high. OK.");
        Report(pack, summary, options, output);
        return 0;
    }

    // What fired, per 100 cycles of one legacy (1e part three, design 4.10): each storylet and vignette and each of its options, and
    // each trait set from 0; then whatever never fired. A warning only: nothing here fails a run.
    private static void Report(ContentPack pack, SimSummary summary, SimOptions options, TextWriter output)
    {
        double cycles = summary.Legacies.Count * (double)options.Cycles;
        int Total(Func<LegacySummary, IReadOnlyDictionary<string, int>> of, string key)
        {
            return summary.Legacies.Sum(l => of(l).GetValueOrDefault(key));
        }

        string Rate(int count)
        {
            return (count * 100.0 / cycles).ToString("0.0", CultureInfo.InvariantCulture);
        }

        var never = new List<string>();
        var items = new List<string>();
        foreach ((string id, IEnumerable<string> optionIds) in pack.Storylets.Select(s => (s.Id, s.Options.Select(o => o.Id)))
            .Concat(pack.Vignettes.Select(v => (v.Id, v.Options.Select(o => o.Id)))))
        {
            int arrived = Total(l => l.Fired, id);
            never.AddRange(arrived == 0 ? [id] : []);
            var parts = new List<string>();
            foreach (string option in optionIds)
            {
                int taken = Total(l => l.Fired, $"{id} {option}");
                parts.Add($"{option} {Rate(taken)}");
                never.AddRange(taken == 0 ? [$"{id} {option}"] : []);
            }

            items.Add(parts.Count == 0 ? $"{id} {Rate(arrived)}" : $"{id} {Rate(arrived)} ({string.Join(", ", parts)})");
        }

        output.WriteLine($"Fired per 100 cycles ({Kebab(options.Answers)} answers): {(items.Count == 0 ? "nothing to fire" : string.Join("; ", items))}.");

        // 1e part four, Task 8 (design 4.11), from the contest each itinerary opened with: each storylet that cast, how often it lost
        // on the budget against how often it cast; and each option settled by a check, its successes over the times it was taken.
        List<string> lost = pack.Storylets.Select(s => s.Id).Where(id => Total(l => l.Cast, id) > 0)
            .Select(id => $"{id} {Rate(Total(l => l.Lost, id))} of {Rate(Total(l => l.Cast, id))} cast").ToList();
        output.WriteLine($"Lost on the budget per 100 cycles: {(lost.Count == 0 ? "nothing cast" : string.Join("; ", lost))}.");
        List<string> checks = pack.Storylets.SelectMany(s => s.Options.Where(o => o.Check is not null).Select(o => $"{s.Id} {o.Id}"))
            .Where(key => Total(l => l.ChecksTaken, key) > 0)
            .Select(key => $"{key} {(Total(l => l.ChecksSucceeded, key) * 100.0 / Total(l => l.ChecksTaken, key)).ToString("0.0", CultureInfo.InvariantCulture)}% of {Total(l => l.ChecksTaken, key)} taken")
            .ToList();
        output.WriteLine($"Check success: {(checks.Count == 0 ? "no check taken" : string.Join("; ", checks))}.");

        List<string> traits = summary.Legacies.SelectMany(l => l.TraitsSet.Keys).Distinct().Order(StringComparer.Ordinal).ToList();
        output.WriteLine($"Traits set from 0 per 100 cycles: {(traits.Count == 0 ? "none" : string.Join("; ", traits.Select(t => $"{t} {Rate(Total(l => l.TraitsSet, t))}")))}.");

        // Never eligible: its first slot never held for anyone (design 4.10). Never cast: eligible at least once, and never cast,
        // out of tries included. Out of tries is always said, since it tells the author when casting needs section 8's search.
        List<string> ids = [.. pack.Storylets.Select(s => s.Id), .. pack.Vignettes.Select(v => v.Id)];
        List<string> neverEligible = ids.Where(id => Total(l => l.Eligible, id) == 0).ToList();
        List<string> neverCast = ids.Where(id => Total(l => l.Eligible, id) > 0 && Total(l => l.Cast, id) == 0).ToList();
        List<string> outOfTries = ids.Where(id => Total(l => l.OutOfTries, id) > 0).Select(id => $"{id} in {Total(l => l.OutOfTries, id)} cycles").ToList();
        if (neverEligible.Count > 0)
        {
            output.WriteLine($"Never eligible: {string.Join(", ", neverEligible)}.");
        }

        if (neverCast.Count > 0)
        {
            output.WriteLine($"Never cast, though eligible: {string.Join(", ", neverCast)}.");
        }

        output.WriteLine($"Out of tries: {(outOfTries.Count == 0 ? "none" : string.Join(", ", outOfTries))}.");
        if (never.Count > 0)
        {
            output.WriteLine($"Never fired: {string.Join(", ", never)}.");
        }
    }

    private static string Kebab(AnswerPolicy policy)
    {
        return policy.ToString().ToLowerInvariant();
    }

    private static int BenchCommand(string[] args, TextWriter output, TextWriter error)
    {
        var line = new CommandLine(
            args,
            ["--objective", "--runs", "--seed", "--team", "--lead", "--autonomy", "--priority", "--gear", "--policy", "--compare", "--replay", "--content", "--out"],
            ["--develop", "--fixed-draw"]);
        if (!line.Has("--objective"))
        {
            throw new UsageException("bench needs --objective.");
        }

        string team = line.Text("--team", "mixed");
        if (team is not ("rookie" or "mixed" or "veteran" or "sweep"))
        {
            throw new UsageException($"--team must be one of rookie, mixed, veteran, sweep, not '{team}'.");
        }

        var options = new BenchOptions(line.Text("--objective", ""))
        {
            Runs = line.Int("--runs", 10000, 1, 10_000_000),
            Seed = line.Seed("--seed", 1),
            Lead = line.Choice("--lead", LeadStyle.Average),
            Parameters = new Parameters(line.Int("--priority", 0, -1, 1), line.Int("--autonomy", 0, -1, 1), line.Has("--develop")),
            GearTags = line.Text("--gear", "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            Policy = line.Choice("--policy", CallPolicy.BackLead),
            FixedDraw = line.Has("--fixed-draw"),
        };
        if (team == "sweep" && (line.Has("--compare") || line.Has("--replay")))
        {
            throw new UsageException("--team sweep cannot be combined with --compare or --replay.");
        }

        if (LoadContent(line.Text("--content", "content"), error) is not { } pack)
        {
            return 1;
        }

        if (pack.Objectives.All(o => o.Id != options.ObjectiveId))
        {
            error.WriteLine($"Unknown objective '{options.ObjectiveId}'. The pack has: {string.Join(", ", pack.Objectives.Select(o => o.Id))}.");
            return 1;
        }

        string[] knownGear = ["comms", "medical", .. pack.Encounters.SelectMany(e => e.Approaches.Select(a => a.GearTag)).OfType<string>()];
        knownGear = knownGear.Distinct().Order(StringComparer.Ordinal).ToArray();
        if (options.GearTags.FirstOrDefault(t => !knownGear.Contains(t)) is { } unknown)
        {
            error.WriteLine($"Unknown gear tag '{unknown}'. The pack has: {string.Join(", ", knownGear)}.");
            return 1;
        }

        MissionTuning? other = null;
        string benchNote = "";
        if (line.Has("--compare"))
        {
            string file = line.Text("--compare", "");
            if (LoadOtherTuning(file, error) is not var (tuning, otherBench))
            {
                return 1;
            }

            other = tuning;
            if (otherBench != pack.Bench)
            {
                benchNote = $"Note: the bench teams come from the content pack; the bench block in {file} is ignored.{Environment.NewLine}";
            }
        }

        if (line.Has("--replay"))
        {
            options = options with { Strength = Strength(team) };
            output.Write(benchNote);
            output.Write(BenchReport.Replay(Bench.Replay(pack, other ?? pack.Tuning, options, line.Seed("--replay", 0))));
            return 0;
        }

        var columns = new List<(string Name, BenchFigures Figures)>();
        if (team == "sweep")
        {
            foreach (TeamStrength strength in Enum.GetValues<TeamStrength>())
            {
                columns.Add((strength.ToString().ToLowerInvariant(), Bench.Run(pack, pack.Tuning, options with { Strength = strength })));
            }
        }
        else
        {
            options = options with { Strength = Strength(team) };
            columns.Add((other is null ? team : "before", Bench.Run(pack, pack.Tuning, options)));
            if (other is not null)
            {
                columns.Add(("after", Bench.Run(pack, other, options)));
            }
        }

        string gear = options.GearTags.Count == 0 ? "none" : string.Join(", ", options.GearTags);
        output.WriteLine(
            $"{options.ObjectiveId}: {options.Runs} missions from seed {options.Seed}. Team {team}, lead {options.Lead}, policy {options.Policy}. "
            + $"Priority {options.Parameters.Priority}, autonomy {options.Parameters.Autonomy}, develop juniors {(options.Parameters.DevelopJuniors ? "on" : "off")}. "
            + $"Gear: {gear}. Draw: {(options.FixedDraw ? "fixed" : "fresh each run")}.");
        output.Write(benchNote);
        output.WriteLine();
        output.Write(BenchReport.Summary(columns, showChange: other is not null));
        foreach ((string name, BenchFigures figures) in columns)
        {
            output.WriteLine();
            output.WriteLine($"Encounters ({name}); worst run: seed {figures.WorstSeed}");
            output.Write(BenchReport.Encounters(figures));
        }

        if (line.Has("--out"))
        {
            File.WriteAllText(line.Text("--out", ""), JsonSerializer.Serialize(columns.ToDictionary(c => c.Name, c => c.Figures), Json));
        }

        return 0;
    }

    private static TeamStrength Strength(string team)
    {
        return team switch
        {
            "rookie" => TeamStrength.Rookie,
            "veteran" => TeamStrength.Veteran,
            _ => TeamStrength.Mixed,
        };
    }

    private static ContentPack? LoadContent(string directory, TextWriter error)
    {
        ContentLoadResult content = ContentLoader.LoadDirectory(directory);
        foreach (ContentError e in content.Errors)
        {
            error.WriteLine($"{e.File}: {e.Message}");
        }

        return content.Pack;
    }

    private static (MissionTuning Tuning, BenchTuning Bench)? LoadOtherTuning(string file, TextWriter error)
    {
        if (!File.Exists(file))
        {
            error.WriteLine($"{file}: Tuning file not found.");
            return null;
        }

        (MissionTuning? tuning, BenchTuning? bench, IReadOnlyList<ContentError> errors) = ContentLoader.LoadTuningText(File.ReadAllText(file));
        foreach (ContentError e in errors)
        {
            error.WriteLine($"{file}: {e.Message}");
        }

        return tuning is null ? null : (tuning, bench!);
    }
}
