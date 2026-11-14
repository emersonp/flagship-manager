using System.Globalization;
using Sim.Core;
using Sim.Core.Content;
using Sim.Missions;

namespace Web.Bridge;

// Systems text for the mission log: ids, numbers, and enum names only.
public static class BeatLines
{
    public static LogLine Line(int index, Beat beat, int stage = 0)
    {
        (string kind, string text) = beat switch
        {
            StageBeat b => ("stage", Stage(b, stage)),
            SkipBeat b => ("skip", Inv($"{b.EncounterId} skipped.")),
            CheckBeat b => ("check", Check(b)),
            EffectsBeat b => ("effects", Effects(b.Effects)),
            InjuryBeat b => ("injury", Inv($"{b.CrewId} takes a {Words(b.Severity)} injury roll at {b.EncounterId}: {N(b.Roll)}, {Words(b.Outcome)}.")),
            LeadChangeBeat b => ("lead", Inv($"{b.CrewId} becomes lead.")),
            ChainBeat b => ("chain", Inv($"Chain to {b.EncounterId}.")),
            CallBeat b => ("call", Inv($"{b.Call.LeadId} calls: {b.Call.DecisionId}. Stakes {b.Call.Stakes}.")),
            NoticeBeat b => ("notice", Notice(b)),
            AnswerBeat b => ("answer", Inv($"Captain answers {b.Answer}: {Words(b.Standing)}.")),
            CollapseBeat => ("collapse", "Team collapse."),
            ResultBeat b => ("result", Inv($"Result: {Words(b.Outcome)}.")),
            _ => throw new ArgumentOutOfRangeException(nameof(beat), beat.GetType().Name, "No line for this beat type."),
        };
        return beat is StageBeat s ? new LogLine(index, kind, text, s.EncounterId, s.CommsBlocked, s.Momentum, s.Strain) : new LogLine(index, kind, text);
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

    private static string Inv(FormattableString text)
    {
        return text.ToString(CultureInfo.InvariantCulture);
    }

    private static string N(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture).Replace('-', '\u2212');
    }

    // The design sets every negative figure with the true minus sign.
    private static string Signed(int value)
    {
        return value.ToString("+0;-0;0", CultureInfo.InvariantCulture).Replace('-', '\u2212');
    }

    // stage is the 1-based position among the log's stage beats; the beat's own cursor also counts skipped and chained slots.
    private static string Stage(StageBeat b, int stage)
    {
        string blocked = b.CommsBlocked ? " Comms blocked." : string.Empty;
        return Inv($"Stage {stage}: {b.EncounterId}.{blocked} Momentum {N(b.Momentum)}, strain {N(b.Strain)}.");
    }

    private static string Check(CheckBeat b)
    {
        string helper = b.HelperId is null ? string.Empty : Inv($", {(b.Supervised ? "supervised by" : "helped by")} {b.HelperId}");
        string fast = b.Approach == Approach.Fast ? " Fast approach." : string.Empty;
        return Inv($"{b.ActorId} checks {b.Skill} at {b.EncounterId}{helper}: {N(b.Score)} + {N(b.Roll)} against {N(b.Target)}, {Words(b.Tier)} ({Signed(b.Margin)}).{fast}");
    }

    private static string Effects(EffectDef e)
    {
        var parts = new List<string>();
        if (e.Progress != 0)
        {
            parts.Add($"Progress {Signed(e.Progress)}.");
        }

        if (e.Momentum != 0)
        {
            parts.Add($"Momentum {Signed(e.Momentum)}.");
        }

        if (e.Strain != 0)
        {
            parts.Add($"Strain {Signed(e.Strain)}.");
        }

        if (e.InjuryRoll is { } severity)
        {
            parts.Add($"{Cap(Words(severity))} injury roll.");
        }

        if (e.Withdraw)
        {
            parts.Add("Withdraw.");
        }

        return parts.Count == 0 ? "No effect." : string.Join(' ', parts);
    }

    private static string Cap(string text)
    {
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    private static string Notice(NoticeBeat b)
    {
        string blocked = b.Blocked ? " Comms blocked." : string.Empty;
        return Inv($"{b.LeadId} decided {b.DecisionId} without calling: chose {b.Chose}. Stakes {b.Stakes}.{blocked}");
    }
}
