using System.Xml.Linq;

namespace Sim.Tests;

// GDD 7.1: the rules that let a new mechanic be added without rewriting old modules.
public class ArchitectureTests
{
    private static readonly string Root = FindRoot();

    private static readonly string[] Banned =
    [
        "DateTime.Now",
        "DateTime.UtcNow",
        "DateTimeOffset.Now",
        "DateTimeOffset.UtcNow",
        "Stopwatch",
        "new Random(",
        "Random.Shared",
        "Guid.NewGuid",
        "Microsoft.Data",
        "Npgsql",
    ];

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
        XDocument csproj = XDocument.Load(Path.Combine(Root, "src", project, project + ".csproj"));
        IEnumerable<string> references = csproj.Descendants("ProjectReference")
            .Select(r => Path.GetFileNameWithoutExtension(r.Attribute("Include")!.Value.Replace('\\', '/')));

        Assert.All(references, reference => Assert.Equal("Sim.Core", reference));
    }

    [Theory]
    [MemberData(nameof(SimProjects))]
    public void Sim_projects_have_no_clock_database_or_ambient_randomness(string project)
    {
        IEnumerable<string> sources = Directory
            .EnumerateFiles(Path.Combine(Root, "src", project), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains("/obj/") && !f.Contains("/bin/"));

        foreach (string file in sources)
        {
            string text = File.ReadAllText(file);
            Assert.All(Banned, banned => Assert.False(text.Contains(banned), $"{Path.GetRelativePath(Root, file)} uses {banned}."));
        }
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
