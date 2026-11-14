using Sim.Content;

namespace Web;

public sealed record KindColumn(string Header, bool Numeric = false);

// The editor's kinds by their route segment, with each list's at-a-glance columns.
public static class ContentKinds
{
    private static readonly (ContentKind Kind, string Segment, string Title)[] All =
    [
        (ContentKind.Skill, "skills", "Skills"),
        (ContentKind.Gear, "gear", "Gear"),
        (ContentKind.Crew, "crew", "Crew"),
        (ContentKind.Assignment, "assignments", "Assignments"),
        (ContentKind.Objective, "objectives", "Objectives"),
        (ContentKind.Encounter, "encounters", "Encounters"),
    ];

    public static IEnumerable<ContentKind> Kinds => All.Select(k => k.Kind);

    // Exact, lower case: anything else is not a kind.
    public static ContentKind? FromSegment(string segment)
    {
        foreach ((ContentKind kind, string name, _) in All)
        {
            if (name == segment)
            {
                return kind;
            }
        }

        return null;
    }

    public static string Segment(ContentKind kind)
    {
        return All.Single(k => k.Kind == kind).Segment;
    }

    public static string Title(ContentKind kind)
    {
        return All.Single(k => k.Kind == kind).Title;
    }

    // What one item of the kind is called in a sentence.
    public static string Noun(ContentKind kind)
    {
        return kind switch
        {
            ContentKind.Skill => "skill",
            ContentKind.Gear => "gear item",
            ContentKind.Crew => "crew member",
            ContentKind.Assignment => "assignment",
            ContentKind.Objective => "objective",
            _ => "encounter",
        };
    }

    // The noun with its article, for a sentence: "a skill", "an encounter".
    public static string WithArticle(ContentKind kind)
    {
        string noun = Noun(kind);
        return ("aeiou".Contains(noun[0], StringComparison.Ordinal) ? "an " : "a ") + noun;
    }

    public static IReadOnlyList<KindColumn> Columns(ContentKind kind)
    {
        return kind switch
        {
            ContentKind.Skill => [new("Tags")],
            ContentKind.Gear => [new("Tag")],
            ContentKind.Crew => [new("Seniority", true), new("Caution", true), new("Discipline", true), new("Skill levels")],
            ContentKind.Assignment => [new("Objective tags"), new("Loadout limit", true)],
            ContentKind.Objective => [new("Tags"), new("Team size", true), new("Need", true), new("Stages", true)],
            ContentKind.Encounter => [new("Tags"), new("Careful"), new("Fast"), new("Chain"), new("Decision")],
            _ => [],
        };
    }

    // The cells for Columns, read leniently from the files; blank when the item cannot be read.
    public static IReadOnlyList<string> Cells(ContentFileSet files, ContentItemRef item)
    {
        int width = Columns(item.Kind).Count;
        string[] blank = Enumerable.Repeat("", width).ToArray();
        switch (item.Kind)
        {
            case ContentKind.Skill when ContentEdits.Get<SkillModel>(files, item.Kind, item.Id) is { } skill:
                return [List(skill.Tags)];
            case ContentKind.Gear when ContentEdits.Get<GearModel>(files, item.Kind, item.Id) is { } gear:
                return [gear.Tag];
            case ContentKind.Crew when ContentEdits.Get<CrewModel>(files, item.Kind, item.Id) is { } crew:
                return [Number(crew.Seniority), Number(crew.Caution), Number(crew.Discipline), string.Join(", ", crew.Skills.Select(s => $"{s.Key} {s.Value}"))];
            case ContentKind.Assignment when ContentEdits.Get<AssignmentModel>(files, item.Kind, item.Id) is { } assignment:
                return [List(assignment.ObjectiveTags), Number(assignment.LoadoutLimit)];
            case ContentKind.Objective when ContentEdits.Get<ObjectiveModel>(files, item.Kind, item.Id) is { } objective:
                return [List(objective.Tags), objective.TeamSizeMin == objective.TeamSizeMax ? Number(objective.TeamSizeMin) : $"{Number(objective.TeamSizeMin)} to {Number(objective.TeamSizeMax)}", Number(objective.Need), Number(objective.Stages.Count)];
            case ContentKind.Encounter when ContentEdits.Get<EncounterModel>(files, item.Kind, item.Id) is { } encounter:
                return [List(encounter.Tags), Approach(encounter.Careful), Approach(encounter.Fast), encounter.Chain ?? "", encounter.Decision?.Id ?? ""];
            default:
                return blank;
        }
    }

    private static string List(IReadOnlyList<string> values)
    {
        return string.Join(", ", values);
    }

    private static string Number(int value)
    {
        return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string Approach(ApproachModel approach)
    {
        return $"{approach.Skill} at {Number(approach.Difficulty)}";
    }
}
