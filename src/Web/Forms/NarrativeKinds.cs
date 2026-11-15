using System.Globalization;
using System.Text.RegularExpressions;
using Sim.Content;
using Sim.Core.Narrative;

namespace Web.Forms;

// A line: its beat, criteria (fact and value as text), and text (slice 1d). The loader judges values against the pack.
internal sealed partial class LineKind : KindForm
{
    public static readonly RowSet When = new("when", ["fact", "value"], Noun: "criterion");

    public override ContentKind Kind => ContentKind.Line;

    public override IReadOnlyList<RowSet> RowSets => [When];

    public override string? NameOf(ContentFileSet files, string id)
    {
        return ContentEdits.Get<LineModel>(files, Kind, id)?.Name;
    }

    public override FormValues? Load(ContentFileSet files, string id, FormChoices choices)
    {
        if (ContentEdits.Get<LineModel>(files, Kind, id) is not { } line)
        {
            return null;
        }

        var fields = new List<KeyValuePair<string, string>> { new("name", Name(line.Name)), new("beat", line.Beat), new("text", line.Text) };
        for (int i = 0; i < line.When.Count; i++)
        {
            fields.Add(new(When.Name(i, "fact"), line.When[i].Fact));
            fields.Add(new(When.Name(i, "value"), ValueText(line.When[i])));
        }

        return new FormValues(fields);
    }

    public override FormValues Blank(FormChoices choices)
    {
        return new FormValues([new("name", ""), new("beat", ""), new("text", "")]);
    }

    public override Bound Bind(string id, FormValues values, FormChoices choices)
    {
        var errors = new Dictionary<string, string>();
        string? name = Checks.Name(values, errors);
        string beat = values.Get("beat");
        if (!NarrativeFacts.Beats.ContainsKey(beat))
        {
            errors["beat"] = "Choose a beat.";
        }

        string text = OwnedText.Clean(values.Get("text"));
        if (text.Length == 0)
        {
            errors["text"] = "Enter the line's text.";
        }
        else if (OwnedText.Problem(text) is { } unreadable)
        {
            errors["text"] = unreadable;
        }

        var criteria = new List<CriterionModel>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        List<Dictionary<string, string>> rows = FormRows.Read(values, When);
        for (int i = 0; i < rows.Count; i++)
        {
            string fact = rows[i]["fact"].Trim();
            string factField = When.Name(i, "fact");
            string valueField = When.Name(i, "value");
            if (fact.Length == 0)
            {
                errors[factField] = "Enter a fact.";
            }
            else if (fact == "beat")
            {
                errors[factField] = "The beat is chosen above.";
            }
            else if (NarrativeFacts.Beats.ContainsKey(beat) && NarrativeFacts.Fact(beat, fact) is null)
            {
                errors[factField] = $"A {beat} beat has no fact '{fact}'.";
            }
            else if (!seen.Add(fact))
            {
                errors[factField] = "Each fact can be tested once.";
            }

            if (ParseValue(fact, rows[i]["value"], out string? problem) is { } criterion)
            {
                criteria.Add(criterion);
            }
            else
            {
                errors[valueField] = problem!;
            }
        }

        return new Bound(errors.Count > 0 ? null : _ => new LineModel(id, name, beat, criteria, text), errors);
    }

    // a | a, b | 1..3 | 1.. | ..3
    public static CriterionModel? ParseValue(string fact, string text, out string? problem)
    {
        problem = null;
        string trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            problem = "Enter a value, values separated by commas, or a range such as 1..3.";
            return null;
        }

        if (RangePattern().Match(trimmed) is { Success: true } range && (range.Groups[1].Success || range.Groups[2].Success))
        {
            int? min = range.Groups[1].Success ? int.Parse(range.Groups[1].Value, CultureInfo.InvariantCulture) : null;
            int? max = range.Groups[2].Success ? int.Parse(range.Groups[2].Value, CultureInfo.InvariantCulture) : null;
            if (min > max)
            {
                problem = "The range's first number is above its last.";
                return null;
            }

            return new CriterionModel(fact, [], false, min, max);
        }

        string[] parts = trimmed.Split(',').Select(p => p.Trim()).ToArray();
        if (parts.Any(p => p.Length == 0))
        {
            problem = "Leave no blank between commas.";
            return null;
        }

        return new CriterionModel(fact, parts, parts.Length > 1);
    }

    public static string ValueText(CriterionModel criterion)
    {
        if (criterion.IsRange)
        {
            return $"{criterion.Min?.ToString(CultureInfo.InvariantCulture)}..{criterion.Max?.ToString(CultureInfo.InvariantCulture)}";
        }

        return string.Join(", ", criterion.Values);
    }

    [GeneratedRegex(@"^(-?\d{1,9})?\s*\.\.\s*(-?\d{1,9})?$")]
    private static partial Regex RangePattern();
}

internal sealed class SymbolKind : KindForm
{
    public static readonly RowSet Options = new("options", ["text"], Noun: "option");

    public override ContentKind Kind => ContentKind.Symbol;

    public override IReadOnlyList<RowSet> RowSets => [Options];

    public override string? NameOf(ContentFileSet files, string id)
    {
        return ContentEdits.Get<SymbolModel>(files, Kind, id)?.Name;
    }

    public override FormValues? Load(ContentFileSet files, string id, FormChoices choices)
    {
        if (ContentEdits.Get<SymbolModel>(files, Kind, id) is not { } symbol)
        {
            return null;
        }

        var fields = new List<KeyValuePair<string, string>> { new("name", Name(symbol.Name)) };
        fields.AddRange(symbol.Options.Select((o, i) => new KeyValuePair<string, string>(Options.Name(i, "text"), o)));
        return new FormValues(fields);
    }

    public override FormValues Blank(FormChoices choices)
    {
        return new FormValues([new("name", ""), new(Options.Name(0, "text"), "")]);
    }

    public override Bound Bind(string id, FormValues values, FormChoices choices)
    {
        var errors = new Dictionary<string, string>();
        string? name = Checks.Name(values, errors);
        var options = new List<string>();
        List<Dictionary<string, string>> rows = FormRows.Read(values, Options);
        for (int i = 0; i < rows.Count; i++)
        {
            string option = rows[i]["text"].Trim();
            if (option.Length == 0)
            {
                errors[Options.Name(i, "text")] = "Enter the option's text, or remove it.";
            }
            else if (option.IndexOfAny(['\r', '\n']) >= 0)
            {
                errors[Options.Name(i, "text")] = "An option is one line.";
            }

            options.Add(option);
        }

        if (rows.Count == 0)
        {
            errors["options"] = "Add at least one option.";
        }

        return new Bound(errors.Count > 0 ? null : _ => new SymbolModel(id, name, options), errors);
    }
}

internal sealed class PronounKind : KindForm
{
    public static readonly string[] Forms = ["subject", "object", "possessive", "possessivePronoun", "reflexive"];

    public override ContentKind Kind => ContentKind.Pronoun;

    public override string? NameOf(ContentFileSet files, string id)
    {
        return ContentEdits.Get<PronounModel>(files, Kind, id)?.Name;
    }

    public override FormValues? Load(ContentFileSet files, string id, FormChoices choices)
    {
        if (ContentEdits.Get<PronounModel>(files, Kind, id) is not { } set)
        {
            return null;
        }

        return new FormValues(
        [
            new("name", Name(set.Name)),
            new("subject", set.Subject),
            new("object", set.Object),
            new("possessive", set.Possessive),
            new("possessivePronoun", set.PossessivePronoun),
            new("reflexive", set.Reflexive),
            new("plural", set.Plural ? "true" : ""),
        ]);
    }

    public override FormValues Blank(FormChoices choices)
    {
        return new FormValues([new("name", ""), .. Forms.Select(f => new KeyValuePair<string, string>(f, ""))]);
    }

    public override Bound Bind(string id, FormValues values, FormChoices choices)
    {
        var errors = new Dictionary<string, string>();
        string? name = Checks.Name(values, errors);
        var forms = new Dictionary<string, string>();
        foreach (string form in Forms)
        {
            string value = values.Get(form).Trim();
            if (value.Length == 0)
            {
                errors[form] = "Enter this form.";
            }
            else if (value.IndexOfAny(['\r', '\n']) >= 0)
            {
                errors[form] = "A form is one line.";
            }

            forms[form] = value;
        }

        bool plural = values.Get("plural") == "true";
        return new Bound(errors.Count > 0 ? null : _ => new PronounModel(id, name, forms["subject"], forms["object"], forms["possessive"], forms["possessivePronoun"], forms["reflexive"], plural), errors);
    }
}
