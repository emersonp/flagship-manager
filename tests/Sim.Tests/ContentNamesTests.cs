using Sim.Content;
using Sim.Core.Content;

namespace Sim.Tests;

// Every authored kind takes an optional display name; ids stay the keys.
public class ContentNamesTests
{
    private const string BadName = "name must be 1 to 80 characters with no line break.";

    // The kind, its file, the text just before where the name goes, the indent, the pack's id for it, and the loader's error prefix.
    public static TheoryData<string, string, string, string, string> Kinds => new()
    {
        { "skill", "skills/base.yaml", "- id: SKILL_A\n", "  ", "SKILL_A" },
        { "crew", "crew/starting.yaml", "- id: CREW_01\n", "  ", "CREW_01" },
        { "gear", "gear/base.yaml", "- { id: GEAR_01, ", "", "GEAR_01" },
        { "objective", "objectives/base.yaml", "- id: OBJECTIVE_TEMPLATE_1\n", "  ", "OBJECTIVE_TEMPLATE_1" },
        { "encounter", "encounters/base.yaml", "- id: ENCOUNTER_01\n", "  ", "ENCOUNTER_01" },
        { "decision", "encounters/base.yaml", "    id: DECISION_PROMPT_1\n", "    ", "DECISION_PROMPT_1" },
        { "assignment", "assignments/base.yaml", "- id: ASSIGNMENT_TEMPLATE_1\n", "  ", "ASSIGNMENT_TEMPLATE_1" },
    };

    // A quoted YAML scalar is inserted as the item's name key.
    private static ContentLoadResult Load(string file, string anchor, string indent, string? yamlValue)
    {
        Dictionary<string, string> files = TestContent.RepoFiles();
        if (yamlValue is not null)
        {
            string insert = indent.Length == 0 ? $"name: {yamlValue}, " : $"{indent}name: {yamlValue}\n";
            string changed = files[file].Replace(anchor, anchor + insert, StringComparison.Ordinal);
            Assert.NotEqual(files[file], changed);
            files[file] = changed;
        }

        return ContentLoader.Load(files);
    }

    private static string? NameOf(ContentPack pack, string kind, string id)
    {
        return kind switch
        {
            "skill" => pack.Skills.Single(s => s.Id == id).Name,
            "crew" => pack.Crew.Single(c => c.Id == id).Name,
            "gear" => pack.Gear.Single(g => g.Id == id).Name,
            "objective" => pack.Objectives.Single(o => o.Id == id).Name,
            "encounter" => pack.Encounters.Single(e => e.Id == id).Name,
            "decision" => pack.Encounters.Single(e => e.Decision?.Id == id).Decision!.Name,
            "assignment" => pack.Assignments.Single(a => a.Id == id).Name,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void A_name_is_optional(string kind, string file, string anchor, string indent, string id)
    {
        ContentLoadResult result = Load(file, anchor, indent, null);

        Assert.Empty(result.Errors);
        Assert.Null(NameOf(result.Pack!, kind, id));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void A_name_is_read_and_trimmed(string kind, string file, string anchor, string indent, string id)
    {
        ContentLoadResult result = Load(file, anchor, indent, "\"  NAME_PLACEHOLDER_1  \"");

        Assert.Empty(result.Errors);
        Assert.Equal("NAME_PLACEHOLDER_1", NameOf(result.Pack!, kind, id));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void A_name_of_exactly_80_characters_is_accepted(string kind, string file, string anchor, string indent, string id)
    {
        string name = new('N', 80);

        ContentLoadResult result = Load(file, anchor, indent, $"\"{name}\"");

        Assert.Empty(result.Errors);
        Assert.Equal(name, NameOf(result.Pack!, kind, id));
    }

    // Empty, blank, over-long, and multi-line names are each refused, naming the file and the id.
    [Theory]
    [MemberData(nameof(Kinds))]
    public void A_bad_name_is_refused_with_its_file_and_id(string _, string file, string anchor, string indent, string id)
    {
        foreach (string bad in new[] { "\"\"", "\"   \"", $"\"{new string('N', 81)}\"", "\"NAME_PLACEHOLDER_1\\nNAME_PLACEHOLDER_2\"", "\"NAME_PLACEHOLDER_1\\r\\nX\"" })
        {
            ContentLoadResult result = Load(file, anchor, indent, bad);

            Assert.Null(result.Pack);
            ContentError error = Assert.Single(result.Errors);
            Assert.Equal(file, error.File);
            Assert.Equal($"{id}: {BadName}", error.Message);
        }
    }
}
