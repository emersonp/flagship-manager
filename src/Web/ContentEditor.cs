using Sim.Content;
using Sim.Core.Content;

namespace Web;

public sealed record SaveResult(bool Saved, IReadOnlyList<ContentError> Errors);

// Thrown by a change that finds, under the lock, that its premise no longer holds (an id now taken, an item now gone): the save is refused with the words.
public sealed class ChangeRefusedException(string message) : Exception(message);

// The folder the content is read from and written to.
public sealed record ContentFolder(string Path);

// Every change to the files goes through here, one at a time: build the candidate, validate it, write it, then swap it in.
// A save changes one file. The disk is read fresh at the start of a save, so a hand edit to that same file made while the save
// runs may be overwritten; a hand edit to another file is never touched.
public sealed class ContentEditor
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly ContentHost host;
    private readonly string directory;
    private readonly Action<string, ContentFileSet, ContentFileSet> write;

    public ContentEditor(ContentHost host, ContentFolder folder)
        : this(host, folder, ContentEdits.Write)
    {
    }

    // The writer is a test hook.
    internal ContentEditor(ContentHost host, ContentFolder folder, Action<string, ContentFileSet, ContentFileSet> write)
    {
        this.host = host;
        directory = folder.Path;
        this.write = write;
    }

    public string Folder => directory;

    // A test hook: runs once the save holds the lock, before the files are read, to stand for another writer getting in first.
    internal Action? AfterGate { get; set; }

    // The files on disk now: the disk is the truth, since the author may edit files by hand or with git.
    public ContentFileSet Read()
    {
        return ContentFileSet.ReadDirectory(directory);
    }

    public async Task<SaveResult> Save(Func<ContentFileSet, ContentFileSet> change, CancellationToken cancellation = default)
    {
        await gate.WaitAsync(cancellation);
        try
        {
            AfterGate?.Invoke();
            ContentFileSet before = Read();
            ContentFileSet after;
            try
            {
                after = change(before);
            }
            catch (ContentLossException lost)
            {
                return Refused(lost.Losses.Select(l => new ContentError(l.File, l.ToString())).ToList());
            }
            catch (ChangeRefusedException refused)
            {
                return Refused([new ContentError("", refused.Message)]);
            }

            int changed = after.Files.Count(f => !before.Files.TryGetValue(f.Key, out string? old) || old != f.Value) + before.Files.Count(f => !after.Files.ContainsKey(f.Key));
            if (changed > 1)
            {
                return Refused([new ContentError("", $"A save changes one file; this change would alter {changed}.")]);
            }

            ContentLoadResult result;
            LivePack? prepared = null;
            try
            {
                result = after.Validate();
                if (result.Pack is { } pack)
                {
                    prepared = host.Prepare(pack);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // A loader that crashes on a candidate refuses it with the reason, never a 500.
                return Refused([new ContentError("", e.Message)]);
            }

            if (prepared is null)
            {
                return Refused(result.Errors);
            }

            try
            {
                write(directory, before, after);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return Refused([new ContentError("", "The files could not be written: " + e.Message)]);
            }

            host.Install(prepared);
            return new SaveResult(true, []);
        }
        finally
        {
            gate.Release();
        }
    }

    // Reads, validates, and swaps without changing any file: for a fix made by hand or with git.
    public async Task<SaveResult> Reload(CancellationToken cancellation = default)
    {
        await gate.WaitAsync(cancellation);
        try
        {
            ContentLoadResult result;
            LivePack? prepared = null;
            try
            {
                result = Read().Validate();
                if (result.Pack is { } pack)
                {
                    prepared = host.Prepare(pack);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                ContentError[] errors = [new ContentError("", e.Message)];
                host.Refused(errors);
                return new SaveResult(false, errors);
            }

            if (prepared is null)
            {
                host.Refused(result.Errors);
                return new SaveResult(false, result.Errors);
            }

            host.Install(prepared);
            return new SaveResult(true, []);
        }
        finally
        {
            gate.Release();
        }
    }

    private SaveResult Refused(IReadOnlyList<ContentError> errors)
    {
        return new SaveResult(false, errors);
    }
}
