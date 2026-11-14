using Sim.Content;
using Sim.Core.Content;

namespace Web.Forms;

public sealed record SkillChoice(string Id, string? Name);

// What a form picks from: read from the files on disk, so a skill added a moment ago is already there.
public sealed record FormChoices(IReadOnlyList<SkillChoice> Skills, IReadOnlyList<string> GearTags, IReadOnlyList<string> ObjectiveTags, int? MaxLevel);

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
    FormChoices Choices)
{
    public string Segment => ContentKinds.Segment(Kind);

    public string Noun => ContentKinds.Noun(Kind);

    // Where the form posts: the kind for a create, the item for a save.
    public string Action => IsNew ? $"/dev/content/{Segment}" : $"/dev/content/{Segment}/{Uri.EscapeDataString(Id)}";

    public string RowsUrl => IsNew ? $"/dev/content/{Segment}/rows" : $"{Action}/rows";

    public string DeleteUrl => $"{Action}/delete";

    public string Heading => IsNew ? $"New {Noun}" : Name is { Length: > 0 } ? Name : Id;

    public string? Error(string name)
    {
        return Errors.GetValueOrDefault(name);
    }
}
