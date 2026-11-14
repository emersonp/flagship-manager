using System.Globalization;
using System.Text;
using Sim.Core;
using Sim.Missions;

namespace MissionBench;

// Plain-text tables for the command line. The same figures feed the tuning page later.
public static class BenchReport
{
    // One column per bench. With showChange, a last column gives the second minus the first.
    public static string Summary(IReadOnlyList<(string Name, BenchFigures Figures)> columns, bool showChange)
    {
        var rows = new List<(string Label, Func<BenchFigures, double?> Value, string Format)>
        {
            ("Full success %", f => Share(f, MissionOutcome.FullSuccess), "0.0"),
            ("Partial success %", f => Share(f, MissionOutcome.PartialSuccess), "0.0"),
            ("Withdrawal %", f => Share(f, MissionOutcome.Withdrawal), "0.0"),
            ("Incomplete %", f => Share(f, MissionOutcome.Incomplete), "0.0"),
            ("Loss %", f => Share(f, MissionOutcome.Loss), "0.0"),
            ("Deaths per 100 crew-missions", f => 100.0 * f.Deaths / f.CrewMissions, "0.0"),
            ("Incapacitated per 100 crew-missions", f => 100.0 * f.Incapacitated / f.CrewMissions, "0.0"),
            ("Calls per mission", f => (double)f.Calls / f.Runs, "0.00"),
            ("Notices per mission", f => (double)f.Notices / f.Runs, "0.00"),
        };
        foreach (string member in columns.SelectMany(c => c.Figures.ExperienceByMember.Keys).Distinct().Order(StringComparer.Ordinal))
        {
            rows.Add(($"Experience, {member}", f => f.ExperienceByMember.TryGetValue(member, out double xp) ? xp : null, "0.0"));
        }

        int stages = columns.Max(c => c.Figures.Stages.Count);
        for (int i = 0; i < stages; i++)
        {
            int stage = i;
            rows.Add(($"Reached encounter {i + 1} %", f => stage < f.Stages.Count ? 100.0 * f.Stages[stage].Reached / f.Runs : null, "0.0"));
            rows.Add(($"Momentum entering encounter {i + 1}", f => stage < f.Stages.Count ? f.Stages[stage].Momentum : null, "0.00"));
            rows.Add(($"Strain entering encounter {i + 1}", f => stage < f.Stages.Count ? f.Stages[stage].Strain : null, "0.00"));
        }

        var table = new List<string[]> { new[] { "" }.Concat(columns.Select(c => c.Name)).Concat(showChange ? ["change"] : Array.Empty<string>()).ToArray() };
        foreach ((string label, Func<BenchFigures, double?> value, string format) in rows)
        {
            List<double?> values = columns.Select(c => value(c.Figures)).ToList();
            var cells = new List<string> { label };
            cells.AddRange(values.Select(v => Number(v, format)));
            if (showChange)
            {
                double? change = values[0] is { } before && values[1] is { } after ? after - before : null;
                cells.Add(change is { } c ? (c >= 0 ? "+" : "") + Number(c, format) : "-");
            }

            table.Add(cells.ToArray());
        }

        string note = stages == 0 ? "" : $"Means are over the missions that reached each encounter. A chained encounter shifts later positions.{Environment.NewLine}";
        return Render(table) + note;
    }

    public static string Encounters(BenchFigures figures)
    {
        var table = new List<string[]> { new[] { "Encounter", "checks", "careful %", "crit %", "succ %", "part %", "fail %", "dis %", "deaths", "incap." } };
        foreach (EncounterFigures e in figures.Encounters)
        {
            string Tier(Tier tier)
            {
                return Number(e.Checks == 0 ? null : 100.0 * e.Tiers[tier] / e.Checks, "0");
            }

            table.Add(
            [
                e.EncounterId, e.Checks.ToString(CultureInfo.InvariantCulture), Number(e.Checks == 0 ? null : 100.0 * e.Careful / e.Checks, "0"),
                Tier(Sim.Missions.Tier.Critical), Tier(Sim.Missions.Tier.Success), Tier(Sim.Missions.Tier.Partial), Tier(Sim.Missions.Tier.Failure), Tier(Sim.Missions.Tier.Disaster),
                e.Deaths.ToString(CultureInfo.InvariantCulture), e.Incapacitated.ToString(CultureInfo.InvariantCulture),
            ]);
        }

        return Render(table);
    }

    public static string Replay(MissionTrace trace)
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"Seed {trace.Seed}. Lead {trace.Team.LeadId}.");
        foreach (TeamMember m in trace.Team.Members)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"  {m.Id}: {string.Join(", ", m.Skills.OrderBy(s => s.Key, StringComparer.Ordinal).Select(s => Inv($"{s.Key} {s.Value}")))}; caution {m.Caution}, discipline {m.Discipline}");
        }

        foreach (Beat beat in trace.Beats)
        {
            text.AppendLine(Describe(beat));
        }

        return text.ToString();
    }

    public static string Describe(Beat beat)
    {
        return beat switch
        {
            StageBeat b => Inv($"Stage {b.Cursor + 1} ({b.Slot}): {b.EncounterId}{(b.CommsBlocked ? ", comms blocked" : "")}. Momentum {b.Momentum}, strain {b.Strain}."),
            SkipBeat b => Inv($"  Skipped {b.EncounterId} on momentum."),
            CheckBeat b => Inv($"  {b.Approach} {b.Skill}: {b.ActorId} {b.ActorSkill}{(b.HelperId is null ? "" : Inv($" + {b.HelperId} {b.Assist}{(b.Supervised ? " supervising" : "")}"))} + gear {b.Gear} + momentum {b.Momentum} - strain {b.Strain} = {b.Score}; rolled {b.Roll} against {b.Target}: margin {b.Margin}, {b.Tier}. Experience {b.Experience:0.0}{(b.Mentoring ? " (mentored)" : "")}{(b.HelperId is null ? "" : Inv($", helper {b.HelperExperience:0.0}"))}."),
            EffectsBeat b => Inv($"  Effects: momentum {b.Effects.Momentum:+0;-0;0}, strain {b.Effects.Strain:+0;-0;0}, progress {b.Effects.Progress:+0;-0;0}{(b.Effects.InjuryRoll is { } r ? $", {r} injury roll" : "")}{(b.Effects.Withdraw ? ", withdraw" : "")}."),
            InjuryBeat b => Inv($"  {b.Severity} injury roll for {b.CrewId}: {b.Roll}, {b.Outcome}."),
            LeadChangeBeat b => Inv($"  {b.CrewId} takes the lead."),
            ChainBeat b => Inv($"  The disaster adds {b.EncounterId}."),
            CallBeat b => Inv($"  CALL {b.Call.DecisionId} (stakes {b.Call.Stakes}, threshold {b.Call.Threshold:0.00}): {b.Call.LeadId} prefers {b.Call.LeadPrefers}."),
            NoticeBeat b => Inv($"  Notice {b.DecisionId} (stakes {b.Stakes}, threshold {b.Threshold:0.00}): {b.LeadId} chose {b.Chose}{(b.Blocked ? ", comms blocked" : "")}."),
            AnswerBeat b => Inv($"  The captain answers {b.Answer}{Standing(b.Standing)}."),
            CollapseBeat => "  The mission collapses under strain.",
            ResultBeat b => Inv($"Result: {b.Outcome}."),
            _ => beat.ToString() ?? "",
        };
    }

    private static string Standing(CallStanding standing)
    {
        return standing switch
        {
            CallStanding.Backed => ", backing the lead",
            CallStanding.Overruled => ", overruling the lead",
            _ => ", withdrawing, which the lead had no stake in",
        };
    }

    private static string Inv(FormattableString text)
    {
        return text.ToString(CultureInfo.InvariantCulture);
    }

    private static double Share(BenchFigures figures, MissionOutcome outcome)
    {
        return 100.0 * figures.Outcomes[outcome] / figures.Runs;
    }

    private static string Number(double? value, string format)
    {
        return value is { } v ? v.ToString(format, CultureInfo.InvariantCulture) : "-";
    }

    private static string Render(List<string[]> table)
    {
        int[] widths = Enumerable.Range(0, table[0].Length).Select(i => table.Max(r => r[i].Length)).ToArray();
        var text = new StringBuilder();
        foreach (string[] row in table)
        {
            text.AppendLine(string.Join("  ", row.Select((cell, i) => i == 0 ? cell.PadRight(widths[i]) : cell.PadLeft(widths[i]))).TrimEnd());
        }

        return text.ToString();
    }
}
