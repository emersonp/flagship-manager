using System.Security.Cryptography;
using System.Text.Json;
using Sim.Content;
using Sim.Core;
using Sim.Core.Content;

namespace Sim.Tests;

// Fails the class if anything under the repository's content/ changes while its tests run.
public sealed class RepoContentGuard : IDisposable
{
    private readonly string _root;
    private readonly string _before;

    public RepoContentGuard()
        : this(Path.Combine(TestContent.Root, "content"))
    {
    }

    internal RepoContentGuard(string root)
    {
        _root = root;
        _before = Hash();
    }

    public void Dispose()
    {
        if (Hash() != _before)
        {
            throw new InvalidOperationException($"A test changed the files under {_root}.");
        }
    }

    private string Hash()
    {
        IEnumerable<string> parts = Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => f + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));
        return string.Join("\n", parts);
    }
}

public class ContentFaithfulnessTests : IClassFixture<RepoContentGuard>
{
    // A repository file with one item's text changed, and what to ask of it.
    public sealed record Case(string Name, ContentKind Kind, string Path, string Marker, Func<string, string> Break, string Other, string Broken, string Fragment, bool HasId = true)
    {
        public override string ToString() => Name;
    }

    private static ContentFileSet Repo() => ContentFileSet.ReadDirectory(System.IO.Path.Combine(TestContent.Root, "content"));

    private static string EditBlock(string text, string marker, Func<string, string> edit)
    {
        int start = text.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"marker {marker} not found");
        int end = text.IndexOf("\n- ", start + 1, StringComparison.Ordinal);
        end = end < 0 ? text.Length : end;
        string block = text[start..end];
        string edited = edit(block);
        Assert.NotEqual(block, edited);
        return text[..start] + edited + text[end..];
    }

    private static ContentFileSet Broken(Case c)
    {
        ContentFileSet repo = Repo();
        string text = EditBlock(repo.Files[c.Path], c.Marker, c.Break);
        return ContentEdits.ReplaceText(repo, c.Path, text);
    }

    private static object ModelOf(ContentFileSet files, ContentKind kind, string id) => kind switch
    {
        ContentKind.Skill => ContentEdits.Get<SkillModel>(files, kind, id)!,
        ContentKind.Gear => ContentEdits.Get<GearModel>(files, kind, id)!,
        ContentKind.Crew => ContentEdits.Get<CrewModel>(files, kind, id)!,
        ContentKind.Assignment => ContentEdits.Get<AssignmentModel>(files, kind, id)!,
        ContentKind.Objective => ContentEdits.Get<ObjectiveModel>(files, kind, id)!,
        ContentKind.Encounter => ContentEdits.Get<EncounterModel>(files, kind, id)!,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private const string Gear2 = "- { id: GEAR_02";
    private const string Enc2 = "- id: ENCOUNTER_02";
    private const string Crew2 = "- id: CREW_02";
    private const string Obj2 = "- id: OBJECTIVE_TEMPLATE_2";

    private static readonly Case[] All =
    [
        new("unknown key on gear", ContentKind.Gear, "gear/base.yaml", Gear2, b => b.Replace("tag: tools }", "tag: tools, typo: 1 }"), "GEAR_01", "GEAR_02", "typo"),
        new("misspelt id key", ContentKind.Gear, "gear/base.yaml", Gear2, b => b.Replace("id:", "idd:"), "GEAR_01", "entry 2", "has no id", HasId: false),
        new("blank entry", ContentKind.Gear, "gear/base.yaml", Gear2, _ => "-", "GEAR_01", "entry 2", "blank", HasId: false),
        new("tagz on a skill", ContentKind.Skill, "skills/base.yaml", "- id: SKILL_B", b => b + "\n  tagz: [x]", "SKILL_A", "SKILL_B", "tagz"),
        new("skils on crew", ContentKind.Crew, "crew/starting.yaml", Crew2, b => b.Replace("skills:", "skils:"), "CREW_01", "CREW_02", "skils"),
        new("offer key that is not a number", ContentKind.Assignment, "assignments/base.yaml", "- id: ASSIGNMENT_TEMPLATE_1", b => b + "\n- id: ASSIGNMENT_2\n  objectives: { tags: [] }\n  offer: { 1: 6, two: 3 }\n  loadoutLimit: 2", "ASSIGNMENT_TEMPLATE_1", "ASSIGNMENT_2", "offer"),
        new("approches on an encounter", ContentKind.Encounter, "encounters/base.yaml", Enc2, b => b.Replace("approaches:", "approches:"), "ENCOUNTER_01", "ENCOUNTER_02", "approches"),
        new("extra key in an approach", ContentKind.Encounter, "encounters/base.yaml", Enc2, b => b.Replace("difficulty: 5 }", "difficulty: 5, extra: 1 }"), "ENCOUNTER_01", "ENCOUNTER_02", "extra"),
        new("extra approach name", ContentKind.Encounter, "encounters/base.yaml", Enc2, b => b.Replace("    fast:", "    slow: { skill: SKILL_B, difficulty: 6 }\n    fast:"), "ENCOUNTER_01", "ENCOUNTER_02", "slow"),
        new("effect with an unknown key", ContentKind.Encounter, "encounters/base.yaml", Enc2, b => b.Replace("{ injuryRoll: light }]", "{ injuryRoll: light }, { bogus: 1 }]"), "ENCOUNTER_01", "ENCOUNTER_02", "bold[2]"),
        new("effect that is not a number", ContentKind.Encounter, "encounters/base.yaml", Enc2, b => b.Replace("{ progress: 1 }", "{ progress: lots }"), "ENCOUNTER_01", "ENCOUNTER_02", "progress"),
        new("stage with no slot", ContentKind.Objective, "objectives/base.yaml", Obj2, b => b.Replace("slot: approach, ", ""), "OBJECTIVE_TEMPLATE_1", "OBJECTIVE_TEMPLATE_2", "slot"),
        new("empty ifUnattended", ContentKind.Objective, "objectives/base.yaml", Obj2, b => b.Replace("{ difficulty: 5 }", "{}"), "OBJECTIVE_TEMPLATE_1", "OBJECTIVE_TEMPLATE_2", "ifUnattended"),
        new("unknown key on a stage", ContentKind.Objective, "objectives/base.yaml", Obj2, b => b.Replace("count: 1 }", "count: 1, bonus: 2 }"), "OBJECTIVE_TEMPLATE_1", "OBJECTIVE_TEMPLATE_2", "bonus"),
        new("seniority that is not a number", ContentKind.Crew, "crew/starting.yaml", Crew2, b => b.Replace("seniority: 4", "seniority: abc"), "CREW_01", "CREW_02", "seniority"),
        new("skill level that is not a number", ContentKind.Crew, "crew/starting.yaml", Crew2, b => b.Replace("SKILL_A: 4", "SKILL_A: high"), "CREW_01", "CREW_02", "SKILL_A"),
        new("decision with no id", ContentKind.Encounter, "encounters/base.yaml", Enc2, b => b.Replace("    id: DECISION_PROMPT_2\n", ""), "ENCOUNTER_01", "ENCOUNTER_02", "decision.id"),
        new("null tag in a tag list", ContentKind.Encounter, "encounters/base.yaml", Enc2, b => b.Replace("[field, social]", "[field, ~, social]"), "ENCOUNTER_01", "ENCOUNTER_02", "tags"),
        new("list inside a tag list", ContentKind.Encounter, "encounters/base.yaml", Enc2, b => b.Replace("[field, social]", "[field, [x], social]"), "ENCOUNTER_01", "ENCOUNTER_02", "tags"),
        new("unknown key whose value is zero", ContentKind.Gear, "gear/base.yaml", Gear2, b => b.Replace("tag: tools }", "tag: tools, typo: 0 }"), "GEAR_01", "GEAR_02", "typo"),
        new("unknown key whose value is null", ContentKind.Gear, "gear/base.yaml", Gear2, b => b.Replace("tag: tools }", "tag: tools, typo: }"), "GEAR_01", "GEAR_02", "typo"),
    ];

    public static TheoryData<Case> Cases()
    {
        var data = new TheoryData<Case>();
        foreach (Case c in All)
        {
            data.Add(c);
        }

        return data;
    }

    // Saving one item must never rewrite another item's text into something else.
    [Theory]
    [MemberData(nameof(Cases))]
    public void Upserting_another_item_refuses_a_file_it_cannot_keep(Case c)
    {
        ContentFileSet files = Broken(c);
        string original = files.Files[c.Path];
        ContentLossException e = Assert.Throws<ContentLossException>(() => ContentEdits.Upsert(files, c.Kind, ModelOf(files, c.Kind, c.Other)));
        Assert.Contains(c.Path + ": " + c.Broken + " ", e.Message);
        Assert.Contains(c.Fragment, e.Message);
        Assert.Contains(e.Losses, l => l.File == c.Path && l.Item == c.Broken);
        Assert.Equal(original, files.Files[c.Path]);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Deleting_another_item_and_canonicalizing_refuse_the_same_file(Case c)
    {
        ContentFileSet files = Broken(c);
        Assert.Throws<ContentLossException>(() => ContentEdits.Delete(files, c.Kind, c.Other));
        ContentLossException e = Assert.Throws<ContentLossException>(() => ContentEdits.Canonicalize(files));
        Assert.Contains(c.Path + ": " + c.Broken + " ", e.Message);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Losses_and_the_index_say_so_in_advance(Case c)
    {
        ContentFileSet files = Broken(c);
        IReadOnlyList<ContentLoss> losses = ContentEdits.Losses(files, c.Path);
        Assert.Contains(losses, l => l.Item == c.Broken && l.Problem.Contains(c.Fragment, StringComparison.Ordinal));
        Assert.All(losses, l => Assert.Equal(c.Broken, l.Item));
        Assert.Empty(ContentEdits.Losses(Repo(), c.Path));
        IReadOnlyList<ContentItemRef> index = files.Index();
        Assert.Equal(c.HasId, index.Any(i => i.Lossy));
        if (c.HasId)
        {
            Assert.Equal(c.Broken, Assert.Single(index, i => i.Lossy).Id);
        }
    }

    // The author may replace a broken item wholesale: that is how it gets fixed.
    [Theory]
    [MemberData(nameof(Cases))]
    public void The_broken_item_itself_can_be_replaced_or_deleted(Case c)
    {
        if (!c.HasId)
        {
            return;
        }

        ContentFileSet files = Broken(c);
        ContentFileSet replaced = ContentEdits.Upsert(files, c.Kind, ModelOf(files, c.Kind, c.Broken));
        Assert.Empty(ContentEdits.Losses(replaced, c.Path));
        Assert.DoesNotContain(replaced.Index(), i => i.Lossy);
        _ = replaced.Validate();

        ContentFileSet deleted = ContentEdits.Delete(files, c.Kind, c.Broken);
        Assert.Empty(ContentEdits.Losses(deleted, c.Path));
        Assert.DoesNotContain(deleted.Index(), i => i.Id == c.Broken);
    }

    // A key list that is wrong is not hidden by a plain file elsewhere: the repository's own files are faithful.
    [Fact]
    public void The_repositorys_files_have_no_losses()
    {
        ContentFileSet files = Repo();
        foreach (string path in files.Files.Keys)
        {
            Assert.Empty(ContentEdits.Losses(files, path));
        }

        Assert.DoesNotContain(files.Index(), i => i.Lossy);
    }

    // The things the canonical layout changes on purpose are not losses.
    [Fact]
    public void Deliberate_layout_equivalences_are_not_losses()
    {
        ContentFileSet repo = Repo();
        string crew = EditBlock(repo.Files["crew/starting.yaml"], "- id: CREW_02", b => b.Replace("  caution: -2\n", "").Replace("  discipline: -1\n", "  name: null\n  discipline: 0\n"));
        string objectives = EditBlock(repo.Files["objectives/base.yaml"], "- id: OBJECTIVE_TEMPLATE_1", b => b.Replace("teamSize: [2, 4]", "teamSize: [3, 3]").Replace("count: 1, skippable: true", "count: [1, 1], skippable: false"));
        string encounters = EditBlock(repo.Files["encounters/base.yaml"], "- id: ENCOUNTER_01", b => b.Replace("tags: [field, hazard]", "commsBlocked: false\n  tags: [field, hazard]").Replace("safe: []", "safe: []\n    name:"));
        string skills = repo.Files["skills/base.yaml"].Replace("- id: SKILL_A", "- id: SKILL_A\n  tags: []");
        string gear = repo.Files["gear/base.yaml"].Replace("{ id: GEAR_01, tag: scanner }", "{ tag: scanner, id: GEAR_01 }");
        ContentFileSet files = repo;
        foreach ((string path, string text) in new[] { ("crew/starting.yaml", crew), ("objectives/base.yaml", objectives), ("encounters/base.yaml", encounters), ("skills/base.yaml", skills), ("gear/base.yaml", gear) })
        {
            files = ContentEdits.ReplaceText(files, path, text);
            Assert.Empty(ContentEdits.Losses(files, path));
        }

        ContentFileSet canonical = ContentEdits.Canonicalize(files);
        Assert.Equal(JsonSerializer.Serialize(files.Validate().Pack, SimJson.Options), JsonSerializer.Serialize(canonical.Validate().Pack, SimJson.Options));
    }

    // Integers the loader reads in another notation are kept exactly as the loader reads them, or refused; never changed silently.
    [Theory]
    [InlineData("0x10")]
    [InlineData("1_0")]
    [InlineData("+5")]
    [InlineData("0b11")]
    [InlineData("0010")]
    [InlineData("\"5\"")]
    [InlineData("5.0")]
    [InlineData("1e1")]
    [InlineData("0o7")]
    public void An_integer_in_another_notation_keeps_its_value_or_is_refused(string seniority)
    {
        ContentFileSet repo = Repo();
        string text = EditBlock(repo.Files["crew/starting.yaml"], "- id: CREW_02", b => b.Replace("seniority: 4", "seniority: " + seniority));
        ContentFileSet files = ContentEdits.ReplaceText(repo, "crew/starting.yaml", text);
        string? before = files.Validate().Pack is { } p ? JsonSerializer.Serialize(p, SimJson.Options) : null;
        ContentFileSet edited;
        try
        {
            edited = ContentEdits.Upsert(files, ContentKind.Crew, ModelOf(files, ContentKind.Crew, "CREW_01"));
        }
        catch (ContentLossException)
        {
            return;
        }

        ContentLoadResult after = edited.Validate();
        Assert.Equal(before is null, after.Pack is null);
        if (before is not null)
        {
            Assert.Equal(before, JsonSerializer.Serialize(after.Pack, SimJson.Options));
        }
    }

    // A missing slot is never written back as an empty one.
    [Fact]
    public void A_stage_with_no_slot_is_not_given_one()
    {
        ContentFileSet files = Broken(All.Single(c => c.Name == "stage with no slot"));
        ContentFileSet edited = ContentEdits.Upsert(files, ContentKind.Objective, ((ObjectiveModel)ModelOf(files, ContentKind.Objective, "OBJECTIVE_TEMPLATE_2")) with { Stages = [new StageModel("approach", ["field"], 1, 1, false)] });
        Assert.DoesNotContain("slot: \"\"", edited.Files["objectives/base.yaml"]);
    }

    // CRLF files stay CRLF so a save is not a whole-file diff.
    [Fact]
    public void A_file_written_with_crlf_is_rewritten_with_crlf()
    {
        ContentFileSet repo = Repo();
        string crlf = repo.Files["gear/base.yaml"].Replace("\n", "\r\n");
        ContentFileSet files = ContentEdits.ReplaceText(repo, "gear/base.yaml", crlf);
        ContentFileSet edited = ContentEdits.Upsert(files, ContentKind.Gear, new GearModel("GEAR_01", "NAME_PLACEHOLDER", "scanner"));
        string text = edited.Files["gear/base.yaml"];
        Assert.DoesNotMatch("[^\r]\n", text);
        Assert.Contains("# Placeholders. The author replaces these.\r\n- id: GEAR_01\r\n  name: NAME_PLACEHOLDER\r\n", text);
        Assert.Empty(edited.Validate().Errors);
    }

    // The leading block ends at the first blank line; a comment after one is an inner comment and is dropped.
    [Fact]
    public void The_leading_comment_ends_at_the_first_blank_line()
    {
        Assert.Equal("# One.\n# Two.", ContentWriter.LeadingComment("\n\n# One.\n# Two.\n\n# Later.\n- id: X\n"));
        Assert.Null(ContentWriter.LeadingComment("- id: X\n# Later.\n"));
        ContentFileSet files = new(new Dictionary<string, string> { ["gear/base.yaml"] = "# One.\n\n# Later.\n- id: GEAR_01\n  tag: scanner\n" });
        Assert.Equal("# One.\n- id: GEAR_01\n  tag: scanner\n", ContentEdits.Canonicalize(files).Files["gear/base.yaml"]);
    }

    // A missing folder is an empty set; Validate then gives the loader's own errors.
    [Fact]
    public void A_missing_folder_reads_as_an_empty_set()
    {
        ContentFileSet files = ContentFileSet.ReadDirectory(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "no-such-content-" + System.IO.Path.GetRandomFileName()));
        Assert.Empty(files.Files);
        Assert.Contains(files.Validate().Errors, e => e.File == "pack.yaml");
    }

    [Fact]
    public void The_content_guard_notices_a_changed_file()
    {
        string root = Temp();
        try
        {
            File.WriteAllText(System.IO.Path.Combine(root, "a.yaml"), "x");
            var guard = new RepoContentGuard(root);
            guard.Dispose();
            File.WriteAllText(System.IO.Path.Combine(root, "a.yaml"), "y");
            Assert.Throws<InvalidOperationException>(guard.Dispose);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("gear/a\0b.yaml")]
    [InlineData("gear/\0.yaml")]
    public void Write_refuses_a_path_with_characters_a_file_name_cannot_hold(string path)
    {
        string root = Temp();
        try
        {
            var after = new ContentFileSet(new Dictionary<string, string> { ["pack.yaml"] = "id: CORE\nversion: 1\n", [path] = "- id: X\n" });
            Assert.Throws<ArgumentException>(() => ContentEdits.Write(root, new ContentFileSet(new Dictionary<string, string>()), after));
            Assert.Empty(Directory.EnumerateFileSystemEntries(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // Nothing is written through a link: not a linked folder on the way, not a linked file.
    [Fact]
    public void Write_refuses_to_write_through_a_symbolic_link()
    {
        string root = Temp();
        string outside = Temp();
        try
        {
            try
            {
                Directory.CreateSymbolicLink(System.IO.Path.Combine(root, "gear"), outside);
                File.CreateSymbolicLink(System.IO.Path.Combine(root, "pack.yaml"), System.IO.Path.Combine(outside, "target.yaml"));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }

            var before = new ContentFileSet(new Dictionary<string, string>());
            var viaFolder = new ContentFileSet(new Dictionary<string, string> { ["gear/base.yaml"] = "- id: X\n" });
            Assert.Throws<ArgumentException>(() => ContentEdits.Write(root, before, viaFolder));
            var viaFile = new ContentFileSet(new Dictionary<string, string> { ["pack.yaml"] = "id: CORE\nversion: 1\n" });
            Assert.Throws<ArgumentException>(() => ContentEdits.Write(root, before, viaFile));
            Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(outside, recursive: true);
        }
    }

    public sealed record Loss2(string Name, ContentKind Kind, string Path, string Marker, string Find, string Replace, string Other)
    {
        public override string ToString() => Name;
    }

    private static Loss2 Enc(string name, string find, string replace) => new(name, ContentKind.Encounter, "encounters/base.yaml", "- id: ENCOUNTER_02", find, replace, "ENCOUNTER_01");

    private static Loss2 Obj(string name, string find, string replace) => new(name, ContentKind.Objective, "objectives/base.yaml", "- id: OBJECTIVE_TEMPLATE_2", find, replace, "OBJECTIVE_TEMPLATE_1");

    // Each key is compared the way the loader reads that key: effect numbers, teamSize, and count as raw integers; withdraw as exactly "true".
    private static readonly Loss2[] Strict = BuildStrict();

    private static Loss2[] BuildStrict()
    {
        var list = new List<Loss2>();
        foreach (string v in new[] { "0x7FFFFFFF", "0b11", "-0x10", "+0x5", "1_000", "1__0", "5_", "0_1", "!!str 0x10" })
        {
            list.Add(Enc("progress " + v, "{ progress: 1 }", "{ progress: " + v + " }"));
            list.Add(Enc("momentum " + v, "{ strain: -1 }", "{ momentum: " + v + " }"));
            list.Add(Obj("teamSize scalar " + v, "teamSize: [2, 3]", "teamSize: " + v));
            list.Add(Obj("teamSize pair " + v, "teamSize: [2, 3]", "teamSize: [2, " + v + "]"));
            list.Add(Obj("count scalar " + v, "count: 1 }", "count: " + v + " }"));
            list.Add(Obj("count pair " + v, "count: [1, 2]", "count: [1, " + v + "]"));
        }

        foreach (string v in new[] { "True", "TRUE", "False", "!!bool true", "yes" })
        {
            list.Add(Enc("withdraw " + v, "{ strain: -1 }", "{ withdraw: " + v + " }"));
        }

        list.Add(Enc("tagged null", "{ progress: 1 }", "{ progress: 1 }, { injuryRoll: !!null x }"));
        list.Add(Enc("tagged string", "difficulty: 5 }", "difficulty: !!str 5 }"));
        list.Add(Enc("tagged flag", "tags: [field, social]", "commsBlocked: !!bool true\n  tags: [field, social]"));
        list.Add(Obj("tagged count", "count: 1 }", "count: !!int 1 }"));
        return [.. list];
    }

    public static TheoryData<Loss2> StrictCases()
    {
        var data = new TheoryData<Loss2>();
        foreach (Loss2 c in Strict)
        {
            data.Add(c);
        }

        return data;
    }

    private static ContentFileSet Mutated(Loss2 c)
    {
        ContentFileSet repo = Repo();
        return ContentEdits.ReplaceText(repo, c.Path, EditBlock(repo.Files[c.Path], c.Marker, b => b.Replace(c.Find, c.Replace)));
    }

    [Theory]
    [MemberData(nameof(StrictCases))]
    public void A_value_the_loader_reads_differently_is_refused_not_repaired(Loss2 c)
    {
        ContentFileSet files = Mutated(c);
        ContentLossException e = Assert.Throws<ContentLossException>(() => ContentEdits.Upsert(files, c.Kind, ModelOf(files, c.Kind, c.Other)));
        Assert.Contains(c.Path, e.Message);
    }

    // The loader reads these effect and count values with int.TryParse, so leading zeros and signs are plain decimals.
    [Theory]
    [InlineData("0010", "10")]
    [InlineData("+5", "5")]
    [InlineData("\"7\"", "7")]
    public void A_value_the_loader_parses_as_decimal_is_kept_as_that_value(string written, string kept)
    {
        ContentFileSet files = Mutated(Enc("momentum", "{ strain: -1 }", "{ momentum: " + written + " }"));
        ContentFileSet edited = ContentEdits.Upsert(files, ContentKind.Encounter, ModelOf(files, ContentKind.Encounter, "ENCOUNTER_01"));
        Assert.Contains("{ momentum: " + kept + " }", edited.Files["encounters/base.yaml"]);
        Assert.Equal(JsonSerializer.Serialize(files.Validate().Pack, SimJson.Options), JsonSerializer.Serialize(edited.Validate().Pack, SimJson.Options));
        ContentFileSet counts = Mutated(Obj("count", "count: [1, 2]", "count: [0001, 0003]"));
        ContentFileSet countsEdited = ContentEdits.Upsert(counts, ContentKind.Objective, ModelOf(counts, ContentKind.Objective, "OBJECTIVE_TEMPLATE_1"));
        Assert.Contains("count: [1, 3]", countsEdited.Files["objectives/base.yaml"]);
    }

    // A file with a second document would lose it on a rewrite.
    [Fact]
    public void A_file_with_a_second_document_is_a_file_level_loss()
    {
        ContentFileSet repo = Repo();
        ContentFileSet files = ContentEdits.ReplaceText(repo, "gear/base.yaml", repo.Files["gear/base.yaml"] + "---\n- id: GEAR_09\n  tag: other\n");
        ContentLoss loss = Assert.Single(ContentEdits.Losses(files, "gear/base.yaml"));
        Assert.Null(loss.Item);
        Assert.Contains("second document", loss.Problem);
        Assert.Throws<ContentLossException>(() => ContentEdits.Upsert(files, ContentKind.Gear, ModelOf(files, ContentKind.Gear, "GEAR_01")));
        Assert.DoesNotContain(files.Index(), i => i.Lossy);
    }

    // The loader reports a pair of nulls as it reports a non-number, instead of crashing.
    [Theory]
    [InlineData("teamSize: [2, 3]", "teamSize: [~, ~]", "teamSize must be")]
    [InlineData("count: [1, 2]", "count: [~, ~]", "count must be")]
    public void The_loader_reports_a_pair_of_nulls(string find, string replace, string message)
    {
        ContentFileSet repo = Repo();
        ContentFileSet files = ContentEdits.ReplaceText(repo, "objectives/base.yaml", repo.Files["objectives/base.yaml"].Replace(find, replace));
        ContentLoadResult result = files.Validate();
        Assert.Null(result.Pack);
        Assert.Contains(result.Errors, e => e.Message.Contains(message, StringComparison.Ordinal));
    }

    private static string Temp()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "content-faith-" + System.IO.Path.GetRandomFileName());
        Directory.CreateDirectory(path);
        return path;
    }
}
