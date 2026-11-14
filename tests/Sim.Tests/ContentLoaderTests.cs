using Sim.Content;
using Sim.Core.Content;

namespace Sim.Tests;

public class ContentLoaderTests
{
    private const string Manifest = "id: CORE\nversion: 1\n";

    [Fact]
    public void A_valid_pack_loads()
    {
        ContentLoadResult result = ContentLoader.Load(new Dictionary<string, string>
        {
            ["pack.yaml"] = Manifest,
            ["tuning/missions.yaml"] = TestContent.TuningText,
            ["skills/base.yaml"] = "- id: SKILL_A\n  tags: [technical]\n- id: SKILL_B\n",
        });

        Assert.Empty(result.Errors);
        Assert.Equal("CORE", result.Pack!.Id);
        Assert.Equal(1, result.Pack.Version);
        Assert.Equal(new[] { "SKILL_A", "SKILL_B" }, result.Pack.Skills.Select(s => s.Id));
        Assert.Equal(new[] { "technical" }, result.Pack.Skills[0].Tags);
    }

    // The author fixes content in batches, so every problem is reported at once and no pack is returned.
    [Fact]
    public void All_errors_are_reported_together()
    {
        ContentLoadResult result = ContentLoader.Load(new Dictionary<string, string>
        {
            ["pack.yaml"] = "id: core\nversion: 0\n",
            ["tuning/missions.yaml"] = TestContent.TuningText,
            ["skills/a.yaml"] = "- id: SKILL_A\n- id: skill b\n",
            ["skills/b.yaml"] = "- id: SKILL_A\n",
            ["skils/c.yaml"] = "- id: SKILL_C\n",
            ["skills/d.yaml"] = "- id: SKILL_D\n  tag: [typo]\n",
        });

        Assert.Null(result.Pack);
        Assert.Equal(
            new[]
            {
                "pack.yaml|Id 'core' must be upper case letters, digits, and underscores, starting with a letter.",
                "pack.yaml|version must be 1 or more.",
                "skills/a.yaml|Id 'skill b' must be upper case letters, digits, and underscores, starting with a letter.",
                "skills/b.yaml|Duplicate id 'SKILL_A', first defined in skills/a.yaml.",
                "skills/d.yaml|Line 2: Property 'tag' not found on type 'Sim.Content.ContentLoader+SkillDto'.",
                "skils/c.yaml|Unknown content folder 'skils'.",
            },
            result.Errors.Select(e => $"{e.File}|{e.Message}"));
    }

    [Fact]
    public void A_missing_manifest_is_an_error()
    {
        ContentLoadResult result = ContentLoader.Load(new Dictionary<string, string> { ["tuning/missions.yaml"] = TestContent.TuningText });

        Assert.Equal("The pack has no pack.yaml.", Assert.Single(result.Errors).Message);
    }

    [Fact]
    public void A_blank_list_item_is_reported_for_its_file()
    {
        ContentLoadResult result = ContentLoader.Load(new Dictionary<string, string>
        {
            ["pack.yaml"] = Manifest,
            ["tuning/missions.yaml"] = TestContent.TuningText,
            ["skills/a.yaml"] = "- id: SKILL_A\n-\n",
        });

        Assert.Null(result.Pack);
        Assert.Equal("skills/a.yaml", Assert.Single(result.Errors).File);
    }

    [Fact]
    public void Tags_with_no_value_load_as_an_empty_list()
    {
        ContentLoadResult result = ContentLoader.Load(new Dictionary<string, string>
        {
            ["pack.yaml"] = Manifest,
            ["tuning/missions.yaml"] = TestContent.TuningText,
            ["skills/a.yaml"] = "- id: SKILL_A\n  tags:\n",
        });

        Assert.Empty(result.Errors);
        Assert.Empty(result.Pack!.Skills[0].Tags);
    }

    [Fact]
    public void A_duplicated_key_is_reported_for_its_file()
    {
        ContentLoadResult result = ContentLoader.Load(new Dictionary<string, string>
        {
            ["pack.yaml"] = Manifest,
            ["tuning/missions.yaml"] = TestContent.TuningText,
            ["skills/a.yaml"] = "- id: SKILL_A\n  id: SKILL_B\n",
        });

        Assert.Null(result.Pack);
        Assert.Equal("skills/a.yaml", Assert.Single(result.Errors).File);
    }
}
