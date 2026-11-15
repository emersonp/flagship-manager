using Sim.Content;
using Sim.Core.Content;

namespace Web.Forms;

public sealed record SkillChoice(string Id, string? Name);

// What a form picks from: read from the files on disk, so a skill added a moment ago is already there.
public sealed record FormChoices(IReadOnlyList<SkillChoice> Skills, IReadOnlyList<string> GearTags, IReadOnlyList<string> ObjectiveTags, int? MaxLevel)
{
    public IReadOnlyList<EncounterChoice> Encounters { get; init; } = [];

    // The tags encounters use: what an objective's stages draw by, and the suggestions for an encounter's own tags.
    public IReadOnlyList<string> EncounterTags { get; init; } = [];

    // Each decision's id and the encounter that holds it.
    public IReadOnlyDictionary<string, string> DecisionOwners { get; init; } = new Dictionary<string, string>();

    // The pack's pronoun sets, for the crew form's pick (slice 1d).
    public IReadOnlyList<SkillChoice> Pronouns { get; init; } = [];

    // The symbols lines may use, for the line form's help.
    public IReadOnlyList<string> Symbols { get; init; } = [];

    // The id of the decision the encounter already has, which cannot change; null when it has none.
    public string? FixedDecisionId { get; init; }

    // How many encounters a stage drawing by these tags can draw, as the game draws: a chain target is never drawn.
    public int Matches(IEnumerable<string> tags)
    {
        List<string> wanted = tags.ToList();
        HashSet<string> targets = Encounters.Select(e => e.Chain).OfType<string>().ToHashSet();
        return Encounters.Count(e => !targets.Contains(e.Id) && wanted.All(e.Tags.Contains));
    }
}

public sealed record EncounterChoice(string Id, string? Name, IReadOnlyList<string> Tags, string? Chain);

// Everything a form is drawn from: the values (as text), the checks that failed, what the loader or the save refused, and the loss warning.
public sealed record ItemFormView(
    ContentKind Kind,
    bool IsNew,
    string Id,
    string? Name,
    FormValues Values,
    IReadOnlyDictionary<string, string> Errors,
    IReadOnlyList<ContentError> Reasons,
    string? Notice,
    string? WarningLead,
    IReadOnlyList<string> Warnings,
    FormChoices Choices,
    string? FocusId = null)
{
    private static readonly IReadOnlyDictionary<string, object> None = new Dictionary<string, object>();

    // A number that differs for each response, so the same element asked to take focus twice in a row still does.
    private static long counter;

    private readonly long nonce = Interlocked.Increment(ref counter);

    // Attributes for the element with this id: when it is the one that should take focus, a data-init (which Datastar runs when
    // the element arrives in a patch, or when the attribute's text changes) that focuses it.
    public IReadOnlyDictionary<string, object> FocusFor(string id)
    {
        return FocusId == id ? new Dictionary<string, object> { ["data-init"] = $"el.focus(); void {nonce}" } : None;
    }

    public string Segment => ContentKinds.Segment(Kind);

    public string Noun => ContentKinds.Noun(Kind);

    // Where the form posts: the kind for a create, the item for a save.
    public string Action => IsNew ? $"/dev/content/{Segment}" : $"/dev/content/{Segment}/{Uri.EscapeDataString(Id)}";

    public string RowsUrl => IsNew ? $"/dev/content/{Segment}/rows" : $"{Action}/rows";

    // The rows route, asking for the element with this id to take focus in the answer.
    public string RowsUrlFocusing(string id)
    {
        return $"{RowsUrl}?focus={Uri.EscapeDataString(id)}";
    }

    public string DeleteUrl => $"{Action}/delete";

    // What else the delete does, said in its confirmation; null when nothing.
    public string? DeleteNote { get; init; }

    public string Heading => IsNew ? $"New {Noun}" : Name is { Length: > 0 } ? Name : Id;

    public string? Error(string name)
    {
        return Errors.GetValueOrDefault(name);
    }

    // The line form's preview against sample beats; null for other kinds.
    public LinePreview? Preview { get; init; }

    // The "What plays here" panel of an encounter or an objective; null for other kinds.
    public PlaysView? Plays { get; init; }

    // The route that answers with the panel's previews for the form as posted, saving nothing.
    public string PlaysUrl => IsNew ? $"/dev/content/{Segment}/plays" : $"{Action}/plays";

    // Symbols and pronoun sets are named in text, so their ids are lower case.
    public string IdRule => Checks.LowerCase(Kind) ? Checks.LowerIdRule : Checks.IdRule;
}
