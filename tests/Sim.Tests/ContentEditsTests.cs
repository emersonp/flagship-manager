using System.Text.Json;
using Sim.Content;
using Sim.Core;
using Sim.Core.Content;

namespace Sim.Tests;

public class ContentEditsTests : IClassFixture<RepoContentGuard>
{
    private static string ContentRoot => Path.Combine(TestContent.Root, "content");

    private static ContentFileSet Repo() => ContentFileSet.ReadDirectory(ContentRoot);

    private static string Json(ContentPack pack) => JsonSerializer.Serialize(pack, SimJson.Options);

    private static ContentPack Valid(ContentFileSet files)
    {
        ContentLoadResult result = files.Validate();
        Assert.True(result.Pack is not null, string.Join("; ", result.Errors.Select(e => $"{e.File}: {e.Message}")));
        return result.Pack;
    }

    private static ContentFileSet With(ContentFileSet files, string path, string? text)
    {
        var copy = new Dictionary<string, string>(files.Files);
        if (text is null)
        {
            copy.Remove(path);
        }
        else
        {
            copy[path] = text;
        }

        return new ContentFileSet(copy);
    }

    private static object ModelOf(ContentFileSet files, ContentItemRef item) => item.Kind switch
    {
        ContentKind.Skill => ContentEdits.Get<SkillModel>(files, item.Kind, item.Id)!,
        ContentKind.Gear => ContentEdits.Get<GearModel>(files, item.Kind, item.Id)!,
        ContentKind.Crew => ContentEdits.Get<CrewModel>(files, item.Kind, item.Id)!,
        ContentKind.Assignment => ContentEdits.Get<AssignmentModel>(files, item.Kind, item.Id)!,
        ContentKind.Objective => ContentEdits.Get<ObjectiveModel>(files, item.Kind, item.Id)!,
        ContentKind.Encounter => ContentEdits.Get<EncounterModel>(files, item.Kind, item.Id)!,
        _ => throw new ArgumentOutOfRangeException(nameof(item)),
    };

    private static string TempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "content-edits-" + Path.GetRandomFileName());
        Directory.CreateDirectory(path);
        return path;
    }

    // Rule 1: reading and upserting every item of the repository's own pack, unchanged, loses nothing.
    [Fact]
    public void Upserting_every_item_unchanged_gives_the_same_pack()
    {
        ContentFileSet original = Repo();
        string expected = Json(Valid(original));
        ContentFileSet edited = original;
        IReadOnlyList<ContentItemRef> items = original.Index();
        Assert.NotEmpty(items);
        foreach (ContentItemRef item in items)
        {
            edited = ContentEdits.Upsert(edited, item.Kind, ModelOf(original, item));
        }

        Assert.Equal(expected, Json(Valid(edited)));
    }

    // Rule 1 per file: the canonical rewrite of every file loads to the same pack, and rewriting twice is stable.
    [Fact]
    public void Canonicalizing_every_file_gives_the_same_pack_and_is_stable()
    {
        ContentFileSet original = Repo();
        ContentFileSet once = ContentEdits.Canonicalize(original);
        Assert.Equal(Json(Valid(original)), Json(Valid(once)));
        ContentFileSet twice = ContentEdits.Canonicalize(once);
        Assert.Equal(once.Files.OrderBy(f => f.Key).ToList(), twice.Files.OrderBy(f => f.Key).ToList());
    }

    // The comparison above must be able to fail: a changed number changes the pack's JSON.
    [Fact]
    public void The_pack_comparison_notices_a_changed_value()
    {
        ContentFileSet original = Repo();
        CrewModel crew = ContentEdits.Get<CrewModel>(original, ContentKind.Crew, "CREW_01")!;
        ContentFileSet edited = ContentEdits.Upsert(original, ContentKind.Crew, crew with { Caution = crew.Caution - 1 });
        Assert.NotEqual(Json(Valid(original)), Json(Valid(edited)));
    }

    [Fact]
    public void The_index_lists_every_item_with_its_file()
    {
        ContentPack pack = Valid(Repo());
        IReadOnlyList<ContentItemRef> index = Repo().Index();
        Assert.Equal(pack.Skills.Count, index.Count(i => i.Kind == ContentKind.Skill));
        Assert.Equal(pack.Gear.Count, index.Count(i => i.Kind == ContentKind.Gear));
        Assert.Equal(pack.Crew.Count, index.Count(i => i.Kind == ContentKind.Crew));
        Assert.Equal(pack.Assignments.Count, index.Count(i => i.Kind == ContentKind.Assignment));
        Assert.Equal(pack.Objectives.Count, index.Count(i => i.Kind == ContentKind.Objective));
        Assert.Equal(pack.Encounters.Count, index.Count(i => i.Kind == ContentKind.Encounter));
        Assert.Contains(new ContentItemRef(ContentKind.Crew, "CREW_01", null, "crew/starting.yaml"), index);
        Assert.Contains(new ContentItemRef(ContentKind.Gear, "GEAR_02", null, "gear/base.yaml"), index);
    }

    // Names ride along: the index shows the trimmed display name.
    [Fact]
    public void The_index_shows_names()
    {
        ContentFileSet files = With(Repo(), "skills/base.yaml", "- id: SKILL_A\n  name:  Name placeholder \n- id: SKILL_B\n");
        Assert.Contains(new ContentItemRef(ContentKind.Skill, "SKILL_A", "Name placeholder", "skills/base.yaml"), files.Index());
    }

    // Rule 6: an editor must open a broken pack to fix it.
    [Fact]
    public void The_index_lists_what_it_can_of_a_broken_pack()
    {
        ContentFileSet files = With(Repo(), "skills/base.yaml", "- id: SKILL_A\n- name: NAME_PLACEHOLDER\n- id: skill b\n");
        files = With(files, "gear/base.yaml", "- id: GEAR_01\n  tag: [unclosed\n");
        IReadOnlyList<ContentItemRef> index = files.Index();
        Assert.Equal(new[] { "SKILL_A", "skill b" }, index.Where(i => i.Kind == ContentKind.Skill).Select(i => i.Id));
        Assert.DoesNotContain(index, i => i.Kind == ContentKind.Gear);
        ContentLoadResult result = files.Validate();
        Assert.Null(result.Pack);
        Assert.Contains(result.Errors, e => e.File == "gear/base.yaml");
    }

    [Fact]
    public void Get_of_a_missing_id_is_null()
    {
        ContentFileSet files = Repo();
        Assert.Null(ContentEdits.Get<CrewModel>(files, ContentKind.Crew, "NO_SUCH_CREW"));
        Assert.Null(ContentEdits.Get<GearModel>(files, ContentKind.Crew, "CREW_01"));
        Assert.Null(ContentEdits.Get<SkillModel>(files, ContentKind.Skill, "skill_a"));
    }

    [Fact]
    public void Get_reads_every_field_of_an_encounter()
    {
        EncounterModel model = ContentEdits.Get<EncounterModel>(Repo(), ContentKind.Encounter, "ENCOUNTER_06")!;
        Assert.Equal(new[] { "field", "hazard" }, model.Tags);
        Assert.True(model.CommsBlocked);
        Assert.Equal("ENCOUNTER_CHAIN", model.Chain);
        Assert.Equal(new ApproachModel("SKILL_C", 6, null), model.Careful);
        Assert.Equal(new ApproachModel("SKILL_D", 7, null), model.Fast);
        Assert.Equal("DECISION_PROMPT_5", model.Decision!.Id);
        Assert.Equal(3, model.Decision.Stakes);
        Assert.Equal(new[] { new EffectModel(1, null, null, null), new EffectModel(null, null, 1, null) }, model.Decision.Bold);
        Assert.Empty(model.Decision.Safe);
    }

    [Fact]
    public void Get_reads_a_count_as_a_number_or_a_pair()
    {
        ObjectiveModel model = ContentEdits.Get<ObjectiveModel>(Repo(), ContentKind.Objective, "OBJECTIVE_TEMPLATE_2")!;
        Assert.Equal((2, 3), (model.TeamSizeMin, model.TeamSizeMax));
        Assert.Equal((1, 1), (model.Stages[0].CountMin, model.Stages[0].CountMax));
        Assert.Equal((1, 2), (model.Stages[1].CountMin, model.Stages[1].CountMax));
        Assert.True(model.Stages[2].Skippable);
        Assert.Equal(5, model.UnattendedDifficulty);
    }

    // Rule 4: only the edited file changes, and the rest are the same string instances.
    [Fact]
    public void Upsert_changes_only_the_file_that_holds_the_item()
    {
        ContentFileSet original = Repo();
        GearModel gear = ContentEdits.Get<GearModel>(original, ContentKind.Gear, "GEAR_01")!;
        ContentFileSet edited = ContentEdits.Upsert(original, ContentKind.Gear, gear with { Name = "NAME_PLACEHOLDER" });
        Assert.Equal(original.Files.Keys.OrderBy(k => k), edited.Files.Keys.OrderBy(k => k));
        foreach ((string path, string text) in original.Files)
        {
            if (path == "gear/base.yaml")
            {
                Assert.NotEqual(text, edited.Files[path]);
            }
            else
            {
                Assert.Same(text, edited.Files[path]);
            }
        }

        Assert.Equal("NAME_PLACEHOLDER", Valid(edited).Gear.First(g => g.Id == "GEAR_01").Name);
    }

    // The item keeps its place in its file.
    [Fact]
    public void Upsert_keeps_the_items_position()
    {
        ContentFileSet edited = ContentEdits.Upsert(Repo(), ContentKind.Crew, ContentEdits.Get<CrewModel>(Repo(), ContentKind.Crew, "CREW_03")! with { Seniority = 9 });
        Assert.Equal(Repo().Index().Where(i => i.Kind == ContentKind.Crew).Select(i => i.Id), edited.Index().Where(i => i.Kind == ContentKind.Crew).Select(i => i.Id));
        Assert.Equal(9, Valid(edited).Crew.First(c => c.Id == "CREW_03").Seniority);
    }

    // A new item goes to the kind's first file in path order.
    [Fact]
    public void Upsert_adds_a_new_item_to_the_first_file_of_its_kind()
    {
        ContentFileSet files = With(Repo(), "gear/a.yaml", "- id: GEAR_05\n  tag: extra\n");
        ContentFileSet edited = ContentEdits.Upsert(files, ContentKind.Gear, new GearModel("GEAR_06", null, "other"));
        Assert.Contains("GEAR_06", edited.Files["gear/a.yaml"]);
        Assert.Same(files.Files["gear/base.yaml"], edited.Files["gear/base.yaml"]);
        Assert.Contains(edited.Index(), i => i is { Id: "GEAR_06", File: "gear/a.yaml" });
    }

    // A kind with no file gets the conventional one.
    [Fact]
    public void Upsert_creates_the_conventional_file_for_an_empty_kind()
    {
        ContentFileSet files = With(Repo(), "gear/base.yaml", null);
        ContentFileSet edited = ContentEdits.Upsert(files, ContentKind.Gear, new GearModel("GEAR_01", null, "scanner"));
        Assert.Equal("- id: GEAR_01\n  tag: scanner\n", edited.Files["gear/base.yaml"]);
        Assert.Equal("crew/starting.yaml", ContentEdits.Upsert(With(Repo(), "crew/starting.yaml", null), ContentKind.Crew, new CrewModel("CREW_01", null, 1, new Dictionary<string, int>(), 0, 0)).Index().Single(i => i.Kind == ContentKind.Crew).File);
        foreach ((ContentKind kind, object model, string path) in new (ContentKind, object, string)[]
        {
            (ContentKind.Skill, new SkillModel("SKILL_A", null, []), "skills/base.yaml"),
            (ContentKind.Assignment, new AssignmentModel("ASSIGNMENT_1", null, [], new Dictionary<int, int> { [1] = 1 }, 0), "assignments/base.yaml"),
            (ContentKind.Objective, new ObjectiveModel("OBJECTIVE_1", null, [], 1, 1, 1, null, []), "objectives/base.yaml"),
            (ContentKind.Encounter, new EncounterModel("ENCOUNTER_1", null, [], false, null, new ApproachModel("SKILL_A", 5, null), new ApproachModel("SKILL_A", 6, null), null), "encounters/base.yaml"),
        })
        {
            Assert.Contains(path, ContentEdits.Upsert(new ContentFileSet(new Dictionary<string, string>()), kind, model).Files.Keys);
        }
    }

    // A first file that does not parse is never overwritten to make room for a new item.
    [Fact]
    public void Upsert_will_not_overwrite_a_file_that_does_not_parse()
    {
        ContentFileSet files = With(Repo(), "gear/a.yaml", "- id: [broken\n");
        ContentFileSet edited = ContentEdits.Upsert(files, ContentKind.Gear, new GearModel("GEAR_06", null, "other"));
        Assert.Same(files.Files["gear/a.yaml"], edited.Files["gear/a.yaml"]);
        Assert.Contains(edited.Index(), i => i is { Id: "GEAR_06", File: "gear/base.yaml" });
    }

    // A comment inside a flow map makes the YAML scanner throw; the file is unparseable, not a crash.
    [Fact]
    public void A_file_the_scanner_rejects_is_listed_as_unparseable_and_never_rewritten()
    {
        string broken = Repo().Files["crew/starting.yaml"].Replace("SKILL_B: 5,", "SKILL_B: 5 # c,");
        Assert.Contains("# c,", broken);
        ContentFileSet files = With(Repo(), "crew/starting.yaml", broken);
        Assert.DoesNotContain(files.Index(), i => i.File == "crew/starting.yaml");
        Assert.NotEmpty(files.Validate().Errors);
        Assert.Null(ContentEdits.Get<CrewModel>(files, ContentKind.Crew, "CREW_01"));
        ContentFileSet edited = ContentEdits.Upsert(files, ContentKind.Gear, new GearModel("GEAR_06", null, "other"));
        Assert.Same(files.Files["crew/starting.yaml"], edited.Files["crew/starting.yaml"]);
    }

    [Fact]
    public void Upsert_refuses_a_model_of_the_wrong_kind()
    {
        Assert.Throws<ArgumentException>(() => ContentEdits.Upsert(Repo(), ContentKind.Crew, new GearModel("GEAR_01", null, "scanner")));
    }

    // Rule 7: an id that exists in another kind is the loader's to refuse.
    [Fact]
    public void Upsert_of_an_id_used_by_another_kind_is_left_to_validation()
    {
        ContentFileSet edited = ContentEdits.Upsert(Repo(), ContentKind.Gear, new GearModel("CREW_01", null, "scanner"));
        ContentLoadResult result = edited.Validate();
        Assert.Null(result.Pack);
        Assert.Contains(result.Errors, e => e.Message.Contains("Duplicate id 'CREW_01'"));
    }

    [Fact]
    public void Delete_removes_one_item_and_only_rewrites_its_file()
    {
        ContentFileSet original = Repo();
        ContentFileSet edited = ContentEdits.Delete(original, ContentKind.Gear, "GEAR_04");
        Assert.Equal(new[] { "GEAR_01", "GEAR_02", "GEAR_03" }, edited.Index().Where(i => i.Kind == ContentKind.Gear).Select(i => i.Id));
        Assert.Equal(3, Valid(edited).Gear.Count);
        foreach ((string path, string text) in original.Files.Where(f => f.Key != "gear/base.yaml"))
        {
            Assert.Same(text, edited.Files[path]);
        }
    }

    // The loader decides whether a delete is allowed; the edit itself always happens.
    [Fact]
    public void Delete_of_a_referenced_item_leaves_the_refusal_to_validation()
    {
        ContentFileSet edited = ContentEdits.Delete(Repo(), ContentKind.Skill, "SKILL_A");
        ContentLoadResult result = edited.Validate();
        Assert.Null(result.Pack);
        Assert.Contains(result.Errors, e => e.Message.Contains("unknown skill 'SKILL_A'"));
    }

    // A file emptied by deletes stays a valid list and keeps its leading comment.
    [Fact]
    public void Delete_of_the_last_item_leaves_an_empty_list_and_the_comment()
    {
        ContentFileSet files = With(Repo(), "gear/base.yaml", "# Placeholders.\n- id: GEAR_01\n  tag: scanner\n");
        ContentFileSet edited = ContentEdits.Delete(files, ContentKind.Gear, "GEAR_01");
        Assert.Equal("# Placeholders.\n[]\n", edited.Files["gear/base.yaml"]);
        Assert.Empty(Valid(edited).Gear);
    }

    [Fact]
    public void Delete_of_a_missing_id_changes_nothing()
    {
        ContentFileSet original = Repo();
        Assert.Same(original, ContentEdits.Delete(original, ContentKind.Gear, "NO_SUCH_GEAR"));
    }

    [Fact]
    public void ReplaceText_changes_only_that_file()
    {
        ContentFileSet original = Repo();
        ContentFileSet edited = ContentEdits.ReplaceText(original, "pack.yaml", "id: CORE\nversion: 2\n");
        Assert.Equal(2, Valid(edited).Version);
        foreach ((string path, string text) in original.Files.Where(f => f.Key != "pack.yaml"))
        {
            Assert.Same(text, edited.Files[path]);
        }
    }

    [Theory]
    [InlineData("/etc/pack.yaml")]
    [InlineData("../pack.yaml")]
    [InlineData("a/../../b.yaml")]
    [InlineData("pack.txt")]
    [InlineData("a\\b.yaml")]
    [InlineData("")]
    public void ReplaceText_refuses_an_unsafe_path(string path)
    {
        Assert.Throws<ArgumentException>(() => ContentEdits.ReplaceText(Repo(), path, "x"));
    }

    [Fact]
    public void ReadDirectory_uses_forward_slashes_and_every_yaml_file()
    {
        ContentFileSet files = Repo();
        Assert.Equal(
            new[] { "assignments/base.yaml", "crew/starting.yaml", "encounters/base.yaml", "gear/base.yaml", "objectives/base.yaml", "pack.yaml", "skills/base.yaml", "tuning/crew.yaml", "tuning/missions.yaml" },
            files.Files.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(File.ReadAllText(Path.Combine(ContentRoot, "pack.yaml")), files.Files["pack.yaml"]);
    }

    [Fact]
    public void Write_writes_only_changed_files_and_deletes_nothing()
    {
        string root = TempDirectory();
        try
        {
            ContentFileSet before = Repo();
            foreach ((string path, string text) in before.Files)
            {
                string full = Path.Combine(root, path);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, text);
                File.SetLastWriteTimeUtc(full, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            }

            ContentFileSet after = ContentEdits.Upsert(before, ContentKind.Gear, new GearModel("GEAR_01", "NAME_PLACEHOLDER", "scanner"));
            after = With(after, "skills/extra.yaml", "- id: SKILL_E\n");
            after = With(after, "crew/starting.yaml", null);
            ContentEdits.Write(root, before, after);

            Assert.Equal(after.Files["gear/base.yaml"], File.ReadAllText(Path.Combine(root, "gear", "base.yaml")));
            Assert.Equal("- id: SKILL_E\n", File.ReadAllText(Path.Combine(root, "skills", "extra.yaml")));
            Assert.True(File.Exists(Path.Combine(root, "crew", "starting.yaml")));
            DateTime old = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            Assert.Equal(old, File.GetLastWriteTimeUtc(Path.Combine(root, "pack.yaml")));
            Assert.Equal(old, File.GetLastWriteTimeUtc(Path.Combine(root, "encounters", "base.yaml")));
            Assert.NotEqual(old, File.GetLastWriteTimeUtc(Path.Combine(root, "gear", "base.yaml")));
            Assert.Empty(Directory.EnumerateFiles(root, "*.tmp", SearchOption.AllDirectories));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // Rule 5: a bad path throws before anything at all is written.
    [Theory]
    [InlineData("/tmp/escape.yaml")]
    [InlineData("../escape.yaml")]
    [InlineData("gear/../../escape.yaml")]
    [InlineData("gear/escape.txt")]
    [InlineData("gear\\escape.yaml")]
    public void Write_refuses_an_unsafe_path_before_writing_anything(string path)
    {
        string root = TempDirectory();
        try
        {
            ContentFileSet before = new(new Dictionary<string, string>());
            var files = new Dictionary<string, string> { ["pack.yaml"] = "id: CORE\nversion: 1\n" };
            files[path] = "- id: X\n";
            Assert.Throws<ArgumentException>(() => ContentEdits.Write(root, before, new ContentFileSet(files)));
            Assert.Empty(Directory.EnumerateFileSystemEntries(root));
            Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(root)!, "escape.yaml")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // Rule 5: a failure while writing leaves the old file as it was, and no temporary file behind.
    [Fact]
    public void Write_that_fails_leaves_the_old_file_intact()
    {
        string root = TempDirectory();
        try
        {
            ContentFileSet before = new(new Dictionary<string, string> { ["gear/base.yaml"] = "- id: GEAR_01\n  tag: scanner\n" });
            Directory.CreateDirectory(Path.Combine(root, "gear"));
            File.WriteAllText(Path.Combine(root, "gear", "base.yaml"), before.Files["gear/base.yaml"]);
            Directory.CreateDirectory(Path.Combine(root, "gear", "base.yaml.tmp"));
            ContentFileSet after = ContentEdits.Upsert(before, ContentKind.Gear, new GearModel("GEAR_01", null, "changed"));

            Assert.ThrowsAny<Exception>(() => ContentEdits.Write(root, before, after));
            Assert.Equal(before.Files["gear/base.yaml"], File.ReadAllText(Path.Combine(root, "gear", "base.yaml")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
