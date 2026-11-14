using Sim.Content;
using Sim.Core.Content;

namespace Web.Components.Content;

// What the index page shows, read once: the pack in play, the files on disk, and what the loader and the editor say of them.
public sealed record ContentIndexView(
    string? LiveId,
    int? LiveVersion,
    string? DiskId,
    int? DiskVersion,
    IReadOnlyList<ContentError> DiskErrors,
    bool FolderExists,
    string Folder,
    int LegacyCount,
    ContentFileSet Files,
    IReadOnlyList<ContentItemRef> Items,
    IReadOnlyDictionary<string, IReadOnlyList<ContentLoss>> FileProblems);

public sealed record IndexAlert(string Message, bool Ok);
