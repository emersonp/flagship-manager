using Sim.Content;
using Sim.Core.Narrative;

namespace Web.Forms;

// The words an encounter, an approach, a decision, or an objective carries for its own beats: one field per key, named
// "<prefix>text.<key>", blank meaning the item has no words of its own there and a shared line (or the systems line) is used.
internal static class OwnedText
{
    public static string Field(string prefix, string key) => $"{prefix}text.{key}";

    public static void Add(List<KeyValuePair<string, string>> fields, string prefix, IReadOnlyList<string> keys, IReadOnlyDictionary<string, string>? texts)
    {
        fields.AddRange(keys.Where(k => texts?.ContainsKey(k) == true).Select(k => new KeyValuePair<string, string>(Field(prefix, k), texts![k])));
    }

    // The texts that were filled in, or null when none was. The loader judges the names and symbols they use.
    public static IReadOnlyDictionary<string, string>? Bind(FormValues values, string prefix, IReadOnlyList<string> keys, Dictionary<string, string> errors)
    {
        var texts = new Dictionary<string, string>();
        foreach (string key in keys)
        {
            string text = values.Get(Field(prefix, key)).Trim();
            if (text.IndexOfAny(['\r', '\n']) >= 0)
            {
                errors[Field(prefix, key)] = "A text is one paragraph with no line break.";
            }
            else if (text.Length > 0)
            {
                texts[key] = text;
            }
        }

        return texts.Count == 0 ? null : texts;
    }

    // The lines written inside an item: a beat, further criteria as "fact: value; fact: value", and the text. The item itself is
    // one more criterion, which no row writes.
    public static readonly RowSet Lines = new("lines", ["beat", "when", "text"], Qualified: true, Noun: "line");

    public static void AddLines(List<KeyValuePair<string, string>> fields, IReadOnlyList<OwnLineModel>? lines)
    {
        for (int i = 0; i < (lines?.Count ?? 0); i++)
        {
            fields.Add(new(Lines.Name(i, "beat"), lines![i].Beat));
            fields.Add(new(Lines.Name(i, "when"), string.Join("; ", lines[i].When.Select(c => $"{c.Fact}: {LineKind.ValueText(c)}"))));
            fields.Add(new(Lines.Name(i, "text"), lines[i].Text));
        }
    }

    // implied is the fact the item stands for ("encounter" or "objective"), which a row may not test again.
    public static IReadOnlyList<OwnLineModel>? BindLines(FormValues values, string implied, Dictionary<string, string> errors)
    {
        var lines = new List<OwnLineModel>();
        List<Dictionary<string, string>> rows = FormRows.Read(values, Lines);
        for (int i = 0; i < rows.Count; i++)
        {
            string beat = rows[i]["beat"].Trim();
            if (!NarrativeFacts.Beats.ContainsKey(beat))
            {
                errors[Lines.Name(i, "beat")] = $"Choose a beat: {string.Join(", ", NarrativeFacts.Kinds)}.";
            }

            var criteria = new List<CriterionModel>();
            foreach (string part in rows[i]["when"].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int colon = part.IndexOf(':', StringComparison.Ordinal);
                string fact = colon < 0 ? part : part[..colon].Trim();
                string? problem = null;
                if (colon < 0)
                {
                    problem = "Write each criterion as fact: value, with ; between them.";
                }
                else if (fact == implied)
                {
                    problem = $"The line already holds only for this {implied}.";
                }
                else if (NarrativeFacts.Beats.ContainsKey(beat) && NarrativeFacts.Fact(beat, fact) is null)
                {
                    problem = $"A {beat} beat has no fact '{fact}'.";
                }
                else if (criteria.Any(c => c.Fact == fact))
                {
                    problem = "Each fact can be tested once.";
                }
                else if (LineKind.ParseValue(fact, part[(colon + 1)..], out problem) is { } criterion)
                {
                    criteria.Add(criterion);
                }

                if (problem is not null)
                {
                    errors[Lines.Name(i, "when")] = problem;
                    break;
                }
            }

            string text = rows[i]["text"].Trim();
            if (text.Length == 0)
            {
                errors[Lines.Name(i, "text")] = "Enter the line's text, or remove the line.";
            }
            else if (text.IndexOfAny(['\r', '\n']) >= 0)
            {
                errors[Lines.Name(i, "text")] = "A line is one paragraph with no line break.";
            }

            lines.Add(new OwnLineModel(beat, criteria, text));
        }

        return lines.Count == 0 ? null : lines;
    }

    // What a text for this beat kind may name, for the hint under its fields.
    public static string Names(string beat)
    {
        BeatSpec spec = NarrativeFacts.Beats[beat];
        return string.Join(", ", spec.People.Concat(spec.Things).Select(r => $"#{r}#"));
    }
}
