using System.Globalization;
using GameHost;
using Sim.Content;
using Sim.Core;
using Sim.Core.Content;
using Sim.Storylets;
using Web.Bridge;

namespace Web.Forms;

// Dev Play's "Save as test ship" (1e part six, Task 14; design 4.6): the legacy as the expressions read it now, written as a test
// ship. It is what IStateSource reads (design 7's minimum), read through the views the module's StateSource reads (the roster,
// the traits, the chronicle, and the storylets module's own records), at the legacy's cycle and day, so a ship's TestShipSource
// answers as LegacySource.Of does. Each time is days and cycles ago from the legacy's today. A crew member is listed only for what
// differs from how a test ship reads someone not listed (the starting file's skills, the tuning's starting morale and loyalty, fit
// and aboard). Morale history and an injury's days to heal are left out: no expression reads them. Capturing reads the legacy only.
internal static class TestShipCapture
{
    // When a name gives no id.
    private const string Fallback = "test_ship";

    // The ship, with no id yet: Save gives it one free under the lock.
    public static TestShipModel Capture(Game game, ContentPack pack, LegacyState state, string legacyName)
    {
        IRosterView roster = game.Bus.View<IRosterView>(state);
        ITraitView traitView = game.Bus.View<ITraitView>(state);
        IChronicleView chronicle = game.Bus.View<IChronicleView>(state);
        StoryletsState storylets = state.Module<StoryletsState>(StoryletsModule.Name);
        int cycle = state.Cycle;
        int day = state.Day;
        HashSet<string> starting = pack.Crew.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        List<string> living = roster.Roster.Where(starting.Contains).ToList();

        var crew = new List<TestShipCrewModel>();
        foreach (CrewDef member in pack.Crew)
        {
            if (!living.Contains(member.Id))
            {
                crew.Add(new TestShipCrewModel(member.Id, Alive: false));
                continue;
            }

            int morale = roster.Morale(member.Id);
            int loyalty = roster.Loyalty(member.Id);
            IReadOnlyDictionary<string, int> now = roster.Member(member.Id)?.Skills ?? new Dictionary<string, int>();
            Dictionary<string, int> skills = pack.Skills.Select(s => s.Id)
                .Where(s => now.GetValueOrDefault(s) != member.Skills.GetValueOrDefault(s))
                .ToDictionary(s => s, s => now.GetValueOrDefault(s), StringComparer.Ordinal);
            var change = new TestShipCrewModel(member.Id,
                morale == pack.CrewTuning.MoraleResting ? null : morale,
                loyalty == pack.CrewTuning.StartingLoyalty ? null : loyalty,
                skills.Count == 0 ? null : skills,
                roster.IsFit(member.Id) ? null : false);
            if (change != new TestShipCrewModel(member.Id))
            {
                crew.Add(change);
            }
        }

        // The ship's, then each living crew member's, pairs included, in the order each was first set.
        var traits = new List<TestShipTraitModel>();
        foreach (string? holder in new string?[] { null }.Concat(living))
        {
            foreach (TraitHeld held in traitView.Held(holder))
            {
                if (held.Value <= 0 || pack.Traits.FirstOrDefault(t => t.Id == held.Trait) is not { } def || (held.TargetId is { } target && !living.Contains(target)))
                {
                    continue;
                }

                string value = def.Levels.TryGetValue(held.Value, out string? level) ? level : held.Value.ToString(CultureInfo.InvariantCulture);
                traits.Add(new TestShipTraitModel(held.Trait, holder, held.TargetId, value, day - held.ChangedAtDay, cycle - held.ChangedAtCycle));
            }
        }

        // The history keeps whole cycles before this one, the last history.cycles of them: the longest window any expression may read.
        int keep = Math.Min(cycle, pack.HistoryCycles);
        var history = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<int>>>(StringComparer.Ordinal);
        foreach (string crewId in living)
        {
            var kinds = new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal);
            foreach (string kind in HistoryKinds.All)
            {
                List<int> ages = storylets.History
                    .Where(c => c.Crew.TryGetValue(crewId, out List<string>? had) && had.Contains(kind))
                    .Select(c => cycle - c.Cycle)
                    .Where(age => age >= 1 && age <= keep)
                    .Order()
                    .ToList();
                if (ages.Count > 0)
                {
                    kinds[kind] = ages;
                }
            }

            if (kinds.Count > 0)
            {
                history[crewId] = kinds;
            }
        }

        // Every entry up to today, since a count may read any number of days; a subject not in the starting crew is not one a slot can be.
        List<TestShipEntryModel> entries = chronicle.Entries
            .Where(e => e.Day <= day)
            .Select(e => new TestShipEntryModel(e.Kind, day - e.Day, e.Subjects.Select(s => s.Id).Where(starting.Contains).Distinct().ToList()))
            .ToList();

        HashSet<string> arrives = pack.Storylets.Select(s => s.Id).Concat(pack.Vignettes.Select(v => v.Id)).ToHashSet(StringComparer.Ordinal);
        List<TestShipArrivalModel> arrivals = storylets.Fired
            .Where(f => arrives.Contains(f.Id))
            .Select(f => new TestShipArrivalModel(f.Id, day - f.LastDay, cycle - f.Last))
            .ToList();

        string date = Dates.Short(pack.Calendar, day);
        string suffix = $" on {date}";
        string legacy = legacyName.Trim();
        legacy = legacy.Length + suffix.Length > 80 ? legacy[..(80 - suffix.Length)].TrimEnd() : legacy;
        string notes = string.Create(CultureInfo.InvariantCulture,
            $"Captured in Dev Play from the legacy {legacyName.Trim()}, cycle {cycle} ({state.Phase}), day {day}. Edit it as text; play never reads it.");
        return new TestShipModel("", legacy + suffix, day, cycle, crew, traits, history, entries, arrivals, notes);
    }

    // Saved: the id given and the file written (pack-relative); else the loader's words.
    public sealed record Saved(bool Done, string Id, string? File, IReadOnlyList<ContentError> Errors);

    // Appended through the editor's usual save: the whole pack built, the loader run (with every test ship, since a testships file
    // changes), then the one file written. The id is made from the name, and made free under the lock.
    public static async Task<Saved> Save(ContentEditor editor, TestShipModel ship, CancellationToken cancellation = default)
    {
        string id = "";
        string? file = null;
        SaveResult result = await editor.Save(files =>
        {
            id = FreeId(files, NewIds.FromName(ship.Name ?? ""));
            ContentFileSet after = ContentEdits.Upsert(files, ContentKind.TestShip, ship with { Id = id });
            file = after.Files.Keys.FirstOrDefault(k => !files.Files.TryGetValue(k, out string? old) || old != after.Files[k]);
            return after;
        }, cancellation);
        return new Saved(result.Saved, id, result.Saved ? file : null, result.Errors);
    }

    // The id, or with _2, _3, and on, the first no test ship holds.
    private static string FreeId(ContentFileSet files, string id)
    {
        id = id.Length == 0 ? Fallback : id;
        if (Checks.UsedBy(files, id, ContentKind.TestShip) is null)
        {
            return id;
        }

        for (int n = 2; ; n++)
        {
            string suffix = "_" + n.ToString(CultureInfo.InvariantCulture);
            string next = (id.Length + suffix.Length > Ids.MaxLength ? id[..(Ids.MaxLength - suffix.Length)].TrimEnd('_') : id) + suffix;
            if (Checks.UsedBy(files, next, ContentKind.TestShip) is null)
            {
                return next;
            }
        }
    }
}
