using System.Security.Cryptography;

namespace Web.Tests;

// Fails the test that wrote to the repository's content/: hashes every file when made, and again on Verify.
public sealed class RepoContentGuard : IDisposable
{
    private readonly string root;
    private readonly string before;

    public RepoContentGuard()
        : this(Path.Combine(TestApp.RepositoryRoot(), "content"))
    {
    }

    internal RepoContentGuard(string root)
    {
        this.root = root;
        before = Hash();
    }

    public void Verify()
    {
        if (Hash() != before)
        {
            throw new InvalidOperationException($"A test changed the files under {root}.");
        }
    }

    public void Dispose()
    {
        Verify();
    }

    private string Hash()
    {
        IEnumerable<string> parts = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => f + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));
        return string.Join("\n", parts);
    }
}
