namespace MissionBench.Tests;

// The noise rule (slice 1f part one, design 4.8), tested either side of its threshold. Each threshold is computed here from the
// formula, not read from BenchNoise.
public class BenchNoiseTests
{
    private static (double Sum, double Squares, int N) Share(double p, int n) => (p * n, 0, n);

    [Fact]
    public void A_share_is_greyed_under_twice_its_standard_error_and_not_over()
    {
        const int n = 10_000;
        static double Threshold(double a, double b) => 2 * Math.Sqrt(a * (1 - a) / n + b * (1 - b) / n);

        // 34.0% against 33.0%: a change of 0.010, under the threshold of about 0.0133.
        Assert.True(0.010 < Threshold(0.34, 0.33));
        Assert.True(BenchNoise.Within(FigureKind.Share, Share(0.34, n), Share(0.33, n)));

        // 34.0% against 35.5%: a change of 0.015, over the threshold of about 0.0135.
        Assert.True(0.015 > Threshold(0.34, 0.355));
        Assert.False(BenchNoise.Within(FigureKind.Share, Share(0.34, n), Share(0.355, n)));
    }

    [Fact]
    public void A_rate_is_greyed_under_twice_its_standard_error_and_not_over()
    {
        const int n = 10_000;
        static double Threshold(double a, double b) => 2 * Math.Sqrt(a / ((double)n * n) + b / ((double)n * n));

        // 300 deaths against 340 in 10,000 crew-missions: a change of 0.0040 a crew-mission, under about 0.0051.
        Assert.True(40.0 / n < Threshold(300, 340));
        Assert.True(BenchNoise.Within(FigureKind.Rate, (300, 0, n), (340, 0, n)));

        // 300 against 360: a change of 0.0060, over about 0.0051.
        Assert.True(60.0 / n > Threshold(300, 360));
        Assert.False(BenchNoise.Within(FigureKind.Rate, (300, 0, n), (360, 0, n)));
    }

    [Fact]
    public void A_mean_is_greyed_under_twice_its_standard_error_and_not_over()
    {
        // 200 runs of calls 0 to 4 in turn: a mean of 2 and a sample variance of 400 / 199.
        double[] runs = [.. Enumerable.Range(0, 200).Select(i => (double)(i % 5))];
        double mean = runs.Average();
        double variance = runs.Sum(x => (x - mean) * (x - mean)) / (runs.Length - 1);
        double threshold = 2 * Math.Sqrt(variance / runs.Length + variance / runs.Length);

        // The same spread, shifted by a little under and a little over the threshold.
        Assert.False(BenchNoise.Within(FigureKind.Mean, Column(runs), Column(Shifted(runs, threshold * 1.05))));
        Assert.True(BenchNoise.Within(FigureKind.Mean, Column(runs), Column(Shifted(runs, threshold * 0.95))));
        Assert.True(BenchNoise.Within(FigureKind.Mean, Column(Shifted(runs, threshold * 0.95)), Column(runs)));

        static double[] Shifted(double[] xs, double by) => [.. xs.Select(x => x + by)];
        static (double, double, int) Column(double[] xs) => (xs.Sum(), xs.Sum(x => x * x), xs.Length);
    }

    [Theory]
    [InlineData(FigureKind.Share)]
    [InlineData(FigureKind.Rate)]
    [InlineData(FigureKind.Mean)]
    public void A_column_of_no_runs_is_never_within_the_noise(FigureKind kind)
    {
        Assert.False(BenchNoise.Within(kind, (0, 0, 0), (0, 0, 0)));
        Assert.False(BenchNoise.Within(kind, (0, 0, 0), (5, 25, 10)));
        Assert.False(BenchNoise.Within(kind, (5, 25, 10), (0, 0, 0)));
    }

    [Fact]
    public void No_change_is_within_the_noise_even_with_no_spread()
    {
        Assert.True(BenchNoise.Within(FigureKind.Rate, (0, 0, 1000), (0, 0, 1000)));
        Assert.True(BenchNoise.Within(FigureKind.Share, (1000, 0, 1000), (1000, 0, 1000)));
    }

    [Fact]
    public void Every_row_has_a_kind_as_design_4_8_lists_them()
    {
        Assert.All(Enum.GetValues<BenchRow>(), row => BenchNoise.KindOf(row));
        Assert.Equal(FigureKind.Share, BenchNoise.KindOf(BenchRow.Result));
        Assert.Equal(FigureKind.Share, BenchNoise.KindOf(BenchRow.EncounterNarrowed));
        Assert.Equal(FigureKind.Rate, BenchNoise.KindOf(BenchRow.Deaths));
        Assert.Equal(FigureKind.Rate, BenchNoise.KindOf(BenchRow.EncounterDeaths));
        Assert.Equal(FigureKind.Mean, BenchNoise.KindOf(BenchRow.Strain));
        Assert.Equal(FigureKind.Mean, BenchNoise.KindOf(BenchRow.EncounterChecks));
    }
}
