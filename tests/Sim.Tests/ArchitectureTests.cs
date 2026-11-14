using System.Xml.Linq;

namespace Sim.Tests;

// GDD 7.1: the rules that let a new mechanic be added without rewriting old modules.
public class ArchitectureTests
{
    private static readonly string Root = FindRoot();

    public static TheoryData<string> SimProjects()
    {
        var data = new TheoryData<string>();
        foreach (string directory in Directory.GetDirectories(Path.Combine(Root, "src"), "Sim.*"))
        {
            data.Add(Path.GetFileName(directory));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SimProjects))]
    public void Sim_projects_reference_only_Sim_Core(string project)
    {
        Assert.All(References(project), reference => Assert.Equal("Sim.Core", reference));
    }

    [Fact]
    public void Persistence_references_only_Sim_Core()
    {
        Assert.Equal(new[] { "Sim.Core" }, References("Persistence"));
    }

    [Fact]
    public void The_bench_references_only_the_pure_core()
    {
        Assert.All(References("MissionBench"), reference => Assert.StartsWith("Sim.", reference, StringComparison.Ordinal));
    }

    // The analyser fails the build on a clock, a database, or ambient randomness under src/Sim.*. This guards its wiring.
    [Fact]
    public void The_pure_core_is_built_with_the_banned_api_list()
    {
        string props = File.ReadAllText(Path.Combine(Root, "Directory.Build.props"));
        string[] banned = File.ReadAllLines(Path.Combine(Root, "BannedSymbols.txt"));

        Assert.Contains("Microsoft.CodeAnalysis.BannedApiAnalyzers", props, StringComparison.Ordinal);
        Assert.Contains("StartsWith('Sim.')", props, StringComparison.Ordinal);
        Assert.Contains(banned, line => line.StartsWith("T:System.Random;", StringComparison.Ordinal));
        Assert.Contains(banned, line => line.StartsWith("P:System.DateTime.UtcNow;", StringComparison.Ordinal));
        Assert.Contains(banned, line => line.StartsWith("N:Microsoft.Data.Sqlite;", StringComparison.Ordinal));
    }

    private static IEnumerable<string> References(string project)
    {
        XDocument csproj = XDocument.Load(Path.Combine(Root, "src", project, project + ".csproj"));
        return csproj.Descendants("ProjectReference")
            .Select(r => Path.GetFileNameWithoutExtension(r.Attribute("Include")!.Value.Replace('\\', '/')));
    }

    private static string FindRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
