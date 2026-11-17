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

        Nm Approach(string encounterId, string approachId)
        {
            return new Nm(names?.Approach(encounterId, approachId) ?? approachId);
        }

        Nm Decision(string id)
        {
            return new Nm(names?.Decision(id) ?? id);
        }

        (string kind, List<LinePart> parts) = beat switch
        {
            StageBeat b => ("stage", Stage(b, stage, Encounter(b.EncounterId))),
            SkipBeat b => ("skip", Compose($"{Encounter(b.EncounterId)} skipped.")),
            CheckBeat b => ("check", Check(b, Crew(b.ActorId), Skill(b.Skill), Encounter(b.EncounterId), b.HelperId is null ? null : Crew(b.HelperId), Approach(b.EncounterId, b.Approach))),
            EffectsBeat b => ("effects", Compose($"{Effects(b.Effects)}")),
            InjuryBeat b => ("injury", Compose($"{Crew(b.CrewId)} takes a {Words(b.Severity)} injury roll at {Encounter(b.EncounterId)}: {N(b.Roll)}, {Words(b.Outcome)}.")),
            LeadChangeBeat b => ("lead", Compose($"{Crew(b.CrewId)} becomes lead.")),
            ChainBeat b => ("chain", Compose($"Chain to {Encounter(b.EncounterId)}.")),
            CallBeat b => ("call", Compose($"{Crew(b.Call.LeadId)} calls: {Decision(b.Call.DecisionId)}. Stakes {b.Call.Stakes}.")),
            InitiativeBeat b => ("initiative", Compose($"{Crew(b.LeadId)} decided {Decision(b.DecisionId)} without calling: chose {b.Chose}. Stakes {b.Stakes}.{(b.Blocked ? " Comms blocked." : string.Empty)}")),
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

    // What a beat changed, as short figures beside its prose (GDD risk 2): generated, never authored, so every line cites them.
    public static IReadOnlyList<string> Tags(Beat beat, Names? names = null)
    {
        var tags = new List<string>();
        switch (beat)
        {
            case CheckBeat b:
                tags.Add($"{Words(b.Tier)} {Signed(b.Margin)}");
                if (b.MoraleBonus != 0)
                {
                    tags.Add($"morale {Signed(b.MoraleBonus)}");
                }

                if (b.MomentumAfter is { } momentum && momentum != b.Momentum)
                {
                    tags.Add($"momentum {Signed(momentum - b.Momentum)}");
                }

                if (b.StrainAfter is { } strain && strain != b.Strain)
                {
                    tags.Add($"strain {Signed(strain - b.Strain)}");
                }

                tags.AddRange((b.Flags ?? []).Select(f => FlagTag(f.Flag, f.Amount, f.Set, f.CrewId is null ? FlagIds.Ship : names?.Crew(f.CrewId) ?? f.CrewId)));
                break;
            case EffectsBeat b:
                if (b.Effects.Progress != 0)
                {
                    tags.Add($"progress {Signed(b.Effects.Progress)}");
                }

                if (b.Effects.Momentum != 0)
                {
                    tags.Add($"momentum {Signed(b.Effects.Momentum)}");
                }

                if (b.Effects.Strain != 0)
                {
                    tags.Add($"strain {Signed(b.Effects.Strain)}");
                }

                if (b.Effects.InjuryRoll is { } roll)
                {
                    tags.Add($"{Words(roll)} injury roll");
                }

                if (b.Effects.Withdraw)
                {
                    tags.Add("withdraw");
                }

                // The beat holds the effect as written, so a flag is tagged with what it was written on.
                tags.AddRange((b.Effects.Flags ?? []).Select(f => FlagTag(f.Flag, f.Amount, f.Set, f.On)));
                break;
            case InjuryBeat b:
                tags.Add(Words(b.Outcome));
                break;
            case CallBeat b:
                tags.Add($"stakes {b.Call.Stakes}");
                break;
            case InitiativeBeat b:
                tags.Add($"stakes {b.Stakes}");
                tags.Add($"chose {Words(b.Chose)}");
                break;
            case AnswerBeat b:
                tags.Add(Words(b.Answer));
                tags.Add(Words(b.Standing));
                break;
            case ResultBeat b:
                tags.Add(Words(b.Outcome));
                break;
        }

        return tags;
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
    internal static string Signed(int value)
    {
        return value.ToString("+0;-0;0", CultureInfo.InvariantCulture).Replace('-', '\u2212');
    }

    // stage is the 1-based position among the log's stage beats; the beat's own cursor also counts skipped and chained slots.
    private static List<LinePart> Stage(StageBeat b, int stage, Nm encounter)
    {
        string blocked = b.CommsBlocked ? " Comms blocked." : string.Empty;
        return Compose($"Stage {stage}: {encounter}.{blocked} Momentum {N(b.Momentum)}, strain {N(b.Strain)}.");
    }

    private static List<LinePart> Check(CheckBeat b, Nm actor, Nm skill, Nm encounter, Nm? helper, Nm approach)
    {
        List<LinePart> helped = helper is { } h ? Compose($", {(b.Supervised ? "supervised by" : "helped by")} {h}") : [];
        return Compose($"{actor} checks {skill} at {encounter}{helped}: {N(b.Score)} + {N(b.Roll)} against {N(b.Target)}, {Words(b.Tier)} ({Signed(b.Margin)}). {approach}.");
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

        parts.AddRange((e.Flags ?? []).Select(f => $"Flag {FlagTag(f.Flag, f.Amount, f.Set, f.On)}."));

        return parts.Count == 0 ? "No effect." : string.Join(' ', parts);
    }

    // "flag_a +1, ship" for an add, "flag_a = 2, CREW_02" for a set.
    private static string FlagTag(string flag, int amount, bool set, string on)
    {
        return $"{flag} {(set ? "= " + N(amount) : Signed(amount))}, {on}";
    }

    private static string Cap(string text)
    {
        return char.ToUpperInvariant(text[0]) + text[1..];
    }
}
