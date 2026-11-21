namespace MissionBench;

// How a figure's spread is judged (slice 1f part one, design 4.8). Share: successes of n (runs, or checks). Rate: a count over n
// crew-missions, as Poisson. Mean: a sum and sum of squares, one term a run, over n runs.
public enum FigureKind { Share, Rate, Mean }

// The rows of the bench's figures, summary then per encounter (design 4.8), so the page asks BenchNoise which kind a row is rather
// than deciding it in a component.
public enum BenchRow
{
    // Shares of runs.
    Result,
    Reached,

    // Rates per crew-mission.
    Deaths,
    Incapacitated,

    // Means per run (momentum and strain over the runs that reached the stage).
    Calls,
    Initiatives,
    Experience,
    Momentum,
    Strain,

    // An encounter's table: its checks a mean per run (the author's answer to the 1f mock-up), shares of its checks, and rates per
    // crew-mission.
    EncounterChecks,
    EncounterTier,
    EncounterPosition,
    EncounterNarrowed,
    EncounterDeaths,
    EncounterIncapacitated,
}

// Decides which changes in the change column are greyed: those within the noise.
public static class BenchNoise
{
    public const string Rule = "Greyed: a change smaller than twice its standard error, as if the two columns were independent samples; the same seeds make the true noise smaller.";

    // Which kind each row is: the one list of it.
    public static FigureKind KindOf(BenchRow row) => row switch
    {
        BenchRow.Result or BenchRow.Reached
            or BenchRow.EncounterTier or BenchRow.EncounterPosition or BenchRow.EncounterNarrowed => FigureKind.Share,
        BenchRow.Deaths or BenchRow.Incapacitated
            or BenchRow.EncounterDeaths or BenchRow.EncounterIncapacitated => FigureKind.Rate,
        BenchRow.Calls or BenchRow.Initiatives or BenchRow.Experience or BenchRow.Momentum or BenchRow.Strain
            or BenchRow.EncounterChecks => FigureKind.Mean,
        _ => throw new ArgumentOutOfRangeException(nameof(row), row, null),
    };

    public static bool Within(BenchRow row, (double Sum, double Squares, int N) a, (double Sum, double Squares, int N) b) =>
        Within(KindOf(row), a, b);

    // True when |b - a| is under twice the standard error of the difference of two independent samples (design 4.8). Each column's
    // variance of its figure: a share's p(1-p)/n, a rate's count/n², a mean's sample variance over n. Squares is read for a mean only.
    // A column with n of 0 is never within the noise (nothing to compare). A change of exactly nothing is within it, even where
    // neither column has any spread (0 deaths against 0).
    public static bool Within(FigureKind kind, (double Sum, double Squares, int N) a, (double Sum, double Squares, int N) b)
    {
        if (a.N <= 0 || b.N <= 0)
        {
            return false;
        }

        double change = Math.Abs(b.Sum / b.N - a.Sum / a.N);
        if (change == 0)
        {
            return true;
        }

        double error = Math.Sqrt(Variance(kind, a) + Variance(kind, b));
        return change < 2 * error;
    }

    private static double Variance(FigureKind kind, (double Sum, double Squares, int N) column)
    {
        double n = column.N;
        switch (kind)
        {
            case FigureKind.Share:
                double p = column.Sum / n;
                return p * (1 - p) / n;
            case FigureKind.Rate:
                return column.Sum / (n * n);
            case FigureKind.Mean:
                if (column.N < 2)
                {
                    return 0;
                }

                // The sample variance, (Σx² - (Σx)²/n) / (n - 1), floored at 0 against rounding.
                double sample = Math.Max(0, (column.Squares - column.Sum * column.Sum / n) / (n - 1));
                return sample / n;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }
}
