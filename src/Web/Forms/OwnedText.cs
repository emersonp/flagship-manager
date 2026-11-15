using Sim.Content;
using Sim.Core.Narrative;

namespace Web.Forms;

// The words an encounter, an approach, a decision, or an objective carries for its own beats: one field per key, named
// "<prefix>text.<key>", blank meaning the item has no words of its own there and a shared line (or the systems line) is used.
// A text is a script (Scripts): one sentence, or several lines of speech, notes, and narration.
internal static class OwnedText
{
    // How many criteria a line written inside an item can add to the one its item implies.
    public const int MaxCriteria = 6;

    public static string Field(string prefix, string key) => $"{prefix}text.{key}";

    public static string LabelField(string answer) => $"decision.label.{answer}";

    public static void Add(List<KeyValuePair<string, string>> fields, string prefix, IReadOnlyList<string> keys, IReadOnlyDictionary<string, string>? texts)
    {
        fields.AddRange(keys.Where(k => texts?.ContainsKey(k) == true).Select(k => new KeyValuePair<string, string>(Field(prefix, k), texts![k])));
    }

    // A text as it is kept: each line trimmed and blank lines dropped, so a script reads the same in the file as in the box.
    public static string Clean(string text)
    {
        return string.Join('\n', text.ReplaceLineEndings("\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));
    }

    // Why a text cannot be read as a script, or null. The loader judges the names, symbols, and speakers it uses.
    public static string? Problem(string text)
    {
        try
        {
            Scripts.Parse(text);
            return null;
        }
        catch (NarrativeFormatException e)
        {
            return e.Message;
        }
    }

    // The texts that were filled in, or null when none was.
    public static IReadOnlyDictionary<string, string>? Bind(FormValues values, string prefix, IReadOnlyList<string> keys, Dictionary<string, string> errors)
    {
        var texts = new Dictionary<string, string>();
        foreach (string key in keys)
        {
            string text = Clean(values.Get(Field(prefix, key)));
            if (text.Length == 0)
            {
                continue;
            }

            if (Problem(text) is { } problem)
            {
                errors[Field(prefix, key)] = problem;
            }

            texts[key] = text;
        }

        return texts.Count == 0 ? null : texts;
    }

    public static void AddLabels(List<KeyValuePair<string, string>> fields, IReadOnlyDictionary<string, string>? labels)
    {
        fields.AddRange(NarrativeFacts.Answers.Where(a => labels?.ContainsKey(a) == true).Select(a => new KeyValuePair<string, string>(LabelField(a), labels![a])));
    }

    // What the call's answer buttons say, or null when none is labelled.
    public static IReadOnlyDictionary<string, string>? BindLabels(FormValues values, Dictionary<string, string> errors)
    {
        var labels = new Dictionary<string, string>();
        foreach (string answer in NarrativeFacts.Answers)
        {
            string label = values.Get(LabelField(answer)).Trim();
            if (label.Length == 0)
            {
                continue;
            }

            if (label.Length > 40 || label.IndexOfAny(['\r', '\n']) >= 0)
            {
                errors[LabelField(answer)] = "A button label is 1 to 40 characters on one line.";
            }

            labels[answer] = label;
        }

        return labels.Count == 0 ? null : labels;
    }

    // The lines written inside an item: a beat, the text, and up to MaxCriteria further criteria as fact and value pairs
    // (f0 and v0, f1 and v1, and so on). The item itself is one more criterion, which no row writes.
    public static readonly RowSet Lines = new(
        "lines",
        ["beat", "text", .. Enumerable.Range(0, MaxCriteria).SelectMany(k => new[] { FactField(k), ValueField(k) })],
        Qualified: true,
        Noun: "line");

    // The row action that starts a line for a beat from what plays there now: "write:injury".
    public const string WritePrefix = "write:";

    public static string FactField(int k) => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"f{k}");

    public static string ValueField(int k) => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"v{k}");

    public static void AddLines(List<KeyValuePair<string, string>> fields, IReadOnlyList<OwnLineModel>? lines)
    {
        for (int i = 0; i < (lines?.Count ?? 0); i++)
        {
            fields.Add(new(Lines.Name(i, "beat"), lines![i].Beat));
            fields.Add(new(Lines.Name(i, "text"), lines[i].Text));
            for (int k = 0; k < Math.Min(lines[i].When.Count, MaxCriteria); k++)
            {
                fields.Add(new(Lines.Name(i, FactField(k)), lines[i].When[k].Fact));
                fields.Add(new(Lines.Name(i, ValueField(k)), LineKind.ValueText(lines[i].When[k])));
            }
        }
    }

    // The criteria rows of one line that hold anything, with the place each sits at.
    public static List<(int Place, string Fact, string Value)> Criteria(IReadOnlyDictionary<string, string> row)
    {
        return Enumerable.Range(0, MaxCriteria)
            .Select(k => (Place: k, Fact: row.GetValueOrDefault(FactField(k), "").Trim(), Value: row.GetValueOrDefault(ValueField(k), "").Trim()))
            .Where(c => c.Fact.Length > 0 || c.Value.Length > 0)
            .ToList();
    }

    // implied is the fact the item stands for ("encounter" or "objective"), which a row may not test again.
    public static IReadOnlyList<OwnLineModel>? BindLines(FormValues values, string implied, Dictionary<string, string> errors)
    {
        var lines = new List<OwnLineModel>();
        List<Dictionary<string, string>> rows = FormRows.Read(values, Lines);
        for (int i = 0; i < rows.Count; i++)
        {
            string beat = rows[i]["beat"].Trim();
            bool known = NarrativeFacts.Beats.ContainsKey(beat);
            if (!known)
            {
                errors[Lines.Name(i, "beat")] = $"Choose a beat: {string.Join(", ", NarrativeFacts.Kinds)}.";
            }

            var criteria = new List<CriterionModel>();
            foreach ((int place, string fact, string value) in Criteria(rows[i]))
            {
                string factField = Lines.Name(i, FactField(place));
                if (fact.Length == 0)
                {
                    errors[factField] = "Enter a fact, or clear the value.";
                }
                else if (fact == implied)
                {
                    errors[factField] = $"The line already holds only for this {implied}.";
                }
                else if (fact == "beat")
                {
                    errors[factField] = "The beat is fixed by where the line sits.";
                }
                else if (known && NarrativeFacts.Fact(beat, fact) is null)
                {
                    errors[factField] = $"A {beat} beat has no fact '{fact}'.";
                }
                else if (criteria.Any(c => c.Fact == fact))
                {
                    errors[factField] = "Each fact can be tested once.";
                }
                else if (LineKind.ParseValue(fact, value, out string? problem) is { } criterion)
                {
                    criteria.Add(criterion);
                }
                else
                {
                    errors[Lines.Name(i, ValueField(place))] = problem!;
                }
            }

            string text = Clean(rows[i]["text"]);
            if (text.Length == 0)
            {
                errors[Lines.Name(i, "text")] = "Enter the line's text, or remove the line.";
            }
            else if (Problem(text) is { } unreadable)
            {
                errors[Lines.Name(i, "text")] = unreadable;
            }

            lines.Add(new OwnLineModel(beat, criteria, text));
        }

        return lines.Count == 0 ? null : lines;
    }
}
