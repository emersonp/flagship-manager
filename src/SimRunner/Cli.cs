using System.Text.Json;
using System.Text.Json.Serialization;
using MissionBench;
using Sim.Content;
using Sim.Core.Content;
using Sim.Missions;

namespace SimRunner;

// The command line. Exit codes: 0 done, 1 content error, and 2 bad usage.
public static class Cli
{
    public const string Usage = """
        Usage:
          SimRunner cycles [--legacies N] [--cycles N] [--seed N] [--content DIR] [--out FILE]
          SimRunner bench --objective ID [--runs N] [--seed N]
                          [--team rookie|mixed|veteran|sweep] [--lead cautious|average|reckless]
                          [--autonomy -1|0|1] [--risk -1|0|1] [--priority -1|0|1] [--develop]
                          [--gear TAG,TAG] [--policy back-lead|bold|safe|withdraw]
                          [--fixed-draw] [--compare TUNING_FILE] [--replay SEED]
                          [--content DIR] [--out FILE]
        """;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };

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
        var line = new CommandLine(args, ["--legacies", "--cycles", "--seed", "--content", "--out"], []);
        var options = new SimOptions(line.Int("--legacies", 200, 1, 1_000_000), line.Int("--cycles", 500, 1, 1_000_000), line.Seed("--seed", 1));
        if (LoadContent(line.Text("--content", "content"), error) is not { } pack)
        {
            return 1;
        }

        SimSummary summary = Simulation.Run(options);
        if (line.Has("--out"))
        {
            File.WriteAllText(line.Text("--out", ""), JsonSerializer.Serialize(summary, Json));
        }

        output.WriteLine($"Content {pack.Id} v{pack.Version}: {summary.Legacies.Count} legacies x {options.Cycles} cycles, {summary.Legacies.Sum(l => l.Events)} events. OK.");
        return 0;
    }

    private static int BenchCommand(string[] args, TextWriter output, TextWriter error)
    {
        var line = new CommandLine(
            args,
            ["--objective", "--runs", "--seed", "--team", "--lead", "--autonomy", "--risk", "--priority", "--gear", "--policy", "--compare", "--replay", "--content", "--out"],
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
            Parameters = new Parameters(line.Int("--priority", 0, -1, 1), line.Int("--risk", 0, -1, 1), line.Int("--autonomy", 0, -1, 1), line.Has("--develop")),
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

        string[] knownGear = ["comms", "medical", .. pack.Encounters.SelectMany(e => new[] { e.Careful.GearTag, e.Fast.GearTag }).OfType<string>()];
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
            + $"Priority {options.Parameters.Priority}, risk {options.Parameters.Risk}, autonomy {options.Parameters.Autonomy}, develop juniors {(options.Parameters.DevelopJuniors ? "on" : "off")}. "
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

        IEnumerable<string> uneven = pack.Encounters.Where(e => e.Fast.Difficulty != e.Careful.Difficulty + 1).Select(e => e.Id);
        if (uneven.Any())
        {
            output.WriteLine();
            output.WriteLine($"Fast is not careful + 1 in: {string.Join(", ", uneven)}.");
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
