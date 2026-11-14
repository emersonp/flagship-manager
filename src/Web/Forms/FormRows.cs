using System.Globalization;
using System.Text.RegularExpressions;

namespace Web.Forms;

// A list of rows inside a form: each row is a set of fields named prefix[index].field, so the rows keep their order when posted.
// Add and Remove post the whole form to a rows route, which answers with the form drawn again from the posted fields and saves nothing.
public sealed record RowSet(string Prefix, IReadOnlyList<string> Fields)
{
    // The posted field that says what to do: "add" or "remove:N".
    public const string ActionField = "row";

    public string Name(int index, string field)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{Prefix}[{index}].{field}");
    }
}

public static class FormRows
{
    // The rows in posted order of index; a row missing a field has it empty.
    public static List<Dictionary<string, string>> Read(FormValues values, RowSet set)
    {
        var pattern = new Regex($"^{Regex.Escape(set.Prefix)}\\[(\\d+)\\]\\.(.+)$");
        var byIndex = new SortedDictionary<int, Dictionary<string, string>>();
        foreach (KeyValuePair<string, string> field in values.Fields)
        {
            Match match = pattern.Match(field.Key);
            if (match.Success && set.Fields.Contains(match.Groups[2].Value) && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int index))
            {
                if (!byIndex.TryGetValue(index, out Dictionary<string, string>? row))
                {
                    row = set.Fields.ToDictionary(f => f, _ => "");
                    byIndex[index] = row;
                }

                row[match.Groups[2].Value] = field.Value;
            }
        }

        return byIndex.Values.ToList();
    }

    // The values with the rows replaced, numbered from zero.
    public static FormValues Write(FormValues values, RowSet set, IReadOnlyList<Dictionary<string, string>> rows)
    {
        var pattern = new Regex($"^{Regex.Escape(set.Prefix)}\\[\\d+\\]\\.");
        FormValues rest = values.Without(name => pattern.IsMatch(name) || name == RowSet.ActionField);
        var fields = new List<KeyValuePair<string, string>>(rest.Fields);
        for (int i = 0; i < rows.Count; i++)
        {
            foreach (string field in set.Fields)
            {
                fields.Add(new KeyValuePair<string, string>(set.Name(i, field), rows[i].GetValueOrDefault(field, "")));
            }
        }

        return new FormValues(fields);
    }

    // The posted action applied: "add" appends an empty row, "remove:N" drops row N when it exists; anything else changes nothing.
    public static FormValues Apply(FormValues values, RowSet set)
    {
        List<Dictionary<string, string>> rows = Read(values, set);
        string action = values.Get(RowSet.ActionField);
        if (action == "add")
        {
            rows.Add(set.Fields.ToDictionary(f => f, _ => ""));
        }
        else if (action.StartsWith("remove:", StringComparison.Ordinal) && int.TryParse(action["remove:".Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int index) && index < rows.Count)
        {
            rows.RemoveAt(index);
        }

        return Write(values, set, rows);
    }
}
