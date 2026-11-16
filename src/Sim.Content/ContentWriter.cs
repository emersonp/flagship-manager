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
                f.Add(When(line.Beat, line.When));
                f.AddRange(Scalar("text:", line.Text));
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

                f.Add(objective.Stages.Count == 0 ? "stages: []" : "stages:");
                foreach (StageModel stage in objective.Stages)
                {
                    string skippable = stage.Skippable ? ", skippable: true" : "";
                    // No draw tags, no draw key: the loader reads an absent draw as no tags.
                    string draw = stage.DrawTags.Count == 0 ? "" : $" draw: {{ tags: {List(stage.DrawTags)} }},";
                    f.Add($"  - {{ slot: {Quote(stage.Slot)},{draw} count: {Count(stage.CountMin, stage.CountMax)}{skippable} }}");
                }

                f.AddRange(Texts(objective.Text, Sim.Core.Narrative.NarrativeFacts.Outcomes));
                f.AddRange(OwnLines(objective.Lines));
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

                f.AddRange(Texts(encounter.Text, Sim.Core.Narrative.NarrativeFacts.EncounterTexts));
                f.Add(encounter.Approaches.Count == 0 ? "approaches: []" : "approaches:");
                f.AddRange(encounter.Approaches.SelectMany(Approach).Select(l => "  " + l));
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
                    f.AddRange(Texts(decision.Text, Sim.Core.Narrative.NarrativeFacts.DecisionTexts).Select(l => "  " + l));
                    f.AddRange(Texts(decision.Labels, Sim.Core.Narrative.NarrativeFacts.Answers, "labels").Select(l => "  " + l));
                }

                f.AddRange(OwnLines(encounter.Lines));

                break;
            default:
                throw new ArgumentException($"Unknown content model {item.GetType().Name}.", nameof(item));
        }

        return f;
    }

    private static string When(string beat, IReadOnlyList<CriterionModel> when)
    {
        return $"when: {Map(when.Select(c => (Quote(c.Fact), Criterion(c))).Prepend(("beat", Quote(beat))))}";
    }

    // The lines written inside an item, last in it. Nothing when there are none.
    private static List<string> OwnLines(IReadOnlyList<OwnLineModel>? lines)
    {
        if (lines is not { Count: > 0 })
        {
            return [];
        }

        var f = new List<string> { "lines:" };
        foreach (OwnLineModel line in lines)
        {
            f.Add($"  - {When(line.Beat, line.When)}");
            f.AddRange(Scalar("    text:", line.Text));
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

    // On one line when the approach has no words of its own; otherwise a block, so each text has its own line.
    private static List<string> Approach(ApproachModel approach)
    {
        var keys = new List<(string Key, string Value)> { ("id", Quote(approach.Id)) };
        if (approach.Name is not null)
        {
            keys.Add(("name", Quote(approach.Name)));
        }

        keys.Add(("skill", Quote(approach.Skill)));
        keys.Add(("difficulty", Num(approach.Difficulty)));
        if (approach.GearTag is not null)
        {
            keys.Add(("gearTag", Quote(approach.GearTag)));
        }

        if (approach.Requires.Count > 0)
        {
            keys.Add(("requires", $"[{string.Join(", ", approach.Requires.Select(Requirement))}]"));
        }

        // The tiers in their fixed order, then any other key as read, so the loader can name it.
        List<(string Key, string Value)> outcomes = Sim.Core.Narrative.NarrativeFacts.Tiers.Where(approach.Outcomes.ContainsKey)
            .Concat(approach.Outcomes.Keys.Where(k => !Sim.Core.Narrative.NarrativeFacts.Tiers.Contains(k)))
            .Select(k => (Quote(k), Effects(approach.Outcomes[k])))
            .ToList();
        if (approach.Text is not { Count: > 0 })
        {
            if (outcomes.Count > 0)
            {
                keys.Add(("outcomes", Map(outcomes)));
            }

            return [$"- {Map(keys)}"];
        }

        var lines = keys.Select((k, i) => $"{(i == 0 ? "- " : "  ")}{k.Key}: {k.Value}").ToList();
        if (outcomes.Count > 0)
        {
            lines.Add("  outcomes:");
            lines.AddRange(outcomes.Select(o => $"    {o.Key}: {o.Value}"));
        }

        lines.AddRange(Texts(approach.Text, Sim.Core.Narrative.NarrativeFacts.ApproachTexts).Select(l => "  " + l));
        return lines;
    }

    private static string Requirement(RequirementModel requirement)
    {
        var entries = new List<(string, string)>();
        if (requirement.Skill is not null)
        {
            entries.Add(("skill", Quote(requirement.Skill)));
        }

        if (requirement.Level is { } level)
        {
            entries.Add(("level", Num(level)));
        }

        if (requirement.Gear is not null)
        {
            entries.Add(("gear", Quote(requirement.Gear)));
        }

        return Map(entries);
    }

    // An item's own words: one key to a line, the known keys in their fixed order and then any others as they were read. Nothing when there are none.
    private static List<string> Texts(IReadOnlyDictionary<string, string>? texts, IReadOnlyList<string> order, string under = "text")
    {
        if (texts is not { Count: > 0 })
        {
            return [];
        }

        var lines = new List<string> { $"{under}:" };
        lines.AddRange(order.Where(texts.ContainsKey).Concat(texts.Keys.Where(k => !order.Contains(k))).SelectMany(k => Scalar($"  {Quote(k)}:", texts[k])));
        return lines;
    }

    // A text after its key. A script of several lines is written as a block, one script line per file line, so it reads in the
    // file as it was typed; anything a block cannot hold exactly is quoted on one line instead.
    private static List<string> Scalar(string key, string value)
    {
        string[] rows = value.Split('\n');
        bool block = rows.Length > 1 && rows.All(r => r.Length > 0 && r.Trim() == r && !r.Any(char.IsControl));
        if (!block)
        {
            return [$"{key} {Quote(value)}"];
        }

        string indent = new string(' ', key.Length - key.TrimStart().Length + 2);
        return [$"{key} |-", .. rows.Select(r => indent + r)];
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

        // A flag change last, as flag, then add or set, then on.
        if (effect.Flag is not null)
        {
            entries.Add(("flag", Quote(effect.Flag)));
        }

        if (effect.FlagAmount is { } amount)
        {
            entries.Add((effect.FlagSet ? "set" : "add", Num(amount)));
        }

        if (effect.FlagOn is not null)
        {
            entries.Add(("on", Quote(effect.FlagOn)));
        }

        return Map(entries);
    }
}
