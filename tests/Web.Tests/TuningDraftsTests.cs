using Microsoft.Extensions.DependencyInjection;
using Sim.Core;
using Sim.Core.Content;
using Sim.Missions;
using SimRunner;
using Web.Tuning;

namespace Web.Tests;

// The draft (1f part one, Task 7; design 4.2 and 4.3): held in memory, one per tuning file, judged by the loader with each error beside its
// field, refused when the file on disk changed under it, and saved in place through the editor, after which a mission in flight keeps
// its tuning and the next one takes the new.
public sealed class TuningDraftsTests : IDisposable
{
    private const string File = TuningDrafts.Missions;

    private readonly TestApp app = new(developerMode: true, scratch: true);

    private TuningDrafts Drafts => app.Services.GetRequiredService<TuningDrafts>();

    private string FilePath => Path.Combine(app.Editor.Folder, "tuning", "missions.yaml");

    public void Dispose()
    {
        app.Dispose();
    }

    [Fact]
    public void A_draft_that_does_not_load_gives_each_error_beside_its_field_and_no_tuning()
    {
        Drafts.Set(File, "check.assistDivisor", ["0"]);
        Drafts.Set(File, "check.target", ["seven"]);
        Drafts.Set(File, "compounding.collapseAt", ["9"]);
        Drafts.Set(File, "bench.teams.rookie.skill", ["3", "1"]);

        DraftLoad load = Drafts.Load(File);
        Assert.Null(load.Tuning);
        Assert.Null(load.Bench);
        Assert.Equal("check.assistDivisor must be a whole number of 1 or more, not '0'.", load.FieldErrors["check.assistDivisor"]);
        Assert.Equal("check.target must be a whole number, not 'seven'.", load.FieldErrors["check.target"]);
        Assert.Empty(load.OtherErrors);

        // Range errors come only once every value reads: each beside its field, a range's beside the range.
        Drafts.Set(File, "check.assistDivisor", ["2"]);
        Drafts.Set(File, "check.target", ["7"]);
        load = Drafts.Load(File);
        Assert.Null(load.Tuning);
        Assert.Equal("compounding.collapseAt must not be above compounding.cap.", load.FieldErrors["compounding.collapseAt"]);
        Assert.Equal("bench.teams.rookie.skill.0 must not be above bench.teams.rookie.skill.1.", load.FieldErrors["bench.teams.rookie.skill"]);
        Assert.Equal(2, load.FieldErrors.Count);
        Assert.Empty(load.OtherErrors);

        // An error naming a group of fields goes to the first field in it; a value that cannot be put in plain goes beside its field.
        Drafts.Discard(File);
        Drafts.Set(File, "check.tiers.success", ["9"]);
        Drafts.Set(File, "calls.base", ["3 # three"]);
        load = Drafts.Load(File);
        Assert.Null(load.Tuning);
        Assert.Equal(["calls.base"], load.FieldErrors.Keys);
        Assert.StartsWith("calls.base does not read back as '3 # three'", load.FieldErrors["calls.base"]);
        Drafts.Set(File, "calls.base", ["3"]);
        load = Drafts.Load(File);
        Assert.Equal("check.tiers must fall from critical to failure.", load.FieldErrors["check.tiers.critical"]);

        // A draft that loads gives its numbers.
        Drafts.Set(File, "check.tiers.success", ["1"]);
        load = Drafts.Load(File);
        Assert.Empty(load.FieldErrors);
        Assert.Equal(1, load.Tuning!.TierSuccess);
        Assert.NotNull(load.Bench);
    }

    [Fact]
    public void Discard_leaves_no_draft_and_the_saved_value_takes_a_path_out()
    {
        DateTimeOffset before = app.Game.Clock.GetUtcNow();
        var changes = new List<string>();
        Drafts.Changed += changes.Add;

        TuningDraft draft = Drafts.Set(File, "check.target", ["8"])!;
        Assert.InRange(draft.Began, before, app.Game.Clock.GetUtcNow());
        Assert.Equal(System.IO.File.ReadAllText(FilePath), draft.BaseText);
        Assert.Equal(["8"], draft.Values["check.target"]);
        Drafts.Set(File, "check.dice", ["3", "6"]);
        Assert.Equal(["3", "6"], Drafts.Current(File)!.Values["check.dice"]);
        Assert.Equal(draft.Began, Drafts.Current(File)!.Began);

        // Setting the saved value back takes the path out; the last one out ends the draft.
        Assert.Equal(["check.dice"], Drafts.Set(File, "check.target", ["7"])!.Values.Keys);
        Assert.Null(Drafts.Set(File, "check.dice", ["2", "6"]));
        Assert.Null(Drafts.Current(File));

        Drafts.Set(File, "check.target", ["8"]);
        Drafts.Discard(File);
        Assert.Null(Drafts.Current(File));
        Assert.Equal(7, Drafts.Load(File).Tuning!.CheckTarget);
        Assert.Equal(6, changes.Count);
        Assert.All(changes, c => Assert.Equal(File, c));

        // A path that is not a field, or the wrong count of values, is refused; so is a file a draft cannot be held for.
        Assert.Throws<ArgumentException>(() => Drafts.Set(File, "check.nothing", ["1"]));
        Assert.Throws<ArgumentException>(() => Drafts.Set(File, "check.dice", ["2"]));
        Assert.Throws<ArgumentException>(() => Drafts.Set("tuning/crew.yaml", "anything", ["1"]));
        Assert.Null(Drafts.Current(File));
    }

    [Fact]
    public void Two_lookups_see_one_draft()
    {
        TuningDrafts first = app.Services.GetRequiredService<TuningDrafts>();
        TuningDrafts second = app.Services.GetRequiredService<TuningDrafts>();
        first.Set(File, "check.gearBonus", ["3"]);

        Assert.Same(first.Current(File), second.Current(File));
        Assert.Equal(3, second.Load(File).Tuning!.GearBonus);
    }

    [Fact]
    public async Task An_edit_on_disk_after_the_draft_began_makes_it_stale_and_the_save_is_refused_naming_the_keys()
    {
        Drafts.Set(File, "check.target", ["8"]);
        Assert.False(Drafts.Stale(File, out IReadOnlyList<string> none));
        Assert.Empty(none);

        // A comment alone makes it stale, with no key changed.
        string text = System.IO.File.ReadAllText(FilePath);
        string commented = text.Replace("gearBonus: 2\n", "gearBonus: 2  # gear\n", StringComparison.Ordinal);
        Assert.NotEqual(text, commented);
        System.IO.File.WriteAllText(FilePath, commented);
        Assert.True(Drafts.Stale(File, out IReadOnlyList<string> keys));
        Assert.Empty(keys);

        string edited = commented.Replace("gearBonus: 2  # gear\n", "gearBonus: 3  # gear\n", StringComparison.Ordinal)
            .Replace("dice: { count: 2, sides: 6 }   # every injury", "dice: { count: 2, sides: 8 }   # every injury", StringComparison.Ordinal);
        System.IO.File.WriteAllText(FilePath, edited);
        Assert.True(Drafts.Stale(File, out keys));
        Assert.Equal(["check.gearBonus", "injury.dice"], keys);

        SaveResult result = await Drafts.Save(File, CancellationToken.None);
        Assert.False(result.Saved);
        Assert.Equal(edited, System.IO.File.ReadAllText(FilePath));
        (IReadOnlyDictionary<string, string> placed, IReadOnlyList<string> other) = TuningDrafts.Place(File, Drafts.Current(File)!.BaseText, result.Errors);
        Assert.Equal("check.gearBonus has changed in the file since this draft began.", placed["check.gearBonus"]);
        Assert.Equal("injury.dice has changed in the file since this draft began.", placed["injury.dice"]);
        Assert.Equal([$"{File} has changed since this draft began, so it is not saved. Discard the draft, or copy its values over by hand."], other);

        // The draft is kept, to be discarded or copied over by hand.
        Assert.Equal(["8"], Drafts.Current(File)!.Values["check.target"]);
    }

    [Fact]
    public async Task A_save_writes_only_the_changed_spans_and_the_live_pack_takes_the_new_numbers()
    {
        string text = System.IO.File.ReadAllText(FilePath);
        Drafts.Set(File, "check.target", ["12"]);
        Drafts.Set(File, "bench.teams.rookie.skill", ["2", "3"]);
        string? changed = null;
        Drafts.Changed += f => changed = f;

        SaveResult result = await Drafts.Save(File, CancellationToken.None);

        Assert.True(result.Saved, string.Join("; ", result.Errors.Select(e => e.Message)));
        string expected = text
            .Replace("  target: 7                  # added", "  target: 12                  # added", StringComparison.Ordinal)
            .Replace("rookie:  { size: 3, skill: [1, 3]", "rookie:  { size: 3, skill: [2, 3]", StringComparison.Ordinal);
        Assert.NotEqual(text, expected);
        Assert.Equal(expected, System.IO.File.ReadAllText(FilePath));
        Assert.Equal(12, app.Live.Pack.Tuning.CheckTarget);
        Assert.Equal(2, app.Live.Pack.Bench.Rookie.SkillMin);
        Assert.Null(Drafts.Current(File));
        Assert.Equal(File, changed);

        // With no draft there is nothing to save; a draft the whole pack refuses is refused with the loader's words, beside the field.
        Assert.False((await Drafts.Save(File, CancellationToken.None)).Saved);
        Drafts.Set(File, "check.assistDivisor", ["0"]);
        result = await Drafts.Save(File, CancellationToken.None);
        Assert.False(result.Saved);
        Assert.Equal(expected, System.IO.File.ReadAllText(FilePath));
        (IReadOnlyDictionary<string, string> placed, _) = TuningDrafts.Place(File, Drafts.Current(File)!.BaseText, result.Errors);
        Assert.Equal("check.assistDivisor must be a whole number of 1 or more, not '0'.", placed["check.assistDivisor"]);
    }

    [Fact]
    public async Task A_mission_in_flight_finishes_on_its_old_tuning_and_the_next_starts_on_the_new()
    {
        (Guid id, string missionId) = await WaitingCall();
        Assert.Equal(7, app.Live.Pack.Tuning.CheckTarget);

        Drafts.Set(File, "check.target", ["9"]);
        Assert.True((await Drafts.Save(File, CancellationToken.None)).Saved);
        LivePack live = app.Live;
        Assert.Equal(9, live.Pack.Tuning.CheckTarget);

        MissionTuning? next = await app.Sessions.Run(live, id, legacy =>
        {
            // The mission in flight answers its calls to the end on the tuning it started with.
            while (Mission(legacy.State) is { State.Pending: not null } mission && mission.Id == missionId)
            {
                Assert.Equal(7, mission.State.Tuning.CheckTarget);
                legacy.Send(AutoCaptain.NextCommand(live.Game, legacy.State, live.Pack));
            }

            Assert.Equal(7, Mission(legacy.State)!.State.Tuning.CheckTarget);

            // The next mission starts on the new.
            for (int steps = 0; steps < 400 && (Mission(legacy.State)?.Id ?? missionId) == missionId; steps++)
            {
                legacy.Send(AutoCaptain.NextCommand(live.Game, legacy.State, live.Pack));
            }

            return Mission(legacy.State)!.State.Tuning;
        });
        Assert.Equal(9, next!.CheckTarget);
    }

    private static MissionInFlight? Mission(LegacyState state)
    {
        return state.TryModule(MissionsModule.Name, out MissionsState? missions) ? missions.Site?.Mission : null;
    }

    // A legacy whose first mission waits on a call.
    private async Task<(Guid Id, string MissionId)> WaitingCall()
    {
        for (ulong seed = 1; seed < 200; seed++)
        {
            Guid id = await app.CreateAtOrders($"LEGACY_{seed}", seed);
            LivePack live = app.Live;
            LoadedLegacy? played = await app.Sessions.Run(live, id, legacy =>
            {
                while (legacy.State.Cycle == 1 && Mission(legacy.State) is null)
                {
                    legacy.Send(AutoCaptain.NextCommand(live.Game, legacy.State, live.Pack));
                }

                return legacy;
            });
            if (Mission(played!.State) is { State.Pending: not null } mission)
            {
                return (id, mission.Id);
            }
        }

        throw new InvalidOperationException("No seed to 200 left a call waiting.");
    }
}
