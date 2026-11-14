using System.Security.Cryptography;

namespace Web.Tests;

// Fails the test that wrote to the repository's content/ or tests/fixtures/content/: hashes every file when made, and again on Verify.
public sealed class RepoContentGuard : IDisposable
{
    private readonly string[] roots;
    private readonly string before;

    public RepoContentGuard()
        : this(Path.Combine(TestApp.RepositoryRoot(), "content"), Path.Combine(TestApp.RepositoryRoot(), "tests", "fixtures", "content"))
    {
    }

    internal RepoContentGuard(params string[] roots)
    {
        this.roots = roots;
        before = Hash();
    }

    public void Verify()
    {
        if (Hash() != before)
        {
            throw new InvalidOperationException($"A test changed the files under {string.Join(" or ", roots)}.");
        }
    }

    public void Dispose()
    {
        Verify();
    }

    private string Hash()
    {
        IEnumerable<string> parts = roots
            .SelectMany(r => Directory.EnumerateFiles(r, "*", SearchOption.AllDirectories))
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => f + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));
        return string.Join("\n", parts);
    }
}
