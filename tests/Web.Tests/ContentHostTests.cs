using Sim.Content;
using Sim.Core.Content;
using Sim.Crew;
using Sim.Missions;
using Persistence;

namespace Web.Tests;

// A temporary copy of the repository's content, so a test that saves never touches content/.
internal sealed class ScratchContent : IDisposable
{
    public ScratchContent()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"starship-sim-scratch-{Guid.NewGuid():N}");
        string source = System.IO.Path.Combine(TestApp.RepositoryRoot(), "content");
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = System.IO.Path.Combine(Path, System.IO.Path.GetRelativePath(source, file));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    public string Path { get; }

    public ContentFolder Folder => new(Path);

    public ContentHost NewHost()
    {
        return new ContentHost(ContentLoader.LoadDirectory(Path).Pack);
    }

    public void Dispose()
    {
        Directory.Delete(Path, true);
    }
}

public sealed class ContentHostTests : IClassFixture<RepoContentGuard>, IDisposable
{
    private readonly ScratchContent scratch = new();

    public void Dispose()
    {
        scratch.Dispose();
    }

    private static ContentFileSet Rename(ContentFileSet files, string crewId, string name)
    {
        CrewModel crew = ContentEdits.Get<CrewModel>(files, ContentKind.Crew, crewId)!;
        return ContentEdits.Upsert(files, ContentKind.Crew, crew with { Name = name });
    }

    private static IReadOnlyList<string> Changed(ContentFileSet before, ContentFileSet after)
    {
        return after.Files.Where(f => !before.Files.TryGetValue(f.Key, out string? old) || old != f.Value).Select(f => f.Key).ToList();
    }

    // The pack is swapped whole: a snapshot taken before keeps its own pack, game, and names.
    [Fact]
    public void A_snapshot_taken_before_a_swap_keeps_the_old_pack_game_and_names()
    {
        ContentPack pack = BridgePack.Load();
        var host = new ContentHost(pack);
        LivePack before = host.Current!;

        host.Swap(NamedPack.Of(pack));
        LivePack after = host.Current!;

        Assert.Equal(1, before.Generation);
        Assert.Equal(2, after.Generation);
        Assert.Equal("CREW_01", before.Names.Crew("CREW_01"));
        Assert.Equal(NamedPack.Prefix + "CREW_01", after.Names.Crew("CREW_01"));
        Assert.Null(before.Pack.Crew[0].Name);
        Assert.NotSame(before.Game, after.Game);
    }

    [Fact]
    public async Task A_valid_save_writes_exactly_one_file_and_swaps_the_pack()
    {
        ContentHost host = scratch.NewHost();
        var editor = new ContentEditor(host, scratch.Folder);
        ContentFileSet before = editor.Read();
        LivePack old = host.Current!;

        SaveResult result = await editor.Save(files => Rename(files, "CREW_01", "NAME_PLACEHOLDER"));

        Assert.True(result.Saved);
        Assert.Empty(result.Errors);
        Assert.Equal(["crew/starting.yaml"], Changed(before, editor.Read()));
        Assert.NotSame(old, host.Current);
        Assert.Equal("NAME_PLACEHOLDER", host.Current!.Names.Crew("CREW_01"));
        Assert.Equal("CREW_01", old.Names.Crew("CREW_01"));
    }

    // A save is one file: a change that would alter two is refused whole.
    [Fact]
    public async Task A_change_that_alters_more_than_one_file_is_refused_and_writes_nothing()
    {
        ContentHost host = scratch.NewHost();
        var editor = new ContentEditor(host, scratch.Folder);
        ContentFileSet before = editor.Read();
        LivePack old = host.Current!;

        SaveResult result = await editor.Save(files => ContentEdits.Upsert(Rename(files, "CREW_01", "NAME_PLACEHOLDER"), ContentKind.Gear, ContentEdits.Get<GearModel>(files, ContentKind.Gear, "GEAR_01")! with { Name = "NAME_PLACEHOLDER" }));

        Assert.False(result.Saved);
        Assert.Contains(result.Errors, e => e.Message.Contains("A save changes one file", StringComparison.Ordinal) && e.Message.Contains('2'));
        Assert.Empty(Changed(before, editor.Read()));
        Assert.Same(old, host.Current);
    }

    // A skill some crew have, written into the scratch copy before the host starts.
    private void AddSkillE()
    {
        File.AppendAllText(System.IO.Path.Combine(scratch.Path, "skills", "base.yaml"), "- id: SKILL_E\n");
        string crew = System.IO.Path.Combine(scratch.Path, "crew", "starting.yaml");
        File.WriteAllText(crew, File.ReadAllText(crew).Replace("SKILL_D: 3 }", "SKILL_D: 3, SKILL_E: 1 }", StringComparison.Ordinal));
    }

    // The one declared change across files writes crew first, then the skill file, each step a pack that loads.
    [Fact]
    public async Task A_declared_change_across_files_writes_the_crew_file_before_the_skill_file()
    {
        AddSkillE();
        ContentHost host = scratch.NewHost();
        var written = new List<string>();
        var editor = new ContentEditor(host, scratch.Folder, (root, before, after) =>
        {
            written.AddRange(Changed(before, after));
            ContentEdits.Write(root, before, after);
        });

        SaveResult result = await editor.SaveAcrossFiles(files => ContentEdits.DeleteSkill(files, "SKILL_E"), ContentEditor.CrewFirst);

        Assert.True(result.Saved);
        Assert.Equal(["crew/starting.yaml", "skills/base.yaml"], written);
        Assert.DoesNotContain(host.Current!.Pack.Skills, s => s.Id == "SKILL_E");
    }

    // A write that stops after the crew file leaves a pack on disk that still loads.
    [Fact]
    public async Task A_declared_change_that_stops_after_the_first_file_leaves_a_valid_pack_and_swaps_nothing()
    {
        AddSkillE();
        ContentHost host = scratch.NewHost();
        LivePack old = host.Current!;
        var editor = new ContentEditor(host, scratch.Folder, (root, before, after) =>
        {
            IReadOnlyList<string> changed = Changed(before, after);
            if (changed.Contains("skills/base.yaml"))
            {
                throw new IOException("STOPPED_PLACEHOLDER");
            }

            ContentEdits.Write(root, before, after);
        });

        SaveResult result = await editor.SaveAcrossFiles(files => ContentEdits.DeleteSkill(files, "SKILL_E"), ContentEditor.CrewFirst);

        Assert.False(result.Saved);
        Assert.Same(old, host.Current);
        ContentLoadResult disk = editor.Read().Validate();
        Assert.NotNull(disk.Pack);
        Assert.Contains(disk.Pack.Skills, s => s.Id == "SKILL_E");
        Assert.All(disk.Pack.Crew, c => Assert.False(c.Skills.ContainsKey("SKILL_E")));
    }

    // The declared save still validates the whole candidate first: a skill an encounter uses is refused, and neither file is written.
    [Fact]
    public async Task A_declared_change_the_loader_refuses_writes_neither_file()
    {
        ContentHost host = scratch.NewHost();
        var editor = new ContentEditor(host, scratch.Folder);
        ContentFileSet before = editor.Read();

        SaveResult result = await editor.SaveAcrossFiles(files => ContentEdits.DeleteSkill(files, "SKILL_A"), ContentEditor.CrewFirst);

        Assert.False(result.Saved);
        Assert.Contains(result.Errors, e => e.Message.Contains("SKILL_A", StringComparison.Ordinal));
        Assert.Empty(Changed(before, editor.Read()));
    }

    // Any exception from the change is a refusal in its words, never a 500, and nothing is written.
    [Fact]
    public async Task A_change_that_throws_is_refused_with_its_reason_and_writes_nothing()
    {
        ContentHost host = scratch.NewHost();
        var editor = new ContentEditor(host, scratch.Folder);
        ContentFileSet before = editor.Read();

        SaveResult result = await editor.Save(_ => throw new InvalidOperationException("REASON_PLACEHOLDER"));

        Assert.False(result.Saved);
        Assert.Equal("REASON_PLACEHOLDER", Assert.Single(result.Errors).Message);
        Assert.Empty(Changed(before, editor.Read()));
    }

    [Fact]
    public async Task An_invalid_save_writes_nothing_returns_the_loaders_errors_and_leaves_the_live_pack()
    {
        ContentHost host = scratch.NewHost();
        var editor = new ContentEditor(host, scratch.Folder);
        ContentFileSet before = editor.Read();
        LivePack old = host.Current!;

        SaveResult result = await editor.Save(files =>
        {
            CrewModel crew = ContentEdits.Get<CrewModel>(files, ContentKind.Crew, "CREW_01")!;
            return ContentEdits.Upsert(files, ContentKind.Crew, crew with { Skills = new Dictionary<string, int> { ["SKILL_NOT_THERE"] = 3 } });
        });

        Assert.False(result.Saved);
        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Message.Contains("SKILL_NOT_THERE", StringComparison.Ordinal));
        Assert.Empty(Changed(before, editor.Read()));
        Assert.Same(old, host.Current);
    }

    // A file the editor cannot keep faithfully is refused as a result, not thrown.
    [Fact]
    public async Task A_change_the_editor_cannot_keep_faithfully_is_refused_with_the_losses_and_writes_nothing()
    {
        string path = Path.Combine(scratch.Path, "gear", "base.yaml");
        File.WriteAllText(path, File.ReadAllText(path).Replace("tag: tools", "tag: tools, typo: 1", StringComparison.Ordinal));
        ContentHost host = scratch.NewHost();
        var editor = new ContentEditor(host, scratch.Folder);
        ContentFileSet before = editor.Read();

        SaveResult result = await editor.Save(files => ContentEdits.Upsert(files, ContentKind.Gear, ContentEdits.Get<GearModel>(files, ContentKind.Gear, "GEAR_01")! with { Name = "NAME_PLACEHOLDER" }));

        Assert.False(result.Saved);
        Assert.Contains(result.Errors, e => e.Message.Contains("typo", StringComparison.Ordinal));
        Assert.Empty(Changed(before, editor.Read()));
    }

    // The loader throwing on a candidate refuses it with the reason.
    [Fact]
    public async Task A_loader_crash_on_the_candidate_is_a_refusal_with_the_exceptions_message()
    {
        ContentHost host = scratch.NewHost();
        var editor = new ContentEditor(host, scratch.Folder);
        LivePack old = host.Current!;

        SaveResult result = await editor.Save(files => new ContentFileSet(new Dictionary<string, string>(files.Files) { ["pack.yaml"] = null! }));

        Assert.False(result.Saved);
        Assert.Single(result.Errors);
        Assert.NotEmpty(result.Errors[0].Message);
        Assert.Same(old, host.Current);
    }

    [Fact]
    public async Task Two_concurrent_saves_apply_in_turn_and_neither_is_lost()
    {
        ContentHost host = scratch.NewHost();
        var editor = new ContentEditor(host, scratch.Folder);
        var entered = new TaskCompletionSource();
        var release = new TaskCompletionSource();

        Task<SaveResult> first = Task.Run(() => editor.Save(files =>
        {
            entered.SetResult();
            release.Task.Wait();
            return Rename(files, "CREW_01", "NAME_PLACEHOLDER_A");
        }));
        await entered.Task;
        Task<SaveResult> second = Task.Run(() => editor.Save(files => Rename(files, "CREW_02", "NAME_PLACEHOLDER_B")));
        await Task.Delay(200);
        Assert.False(second.IsCompleted, "The second save waits for the first.");
        release.SetResult();
        SaveResult[] results = await Task.WhenAll(first, second);

        Assert.All(results, r => Assert.True(r.Saved));
        LivePack live = host.Current!;
        Assert.Equal("NAME_PLACEHOLDER_A", live.Names.Crew("CREW_01"));
        Assert.Equal("NAME_PLACEHOLDER_B", live.Names.Crew("CREW_02"));
        Assert.Equal(3, live.Generation);
        ContentFileSet disk = editor.Read();
        Assert.Equal("NAME_PLACEHOLDER_A", ContentEdits.Get<CrewModel>(disk, ContentKind.Crew, "CREW_01")!.Name);
        Assert.Equal("NAME_PLACEHOLDER_B", ContentEdits.Get<CrewModel>(disk, ContentKind.Crew, "CREW_02")!.Name);
    }

    [Fact]
    public async Task A_write_that_fails_swaps_nothing_and_changes_no_file()
    {
        ContentHost host = scratch.NewHost();
        var editor = new ContentEditor(host, scratch.Folder, (_, _, _) => throw new IOException("DISK_FULL_PLACEHOLDER"));
        ContentFileSet before = editor.Read();
        LivePack old = host.Current!;

        SaveResult result = await editor.Save(files => Rename(files, "CREW_01", "NAME_PLACEHOLDER"));

        Assert.False(result.Saved);
        Assert.Contains(result.Errors, e => e.Message.Contains("DISK_FULL_PLACEHOLDER", StringComparison.Ordinal));
        Assert.Same(old, host.Current);
        Assert.Empty(Changed(before, editor.Read()));
    }

    // The disk is the truth: a hand edit made between saves is read, kept, and swapped in with the next save.
    [Fact]
    public async Task A_save_reads_the_disk_fresh_and_keeps_a_hand_edit()
    {
        ContentHost host = scratch.NewHost();
        var editor = new ContentEditor(host, scratch.Folder);
        await editor.Save(files => Rename(files, "CREW_01", "NAME_PLACEHOLDER_A"));
        string path = Path.Combine(scratch.Path, "crew", "starting.yaml");
        File.WriteAllText(path, File.ReadAllText(path).Replace("NAME_PLACEHOLDER_A", "NAME_PLACEHOLDER_HAND", StringComparison.Ordinal));

        SaveResult result = await editor.Save(files => Rename(files, "CREW_02", "NAME_PLACEHOLDER_B"));

        Assert.True(result.Saved);
        Assert.Equal("NAME_PLACEHOLDER_HAND", host.Current!.Names.Crew("CREW_01"));
        Assert.Equal("NAME_PLACEHOLDER_B", host.Current.Names.Crew("CREW_02"));
    }

    [Fact]
    public async Task Reload_swaps_a_hand_edit_without_writing_and_refuses_an_invalid_disk()
    {
        ContentHost host = scratch.NewHost();
        var editor = new ContentEditor(host, scratch.Folder);
        string path = Path.Combine(scratch.Path, "crew", "starting.yaml");
        string original = File.ReadAllText(path);
        File.WriteAllText(path, original.Replace("- id: CREW_01\n", "- id: CREW_01\n  name: NAME_PLACEHOLDER_HAND\n", StringComparison.Ordinal));

        SaveResult reloaded = await editor.Reload();

        Assert.True(reloaded.Saved);
        Assert.Equal("NAME_PLACEHOLDER_HAND", host.Current!.Names.Crew("CREW_01"));
        LivePack kept = host.Current;
        File.WriteAllText(path, "not: [valid");

        SaveResult refused = await editor.Reload();

        Assert.False(refused.Saved);
        Assert.NotEmpty(refused.Errors);
        Assert.Same(kept, host.Current);
        Assert.Equal(refused.Errors, host.LoadErrors);
    }

    // Reading Current twice in a request could mix two packs; the session takes the snapshot it is given.
    [Fact]
    public async Task A_session_run_uses_the_snapshot_it_was_given_even_if_the_pack_is_swapped_inside_the_run()
    {
        ContentHost host = scratch.NewHost();
        string db = Path.Combine(scratch.Path, "..", $"starship-sim-session-{Guid.NewGuid():N}.db");
        try
        {
            var services = new GameServices(new SqliteLegacyStore($"Data Source={db}"), TimeProvider.System);
            var sessions = new LegacySession(services);
            LivePack snapshot = host.Current!;
            Guid id = await sessions.CreateLegacy(snapshot, "LEGACY", 1);

            LoadedLegacy? legacy = await sessions.Run(snapshot, id, loaded =>
            {
                host.Swap(NamedPack.Of(snapshot.Pack));
                loaded.Send(new Sim.Core.AdvancePhase());
                return loaded;
            });

            Assert.NotNull(legacy);
            Assert.Equal(2, host.Current!.Generation);
            Assert.Equal(1, snapshot.Generation);
            Assert.Equal(snapshot.Pack.Version, legacy.Record.ContentPackVersion);
            Assert.NotEmpty(services.Store.ReadEvents(id));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(db);
        }
    }
}
