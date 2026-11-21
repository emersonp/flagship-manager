using System.Globalization;
using MissionBench;
using Sim.Core;
using Sim.Core.Content;
using Sim.Missions;

namespace Web.Tuning;

// One row of a figures table: its label, its kind for the noise rule (BenchNoise.KindOf), the format its values are printed in, its value
// from one column's figures (null where that column has none, printed "-"), and what the noise rule reads from them (a sum, its sum of
// squares, and the count it is over). Group names the quiet row-group head the row opens, or is null.
public sealed record FigureRow(
    string Label,
    BenchRow Row,
    string Format,
    Func<BenchFigures, double?> Value,
    Func<BenchFigures, (double Sum, double Squares, int N)?> Noise,
    string? Group = null);

// The Tuning page's figures (1f part one, Task 10; design 4.8), drawn from BenchFigures as HTML tables. The summary is BenchReport.Summary's
// rows, in its labels, order, and formats; BenchReport is not changed for the page's sake, so the two lists are kept equal by a test
// (TuningRunsTests), not by sharing. Then each encounter's table: its checks, approaches, narrowed, tiers, deaths, and incapacitations.
public static class FigureRows
{
    public const string MeansNote = "Means are over the missions that reached each encounter. A chained encounter shifts later positions.";

    // The summary's rows for these columns: an experience row for every member any column has, and three rows for each encounter
    // position any column reached.
    public static IReadOnlyList<FigureRow> Summary(IReadOnlyCollection<BenchFigures> columns)
    {
        var rows = new List<FigureRow>
        {
            Outcome("Full success %", MissionOutcome.FullSuccess, "Results"),
            Outcome("Partial success %", MissionOutcome.PartialSuccess),
            Outcome("Withdrawal %", MissionOutcome.Withdrawal),
            Outcome("Incomplete %", MissionOutcome.Incomplete),
            Outcome("Loss %", MissionOutcome.Loss),
            new("Deaths per 100 crew-missions", BenchRow.Deaths, "0.0", f => 100.0 * f.Deaths / f.CrewMissions, f => (f.Deaths, 0, f.CrewMissions), "Harm"),
            new("Incapacitated per 100 crew-missions", BenchRow.Incapacitated, "0.0", f => 100.0 * f.Incapacitated / f.CrewMissions, f => (f.Incapacitated, 0, f.CrewMissions)),
            new("Calls per mission", BenchRow.Calls, "0.00", f => (double)f.Calls / f.Runs, f => (f.Calls, f.CallsSquares, f.Runs), "Calls and initiatives"),
            new("Initiatives per mission", BenchRow.Initiatives, "0.00", f => (double)f.Initiatives / f.Runs, f => (f.Initiatives, f.InitiativesSquares, f.Runs)),
        };
        List<string> members = columns.SelectMany(c => c.ExperienceByMember.Keys).Distinct().Order(StringComparer.Ordinal).ToList();
        foreach ((string member, int i) in members.Select((m, i) => (m, i)))
        {
            rows.Add(new FigureRow(
                $"Experience, {member}",
                BenchRow.Experience,
                "0.0",
                f => f.ExperienceByMember.TryGetValue(member, out double xp) ? xp : null,
                f => f.ExperienceByMember.TryGetValue(member, out double xp) ? (xp * f.Runs, f.ExperienceSquaresByMember.GetValueOrDefault(member), f.Runs) : null,
                i == 0 ? "Experience per member" : null));
        }

        int stages = columns.Count == 0 ? 0 : columns.Max(c => c.Stages.Count);
        for (int i = 0; i < stages; i++)
        {
            int stage = i;
            StageFigures? At(BenchFigures f) => stage < f.Stages.Count ? f.Stages[stage] : null;
            rows.Add(new FigureRow($"Reached encounter {i + 1} %", BenchRow.Reached, "0.0", f => At(f) is { } s ? 100.0 * s.Reached / f.Runs : null, f => At(f) is { } s ? (s.Reached, 0, f.Runs) : null, $"Encounter {i + 1}"));
            rows.Add(new FigureRow($"Momentum entering encounter {i + 1}", BenchRow.Momentum, "0.00", f => At(f)?.Momentum, f => At(f) is { } s ? (s.Momentum * s.Reached, s.MomentumSquares, s.Reached) : null));
            rows.Add(new FigureRow($"Strain entering encounter {i + 1}", BenchRow.Strain, "0.00", f => At(f)?.Strain, f => At(f) is { } s ? (s.Strain * s.Reached, s.StrainSquares, s.Reached) : null));
        }

        return rows;
    }

    // An encounter's rows. A column that never reached it reads as no checks: its shares are "-", its counts 0.
    public static IReadOnlyList<FigureRow> Encounter(string encounterId)
    {
        EncounterFigures Of(BenchFigures f) => f.Encounters.FirstOrDefault(e => e.EncounterId == encounterId) ?? Empty(encounterId);
        FigureRow Share(string label, BenchRow row, Func<EncounterFigures, int> count) => new(
            label,
            row,
            "0.0",
            f => Of(f) is { Checks: > 0 } e ? 100.0 * count(e) / e.Checks : null,
            f => Of(f) is { Checks: > 0 } e ? (count(e), 0, e.Checks) : null);
        var rows = new List<FigureRow>
        {
            new("Checks", BenchRow.EncounterChecks, "#,0", f => Of(f).Checks, f => (Of(f).Checks, Of(f).ChecksSquares, f.Runs)),
        };
        for (int i = 0; i < EncounterDef.MaxApproaches; i++)
        {
            int position = i;
            rows.Add(Share($"Approach {i + 1} %", BenchRow.EncounterPosition, e => e.ByPosition[position]));
        }

        rows.Add(Share("Narrowed %", BenchRow.EncounterNarrowed, e => e.Narrowed));
        foreach (Tier tier in new[] { Tier.Critical, Tier.Success, Tier.Partial, Tier.Failure, Tier.Disaster })
        {
            rows.Add(Share($"{tier} %", BenchRow.EncounterTier, e => e.Tiers[tier]));
        }

        rows.Add(new FigureRow("Deaths", BenchRow.EncounterDeaths, "#,0", f => Of(f).Deaths, f => (Of(f).Deaths, 0, f.CrewMissions)));
        rows.Add(new FigureRow("Incapacitated", BenchRow.EncounterIncapacitated, "#,0", f => Of(f).Incapacitated, f => (Of(f).Incapacitated, 0, f.CrewMissions)));
        return rows;
    }

    // The encounters any column reached, in the figures' order (by id).
    public static IReadOnlyList<string> Encounters(IEnumerable<BenchFigures> columns)
    {
        return columns.SelectMany(c => c.Encounters.Select(e => e.EncounterId)).Distinct().Order(StringComparer.Ordinal).ToList();
    }

    public static int Checks(BenchFigures figures, string encounterId)
    {
        return figures.Encounters.FirstOrDefault(e => e.EncounterId == encounterId)?.Checks ?? 0;
    }

    // A value as the command line prints it, or "-".
    public static string Number(double? value, string format)
    {
        return value is { } v ? v.ToString(format, CultureInfo.InvariantCulture) : "-";
    }

    // The draft minus the saved, always signed, with a true minus; "-" when either column has no value.
    public static string Change(double? saved, double? draft, string format)
    {
        if (saved is not { } before || draft is not { } after)
        {
            return "-";
        }

        double change = after - before;
        return (change < 0 ? "−" : "+") + Math.Abs(change).ToString(format, CultureInfo.InvariantCulture);
    }

    // Whether the change from saved to draft is within the noise (design 4.8). Never when either column has no value.
    public static bool Within(FigureRow row, BenchFigures saved, BenchFigures draft)
    {
        return row.Noise(saved) is { } a && row.Noise(draft) is { } b && row.Value(saved) is not null && row.Value(draft) is not null
            && BenchNoise.Within(row.Row, a, b);
    }

    private static FigureRow Outcome(string label, MissionOutcome outcome, string? group = null)
    {
        return new FigureRow(label, BenchRow.Result, "0.0", f => 100.0 * f.Outcomes[outcome] / f.Runs, f => (f.Outcomes[outcome], 0, f.Runs), group);
    }

    private static EncounterFigures Empty(string id)
    {
        return new EncounterFigures(id, 0, new int[EncounterDef.MaxApproaches], 0, Enum.GetValues<Tier>().ToDictionary(t => t, _ => 0), 0, 0, 0);
    }
}
