using System.Globalization;
using System.Text.RegularExpressions;
using Sim.Core;
using Sim.Core.Content;
using Sim.Missions;

namespace Web.Bridge;

// Systems text for the mission log: names (ids when unnamed), numbers, and enum names only.
// A line is cut into parts so a name is never mistaken for a figure and is encoded as plain text wherever it is shown.
public static partial class BeatLines
{
    // A name placed in a composed line.
    internal readonly record struct Nm(string Value);

    public static LogLine Line(int index, Beat beat, int stage = 0, Names? names = null)
    {
        Nm Crew(string id)
        {
            return new Nm(names?.Crew(id) ?? id);
        }

        Nm Encounter(string id)
        {
            return new Nm(names?.Encounter(id) ?? id);
        }

        Nm Skill(string id)
        {
            return new Nm(names?.Skill(id) ?? id);
        }

        Nm Decision(string id)
        {
            return new Nm(names?.Decision(id) ?? id);
        }

        (string kind, List<LinePart> parts) = beat switch
        {
            StageBeat b => ("stage", Stage(b, stage, Encounter(b.EncounterId))),
            SkipBeat b => ("skip", Compose($"{Encounter(b.EncounterId)} skipped.")),
            CheckBeat b => ("check", Check(b, Crew(b.ActorId), Skill(b.Skill), Encounter(b.EncounterId), b.HelperId is null ? null : Crew(b.HelperId))),
            EffectsBeat b => ("effects", Compose($"{Effects(b.Effects)}")),
            InjuryBeat b => ("injury", Compose($"{Crew(b.CrewId)} takes a {Words(b.Severity)} injury roll at {Encounter(b.EncounterId)}: {N(b.Roll)}, {Words(b.Outcome)}.")),
            LeadChangeBeat b => ("lead", Compose($"{Crew(b.CrewId)} becomes lead.")),
            ChainBeat b => ("chain", Compose($"Chain to {Encounter(b.EncounterId)}.")),
            CallBeat b => ("call", Compose($"{Crew(b.Call.LeadId)} calls: {Decision(b.Call.DecisionId)}. Stakes {b.Call.Stakes}.")),
            NoticeBeat b => ("notice", Compose($"{Crew(b.LeadId)} decided {Decision(b.DecisionId)} without calling: chose {b.Chose}. Stakes {b.Stakes}.{(b.Blocked ? " Comms blocked." : string.Empty)}")),
            AnswerBeat b => ("answer", Compose($"Captain answers {b.Answer}: {Words(b.Standing)}.")),
            CollapseBeat => ("collapse", Compose($"Team collapse.")),
            ResultBeat b => ("result", Compose($"Result: {Words(b.Outcome)}.")),
            _ => throw new ArgumentOutOfRangeException(nameof(beat), beat.GetType().Name, "No line for this beat type."),
        };
        string text = string.Concat(parts.Select(p => p.Text));
        return beat is StageBeat s
            ? new LogLine(index, kind, text, s.EncounterId, s.CommsBlocked, s.Momentum, s.Strain, parts, names?.Encounter(s.EncounterId) ?? s.EncounterId)
            : new LogLine(index, kind, text, Parts: parts);
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

    // Invariant text where a placeholder holds a number or word, a name part where it holds a name, and spliced parts where it holds parts.
    internal static List<LinePart> Compose(FormattableString text)
    {
        var parts = new List<LinePart>();
        void Text(string value)
        {
            if (value.Length == 0)
            {
                return;
            }

            if (parts.Count > 0 && !parts[^1].IsName)
            {
                parts[^1] = new LinePart(false, parts[^1].Text + value);
            }
            else
            {
                parts.Add(new LinePart(false, value));
            }
        }

        string format = text.Format;
        int at = 0;
        foreach (Match match in Placeholder().Matches(format))
        {
            Text(format[at..match.Index]);
            switch (text.GetArgument(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)))
            {
                case Nm name:
                    parts.Add(new LinePart(true, name.Value));
                    break;
                case List<LinePart> spliced:
                    foreach (LinePart part in spliced)
                    {
                        if (part.IsName)
                        {
                            parts.Add(part);
                        }
                        else
                        {
                            Text(part.Text);
                        }
                    }

                    break;
                case var other:
                    Text(Convert.ToString(other, CultureInfo.InvariantCulture) ?? string.Empty);
                    break;
            }

            at = match.Index + match.Length;
        }

        Text(format[at..]);
        return parts;
    }

    [GeneratedRegex(@"\{(\d+)\}")]
    private static partial Regex Placeholder();

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
    private static List<LinePart> Stage(StageBeat b, int stage, Nm encounter)
    {
        string blocked = b.CommsBlocked ? " Comms blocked." : string.Empty;
        return Compose($"Stage {stage}: {encounter}.{blocked} Momentum {N(b.Momentum)}, strain {N(b.Strain)}.");
    }

    private static List<LinePart> Check(CheckBeat b, Nm actor, Nm skill, Nm encounter, Nm? helper)
    {
        List<LinePart> helped = helper is { } h ? Compose($", {(b.Supervised ? "supervised by" : "helped by")} {h}") : [];
        string fast = b.Approach == Approach.Fast ? " Fast approach." : string.Empty;
        return Compose($"{actor} checks {skill} at {encounter}{helped}: {N(b.Score)} + {N(b.Roll)} against {N(b.Target)}, {Words(b.Tier)} ({Signed(b.Margin)}).{fast}");
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
}
