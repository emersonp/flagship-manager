using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Web;

namespace Web.Tests;

// The real app on a throwaway database file, with the repository's content and no beat delay.
public sealed class TestApp : WebApplicationFactory<Program>
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"starship-sim-web-{Guid.NewGuid():N}.db");

    public GameServices Game => Services.GetRequiredService<GameServices>();

    public LegacySession Sessions => Services.GetRequiredService<LegacySession>();

    public HttpClient NewClient()
    {
        return CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Web:DatabasePath"] = path,
            ["Web:ContentDirectory"] = Path.Combine(RepositoryRoot(), "content"),
            ["Web:BeatDelayMs"] = "0",
        }));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        File.Delete(path);
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
