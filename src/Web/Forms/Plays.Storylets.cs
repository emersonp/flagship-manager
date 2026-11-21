using System.Globalization;
using Sim.Chronicle;
using Sim.Content;
using Sim.Core;
using Sim.Core.Content;
using Sim.Core.Narrative;
using Sim.Crew;
using Sim.Narrative;
using Sim.Storylets;
using Web.Bridge;

namespace Web.Forms;

// A storylet's "What plays here" (1e part six, design 4.7): the form as posted is built into a candidate pack, the loader judges it, and
// the storylet is cast on the chosen test ship with Preview.Storylet, which casts through Casting.Cast on the ship's IStateSource, the
// path the module casts by (review focus 3). The script, each option's words, and the chronicle line each would write are rendered as
// play renders them (Prose.Storylet and the chronicle module's own situation), with the cast's names and pronouns.
internal static partial class Plays
{
    public const string ShipField = "ship";

    public const string AnywayField = "anyway";

    // The id a new storylet is previewed under while its id box is blank.
    private const string Unnamed = "STORYLET_PREVIEW";

    private static PlaysView Storylet(KindForm form, ContentFileSet files, string id, FormValues values, FormChoices choices)
    {
        int sample = SampleOf(values);
        string storyletId = id.Trim().Length > 0 ? id.Trim() : Unnamed;
        List<Dictionary<string, string>> rows = FormRows.Read(values, StoryletParts.Options);

        // The candidate: the form's storylet in the pack, as a save would write it. When it does not load, the stored storylet on disk
        // is previewed in its place, greyed, and the panel says why.
        (ContentFileSet? candidate, string? stale) = Candidate(form, files, storyletId, values, choices);
        ContentFileSet? shown = candidate;
        if (candidate is null && files.Validate().Pack is { } disk && disk.Storylets.Any(s => s.Id == storyletId))
        {
            shown = files;
        }

        ContentPack? pack = shown?.Validate().Pack;
        StoryletDef? storylet = pack?.Storylets.FirstOrDefault(s => s.Id == storyletId);
        if (pack is null || storylet is null)
        {
            var empty = new PlaysCasting([], "", false, "", false, [], false, false, rows.Count, new HashSet<int>(), stale);
            return new PlaysView(sample, "", [], stale is null ? "The pack on disk does not load, so there is nothing to cast on. The boxes still save." : null) { Casting = empty };
        }

        // The ships, read against the pack previewed; with none, the starting crew alone, with Preview anyway on.
        IReadOnlyList<TestShipDef> ships = shown!.TestShips().Ships;
        bool noShips = ships.Count == 0;
        if (noShips)
        {
            ships = [new TestShipDef("", "The starting crew", 0, 1, [], [], new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<int>>>(), [], [])];
        }

        string asked = values.Get(ShipField).Trim();
        TestShipDef ship = ships.FirstOrDefault(s => s.Id == asked) ?? ships[0];
        bool anyway = noShips || values.Get(AnywayField) == "true";
        var source = new TestShipSource(pack, ship);
        PreviewCast cast = Preview.Storylet(pack, storyletId, source, source.Roster, sample, anyway);
        var names = new Names(pack);

        string top = string.Join(" · ", new[]
        {
            cast.RequiresHolds ? "Requires holds" : $"Requires fails: {cast.RequiresFailed}",
            cast.CastCooldownClear ? "cast cooldown clear" : "the cast cooldown would hold it back",
            cast.GlobalCooldownClear ? "global cooldown clear" : "the global cooldown would hold it back",
        });
        List<string> why = cast.Why.Select(w => w.Slot.Length == 0 ? w.Reason : $"{w.Slot}: {w.Reason}").ToList();
        string castWords = cast.Cast is not { } bound
            ? $"No one fits on {ship.Name}."
            : bound.Count == 0
                ? $"Sample {sample + 1}: for the ship, with no one cast."
                : $"{(cast.Unconditioned ? "Unconditioned" : $"Sample {sample + 1}")}: {string.Join(", ", bound.Select(c => $"{names.Crew(c.Value)} as {c.Key}"))}{(cast.Handler is { } h ? $"; {names.Crew(h)} would be sent on a check" : "")}.";

        // Each option row to its option in the pack previewed: by place in the candidate, by id in the stored storylet.
        StoryletOption? OptionOf(int row) => candidate is not null
            ? row < storylet.Options.Count ? storylet.Options[row] : null
            : storylet.Options.FirstOrDefault(o => o.Id == rows[row]["id"].Trim());

        var notOffered = new HashSet<int>();
        for (int i = 0; i < rows.Count; i++)
        {
            if (cast.Cast is not null && OptionOf(i) is { } option && !cast.OptionOffered.GetValueOrDefault(option.Id, true))
            {
                notOffered.Add(i);
            }
        }

        List<CrewMember> crew = Crew(pack, source);
        string key = string.Create(CultureInfo.InvariantCulture, $"plays|{storyletId}|{ship.Id}|{sample}");

        // One of the storylet's texts: what plays there with this cast, and, while its box is blank, the text of the line that does.
        PlaysSlot Text(string field, string label, string ownId, string? optionId, string? handler, string? outcome)
        {
            string elementId = "pv-" + FormValues.Slug(field);
            if (cast.Cast is not { } bound)
            {
                return new PlaysSlot(field, label, "", new PlaysPreview(elementId, null, "No one fits on this ship, so nothing is read. Preview anyway fills the slots to read the words.", null));
            }

            Situation situation = Prose.StoryletSituation(pack, names, crew, storyletId, bound, optionId, handler, outcome);
            BeatProse? prose = Prose.Storylet(pack, situation, $"{key}|{field}");
            bool written = OwnedText.Clean(values.Get(field)).Length > 0;
            string? winner = prose?.LineId;
            string note = prose is null ? (optionId is null ? "No line holds here, so the itinerary shows the systems line." : "No line holds here, so the log shows the systems line.")
                : winner == ownId ? "Its own words."
                : written ? $"Shared line {winner} wins with this cast: it tests as much or more."
                : $"Shared line {winner} plays here.";
            string placeholder = !written && winner is not null ? pack.Lines.FirstOrDefault(l => l.Id == winner)?.Text ?? "" : "";
            return new PlaysSlot(field, label, placeholder, new PlaysPreview(elementId, prose?.Entries, note, null));
        }

        // The chronicle entry an answer writes, as the chronicle module writes it: the option's own text, a line, or the fallback.
        PlaysSlot Chronicle(int row, StoryletOption option, string label, bool? success)
        {
            string field = StoryletParts.Options.Name(row, "chronicleText");
            string elementId = "pv-" + FormValues.Slug(field) + (success is { } s ? s ? "-success" : "-failure" : "");
            if (cast.Cast is not { } bound)
            {
                return new PlaysSlot(field, label, "", new PlaysPreview(elementId, null, "No one fits on this ship, so nothing is written.", null));
            }

            int weight = option.Chronicle?.Weight ?? storylet.Chronicle?.Weight ?? pack.Chronicle.Weights[ChronicleTuning.Storylet];
            Situation situation = ChronicleSamples.Storylet(pack, storyletId, bound, option.Id, option.Check is null ? null : cast.Handler, success);
            string chronicleKey = $"{key}|chronicle|{option.Id}|{success}";
            var narrator = new Narrator(pack);
            string? own = option.Chronicle?.Text is { } text ? narrator.Expand(text, situation, chronicleKey) : null;
            LineDef? line = own is null ? LineSelector.Select(pack.Lines, situation, new HashSet<string>(), chronicleKey) : null;
            string? said = own ?? (line is null ? null : narrator.Expand(line.Text, situation, chronicleKey));
            string placeholder = option.Chronicle?.Text is null && line is not null ? line.Text : "";
            if (weight <= 0)
            {
                return new PlaysSlot(field, label, placeholder, new PlaysPreview(elementId, null, "Weight 0: no chronicle entry is written.", null));
            }

            string words = said ?? ChronicleModule.StoryletFallback;
            string note = own is not null ? $"Its own words, at weight {weight}."
                : line is not null ? $"Shared line {line.Id}, at weight {weight}."
                : $"No line holds, so the chronicle says its fallback, at weight {weight}.";
            return new PlaysSlot(field, label, placeholder, new PlaysPreview(elementId, [new ProseEntry(EntryKind.Narration, null, [new LinePart(false, words)])], note, null));
        }

        var groups = new List<PlaysGroup>
        {
            new("script", "Arrival", "What the itinerary shows when it arrives.", true, [Text("script", "Script", $"{storyletId}.text", null, null, null)], [], [], [], false),
        };
        for (int i = 0; i < rows.Count; i++)
        {
            string title = StoryletParts.OptionTitle(rows[i], i);
            string Name(string field) => StoryletParts.Options.Name(i, field);
            if (OptionOf(i) is not { } option)
            {
                groups.Add(new PlaysGroup(OptionKey(i), title, "Not in the version previewed.", true, [], [], [], [], false));
                continue;
            }

            var slots = new List<PlaysSlot>();
            if (option.Check is null)
            {
                slots.Add(Text(Name("text"), "Its words", $"{storyletId}.{option.Id}.text", option.Id, null, null));
                slots.Add(Chronicle(i, option, "Chronicle", null));
            }
            else
            {
                foreach ((string outcome, string said) in new[] { ("success", "On a success"), ("failure", "On a failure") })
                {
                    slots.Add(Text(Name($"{outcome}.text"), said, $"{storyletId}.{option.Id}.{outcome}.text", option.Id, cast.Handler, outcome));
                }

                slots.Add(Chronicle(i, option, "Chronicle, on a success", true));
                slots.Add(Chronicle(i, option, "Chronicle, on a failure", false));
            }

            groups.Add(new PlaysGroup(OptionKey(i), title, notOffered.Contains(i) ? NotOfferedWords : null, true, slots, [], [], [], false));
        }

        List<PlaysShip> listed = noShips ? [] : ships.Select(s => new PlaysShip(s.Id, s.Name)).ToList();
        var casting = new PlaysCasting(listed, ship.Id, noShips, top, cast.Cast is not null && !cast.Unconditioned, why, anyway, cast.Unconditioned, rows.Count, notOffered, stale);
        return new PlaysView(sample, castWords, groups, null) { Casting = casting };
    }

    public const string NotOfferedWords = "Not offered with this cast.";

    public static string OptionKey(int row) => string.Create(CultureInfo.InvariantCulture, $"option-{row}");

    // The form's storylet built into the pack and judged by the loader, or null and what was said of it.
    private static (ContentFileSet? Files, string? Stale) Candidate(KindForm form, ContentFileSet files, string id, FormValues values, FormChoices choices)
    {
        Bound bound = form.Bind(id, values, choices);
        if (bound.Make is null)
        {
            string first = bound.Errors.Values.FirstOrDefault() ?? "a field is not complete";
            return (null, $"The form as it stands would not save ({first.TrimEnd('.')}), so this is the last version that loaded.");
        }

        ContentFileSet candidate;
        try
        {
            candidate = ContentEdits.Upsert(files, form.Kind, bound.Make(files));
        }
        catch (Exception e) when (e is ChangeRefusedException or InvalidOperationException or ArgumentException)
        {
            return (null, $"{e.Message.TrimEnd('.')}, so this is the last version that loaded.");
        }

        ContentLoadResult result = candidate.Validate();
        if (result.Pack is null)
        {
            string said = string.Join(" ", result.Errors.Take(2).Select(e => e.Message.TrimEnd('.') + "."));
            return (null, $"The loader refuses the form as it stands: {said} This is the last version that loaded.");
        }

        return (candidate, null);
    }

    // The ship's crew as play's storylet text reads them: the roster, fit and aboard as Active, each skill at the ship's level.
    private static List<CrewMember> Crew(ContentPack pack, TestShipSource source)
    {
        var crew = new List<CrewMember>();
        foreach (string id in source.Roster)
        {
            if (pack.Crew.FirstOrDefault(c => c.Id == id) is not { } def)
            {
                continue;
            }

            crew.Add(new CrewMember
            {
                Id = id,
                Seniority = def.Seniority,
                Skills = pack.Skills.ToDictionary(s => s.Id, s => new SkillState { Level = source.Skill(id, s.Id), Xp = 0 }),
                Caution = def.Caution,
                Discipline = def.Discipline,
                Status = source.IsFit(id) && source.IsAboard(id) ? CrewStatus.Active : CrewStatus.Recovering,
                LightInjuriesHealAtDay = [],
                ReturnsAtDay = null,
                Loyalty = source.Loyalty(id),
                AwayMissions = 0,
                Morale = source.Morale(id),
                LastSentCycle = null,
            });
        }

        return crew;
    }
}
