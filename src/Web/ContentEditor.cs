using Sim.Content;
using Sim.Core.Content;

namespace Web;

public sealed record SaveResult(bool Saved, IReadOnlyList<ContentError> Errors);

// The folder the content is read from and written to.
public sealed record ContentFolder(string Path);

// Every change to the files goes through here, one at a time: build the candidate, validate it, write it, then swap it in.
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
