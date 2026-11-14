using Sim.Content;
using Sim.Core.Content;

namespace Web;

public sealed record SaveResult(bool Saved, IReadOnlyList<ContentError> Errors);

// Thrown by a change that finds, under the lock, that its premise no longer holds (an id now taken, an item now gone): the save is refused with the words.
public sealed class ChangeRefusedException(string message) : Exception(message);

// The folder the content is read from and written to.
public sealed record ContentFolder(string Path);

// Every change to the files goes through here, one at a time: build the candidate, validate it, write it, then swap it in.
// A save changes one file; the only exception is declared with SaveAcrossFiles. The disk is read fresh at the start of a save, so a hand edit to that same file made while the save
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

    public Task<SaveResult> Save(Func<ContentFileSet, ContentFileSet> change, CancellationToken cancellation = default)
    {
        return Commit(change, null, cancellation);
    }

    // The one change declared to alter several files: deleting a skill also takes it from the starting crew. The files are written
    // one at a time in writeOrder, and every step on the way must load, so a write that stops part-way leaves a valid pack on disk.
    public Task<SaveResult> SaveAcrossFiles(Func<ContentFileSet, ContentFileSet> change, Func<string, int> writeOrder, CancellationToken cancellation = default)
    {
        return Commit(change, writeOrder, cancellation);
    }

    // Crew files before the rest: a crew member without a skill loads, but crew naming a skill that is gone do not.
    public static int CrewFirst(string path)
    {
        return path.StartsWith("crew/", StringComparison.Ordinal) ? 0 : 1;
    }

    private async Task<SaveResult> Commit(Func<ContentFileSet, ContentFileSet> change, Func<string, int>? writeOrder, CancellationToken cancellation)
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

            List<string> changed = after.Files.Keys.Where(k => !before.Files.TryGetValue(k, out string? old) || old != after.Files[k])
                .Concat(before.Files.Keys.Where(k => !after.Files.ContainsKey(k)))
                .OrderBy(k => writeOrder?.Invoke(k) ?? 0).ThenBy(k => k, StringComparer.Ordinal)
                .ToList();
            if (writeOrder is null && changed.Count > 1)
            {
                return Refused([new ContentError("", $"A save changes one file; this change would alter {changed.Count}.")]);
            }

            // One set per file written: each holds the files written so far, so each write changes one file.
            List<ContentFileSet> steps = Steps(before, after, changed);
            ContentLoadResult result;
            LivePack? prepared = null;
            try
            {
                result = after.Validate();
                if (result.Pack is { } pack)
                {
                    prepared = host.Prepare(pack);
                }

                if (prepared is not null && steps.SkipLast(1).Any(step => step.Validate().Pack is null))
                {
                    return Refused([new ContentError("", "This change cannot be written one file at a time with the pack loading after each.")]);
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
                ContentFileSet written = before;
                foreach (ContentFileSet step in steps)
                {
                    write(directory, written, step);
                    written = step;
                }
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

    private static List<ContentFileSet> Steps(ContentFileSet before, ContentFileSet after, IReadOnlyList<string> changed)
    {
        var steps = new List<ContentFileSet>();
        var files = new Dictionary<string, string>(before.Files);
        foreach (string path in changed)
        {
            if (after.Files.TryGetValue(path, out string? text))
            {
                files[path] = text;
            }
            else
            {
                files.Remove(path);
            }

            steps.Add(new ContentFileSet(new Dictionary<string, string>(files)));
        }

        return steps;
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
