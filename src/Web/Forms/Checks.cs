using System.Globalization;
using Sim.Content;
using Sim.Core.Content;

namespace Web.Forms;

// The field checks every kind shares. Each agrees with the loader: the id rule, the name rule, and whole numbers in a range.
internal static class Checks
{
    // Symbols, pronoun sets, traits, and test ships: named in text, each with ids of its own apart from everything else's (traits:
    // docs/conventions.md; test ships: 1e part six).
    public static bool LowerCase(ContentKind kind) => kind is ContentKind.Symbol or ContentKind.Pronoun or ContentKind.Trait or ContentKind.TestShip;

    public static void Id(string id, ContentFileSet files, Dictionary<string, string> errors, ContentKind kind = ContentKind.Skill)
    {
        if (id.Length == 0)
        {
            errors["id"] = "Enter an id.";
        }
        else if (LowerCase(kind) ? !Ids.IsInnerId(id) : !Ids.IsItemId(id))
        {
            errors["id"] = LowerCase(kind) ? Ids.InnerRule : Ids.ItemRule;
        }
        else if (UsedBy(files, id, kind) is { } used)
        {
            errors["id"] = $"That id is already used by {ContentKinds.WithArticle(used.Kind)}.";
        }
    }

    // The item already holding the id in the namespace this kind shares.
    public static ContentItemRef? UsedBy(ContentFileSet files, string id, ContentKind kind)
    {
        return files.Index().FirstOrDefault(i => i.Id == id && (LowerCase(kind) ? i.Kind == kind : !LowerCase(i.Kind)));
    }

    // A decision's id: the id rule; an id another encounter holds is refused (the abort call's id is the game's own).
    public static void DecisionId(string id, string encounterId, FormChoices choices, string field, Dictionary<string, string> errors)
    {
        if (id.Length == 0)
        {
            errors[field] = "Enter an id.";
        }
        else if (!Ids.IsItemId(id))
        {
            errors[field] = Ids.ItemRule;
        }
        else if (id == Sim.Core.Content.DecisionIds.Abort)
        {
            errors[field] = "That id is reserved for the abort call.";
        }
        else if (choices.DecisionOwners.TryGetValue(id, out string? owner) && owner != encounterId)
        {
            errors[field] = $"That id is already used by {owner}.";
        }
    }

    // The trimmed name, or null when none was given. The loader's rule: 1 to 80 characters with no line break.
    public static string? Name(FormValues values, Dictionary<string, string> errors, string field = "name")
    {
        string name = values.Get(field).Trim();
        if (name.Length == 0)
        {
            return null;
        }

        if (name.Length > 80 || name.IndexOfAny(['\r', '\n', '\u0085', '\u2028', '\u2029']) >= 0)
        {
            errors[field] = "A name is 1 to 80 characters with no line break.";
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

    // A whole number that may be left blank: blank is absent.
    public static int? OptionalWhole(FormValues values, string field, int? min, int? max, Dictionary<string, string> errors)
    {
        return values.Get(field).Trim().Length == 0 ? null : Whole(values, field, min, max, errors);
    }

    // Tags typed as text with commas: trimmed, each once, none blank, each an inner id as the loader asks.
    public static List<string> Tags(FormValues values, string field, Dictionary<string, string> errors)
    {
        string text = values.Get(field);
        if (text.IndexOfAny(['\r', '\n', '\u0085', '\u2028', '\u2029']) >= 0)
        {
            errors[field] = "A tag is one line.";
            return [];
        }

        if (text.Trim().Length == 0)
        {
            return [];
        }

        List<string> tags = text.Split(',').Select(t => t.Trim()).ToList();
        if (tags.Any(t => t.Length == 0))
        {
            errors[field] = "A tag cannot be blank.";
            return [];
        }

        if (tags.Any(t => !Ids.IsInnerId(t)))
        {
            errors[field] = Ids.InnerRule;
            return [];
        }

        return tags.Distinct().ToList();
    }

    // An option's words for a named item. A select at 400 wide shows about 14 characters, so a long pair puts the id first and the id stays in view.
    public static string Choice(string id, string name)
    {
        return name.Length + id.Length <= 14 ? $"{name} ({id})" : $"{id} ({name})";
    }

    public static string Text(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}
