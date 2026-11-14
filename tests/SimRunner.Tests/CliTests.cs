using SimRunner;

namespace SimRunner.Tests;

public class CliTests
{
    private static readonly string Content = Path.Combine(FindRoot(), "content");

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
            Assert.Equal("Content CORE v1: 2 legacies x 3 cycles, 36 events. OK.", output.Trim());
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
        Assert.Contains("OBJECTIVE_TEMPLATE_1: 50 missions from seed 1; lead Cautious, policy BackLead.", output, StringComparison.Ordinal);
        Assert.Contains("Full success %", output, StringComparison.Ordinal);
        Assert.Contains("Encounters (mixed); worst run: seed", output, StringComparison.Ordinal);
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
}
