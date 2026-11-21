using System.Globalization;
using System.Text.RegularExpressions;

namespace Web.Forms;

// A list of rows inside a form: each row is a set of fields named prefix[index].field, so the rows keep their order when posted.
// Add, Remove, and Move post the whole form to a rows route, which answers with the form drawn again from the posted fields and saves nothing.
// A form with several lists qualifies each action with its list's prefix; a form with one keeps the short actions.
// MultiFields are fields that post several values for one row (ticked boxes); a row holds them joined by Separator.
// Carry: a row also holds every other field posted under it (prefix[N].anything), so the lists and widgets nested in a row (an option's
// effects, its requires) move and go with it (1e part six). A carried field is kept under its name after the row's prefix.
public sealed record RowSet(string Prefix, IReadOnlyList<string> Fields, bool Qualified = false, IReadOnlyList<string>? MultiFields = null, string Noun = "row", IReadOnlyDictionary<string, string>? Defaults = null, bool Carry = false)
{
    // The posted field that says what to do: "add", "remove:N", "up:N", or "down:N" (with the prefix before N when qualified).
    public const string ActionField = "row";

    public const char Separator = '\u001f';

    public string Name(int index, string field)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{Prefix}[{index}].{field}");
    }

    // A row added by "Add": empty, except for the fields that have a starting value.
    public Dictionary<string, string> NewRow()
    {
        return Fields.ToDictionary(f => f, f => Defaults?.GetValueOrDefault(f) ?? "");
    }

    public bool IsMulti(string field)
    {
        return MultiFields?.Contains(field) == true;
    }

    public string AddAction => Qualified ? $"add:{Prefix}" : "add";

    public string Action(string verb, int index)
    {
        string row = index.ToString(CultureInfo.InvariantCulture);
        return Qualified ? $"{verb}:{Prefix}:{row}" : $"{verb}:{row}";
    }

    // Element ids: the Add button, and a row's own buttons.
    public string AddId => Qualified ? $"row-add-{Prefix}" : "row-add";

    public string ButtonId(string verb, int index)
    {
        string row = index.ToString(CultureInfo.InvariantCulture);
        return Qualified ? $"row-{verb}-{Prefix}-{row}" : $"row-{verb}-{row}";
    }

    // The values of a multi field in a row.
    public static IReadOnlyList<string> Split(string value)
    {
        return value.Length == 0 ? [] : value.Split(Separator);
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
            if (match.Success && (set.Carry || set.Fields.Contains(match.Groups[2].Value)) && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int index))
            {
                if (!byIndex.TryGetValue(index, out Dictionary<string, string>? row))
                {
                    row = set.Fields.ToDictionary(f => f, _ => "");
                    byIndex[index] = row;
                }

                string name = match.Groups[2].Value;
                row[name] = set.IsMulti(name) && row[name].Length > 0 ? row[name] + RowSet.Separator + field.Value : field.Value;
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
                string value = rows[i].GetValueOrDefault(field, "");
                if (set.IsMulti(field))
                {
                    fields.AddRange(RowSet.Split(value).Select(v => new KeyValuePair<string, string>(set.Name(i, field), v)));
                }
                else
                {
                    fields.Add(new KeyValuePair<string, string>(set.Name(i, field), value));
                }
            }

            if (set.Carry)
            {
                fields.AddRange(rows[i].Where(f => !set.Fields.Contains(f.Key)).Select(f => new KeyValuePair<string, string>(set.Name(i, f.Key), f.Value)));
            }
        }

        return new FormValues(fields);
    }

    // True when the posted action is a remove or a move whose row number is not a whole number of 0 or more.
    // A number that names no row (7 of 3) is not bad: that action changes nothing.
    public static bool HasBadRowNumber(FormValues values, IReadOnlyList<RowSet> sets)
    {
        string action = values.Get(RowSet.ActionField);
        // A form may hold one unqualified list beside qualified ones; an action that names a qualified list is not the unqualified list's.
        bool named = sets.Any(s => s.Qualified && (action.StartsWith($"remove:{s.Prefix}:", StringComparison.Ordinal) || action.StartsWith($"up:{s.Prefix}:", StringComparison.Ordinal) || action.StartsWith($"down:{s.Prefix}:", StringComparison.Ordinal)));
        foreach (RowSet set in sets.Where(s => s.Qualified || !named))
        {
            string head = set.Qualified ? $"^(remove|up|down):{Regex.Escape(set.Prefix)}:(.*)$" : "^(remove|up|down):(.*)$";
            Match match = Regex.Match(action, head, RegexOptions.Singleline);
            if (match.Success && !Regex.IsMatch(match.Groups[2].Value, "^[0-9]+\\z"))
            {
                return true;
            }
        }

        return false;
    }

    // What the posted action did, for the focus: the set it acted on, the verb, and the row it left the list on (the new row, or the moved row).
    public sealed record Applied(FormValues Values, RowSet? Set, string Verb, int Row);

    // The posted action applied: "add" appends an empty row, "remove" drops a row, "up" and "down" swap a row with its neighbour;
    // a row that is not there, a move off the end, or any other action changes nothing.
    public static Applied Apply(FormValues values, IReadOnlyList<RowSet> sets)
    {
        string action = values.Get(RowSet.ActionField);
        // Every list is numbered from zero first, so an action's row number is a place on the page.
        foreach (RowSet set in sets)
        {
            values = Write(values, set, Read(values, set));
        }

        foreach (RowSet set in sets)
        {
            List<Dictionary<string, string>> rows = Read(values, set);
            if (action == set.AddAction)
            {
                rows.Add(set.NewRow());
                return new Applied(Write(values, set, rows), set, "add", rows.Count - 1);
            }

            string head = set.Qualified ? $"^(remove|up|down):{Regex.Escape(set.Prefix)}:(\\d+)$" : "^(remove|up|down):(\\d+)$";
            Match match = Regex.Match(action, head);
            if (!match.Success || !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int index) || index >= rows.Count)
            {
                continue;
            }

            string verb = match.Groups[1].Value;
            if (verb == "remove")
            {
                rows.RemoveAt(index);
                return new Applied(Write(values, set, rows), set, verb, index);
            }

            int to = verb == "up" ? index - 1 : index + 1;
            if (to < 0 || to >= rows.Count)
            {
                break;
            }

            (rows[index], rows[to]) = (rows[to], rows[index]);
            return new Applied(Write(values, set, rows), set, verb, to);
        }

        return new Applied(values, null, "", -1);
    }
}
