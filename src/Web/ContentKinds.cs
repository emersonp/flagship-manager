using Sim.Content;
using Sim.Core.Content;
using Sim.Storylets;
using Web.Forms;

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
        (ContentKind.Line, "lines", "Lines"),
        (ContentKind.Symbol, "symbols", "Symbols"),
        (ContentKind.Pronoun, "pronouns", "Pronoun sets"),
        (ContentKind.Storylet, "storylets", "Storylets"),
        (ContentKind.Vignette, "vignettes", "Vignettes"),
        (ContentKind.Trait, "traits", "Traits"),
        (ContentKind.TestShip, "testships", "Test ships"),
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
            ContentKind.Line => "line",
            ContentKind.Symbol => "symbol",
            ContentKind.Pronoun => "pronoun set",
            ContentKind.Storylet => "storylet",
            ContentKind.Vignette => "vignette",
            ContentKind.Trait => "trait",
            ContentKind.TestShip => "test ship",
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
            ContentKind.Objective => [new("Tags"), new("Team size", true), new("Need", true), new("Stages", true), new("Words written")],
            ContentKind.Encounter => [new("Tags"), new("Approaches"), new("Chain"), new("Decision"), new("Words written")],
            ContentKind.Line => [new("Beat"), new("Criteria", true), new("Text")],
            ContentKind.Symbol => [new("Options", true), new("First option")],
            ContentKind.Pronoun => [new("Forms"), new("Plural")],
            ContentKind.Storylet => [new("Stakes", true), new("Cast"), new("Requires"), new("Options", true), new("Cooldown"), new("Casts on")],
            ContentKind.Vignette => [new("Cast"), new("Choice"), new("Casts on")],
            ContentKind.Trait => [new("On"), new("Levels or max"), new("Shown"), new("Used by", true)],
            ContentKind.TestShip => [new("Date"), new("Crew", true), new("Traits held", true)],
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
                return [List(objective.Tags), objective.TeamSizeMin == objective.TeamSizeMax ? Number(objective.TeamSizeMin) : $"{Number(objective.TeamSizeMin)} to {Number(objective.TeamSizeMax)}", Number(objective.Need), Number(objective.Stages.Count), Forms.Plays.Written(objective)];
            case ContentKind.Encounter when ContentEdits.Get<EncounterModel>(files, item.Kind, item.Id) is { } encounter:
                return [List(encounter.Tags), string.Join("; ", encounter.Approaches.Select(Approach)), encounter.Chain ?? "", encounter.Decision?.Id ?? "", Forms.Plays.Written(encounter)];
            case ContentKind.Line when ContentEdits.Get<LineModel>(files, item.Kind, item.Id) is { } line:
                return [line.Beat, Number(line.When.Count + 1), Clip(line.Text)];
            case ContentKind.Symbol when ContentEdits.Get<SymbolModel>(files, item.Kind, item.Id) is { } symbol:
                return [Number(symbol.Options.Count), symbol.Options.Count > 0 ? Clip(symbol.Options[0]) : ""];
            case ContentKind.Pronoun when ContentEdits.Get<PronounModel>(files, item.Kind, item.Id) is { } set:
                return [$"{set.Subject}, {set.Object}, {set.Possessive}, {set.PossessivePronoun}, {set.Reflexive}", set.Plural ? "Yes" : "No"];
            case ContentKind.Storylet when ContentEdits.Get<StoryletModel>(files, item.Kind, item.Id) is { } storylet:
                return [Number(storylet.Stakes), Slots(storylet.Cast), Clip(storylet.Requires ?? "", 40), Number(storylet.Options.Count), Cooldown(storylet.CastCooldown, storylet.GlobalCooldown), CastsOn(files, item)];
            case ContentKind.Vignette when ContentEdits.Get<VignetteModel>(files, item.Kind, item.Id) is { } vignette:
                return [Slots(vignette.Cast), vignette.Options.Count > 0 ? "Yes" : "No", CastsOn(files, item)];
            case ContentKind.Trait when ContentEdits.Get<TraitModel>(files, item.Kind, item.Id) is { } trait:
                // Used by counts the items the trait form's "Where it's used" lists; blank while the pack does not load, as they cannot be read.
                return [trait.On ?? "ship or crew", Levels(trait), trait.Shown == false ? "No" : "Yes", UsedBy(files, trait.Id)];
            case ContentKind.TestShip when ContentEdits.Get<TestShipModel>(files, item.Kind, item.Id) is { } ship:
                return [ship.Date is { } date ? Date(files, date) : "", Number(ship.Crew.Count), Number(ship.Traits.Count)];
            default:
                return blank;
        }
    }

    // The start of a long text, for a table cell.
    private static string Clip(string text, int length = 60)
    {
        return text.Length <= length ? text : text[..(length - 1)] + "\u2026";
    }

    // Where an item opens: its form, or, for a kind with no form (test ships, and the others until their forms come), its file's text.
    public static string Href(ContentItemRef item)
    {
        string form = $"/dev/content/{Segment(item.Kind)}/{Uri.EscapeDataString(item.Id)}";
        return HasForm(item.Kind) ? form : (TextFile.All.FirstOrDefault(f => f.Path == item.File) ?? TextOf(item.Kind))?.Action ?? form;
    }

    // The text view of a kind's file, for its list's "Edit as text"; null for a kind edited only in forms.
    public static TextFile? TextOf(ContentKind kind)
    {
        return TextFile.Find(Segment(kind));
    }

    // Whether the kind has a form; one without opens as text alone.
    public static bool HasForm(ContentKind kind)
    {
        return KindForm.For(Segment(kind)) is not null;
    }

    private static string Slots(IReadOnlyList<CastSlotModel> cast)
    {
        return string.Join(", ", cast.Select(c => c.Name));
    }

    // As written: blank when the item leaves both to the tuning file.
    private static string Cooldown(int? cast, int? global)
    {
        var parts = new List<string>();
        if (cast is { } c)
        {
            parts.Add($"cast {Number(c)}");
        }

        if (global is { } g)
        {
            parts.Add($"global {Number(g)}");
        }

        return string.Join(", ", parts);
    }

    private static string UsedBy(ContentFileSet files, string trait)
    {
        TraitUses uses = TraitUses.Of(files, trait);
        return uses.Loaded ? Number(uses.Count) : "";
    }

    private static string Levels(TraitModel trait)
    {
        if (trait.Levels is { Count: > 0 } levels)
        {
            return string.Join(", ", levels.OrderBy(l => l.Key).Select(l => $"{Number(l.Key)} {l.Value}"));
        }

        return trait.Max is { } max ? $"max {Number(max)}" : "";
    }

    // A test ship's today as the calendar shows it; the day number while the pack does not load.
    private static string Date(ContentFileSet files, int day)
    {
        return files.Validate().Pack is { } pack ? Web.Bridge.Dates.Short(pack.Calendar, day) : Number(day);
    }

    // "1 of 2": how many of the test ships the storylet or vignette casts on, by the preview's own casting (design 4.2), cooldowns aside as
    // the preview reports and does not apply them. Blank while the pack does not load, since then nothing can be cast.
    private static string CastsOn(ContentFileSet files, ContentItemRef item)
    {
        if (files.Validate().Pack is not { } pack)
        {
            return "";
        }

        bool storylet = item.Kind == ContentKind.Storylet;
        if (storylet ? pack.Storylets.All(s => s.Id != item.Id) : pack.Vignettes.All(v => v.Id != item.Id))
        {
            return "";
        }

        IReadOnlyList<TestShipDef> ships = files.TestShips().Ships;
        int casts = 0;
        foreach (TestShipDef ship in ships)
        {
            var source = new TestShipSource(pack, ship);
            PreviewCast cast = storylet ? Preview.Storylet(pack, item.Id, source, source.Roster, 0, false) : Preview.Vignette(pack, item.Id, source, source.Roster, 0, false);
            if (cast.Cast is not null)
            {
                casts++;
            }
        }

        return $"{Number(casts)} of {Number(ships.Count)}";
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
        return $"{approach.Name ?? approach.Id}: {approach.Skill} at {Number(approach.Difficulty)}";
    }
}
