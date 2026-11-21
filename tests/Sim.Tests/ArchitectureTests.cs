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

    // The expression language (1e part four) is a library, not a module: the loader checks with it, and the storylets module runs it.
    private static readonly string[] ExpressionUsers = ["Sim.Content", "Sim.Storylets"];

    [Theory]
    [MemberData(nameof(SimProjects))]
    public void Sim_projects_reference_only_Sim_Core(string project)
    {
        string[] allowed = ExpressionUsers.Contains(project) ? ["Sim.Core", "Sim.Expressions"] : ["Sim.Core"];
        Assert.All(References(project), reference => Assert.Contains(reference, allowed));
    }

    // Named on its own so the rule holds before the folder exists for the theory above to find.
    [Fact]
    public void The_storylets_module_references_only_Sim_Core_and_the_expression_language()
    {
        Assert.All(References("Sim.Storylets"), reference => Assert.Contains(reference, new[] { "Sim.Core", "Sim.Expressions" }));
    }

    // It holds no state and joins no bus, and it reads only the evaluator's package beside Sim.Core.
    [Fact]
    public void The_expression_language_is_a_library_not_a_module()
    {
        Assert.Equal(new[] { "Sim.Core" }, References("Sim.Expressions"));
        Assert.Equal(new[] { "DynamicExpresso.Core" }, Packages("Sim.Expressions"));
        Assert.All(Directory.EnumerateFiles(Path.Combine(Root, "src", "Sim.Expressions"), "*.cs"), file =>
        {
            string text = File.ReadAllText(file);
            Assert.DoesNotContain("EventBus", text, StringComparison.Ordinal);
            Assert.DoesNotContain("LegacyState", text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Persistence_references_only_Sim_Core()
    {
        Assert.Equal(new[] { "Sim.Core" }, References("Persistence"));
    }

    [Fact]
    public void GameHost_references_only_Sim_projects()
    {
        Assert.All(References("GameHost"), reference => Assert.StartsWith("Sim.", reference, StringComparison.Ordinal));
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

    // The abort call's id has one home, which the loader, the editor's check, and the resolver all use.
    [Fact]
    public void The_abort_decision_id_is_written_once_in_the_source()
    {
        List<string> holders = Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal)) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains("\"DECISION_ABORT\"", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(Root, f).Replace('\\', '/'))
            .ToList();

        Assert.Equal(["src/Sim.Core/Content/MissionContent.cs"], holders);
    }

    // Review focus 4, as the plan asks: nothing that plays reads a test ship. GameHost, SimRunner, and the bridge are not referenced
    // by this project, so their source is read, as the abort id's test reads it (1e part six, design 4.6).
    [Fact]
    public void Nothing_that_plays_reads_a_test_ship()
    {
        string[] words = ["LoadTestShips", "TestShipDef", "TestShipSource", "TestShipsLoadResult", "testships"];
        string[] roots = [Path.Combine("src", "GameHost"), Path.Combine("src", "SimRunner"), Path.Combine("src", "Web", "Bridge")];
        foreach (string root in roots)
        {
            string folder = Path.Combine(Root, root);
            Assert.True(Directory.Exists(folder), folder);
            IEnumerable<string> sources = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                .Where(f => (f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal))
                    && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
            foreach (string file in sources)
            {
                string text = File.ReadAllText(file);
                Assert.All(words, word => Assert.False(text.Contains(word, StringComparison.Ordinal), $"{Path.GetRelativePath(Root, file)} names {word}."));
            }
        }
    }

    private static IEnumerable<string> References(string project)
    {
        XDocument csproj = XDocument.Load(Path.Combine(Root, "src", project, project + ".csproj"));
        return csproj.Descendants("ProjectReference")
            .Select(r => Path.GetFileNameWithoutExtension(r.Attribute("Include")!.Value.Replace('\\', '/')));
    }

    private static IEnumerable<string> Packages(string project)
    {
        XDocument csproj = XDocument.Load(Path.Combine(Root, "src", project, project + ".csproj"));
        return csproj.Descendants("PackageReference").Select(r => r.Attribute("Include")!.Value);
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
