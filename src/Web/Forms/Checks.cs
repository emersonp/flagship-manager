using System.Globalization;
using System.Text.RegularExpressions;
using Sim.Content;

namespace Web.Forms;

// The field checks every kind shares. Each agrees with the loader: the id rule, the name rule, and whole numbers in a range.
internal static partial class Checks
{
    public const string IdRule = "Upper case letters, digits, and underscores, starting with a letter.";

    // The same pattern as the loader's, but a trailing line break is not let through.
    [GeneratedRegex("^[A-Z][A-Z0-9_]*\\z")]
    private static partial Regex IdPattern();

    public static void Id(string id, ContentFileSet files, Dictionary<string, string> errors)
    {
        if (id.Length == 0)
        {
            errors["id"] = "Enter an id.";
        }
        else if (!IdPattern().IsMatch(id))
        {
            errors["id"] = "An id must be upper case letters, digits, and underscores, starting with a letter.";
        }
        else if (files.Index().FirstOrDefault(i => i.Id == id) is { } used)
        {
            errors["id"] = $"That id is already used by a {ContentKinds.Noun(used.Kind)}.";
        }
    }

    // The trimmed name, or null when none was given. The loader's rule: 1 to 80 characters with no line break.
    public static string? Name(FormValues values, Dictionary<string, string> errors)
    {
        string name = values.Get("name").Trim();
        if (name.Length == 0)
        {
            return null;
        }

        if (name.Length > 80 || name.IndexOfAny(['\r', '\n', '\u0085', '\u2028', '\u2029']) >= 0)
        {
            errors["name"] = "A name is 1 to 80 characters with no line break.";
            return null;
        }

        return name;
    }

    // A whole number between min and max, either bound optional; the words say which bound failed.
    public static int Whole(FormValues values, string field, int? min, int? max, Dictionary<string, string> errors)
    {
        string text = values.Get(field).Trim();
        if (text.Length == 0)
        {
            errors[field] = "Enter a whole number.";
            return 0;
        }

        if (!int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int number))
        {
            errors[field] = "Must be a whole number.";
            return 0;
        }

        if ((min is not null && number < min) || (max is not null && number > max))
        {
            errors[field] = (min, max) switch
            {
                (int low, int high) => string.Create(CultureInfo.InvariantCulture, $"Must be {low} to {high}."),
                (int low, null) => string.Create(CultureInfo.InvariantCulture, $"Must be {low} or more."),
                (null, int high) => string.Create(CultureInfo.InvariantCulture, $"Must be {high} or less."),
                _ => "Must be a whole number.",
            };
        }

        return number;
    }

    public static string Text(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}
