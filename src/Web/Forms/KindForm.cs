using Sim.Content;

namespace Web.Forms;

// What a form needs from the model it edits: the checks, the model made from the fields, and the fields made from the model.
// Fields the form does not show come from the stored model, read again when the save runs.
internal abstract class KindForm
{
    private static readonly KindForm[] All = [new SkillKind(), new GearKind(), new CrewKind(), new AssignmentKind(), new ObjectiveKind(), new EncounterKind(), new LineKind(), new SymbolKind(), new PronounKind()];

    public abstract ContentKind Kind { get; }

    // The lists of rows the form has; none for most kinds.
    public virtual IReadOnlyList<RowSet> RowSets => [];

    // The kinds that have a form yet.
    public static KindForm? For(string segment)
    {
        return ContentKinds.FromSegment(segment) is { } kind ? All.FirstOrDefault(f => f.Kind == kind) : null;
    }

    // The choices with what the stored item already has added, so an item holding a tag nothing else uses can be saved as it is.
    public virtual FormChoices Widen(FormChoices choices, ContentFileSet files, string id)
    {
        return choices;
    }

    public abstract string? NameOf(ContentFileSet files, string id);

    // The fields of a stored item; null when the id holds none of this kind.
    public abstract FormValues? Load(ContentFileSet files, string id, FormChoices choices);

    public abstract FormValues Blank(FormChoices choices);

    // The checks on the posted fields; the model is made inside the save, from the files as they are then.
    public abstract Bound Bind(string id, FormValues values, FormChoices choices);

    protected static string Name(string? name)
    {
        return name ?? "";
    }
}

internal sealed record Bound(Func<ContentFileSet, object>? Make, IReadOnlyDictionary<string, string> Errors);

internal sealed class SkillKind : KindForm
{
    public override ContentKind Kind => ContentKind.Skill;

    public override string? NameOf(ContentFileSet files, string id)
    {
        return ContentEdits.Get<SkillModel>(files, Kind, id)?.Name;
    }

    public override FormValues? Load(ContentFileSet files, string id, FormChoices choices)
    {
        return ContentEdits.Get<SkillModel>(files, Kind, id) is { } skill ? new FormValues([new("name", Name(skill.Name))]) : null;
    }

    public override FormValues Blank(FormChoices choices)
    {
        return new FormValues([new("name", "")]);
    }

    public override Bound Bind(string id, FormValues values, FormChoices choices)
    {
        var errors = new Dictionary<string, string>();
        string? name = Checks.Name(values, errors);
        return new Bound(errors.Count > 0 ? null : files => new SkillModel(id, name, ContentEdits.Get<SkillModel>(files, Kind, id)?.Tags ?? []), errors);
    }
}

internal sealed class GearKind : KindForm
{
    public override ContentKind Kind => ContentKind.Gear;

    public override string? NameOf(ContentFileSet files, string id)
    {
        return ContentEdits.Get<GearModel>(files, Kind, id)?.Name;
    }

    public override FormValues? Load(ContentFileSet files, string id, FormChoices choices)
    {
        return ContentEdits.Get<GearModel>(files, Kind, id) is { } gear ? new FormValues([new("name", Name(gear.Name)), new("tag", gear.Tag)]) : null;
    }

    public override FormValues Blank(FormChoices choices)
    {
        return new FormValues([new("name", ""), new("tag", "")]);
    }

    public override Bound Bind(string id, FormValues values, FormChoices choices)
    {
        var errors = new Dictionary<string, string>();
        string? name = Checks.Name(values, errors);
        string tag = values.Get("tag").Trim();
        // The loader asks only that a tag is not blank; one line keeps the file readable.
        if (tag.Length == 0)
        {
            errors["tag"] = "Enter a tag.";
        }
        else if (tag.IndexOfAny(['\r', '\n', '\u0085', '\u2028', '\u2029']) >= 0)
        {
            errors["tag"] = "A tag is one line.";
        }

        return new Bound(errors.Count > 0 ? null : _ => new GearModel(id, name, tag), errors);
    }
}

internal sealed class CrewKind : KindForm
{
    public override ContentKind Kind => ContentKind.Crew;

    public override string? NameOf(ContentFileSet files, string id)
    {
        return ContentEdits.Get<CrewModel>(files, Kind, id)?.Name;
    }

    public override FormValues? Load(ContentFileSet files, string id, FormChoices choices)
    {
        if (ContentEdits.Get<CrewModel>(files, Kind, id) is not { } crew)
        {
            return null;
        }

        var fields = new List<KeyValuePair<string, string>>
        {
            new("name", Name(crew.Name)),
            new("seniority", Checks.Text(crew.Seniority)),
            new("caution", Checks.Text(crew.Caution)),
            new("discipline", Checks.Text(crew.Discipline)),
            new("pronouns", crew.Pronouns ?? ""),
        };
        // Blank for a skill the member has no key for: they do not have it and cannot train it.
        fields.AddRange(choices.Skills.Select(s => new KeyValuePair<string, string>("skills." + s.Id, crew.Skills.TryGetValue(s.Id, out int level) ? Checks.Text(level) : "")));
        return new FormValues(fields);
    }

    public override FormValues Blank(FormChoices choices)
    {
        var fields = new List<KeyValuePair<string, string>> { new("name", ""), new("seniority", "1"), new("caution", "0"), new("discipline", "0"), new("pronouns", "") };
        fields.AddRange(choices.Skills.Select(s => new KeyValuePair<string, string>("skills." + s.Id, "")));
        return new FormValues(fields);
    }

    public override Bound Bind(string id, FormValues values, FormChoices choices)
    {
        var errors = new Dictionary<string, string>();
        string? name = Checks.Name(values, errors);
        int seniority = Checks.Whole(values, "seniority", 1, null, errors);
        int caution = Checks.Whole(values, "caution", -2, 2, errors);
        int discipline = Checks.Whole(values, "discipline", -2, 2, errors);
        // Blank is no key: the crew member uses "they".
        string? pronouns = values.Get("pronouns") is { Length: > 0 } set ? set : null;
        if (pronouns is not null && !choices.Pronouns.Any(p => p.Id == pronouns))
        {
            errors["pronouns"] = "Choose a pronoun set the pack has.";
        }

        // Blank writes no key (the member does not have the skill); 0 is untrained but trainable.
        var levels = new Dictionary<string, int>();
        foreach (SkillChoice skill in choices.Skills)
        {
            if (Checks.OptionalWhole(values, "skills." + skill.Id, 0, choices.MaxLevel, errors) is { } level)
            {
                levels[skill.Id] = level;
            }
        }

        if (errors.Count > 0)
        {
            return new Bound(null, errors);
        }

        return new Bound(files =>
        {
            CrewModel? stored = ContentEdits.Get<CrewModel>(files, Kind, id);
            // Skills the member already has keep their place; the rest follow in the pack's order.
            var skills = new Dictionary<string, int>();
            foreach (string key in stored?.Skills.Keys ?? [])
            {
                if (levels.TryGetValue(key, out int level))
                {
                    skills[key] = level;
                }
            }

            foreach ((string key, int level) in levels)
            {
                skills.TryAdd(key, level);
            }

            return new CrewModel(id, name, seniority, skills, caution, discipline, pronouns);
        }, errors);
    }
}

internal sealed class AssignmentKind : KindForm
{
    public static readonly RowSet Offer = new("offer", ["count", "weight"]);

    public override ContentKind Kind => ContentKind.Assignment;

    public override IReadOnlyList<RowSet> RowSets => [Offer];

    public override string? NameOf(ContentFileSet files, string id)
    {
        return ContentEdits.Get<AssignmentModel>(files, Kind, id)?.Name;
    }

    public override FormChoices Widen(FormChoices choices, ContentFileSet files, string id)
    {
        return ContentEdits.Get<AssignmentModel>(files, Kind, id) is { } assignment ? choices with { ObjectiveTags = [.. choices.ObjectiveTags, .. assignment.ObjectiveTags.Where(t => !choices.ObjectiveTags.Contains(t))] } : choices;
    }

    public override FormValues? Load(ContentFileSet files, string id, FormChoices choices)
    {
        if (ContentEdits.Get<AssignmentModel>(files, Kind, id) is not { } assignment)
        {
            return null;
        }

        var fields = new List<KeyValuePair<string, string>> { new("name", Name(assignment.Name)), new("loadoutLimit", Checks.Text(assignment.LoadoutLimit)) };
        fields.AddRange(assignment.ObjectiveTags.Select(t => new KeyValuePair<string, string>("tags", t)));
        int row = 0;
        foreach ((int count, int weight) in assignment.Offer)
        {
            fields.Add(new(Offer.Name(row, "count"), Checks.Text(count)));
            fields.Add(new(Offer.Name(row, "weight"), Checks.Text(weight)));
            row++;
        }

        return new FormValues(fields);
    }

    public override FormValues Blank(FormChoices choices)
    {
        return new FormValues([new("name", ""), new("loadoutLimit", "0"), new(Offer.Name(0, "count"), "1"), new(Offer.Name(0, "weight"), "1")]);
    }

    public override Bound Bind(string id, FormValues values, FormChoices choices)
    {
        var errors = new Dictionary<string, string>();
        string? name = Checks.Name(values, errors);
        int limit = Checks.Whole(values, "loadoutLimit", 0, null, errors);
        List<string> tags = values.All("tags").Distinct().ToList();
        if (tags.Any(t => !choices.ObjectiveTags.Contains(t)))
        {
            errors["tags"] = "Unknown tag.";
        }

        var offer = new Dictionary<int, int>();
        List<Dictionary<string, string>> rows = FormRows.Read(values, Offer);
        for (int i = 0; i < rows.Count; i++)
        {
            int count = Checks.Whole(values, Offer.Name(i, "count"), 1, null, errors);
            int weight = Checks.Whole(values, Offer.Name(i, "weight"), 1, null, errors);
            if (!errors.ContainsKey(Offer.Name(i, "count")) && !offer.TryAdd(count, weight))
            {
                errors[Offer.Name(i, "count")] = "Each number of objectives can be offered once.";
            }
        }

        return new Bound(errors.Count > 0 ? null : _ => new AssignmentModel(id, name, tags, offer, limit), errors);
    }
}
