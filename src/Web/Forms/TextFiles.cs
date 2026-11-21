using Sim.Content;
using Sim.Core.Content;
using YamlDotNet.RepresentationModel;

namespace Web.Forms;

// The files edited as text: the pack file, the tuning files, and the calendar, listed on the index; and the files of the storylets,
// vignettes, traits, and test ships, reached from each kind's list ("Edit as text"), which keep their text view beside their forms
// (1e part six). The path comes from this list, never from a request.
// Link, when given, is a page the notes point to: the words of the notes it is drawn on, and its address.
public sealed record TextFile(string Name, string Path, string Title, IReadOnlyList<string> Notes, TextFileLink? Link = null)
{
    private const string InFlight = "A mission in flight keeps the tuning it started with.";

    public static readonly IReadOnlyList<TextFile> All =
    [
        new("pack", "pack.yaml", "Pack file", ["The pack's id cannot be changed here. Changing the version makes every existing legacy show a warning that its content has changed."]),
        // The Tuning page edits these numbers beside the bench (1f part one, Task 13); the crew's wait for its Legacy mode (part two).
        new("missions", "tuning/missions.yaml", "Mission tuning", ["Edit these numbers and re-run the bench on the Tuning page.", InFlight], new("the Tuning page", Web.Tuning.TuningQuery.Path)),
        new("crew-tuning", "tuning/crew.yaml", "Crew tuning", ["The Tuning page will measure these numbers in its Legacy mode (1f part two); edit them as text until then.", InFlight]),
        new("calendar-tuning", "tuning/calendar.yaml", "Calendar tuning", ["How many days a cycle can move the calendar.", "Saving checks this file with the rest of the pack."]),
        new("chronicle-tuning", "tuning/chronicle.yaml", "Chronicle tuning", ["How much each kind of chronicle entry weighs, and the least weight Notable shows.", "Saving checks this file with the rest of the pack."]),
        new("calendars", "calendars/base.yaml", "Calendars", ["The months, the weekdays, and the date a legacy starts on. pack.yaml names the calendar in use.", "Saving checks this file with the rest of the pack."]),
        new("storylets", "storylets/base.yaml", "Storylets", ["Each storylet also opens in its form, from the Storylets list.", "Saving checks this file with the rest of the pack."]),
        new("vignettes", "vignettes/base.yaml", "Vignettes", ["Each vignette also opens in its form, from the Vignettes list.", "Saving checks this file with the rest of the pack."]),
        new("traits", "traits/base.yaml", "Traits", ["Each trait also opens in its form, from the Traits list.", "Saving checks this file with the rest of the pack, and every use of a trait against its definition here."]),
        new("testships", "testships/base.yaml", "Test ships", ["Ships the editor casts storylets and vignettes on. Play never reads them.", "Saving checks every crew member, trait, and storylet named here against the rest of the pack."]),
    ];

    // Exact names only: anything else is not a text file.
    internal static TextFile? Find(string name)
    {
        return All.FirstOrDefault(f => f.Name == name);
    }

    public string Action => $"/dev/content/files/{Name}";
}

// A link drawn on the words Text where they first appear in a text file's notes.
public sealed record TextFileLink(string Text, string Href);

// What a text file page shows: the text as typed or as stored, what the save refused, and the notice. Warnings never block a save.
public sealed record TextFileView(TextFile File, string Text, IReadOnlyList<ContentError> Reasons, string? Notice, bool FocusAlert)
{
    public IReadOnlyList<string> Warnings { get; init; } = [];

    // Where each refused expression is in the text as typed (1e part four, design 4.12).
    public IReadOnlyDictionary<ContentError, TextSpot> Spots { get; init; } = new Dictionary<ContentError, TextSpot>();

    // A number that differs for each response, so the alert takes focus again on a second refusal in a row.
    private static long counter;

    private readonly long nonce = Interlocked.Increment(ref counter);

    public IReadOnlyDictionary<string, object> AlertFocus => FocusAlert
        ? new Dictionary<string, object> { ["data-init"] = $"el.focus(); void {nonce}" }
        : new Dictionary<string, object>();
}

// A trait an item requires above 0 that nothing in the pack sets, on the ship or on a crew member.
internal sealed record UnsetTrait(string Item, string Trait, bool OnShip);

// What each text file request does. A save goes through ContentEditor.Save like every other change.
internal static class TextFileFlow
{
    // A missing file opens empty; the first save creates it.
    public static TextFileView Open(TextFile file, ContentFileSet files, string? notice = null)
    {
        IReadOnlyList<string> warnings = file.Name is "storylets" or "vignettes" or "traits" && files.Validate().Pack is { } pack ? TraitWarnings(pack) : [];
        return new TextFileView(file, files.Files.GetValueOrDefault(file.Path, ""), [], notice, false) { Warnings = warnings };
    }

    // Traits that cannot work as written: one a storylet needs above 0 that nothing raises where it reads it, one an encounter
    // raises that no storylet requires, and one defined that nothing uses (1e part three). A change that can only lower a trait (add
    // below 0, set 0) does not count as setting it; a definition's launch rules and onTargetDied count, as they change crew's traits.
    internal static IReadOnlyList<string> TraitWarnings(ContentPack pack)
    {
        return TraitReckoning(pack, []);
    }

    // A storylet or vignette that requires a trait above 0 where nothing in the pack raises it: TraitWarnings' first kind, by item, for
    // the lint on the storylets list (1e part six, design 4.10). One reckoning serves both.
    internal static IReadOnlyList<UnsetTrait> Unset(ContentPack pack)
    {
        var unset = new List<UnsetTrait>();
        TraitReckoning(pack, unset);
        return unset;
    }

    private static List<string> TraitReckoning(ContentPack pack, List<UnsetTrait> unset)
    {
        // A trait is on the ship or on a crew member; the two are separate values, so each place is raised and read apart.
        var raised = new HashSet<(string Trait, bool Ship)>();
        foreach (EncounterDef encounter in pack.Encounters)
        {
            foreach (TraitChange change in Raises(encounter))
            {
                raised.Add((change.Trait, change.On == TraitOn.Ship));
            }
        }

        // A definition's rules change traits on crew: the holder, or a pair's target (1e part three).
        foreach (TraitDef def in pack.Traits)
        {
            IEnumerable<TraitRuleEffect> rules = (def.Launch is { } launch ? launch.Sent.Concat(launch.NotSent).Concat(launch.WithTarget) : []).Concat(def.OnTargetDied);
            foreach (TraitRuleEffect rule in rules.Where(r => r.ChangesTrait && r.Amount > 0))
            {
                raised.Add((rule.Trait ?? def.Id, false));
            }
        }

        // Storylets and vignettes alike (1e part two): each reads traits through its expressions (1e part four) and may raise them.
        var readers = pack.Storylets.Select(s => (s.Id, Cast: s.Cast.Count, s.Reads, s.Needs, Effects: s.Options.SelectMany(o => o.Outcomes.SelectMany(e => e)).ToList()))
            .Concat(pack.Vignettes.Select(v => (v.Id, Cast: v.Cast.Count, v.Reads, v.Needs, Effects: v.Effects.Concat(v.Options.SelectMany(o => o.Effects)).ToList())))
            .ToList();
        foreach ((string _, int cast, IReadOnlyList<TraitRead> _, IReadOnlyList<TraitRead> _, List<StoryletEffect> effects) in readers)
        {
            foreach (TraitChange change in effects.Select(e => e.Trait).OfType<TraitChange>().Where(f => f.Amount > 0))
            {
                // Empty means the first slot, or the ship with no cast; every slot is a crew member.
                raised.Add((change.Trait, change.On == StoryletScopes.Ship || (change.On.Length == 0 && cast == 0)));
            }
        }

        HashSet<string> read = readers.SelectMany(r => r.Reads).Select(r => r.Trait).ToHashSet(StringComparer.Ordinal);
        // A definition that gives a trait a bite reads it too (1e part three): a check bonus, launch rules, or onTargetDied.
        read.UnionWith(pack.Traits.Where(t => t.Mission is not null || t.Launch is not null || t.OnTargetDied.Count > 0).Select(t => t.Id));
        var warnings = new List<string>();
        foreach ((string id, _, _, IReadOnlyList<TraitRead> needs, _) in readers)
        {
            // An expression that holds with its trait at 0 needs nothing to set it.
            foreach ((string trait, bool onShip) in needs.Select(r => (r.Trait, r.Slot == StoryletScopes.Ship)).Distinct())
            {
                if (!raised.Contains((trait, onShip)))
                {
                    unset.Add(new UnsetTrait(id, trait, onShip));
                    warnings.Add($"Nothing sets {trait} on {(onShip ? "the ship" : "a crew member")}, which {id} requires.");
                }
            }
        }

        foreach (EncounterDef encounter in pack.Encounters)
        {
            warnings.AddRange(Raises(encounter).Select(c => c.Trait).Distinct().Where(f => !read.Contains(f)).Select(f => $"Nothing reads {f}, which {encounter.Id} sets."));
        }

        HashSet<string> used = Used(pack);
        warnings.AddRange(pack.Traits.Where(t => !used.Contains(t.Id)).Select(t => $"{t.Id} is defined and never used."));
        return warnings;
    }

    // Every trait any encounter, storylet, vignette, or another trait's rule names, whether it reads, raises, or lowers it.
    private static HashSet<string> Used(ContentPack pack)
    {
        IEnumerable<EffectDef> outcomes = pack.Encounters.SelectMany(e => e.Approaches.SelectMany(a => a.Outcomes.Values).Concat(e.Decision is { } d ? [d.Bold, d.Safe] : []));
        IEnumerable<StoryletEffect> answers = pack.Storylets.SelectMany(s => s.Options.SelectMany(o => o.Outcomes.SelectMany(e => e)))
            .Concat(pack.Vignettes.SelectMany(v => v.Effects.Concat(v.Options.SelectMany(o => o.Effects))));
        IEnumerable<string> rules = pack.Traits.SelectMany(t => (t.Launch is { } launch ? launch.Sent.Concat(launch.NotSent).Concat(launch.WithTarget) : []).Concat(t.OnTargetDied)
            .Where(r => r.ChangesTrait && r.Trait is not null && r.Trait != t.Id).Select(r => r.Trait!));
        return outcomes.SelectMany(e => e.Traits ?? []).Select(c => c.Trait)
            .Concat(answers.Select(e => e.Trait?.Trait).OfType<string>())
            .Concat(pack.Storylets.SelectMany(s => s.Reads).Concat(pack.Vignettes.SelectMany(v => v.Reads)).Select(r => r.Trait))
            .Concat(rules)
            .ToHashSet(StringComparer.Ordinal);
    }

    // Every trait change an encounter's approaches or decision raise.
    private static IEnumerable<TraitChange> Raises(EncounterDef encounter)
    {
        IEnumerable<EffectDef> effects = encounter.Approaches.SelectMany(a => a.Outcomes.Values);
        if (encounter.Decision is { } decision)
        {
            effects = effects.Append(decision.Bold).Append(decision.Safe);
        }

        return effects.SelectMany(e => e.Traits ?? []).Where(f => f.Amount > 0);
    }

    public static async Task<(TextFileView View, bool Saved)> Save(TextFile file, ContentEditor editor, string typed, CancellationToken cancellation)
    {
        SaveResult result = await editor.Save(files =>
        {
            string stored = files.Files.GetValueOrDefault(file.Path, "");
            string text = WithLineEndingsOf(stored, typed);
            if (file.Name == "pack")
            {
                CheckPackId(stored, text);
            }

            return ContentEdits.ReplaceText(files, file.Path, text);
        }, cancellation);
        if (!result.Saved)
        {
            return (new TextFileView(file, typed, result.Errors, null, true) { Spots = ExpressionSpots.Find(file, typed, result.Errors) }, false);
        }

        return (Open(file, editor.Read(), "Saved."), true);
    }

    // A browser posts a text area's line breaks as CRLF. The file keeps the ending it has (LF unless it already holds CRLF), so a save
    // changes only what was typed and is never a whole-file diff. Nothing else is touched: a trailing line break stays as typed.
    internal static string WithLineEndingsOf(string stored, string typed)
    {
        string lf = typed.Replace("\r\n", "\n", StringComparison.Ordinal);
        return stored.Contains("\r\n", StringComparison.Ordinal) ? lf.Replace("\n", "\r\n", StringComparison.Ordinal) : lf;
    }

    private static void CheckPackId(string stored, string text)
    {
        if (ManifestId(stored) is { } was && ManifestId(text) is { } now && was != now)
        {
            throw new ChangeRefusedException($"The pack's id cannot be changed here. It is {was}.");
        }
    }

    // The id in a pack file; null when the text does not read as a mapping with a plain id (the loader says what is wrong).
    private static string? ManifestId(string text)
    {
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(text));
            if (stream.Documents.Count > 0 && stream.Documents[0].RootNode is YamlMappingNode map)
            {
                foreach ((YamlNode key, YamlNode value) in map.Children)
                {
                    if (key is YamlScalarNode { Value: "id" } && value is YamlScalarNode { Value: { Length: > 0 } id })
                    {
                        return id;
                    }
                }
            }
        }
        catch (YamlDotNet.Core.YamlException)
        {
        }

        return null;
    }
}
