using SimRunner;

namespace SimRunner.Tests;

public class CliTests
{
    private static readonly string Content = Path.Combine(FindRoot(), "tests", "fixtures", "content");

    [Theory]
    [InlineData()]
    [InlineData("fly")]
    [InlineData("cycles", "--cycles", "abc")]
    [InlineData("cycles", "--cycles")]
    [InlineData("cycles", "--speed", "3")]
    [InlineData("bench")]
    [InlineData("bench", "--objective", "OBJECTIVE_TEMPLATE_1", "--team", "heroes")]
    [InlineData("bench", "--objective", "OBJECTIVE_TEMPLATE_1", "--autonomy", "2")]
    [InlineData("bench", "--objective", "OBJECTIVE_TEMPLATE_1", "--policy", "random")]
    [InlineData("bench", "--objective", "OBJECTIVE_TEMPLATE_1", "--team", "sweep", "--replay", "3")]
    public void Bad_usage_exits_2_and_prints_the_usage(params string[] args)
    {
        (int exit, string output, string error) = Run(args);

        Assert.Equal(2, exit);
        Assert.Equal("", output);
        Assert.Contains("Usage:", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_content_or_an_unknown_objective_exits_1()
    {
        Assert.Equal(1, Run("cycles", "--content", "no-such-folder").Exit);
        (int exit, _, string error) = Run("bench", "--objective", "OBJECTIVE_TEMPLATE_9", "--content", Content);

        Assert.Equal(1, exit);
        Assert.Contains("OBJECTIVE_TEMPLATE_1", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Cycles_runs_and_writes_its_summary()
    {
        string file = Path.Combine(Path.GetTempPath(), $"simrunner-{Guid.NewGuid():N}.json");
        try
        {
            (int exit, string output, _) = Run("cycles", "--legacies", "2", "--cycles", "3", "--content", Content, "--out", file);

            Assert.Equal(0, exit);
            Assert.Equal("Content CORE v1: 2 legacies x 3 cycles, 274 events. Missions: 3 full, 2 partial, 0 incomplete, 1 withdrawn, 0 lost. Deaths: 0. Storylets per 100 cycles: 0.0 offered, 0.0 answered. Vignettes per 100 cycles: 100.0 arrived, 0.0 left to their default. Morale at the end: 0 low, 16 steady, 0 high. OK.", output.Split('\n')[0].Trim());
            Assert.Contains("LogHash", File.ReadAllText(file), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Bench_prints_the_figures()
    {
        (int exit, string output, string error) = Run("bench", "--objective", "OBJECTIVE_TEMPLATE_1", "--runs", "50", "--lead", "cautious", "--autonomy", "-1", "--gear", "scanner,tools", "--content", Content);

        Assert.Equal((0, ""), (exit, error));
        Assert.Contains("OBJECTIVE_TEMPLATE_1: 50 missions from seed 1. Team mixed, lead Cautious, policy BackLead. Priority 0, autonomy -1, develop juniors off. Gear: scanner, tools. Draw: fresh each run.", output, StringComparison.Ordinal);
        Assert.Contains("Full success %", output, StringComparison.Ordinal);
        Assert.Contains("Encounters (mixed); worst run: seed", output, StringComparison.Ordinal);
    }

    // Approaches are the author's to tune, so the bench says nothing of how one's difficulty compares with another's.
    [Fact]
    public void Bench_says_nothing_of_approach_difficulties()
    {
        string root = Path.Combine(Path.GetTempPath(), $"content-{Guid.NewGuid():N}");
        try
        {
            foreach (string source in Directory.GetFiles(Content, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(root, Path.GetRelativePath(Content, source));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target);
            }

            string encounters = Path.Combine(root, "encounters", "base.yaml");
            File.WriteAllText(encounters, File.ReadAllText(encounters).Replace("skill: SKILL_C, difficulty: 6,", "skill: SKILL_C, difficulty: 9,", StringComparison.Ordinal));

            (int exit, string output, string error) = Run("bench", "--objective", "OBJECTIVE_TEMPLATE_1", "--runs", "5", "--content", root);

            Assert.Equal((0, ""), (exit, error));
            Assert.DoesNotContain("The second approach", output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // Risk tolerance is gone (approaches plan, Task 4), so the option is unknown.
    [Fact]
    public void Bench_refuses_the_risk_option()
    {
        (int exit, _, string error) = Run("bench", "--objective", "OBJECTIVE_TEMPLATE_1", "--runs", "5", "--risk", "1", "--content", Content);

        Assert.Equal(2, exit);
        Assert.Contains("--risk", error, StringComparison.Ordinal);
        Assert.DoesNotContain("--risk", Cli.Usage, StringComparison.Ordinal);
    }

    [Fact]
    public void The_header_says_no_gear_and_a_fixed_draw_when_so()
    {
        (_, string output, _) = Run("bench", "--objective", "OBJECTIVE_TEMPLATE_1", "--runs", "5", "--develop", "--fixed-draw", "--content", Content);

        Assert.Contains("develop juniors on. Gear: none. Draw: fixed.", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_mistyped_gear_tag_exits_1_and_lists_the_known_ones()
    {
        (int exit, string output, string error) = Run("bench", "--objective", "OBJECTIVE_TEMPLATE_1", "--gear", "scaner", "--content", Content);

        Assert.Equal(1, exit);
        Assert.Equal("", output);
        Assert.Contains("'scaner'", error, StringComparison.Ordinal);
        Assert.Contains("comms, medical, scanner, tools", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sweep_shows_the_three_strengths_side_by_side()
    {
        (int exit, string output, _) = Run("bench", "--objective", "OBJECTIVE_TEMPLATE_1", "--runs", "20", "--team", "sweep", "--content", Content);

        Assert.Equal(0, exit);
        Assert.Matches(@"rookie\s+mixed\s+veteran", output);
    }

    // The tuning loop: the same seeds under the pack's tuning and under an edited copy.
    [Fact]
    public void Compare_shows_before_after_and_change()
    {
        string file = Path.Combine(Path.GetTempPath(), $"tuning-{Guid.NewGuid():N}.yaml");
        try
        {
            File.WriteAllText(file, File.ReadAllText(Path.Combine(Content, "tuning", "missions.yaml")).Replace("  target: 7 ", "  target: 9 ", StringComparison.Ordinal));

            (int exit, string output, _) = Run("bench", "--objective", "OBJECTIVE_TEMPLATE_1", "--runs", "200", "--compare", file, "--content", Content);

            Assert.Equal(0, exit);
            Assert.Matches(@"before\s+after\s+change", output);
            Assert.Matches(@"Full success %\s+\S+\s+\S+\s+-\d", output);
            Assert.DoesNotContain("Note:", output, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void A_comparison_file_with_other_bench_teams_says_they_are_ignored()
    {
        string file = Path.Combine(Path.GetTempPath(), $"tuning-{Guid.NewGuid():N}.yaml");
        try
        {
            File.WriteAllText(file, File.ReadAllText(Path.Combine(Content, "tuning", "missions.yaml")).Replace("mixed:   { size: 3", "mixed:   { size: 4", StringComparison.Ordinal));

            (int exit, string output, _) = Run("bench", "--objective", "OBJECTIVE_TEMPLATE_1", "--runs", "20", "--compare", file, "--content", Content);

            Assert.Equal(0, exit);
            Assert.Contains($"Note: the bench teams come from the content pack; the bench block in {file} is ignored.", output, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void A_broken_comparison_file_exits_1_and_names_the_value()
    {
        string file = Path.Combine(Path.GetTempPath(), $"tuning-{Guid.NewGuid():N}.yaml");
        try
        {
            File.WriteAllText(file, "check:\n  target: 7\n");

            (int exit, _, string error) = Run("bench", "--objective", "OBJECTIVE_TEMPLATE_1", "--compare", file, "--content", Content);

            Assert.Equal(1, exit);
            Assert.Contains("check.gearBonus is missing.", error, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Replay_prints_one_mission_beat_by_beat()
    {
        (int exit, string output, _) = Run("bench", "--objective", "OBJECTIVE_TEMPLATE_1", "--replay", "7", "--content", Content);

        Assert.Equal(0, exit);
        Assert.StartsWith("Seed 7. Lead CREW_01.", output, StringComparison.Ordinal);
        Assert.Contains("Stage 1 (approach):", output, StringComparison.Ordinal);
        Assert.Contains("Result:", output, StringComparison.Ordinal);
    }

    // The bench's full text, pinned before slice 1f part one moved its loop into a tally (Task 1), so the command line prints byte
    // for byte what it printed: a single team, a sweep, --compare, and --replay, in tests/SimRunner.Tests/golden-bench.txt. If a
    // change is intended, accept it in the same commit: the test writes what it found beside the file, as golden-bench.received.txt.
    [Fact]
    public void Golden_bench_output_matches_the_accepted_text()
    {
        string file = Path.Combine(Path.GetTempPath(), $"tuning-{Guid.NewGuid():N}.yaml");
        try
        {
            File.WriteAllText(file, File.ReadAllText(Path.Combine(Content, "tuning", "missions.yaml")).Replace("  target: 7 ", "  target: 9 ", StringComparison.Ordinal));
            string[][] cases =
            [
                ["bench", "--objective", "OBJECTIVE_TEMPLATE_1", "--runs", "300", "--seed", "4", "--team", "rookie", "--lead", "reckless", "--gear", "scanner", "--content", Content],
                ["bench", "--objective", "OBJECTIVE_TEMPLATE_1", "--runs", "200", "--team", "sweep", "--policy", "safe", "--fixed-draw", "--content", Content],
                ["bench", "--objective", "OBJECTIVE_TEMPLATE_1", "--runs", "250", "--compare", file, "--content", Content],
                ["bench", "--objective", "OBJECTIVE_TEMPLATE_1", "--team", "veteran", "--replay", "11", "--content", Content],
                ["bench", "--objective", "OBJECTIVE_TEMPLATE_1", "--replay", "5", "--compare", file, "--content", Content],
            ];
            string actual = string.Concat(cases.Select(args =>
            {
                (int exit, string output, string error) = Run(args);
                return $"$ {string.Join(' ', args.Where(a => a != Content && a != file))} -> {exit}{error}\n{output.ReplaceLineEndings("\n")}\n";
            }));
            string path = Path.Combine(FindRoot(), "tests", "SimRunner.Tests", "golden-bench.txt");
            string received = Path.ChangeExtension(path, ".received.txt");
            string expected = File.Exists(path) ? File.ReadAllText(path).ReplaceLineEndings("\n") : "";
            if (actual == expected)
            {
                File.Delete(received);
            }
            else
            {
                File.WriteAllText(received, actual);
            }

            Assert.Equal(expected, actual);
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static (int Exit, string Output, string Error) Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        int exit = Cli.Run(args, output, error);
        return (exit, output.ToString(), error.ToString());
    }

    private static string FindRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    // 1e part three (design 4.10): what fired per 100 cycles, by storylet, vignette, and option, and what never did, as a warning.
    [Fact]
    public void Cycles_reports_what_fired_and_what_never_did()
    {
        (int exit, string output, _) = Run("cycles", "--legacies", "2", "--cycles", "3", "--answers", "random", "--content", Content);

        Assert.Equal(0, exit);
        Assert.Contains("Fired per 100 cycles (random answers): STORYLET_01 0.0 (option_a 0.0, option_b 0.0);", output, StringComparison.Ordinal);
        Assert.Contains("Traits set from 0 per 100 cycles: ", output, StringComparison.Ordinal);
        Assert.Contains("Never fired: STORYLET_01, STORYLET_01 option_a,", output, StringComparison.Ordinal);
        Assert.Equal(2, Run("cycles", "--answers", "ignore", "--content", Content).Exit);
    }

    // Slice 1e-calendar, Task 6: the spread of cycle lengths, by turn, and the span of the legacies' last dates.
    [Fact]
    public void Cycles_reports_the_calendar()
    {
        (int exit, string output, _) = Run("cycles", "--legacies", "2", "--cycles", "3", "--content", Content);

        Assert.Equal(0, exit);
        Assert.Matches(@"(?m)^Calendar, by turn: 6 turns, of 5 days \d+\.\d%, 6 days \d+\.\d%, 7 days \d+\.\d%; mean \d\.\d\d days\. Last dates?: \d+ MONTH_\w+ \d+( to \d+ MONTH_\w+ \d+)?\.\r?$", output);
    }

    // 1e part five, Task 8: the chronicle's entries by kind, and those a weight of 0 skipped.
    [Fact]
    public void Cycles_reports_the_chronicle()
    {
        (int exit, string output, _) = Run("cycles", "--legacies", "2", "--cycles", "3", "--content", Content);

        Assert.Equal(0, exit);
        Assert.Contains("Chronicle: 19 entries (2 founding, 0 death, 6 mission, 11 call, 0 storylet); 0 skipped at weight 0 (0 founding, 0 death, 0 mission, 0 call, 0 storylet).", output, StringComparison.Ordinal);
    }
}
