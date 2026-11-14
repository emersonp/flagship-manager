using System.Globalization;
using Sim.Core;
using Sim.Core.Content;
using Sim.Missions;

namespace Web.Bridge;

// Systems text for the mission log: ids, numbers, and enum names only.
public static class BeatLines
{
    public static LogLine Line(int index, Beat beat)
    {
        (string kind, string text) = beat switch
        {
            StageBeat b => ("stage", Stage(b)),
            SkipBeat b => ("skip", $"{b.EncounterId} skipped."),
            CheckBeat b => ("check", Check(b)),
            EffectsBeat b => ("effects", Effects(b.Effects)),
            InjuryBeat b => ("injury", $"{b.CrewId} injury roll at {b.EncounterId}: {Words(b.Severity)} severity, roll {b.Roll}, {Words(b.Outcome)}."),
            LeadChangeBeat b => ("lead", $"{b.CrewId} becomes lead."),
            ChainBeat b => ("chain", $"Chain to {b.EncounterId}."),
            CallBeat b => ("call", Call(b.Call)),
            NoticeBeat b => ("notice", Notice(b)),
            AnswerBeat b => ("answer", $"Answer {Words(b.Answer)}, {Words(b.Standing)}, lead {b.LeadId}."),
            CollapseBeat => ("collapse", "Team collapse."),
            ResultBeat b => ("result", $"Result: {Words(b.Outcome)}."),
            _ => throw new ArgumentOutOfRangeException(nameof(beat), beat.GetType().Name, "No line for this beat type."),
        };
        return new LogLine(index, kind, text);
    }

    // PascalCase enum name as lower-case words.
    internal static string Words<T>(T value)
        where T : struct, Enum
    {
        string name = value.ToString();
        var builder = new System.Text.StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
            {
                builder.Append(' ');
            }

            builder.Append(char.ToLowerInvariant(name[i]));
        }

        return builder.ToString();
    }

    private static string Num(double value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string Signed(int value)
    {
        return value.ToString("+0;-0;0", CultureInfo.InvariantCulture);
    }

    private static string Stage(StageBeat b)
    {
        string blocked = b.CommsBlocked ? ", comms blocked" : string.Empty;
        return $"Stage {b.EncounterId} in slot {b.Slot}: momentum {b.Momentum}, strain {b.Strain}{blocked}.";
    }

    private static string Check(CheckBeat b)
    {
        string text = $"{b.ActorId} checks {b.Skill} at {b.EncounterId}: {b.Score + b.Roll} against {b.Target}, {Words(b.Tier)}.";
        if (b.Approach == Approach.Fast)
        {
            text += " Fast approach.";
        }

        if (b.HelperId is not null)
        {
            text += $" {(b.Supervised ? "Supervised by" : "Helped by")} {b.HelperId}.";
        }

        return text;
    }

    private static string Effects(EffectDef e)
    {
        var parts = new List<string>();
        if (e.Momentum != 0)
        {
            parts.Add($"momentum {Signed(e.Momentum)}");
        }

        if (e.Strain != 0)
        {
            parts.Add($"strain {Signed(e.Strain)}");
        }

        if (e.Progress != 0)
        {
            parts.Add($"progress {Signed(e.Progress)}");
        }

        if (e.InjuryRoll is { } severity)
        {
            parts.Add($"{Words(severity)} injury roll");
        }

        if (e.Withdraw)
        {
            parts.Add("withdraw");
        }

        return parts.Count == 0 ? "Effects: none." : $"Effects: {string.Join(", ", parts)}.";
    }

    private static string Call(PendingCall c)
    {
        string options = string.Join(", ", c.Options.Select(Words));
        return $"Call {c.DecisionId}: stakes {c.Stakes}, threshold {Num(c.Threshold)}, lead {c.LeadId} prefers {Words(c.LeadPrefers)}, options {options}.";
    }

    private static string Notice(NoticeBeat b)
    {
        string blocked = b.Blocked ? " Comms blocked." : string.Empty;
        return $"Notice {b.DecisionId}: stakes {b.Stakes}, threshold {Num(b.Threshold)}, lead {b.LeadId} chose {Words(b.Chose)}.{blocked}";
    }
}
