using Sim.Content;
using Sim.Core.Content;
using YamlDotNet.RepresentationModel;

namespace Web.Forms;

// The files edited as text: the pack file, the two tuning files, and the storylets until their form exists. The path comes from this
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
        IReadOnlyList<string> warnings = file.Name == "storylets" && files.Validate().Pack is { } pack ? FlagWarnings(pack) : [];
        return new TextFileView(file, files.Files.GetValueOrDefault(file.Path, ""), [], notice, false) { Warnings = warnings };
    }

    // Flags that cannot work as written: one a storylet requires above 0 that nothing raises on the scope it reads, and one an encounter
    // raises that no storylet requires. A change that can only lower a flag (add below 0, set 0) does not count as setting it.
    internal static IReadOnlyList<string> FlagWarnings(ContentPack pack)
    {
        // A flag is on the ship or on a crew member; the two are separate values, so each scope is raised and read apart.
        var raised = new HashSet<(string Flag, bool Ship)>();
        foreach (EncounterDef encounter in pack.Encounters)
        {
            foreach (FlagChange change in Raises(encounter))
            {
                raised.Add((change.Flag, change.On == FlagIds.Ship));
            }
        }

        foreach (StoryletDef storylet in pack.Storylets)
        {
            foreach (FlagChange change in storylet.Options.SelectMany(o => o.Effects).Select(e => e.Flag).OfType<FlagChange>().Where(f => f.Amount > 0))
            {
                // Empty means the scope the storylet arrived for; subject is always a crew member.
                raised.Add((change.Flag, change.On == StoryletScopes.Ship || (change.On.Length == 0 && storylet.Scope == StoryletScopes.Ship)));
            }
        }

        HashSet<string> read = pack.Storylets.SelectMany(s => s.Flags).Select(r => r.Flag).ToHashSet(StringComparer.Ordinal);
        var warnings = new List<string>();
        foreach (StoryletDef storylet in pack.Storylets)
        {
            // A requirement that holds at 0 (only a max) needs nothing to set its flag.
            bool onShip = storylet.Scope == StoryletScopes.Ship;
            string where = onShip ? "the ship" : "a crew member";
            warnings.AddRange(storylet.Flags.Where(r => r.Min > 0).Select(r => r.Flag).Distinct().Where(f => !raised.Contains((f, onShip))).Select(f => $"Nothing sets {f} on {where}, which {storylet.Id} requires."));
        }

        foreach (EncounterDef encounter in pack.Encounters)
        {
            warnings.AddRange(Raises(encounter).Select(c => c.Flag).Distinct().Where(f => !read.Contains(f)).Select(f => $"Nothing reads {f}, which {encounter.Id} sets."));
        }

        return warnings;
    }

    // Every flag change an encounter's approaches or decision raise.
    private static IEnumerable<FlagChange> Raises(EncounterDef encounter)
    {
        IEnumerable<EffectDef> effects = encounter.Approaches.SelectMany(a => a.Outcomes.Values);
        if (encounter.Decision is { } decision)
        {
            effects = effects.Append(decision.Bold).Append(decision.Safe);
        }

        return effects.SelectMany(e => e.Flags ?? []).Where(f => f.Amount > 0);
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
