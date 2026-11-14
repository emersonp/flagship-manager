using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Web;

namespace Web.Tests;

// The real app on a throwaway database file, with the repository's content and no beat delay.
public sealed class TestApp(int beatDelayMs = 0) : WebApplicationFactory<Program>
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"starship-sim-web-{Guid.NewGuid():N}.db");

    public CapturedLogs Logs { get; } = new();

    public GameServices Game => Services.GetRequiredService<GameServices>();

    public LegacySession Sessions => Services.GetRequiredService<LegacySession>();

    public HttpClient NewClient()
    {
        return CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Web:DatabasePath"] = path,
            ["Web:ContentDirectory"] = Path.Combine(RepositoryRoot(), "content"),
            ["Web:BeatDelayMs"] = beatDelayMs.ToString(),
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

// Every log entry the app writes, for tests that assert on what was or was not logged.
public sealed class CapturedLogs : ILoggerProvider
{
    private readonly List<(LogLevel Level, string Category, string Message)> entries = [];

    public IReadOnlyList<(LogLevel Level, string Category, string Message)> Entries
    {
        get
        {
            lock (entries)
            {
                return entries.ToList();
            }
        }
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new Sink(this, categoryName);
    }

    public void Dispose()
    {
    }

    private sealed class Sink(CapturedLogs owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (owner.entries)
            {
                owner.entries.Add((logLevel, category, formatter(state, exception) + (exception is null ? "" : " " + exception)));
            }
        }
    }
}
