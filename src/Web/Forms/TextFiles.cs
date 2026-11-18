using Sim.Content;
using Sim.Core.Content;
using YamlDotNet.RepresentationModel;

namespace Web.Forms;

// The files edited as text: the pack file, the two tuning files, and the storylets, vignettes, and traits until their forms exist. The path comes from this
// list, never from a request.
public sealed record TextFile(string Name, string Path, string Title, IReadOnlyList<string> Notes)
{
    private const string Tuning = "The page that edits these numbers and re-runs the bench comes later. A mission in flight keeps the tuning it started with.";

    public static readonly IReadOnlyList<TextFile> All =
    [
        new("pack", "pack.yaml", "Pack file", ["The pack's id cannot be changed here. Changing the version makes every existing legacy show a warning that its content has changed."]),
        new("missions", "tuning/missions.yaml", "Mission tuning", [Tuning]),
        new("crew-tuning", "tuning/crew.yaml", "Crew tuning", [Tuning]),
        new("storylets", "storylets/base.yaml", "Storylets", ["The storylet form comes later.", "Saving checks this file with the rest of the pack."]),
        new("vignettes", "vignettes/base.yaml", "Vignettes", ["The vignette form comes later.", "Saving checks this file with the rest of the pack."]),
        new("traits", "traits/base.yaml", "Traits", ["The trait form comes later.", "Saving checks this file with the rest of the pack, and every use of a trait against its definition here."]),
    ];

    // Exact names only: anything else is not a text file.
    internal static TextFile? Find(string name)
    {
        return All.FirstOrDefault(f => f.Name == name);
    }

    public string Action => $"/dev/content/files/{Name}";
}

// What a text file page shows: the text as typed or as stored, what the save refused, and the notice. Warnings never block a save.
public sealed record TextFileView(TextFile File, string Text, IReadOnlyList<ContentError> Reasons, string? Notice, bool FocusAlert)
{
    public IReadOnlyList<string> Warnings { get; init; } = [];

    // A number that differs for each response, so the alert takes focus again on a second refusal in a row.
    private static long counter;

    private readonly long nonce = Interlocked.Increment(ref counter);

    public IReadOnlyDictionary<string, object> AlertFocus => FocusAlert
        ? new Dictionary<string, object> { ["data-init"] = $"el.focus(); void {nonce}" }
        : new Dictionary<string, object>();
}

// What each text file request does. A save goes through ContentEditor.Save like every other change.
internal static class TextFileFlow
{
    // A missing file opens empty; the first save creates it.
    public static TextFileView Open(TextFile file, ContentFileSet files, string? notice = null)
    {
        IReadOnlyList<string> warnings = file.Name is "storylets" or "vignettes" or "traits" && files.Validate().Pack is { } pack ? TraitWarnings(pack) : [];
        return new TextFileView(file, files.Files.GetValueOrDefault(file.Path, ""), [], notice, false) { Warnings = warnings };
    }

    // Traits that cannot work as written: one a storylet requires above 0 that nothing raises on the scope it reads, one an encounter
    // raises that no storylet requires, and one defined that nothing uses (1e part three). A change that can only lower a trait (add
    // below 0, set 0) does not count as setting it; a definition's launch rules and onTargetDied count, as they change crew's traits.
    internal static IReadOnlyList<string> TraitWarnings(ContentPack pack)
    {
        // A trait is on the ship or on a crew member; the two are separate values, so each scope is raised and read apart.
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

        // Storylets and vignettes alike (1e part two): each reads traits on its scope and may raise them.
        var readers = pack.Storylets.Select(s => (s.Id, s.Scope, Traits: s.Traits.ToList(), Effects: s.Options.SelectMany(o => o.Effects).ToList()))
            .Concat(pack.Vignettes.Select(v => (v.Id, v.Scope, Traits: v.Requires.OfType<TraitRange>().ToList(), Effects: v.Effects.Concat(v.Options.SelectMany(o => o.Effects)).ToList())))
            .ToList();
        foreach ((string _, string scope, List<TraitRange> _, List<StoryletEffect> effects) in readers)
        {
            foreach (TraitChange change in effects.Select(e => e.Trait).OfType<TraitChange>().Where(f => f.Amount > 0))
            {
                // Empty means the scope it arrived for; subject is always a crew member.
                raised.Add((change.Trait, change.On == StoryletScopes.Ship || (change.On.Length == 0 && scope == StoryletScopes.Ship)));
            }
        }

        HashSet<string> read = readers.SelectMany(r => r.Traits).Select(r => r.Trait).ToHashSet(StringComparer.Ordinal);
        // A definition that gives a trait a bite reads it too (1e part three): a check bonus, launch rules, or onTargetDied.
        read.UnionWith(pack.Traits.Where(t => t.Mission is not null || t.Launch is not null || t.OnTargetDied.Count > 0).Select(t => t.Id));
        var warnings = new List<string>();
        foreach ((string id, string scope, List<TraitRange> traits, _) in readers)
        {
            // A requirement that holds at 0 (only a max) needs nothing to set its trait.
            bool onShip = scope == StoryletScopes.Ship;
            string where = onShip ? "the ship" : "a crew member";
            warnings.AddRange(traits.Where(r => r.Min > 0).Select(r => r.Trait).Distinct().Where(f => !raised.Contains((f, onShip))).Select(f => $"Nothing sets {f} on {where}, which {id} requires."));
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
        IEnumerable<StoryletEffect> answers = pack.Storylets.SelectMany(s => s.Options.SelectMany(o => o.Effects))
            .Concat(pack.Vignettes.SelectMany(v => v.Effects.Concat(v.Options.SelectMany(o => o.Effects))));
        IEnumerable<string> rules = pack.Traits.SelectMany(t => (t.Launch is { } launch ? launch.Sent.Concat(launch.NotSent).Concat(launch.WithTarget) : []).Concat(t.OnTargetDied)
            .Where(r => r.ChangesTrait && r.Trait is not null && r.Trait != t.Id).Select(r => r.Trait!));
        return outcomes.SelectMany(e => e.Traits ?? []).Select(c => c.Trait)
            .Concat(answers.Select(e => e.Trait?.Trait).OfType<string>())
            .Concat(pack.Storylets.SelectMany(s => s.Traits).Concat(pack.Vignettes.SelectMany(v => v.Requires.OfType<TraitRange>())).Select(r => r.Trait))
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
            return (new TextFileView(file, typed, result.Errors, null, true), false);
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
