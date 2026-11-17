using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Persistence;
using Web;

namespace Web.Tests;

// The real app on a throwaway database file, with the repository's content and no beat delay.
// Options: developerMode turns the editor's routes on; scratch runs on a temporary copy of the repository's content (any test that
// saves needs it); prepare gets that copy's folder before the app starts, to break it or change it.
public sealed class TestApp(int beatDelayMs = 0, bool wideDice = false, bool noCalls = false, bool oddNames = false, bool developerMode = false, bool scratch = false, Action<string>? prepare = null) : WebApplicationFactory<Program>
{
    private readonly RepoContentGuard guard = new();
    private string? wideContent;

    private readonly string path = Path.Combine(Path.GetTempPath(), $"starship-sim-web-{Guid.NewGuid():N}.db");

    public CapturedLogs Logs { get; } = new();

    public ContentHost Host => Services.GetRequiredService<ContentHost>();

    public ContentEditor Editor => Services.GetRequiredService<ContentEditor>();

    // The pack in play now; a test that needs one snapshot reads it once.
    public LivePack Live => Host.Current!;

    // The scratch copy of the content, when the app runs on one.
    public string ContentPath => wideContent ?? throw new InvalidOperationException("This app runs on the repository's content.");

    public GameServices Game => Services.GetRequiredService<GameServices>();

    public LegacySession Sessions => Services.GetRequiredService<LegacySession>();

    public HttpClient NewClient()
    {
        return CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    // A new legacy moved on past its first itinerary (1e part two) to the orders, where most tests begin.
    public async Task<Guid> CreateAtOrders(string name, ulong seed)
    {
        Guid id = await Sessions.CreateLegacy(Live, name, seed);
        await Sessions.Run(Live, id, legacy =>
        {
            legacy.Send(new Sim.Core.AdvancePhase());
            return legacy;
        });
        return id;
    }

    // The cycle and phase a page drawn now carries in its action forms.
    public List<KeyValuePair<string, string>> Stamp(Guid id)
    {
        LegacyRecord record = Game.Store.Load(id, Live.Game.ModuleTypes)!;
        return [new("cycle", record.State.Cycle.ToString(System.Globalization.CultureInfo.InvariantCulture)), new("phase", record.State.Phase.ToString())];
    }

    // The form with the current stamp added, unless it already carries a cycle or a phase.
    public List<KeyValuePair<string, string>> Stamped(Guid id, IEnumerable<KeyValuePair<string, string>> form)
    {
        List<KeyValuePair<string, string>> fields = form.ToList();
        if (!fields.Any(f => f.Key is "cycle" or "phase"))
        {
            fields.AddRange(Stamp(id));
        }

        return fields;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Web:DatabasePath"] = path,
            ["Web:ContentDirectory"] = wideDice || oddNames || scratch ? WideContent() : Path.Combine(RepositoryRoot(), "tests", "fixtures", "content"),
            ["Web:DeveloperMode"] = developerMode ? "true" : "false",
            ["Web:BeatDelayMs"] = beatDelayMs.ToString(),
        }));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        File.Delete(path);
        if (wideContent is not null && Directory.Exists(wideContent))
        {
            Directory.Delete(wideContent, true);
        }

        guard.Verify();
    }

    // A copy of the content with 1d20 check dice and deadly severe injuries, so lead changes are common inside a short run.
    private string WideContent()
    {
        if (wideContent is not null)
        {
            return wideContent;
        }

        wideContent = Path.Combine(Path.GetTempPath(), $"starship-sim-content-{Guid.NewGuid():N}");
        string source = Path.Combine(RepositoryRoot(), "tests", "fixtures", "content");
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(wideContent, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        if (oddNames)
        {
            NameEverything(wideContent);
        }

        prepare?.Invoke(wideContent);

        if (!wideDice)
        {
            return wideContent;
        }

        string tuning = Path.Combine(wideContent, "tuning", "missions.yaml");
        string text = File.ReadAllText(tuning);
        string wide = text.Replace("dice: { count: 2, sides: 6 }   # every check", "dice: { count: 1, sides: 20 }   # every check", StringComparison.Ordinal);
        if (wide == text)
        {
            throw new InvalidOperationException("The check dice line was not found.");
        }

        // Every severe injury roll kills, so a lead is lost as soon as one is hurt.
        wide = wide.Replace("deadBelow: 5 ", "deadBelow: 13", StringComparison.Ordinal).Replace("incapacitatedBelow: 8 ", "incapacitatedBelow: 13", StringComparison.Ordinal);
        // The lead decides every call itself, so one launch plays the whole mission.
        wide = noCalls ? wide.Replace("  base: 3\n", "  base: 50\n", StringComparison.Ordinal) : wide;
        File.WriteAllText(tuning, wide);
        return wideContent;
    }

    public const string OddName = "NAME 7 <b> & -3";

    // Every item gets a name made of markup, an ampersand, and digits, then its id.
    private static void NameEverything(string root)
    {
        foreach (string file in Directory.GetFiles(root, "*.yaml", SearchOption.AllDirectories).Where(f => !f.Contains("tuning", StringComparison.Ordinal)))
        {
            string text = File.ReadAllText(file);
            text = System.Text.RegularExpressions.Regex.Replace(text, @"(?m)^(\s*)- id: (\w+)\n", m => $"{m.Value}{m.Groups[1].Value}  name: \"{OddName} {m.Groups[2].Value}\"\n");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"(?m)^(    )id: (DECISION\w+)\n", m => $"{m.Value}    name: \"{OddName} {m.Groups[2].Value}\"\n");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\{ id: (GEAR\w+),", m => $"{{ id: {m.Groups[1].Value}, name: \"{OddName} {m.Groups[1].Value}\",");
            File.WriteAllText(file, text);
        }
    }

    internal static string RepositoryRoot()
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

    // Called with each entry as it is written, so a test can wait on one instead of polling.
    public Action<(LogLevel Level, string Category, string Message)>? OnEntry { get; set; }

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
            (LogLevel, string, string) entry = (logLevel, category, formatter(state, exception) + (exception is null ? "" : " " + exception));
            lock (owner.entries)
            {
                owner.entries.Add(entry);
            }

            owner.OnEntry?.Invoke(entry);
        }
    }
}
