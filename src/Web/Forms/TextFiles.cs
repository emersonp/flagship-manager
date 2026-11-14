using Sim.Content;
using Sim.Core.Content;
using YamlDotNet.RepresentationModel;

namespace Web.Forms;

// The files edited as text: the pack file and the two tuning files. The path comes from this list, never from a request.
public sealed record TextFile(string Name, string Path, string Title, IReadOnlyList<string> Notes)
{
    private const string Tuning = "The page that edits these numbers and re-runs the bench comes later. A mission in flight keeps the tuning it started with.";

    public static readonly IReadOnlyList<TextFile> All =
    [
        new("pack", "pack.yaml", "Pack file", ["The pack's id cannot be changed here. Changing the version makes every existing legacy show a warning that its content has changed."]),
        new("missions", "tuning/missions.yaml", "Mission tuning", [Tuning]),
        new("crew-tuning", "tuning/crew.yaml", "Crew tuning", [Tuning]),
    ];

    // Exact names only: anything else is not a text file.
    internal static TextFile? Find(string name)
    {
        return All.FirstOrDefault(f => f.Name == name);
    }

    public string Action => $"/dev/content/files/{Name}";
}

// What a text file page shows: the text as typed or as stored, what the save refused, and the notice.
public sealed record TextFileView(TextFile File, string Text, IReadOnlyList<ContentError> Reasons, string? Notice, bool FocusAlert)
{
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
    public static TextFileView Open(TextFile file, ContentFileSet files, string? notice = null)
    {
        return new TextFileView(file, files.Files.GetValueOrDefault(file.Path, ""), [], notice, false);
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
