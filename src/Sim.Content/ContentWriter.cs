using System.Globalization;
using System.Text;

namespace Sim.Content;

// Writes list files in one canonical layout: block items, keys in a fixed order, short lists and maps in flow style.
public static class ContentWriter
{
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase) { "true", "false", "yes", "no", "on", "off", "null", "y", "n" };

    // The one way a string reaches the file: plain when no YAML reader could take it for anything else, otherwise double-quoted.
    public static string Quote(string value)
    {
        if (IsPlain(value))
        {
            return value;
        }

        var text = new StringBuilder("\"");
        foreach (char c in value)
        {
            switch (c)
            {
                case '"':
                    text.Append("\\\"");
                    break;
                case '\\':
                    text.Append("\\\\");
                    break;
                case '\n':
                    text.Append("\\n");
                    break;
                case '\r':
                    text.Append("\\r");
                    break;
                case '\t':
                    text.Append("\\t");
                    break;
                case < ' ' or '\u007f' or '\u0085' or '\u2028' or '\u2029' or '\ufeff':
                    text.Append(CultureInfo.InvariantCulture, $"\\u{(int)c:X4}");
                    break;
                default:
                    text.Append(c);
                    break;
            }
        }

        return text.Append('"').ToString();
    }

    // The comment lines at the top of a file, as written, or null. Blank lines before the first comment are skipped; the block ends at the
    // first blank line or the first line that is not a comment, so a comment after a blank line is an inner comment and is dropped.
    public static string? LeadingComment(string text)
    {
        var lines = new List<string>();
        foreach (string raw in text.TrimStart('\ufeff').Split('\n'))
        {
            string line = raw.TrimEnd();
            if (line.StartsWith('#'))
            {
                lines.Add(line);
            }
            else if (line.Length > 0 || lines.Count > 0)
            {
                break;
            }
        }

        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    // A list file: the leading comment, then each item. An empty list is written as [] so the file stays a list.
    public static string Render(string? leadingComment, IReadOnlyList<IContentModel> items)
    {
        var lines = new List<string>();
        if (leadingComment is not null)
        {
            lines.AddRange(leadingComment.Split('\n').Select(l => l.TrimEnd()));
        }

        if (items.Count == 0)
        {
            lines.Add("[]");
        }

        foreach (IContentModel item in items)
        {
            List<string> fields = Fields(item);
            lines.Add("- " + fields[0]);
            lines.AddRange(fields.Skip(1).Select(l => "  " + l));
        }

        return string.Join("\n", lines) + "\n";
    }

    private static bool IsPlain(string value)
    {
        if (value.Length == 0 || !(char.IsLetter(value[0]) || value[0] == '_') || value[^1] == ' ' || Reserved.Contains(value))
        {
            return false;
        }

        return value.All(c => char.IsLetterOrDigit(c) || c is '_' or ' ' or '.' or '-' or '/' or '\'' or '(' or ')');
    }

    private static List<string> Fields(IContentModel item)
    {
        var f = new List<string> { $"id: {Quote(item.Id)}" };
        if (item.Name is not null)
        {
            f.Add($"name: {Quote(item.Name)}");
        }

        switch (item)
        {
            case SkillModel skill:
                if (skill.Tags.Count > 0)
                {
                    f.Add($"tags: {List(skill.Tags)}");
                }

                break;
            case GearModel gear:
                f.Add($"tag: {Quote(gear.Tag)}");
                break;
            case CrewModel crew:
                f.Add($"seniority: {Num(crew.Seniority)}");
                f.Add($"skills: {Map(crew.Skills.Select(s => (Quote(s.Key), Num(s.Value))))}");
                f.Add($"caution: {Num(crew.Caution)}");
                f.Add($"discipline: {Num(crew.Discipline)}");
                if (crew.Pronouns is not null)
                {
                    f.Add($"pronouns: {Quote(crew.Pronouns)}");
                }

                break;
            case LineModel line:
                if (line.Use is not null)
                {
                    f.Add($"use: {Quote(line.Use)}");
                }

                f.Add($"when: {Map(line.When.Select(c => (Quote(c.Fact), Criterion(c))).Prepend(("beat", Quote(line.Beat))))}");
                f.Add($"text: {Quote(line.Text)}");
                break;
            case SymbolModel symbol:
                if (symbol.Options.Count == 0)
                {
                    f.Add("options: []");
                    break;
                }

                f.Add("options:");
                f.AddRange(symbol.Options.Select(o => $"  - {Quote(o)}"));
                break;
            case PronounModel set:
                f.Add($"subject: {Quote(set.Subject)}");
                f.Add($"object: {Quote(set.Object)}");
                f.Add($"possessive: {Quote(set.Possessive)}");
                f.Add($"possessivePronoun: {Quote(set.PossessivePronoun)}");
                f.Add($"reflexive: {Quote(set.Reflexive)}");
                if (set.Plural)
                {
                    f.Add("plural: true");
                }

                break;
            case AssignmentModel assignment:
                f.Add($"objectives: {{ tags: {List(assignment.ObjectiveTags)} }}");
                f.Add($"offer: {Map(assignment.Offer.Select(o => (Num(o.Key), Num(o.Value))))}");
                f.Add($"loadoutLimit: {Num(assignment.LoadoutLimit)}");
                break;
            case ObjectiveModel objective:
                f.Add($"tags: {List(objective.Tags)}");
                f.Add($"teamSize: {Count(objective.TeamSizeMin, objective.TeamSizeMax)}");
                f.Add($"need: {Num(objective.Need)}");
                if (objective.UnattendedDifficulty is { } difficulty)
                {
                    f.Add($"ifUnattended: {{ difficulty: {Num(difficulty)} }}");
                }

                if (objective.Stages.Count == 0)
                {
                    f.Add("stages: []");
                    break;
                }

                f.Add("stages:");
                foreach (StageModel stage in objective.Stages)
                {
                    string skippable = stage.Skippable ? ", skippable: true" : "";
                    // No draw tags, no draw key: the loader reads an absent draw as no tags.
                    string draw = stage.DrawTags.Count == 0 ? "" : $" draw: {{ tags: {List(stage.DrawTags)} }},";
                    f.Add($"  - {{ slot: {Quote(stage.Slot)},{draw} count: {Count(stage.CountMin, stage.CountMax)}{skippable} }}");
                }

                break;
            case EncounterModel encounter:
                f.Add($"tags: {List(encounter.Tags)}");
                if (encounter.CommsBlocked)
                {
                    f.Add("commsBlocked: true");
                }

                if (encounter.Chain is not null)
                {
                    f.Add($"chain: {Quote(encounter.Chain)}");
                }

                f.Add("approaches:");
                f.Add($"  careful: {Approach(encounter.Careful)}");
                f.Add($"  fast: {Approach(encounter.Fast)}");
                if (encounter.Decision is { } decision)
                {
                    f.Add("decision:");
                    f.Add($"  id: {Quote(decision.Id)}");
                    if (decision.Name is not null)
                    {
                        f.Add($"  name: {Quote(decision.Name)}");
                    }

                    f.Add($"  stakes: {Num(decision.Stakes)}");
                    f.Add($"  bold: {Effects(decision.Bold)}");
                    f.Add($"  safe: {Effects(decision.Safe)}");
                }

                break;
            default:
                throw new ArgumentException($"Unknown content model {item.GetType().Name}.", nameof(item));
        }

        return f;
    }

    private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

    // A value, a list of values, or a range, as the criterion was written.
    private static string Criterion(CriterionModel criterion)
    {
        if (criterion.IsRange)
        {
            var ends = new List<(string, string)>();
            if (criterion.Min is { } min)
            {
                ends.Add(("min", Num(min)));
            }

            if (criterion.Max is { } max)
            {
                ends.Add(("max", Num(max)));
            }

            return Map(ends);
        }

        if (criterion.IsList || criterion.Values.Count != 1)
        {
            string[] values = criterion.Values.Select(CriterionValue).ToArray();
            return values.Length == 0 ? "[]" : $"[{string.Join(", ", values)}]";
        }

        return CriterionValue(criterion.Values[0]);
    }

    // A flag or a whole number is written bare, as an author would; the loader reads either way, and anything else is quoted as usual.
    private static string CriterionValue(string value)
    {
        return value is "true" or "false" || System.Text.RegularExpressions.Regex.IsMatch(value, "^-?[0-9]+$") ? value : Quote(value);
    }

    private static string List(IEnumerable<string> values)
    {
        string[] quoted = values.Select(Quote).ToArray();
        return quoted.Length == 0 ? "[]" : $"[{string.Join(", ", quoted)}]";
    }

    private static string Map(IEnumerable<(string Key, string Value)> pairs)
    {
        string[] entries = pairs.Select(p => $"{p.Key}: {p.Value}").ToArray();
        return entries.Length == 0 ? "{}" : $"{{ {string.Join(", ", entries)} }}";
    }

    // One number when both ends match, otherwise [min, max].
    private static string Count(int min, int max) => min == max ? Num(min) : $"[{Num(min)}, {Num(max)}]";

    private static string Approach(ApproachModel approach)
    {
        string gear = approach.GearTag is null ? "" : $", gearTag: {Quote(approach.GearTag)}";
        return $"{{ skill: {Quote(approach.Skill)}, difficulty: {Num(approach.Difficulty)}{gear} }}";
    }

    private static string Effects(IReadOnlyList<EffectModel> effects)
    {
        return effects.Count == 0 ? "[]" : $"[{string.Join(", ", effects.Select(Effect))}]";
    }

    private static string Effect(EffectModel effect)
    {
        var entries = new List<(string, string)>();
        if (effect.Progress is { } progress)
        {
            entries.Add(("progress", Num(progress)));
        }

        if (effect.Momentum is { } momentum)
        {
            entries.Add(("momentum", Num(momentum)));
        }

        if (effect.Strain is { } strain)
        {
            entries.Add(("strain", Num(strain)));
        }

        if (effect.InjuryRoll is not null)
        {
            entries.Add(("injuryRoll", Quote(effect.InjuryRoll)));
        }

        if (effect.Withdraw)
        {
            entries.Add(("withdraw", "true"));
        }

        return Map(entries);
    }
}
