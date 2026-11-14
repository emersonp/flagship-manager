using Sim.Content;
using Sim.Core.Content;

namespace Sim.Tests;

// The placeholder pack in tests/fixtures/content, not the game's content.
internal static partial class TestContent
{
    public static readonly string Root = FindRoot();

    public static readonly string TuningText = File.ReadAllText(Path.Combine(Root, "tests", "fixtures", "content", "tuning", "missions.yaml"));

    public static readonly ContentPack Pack = LoadPack();

    public static MissionTuning Tuning => Pack.Tuning;

    public static ObjectiveDef Objective => Pack.Objectives.First(o => o.Id == "OBJECTIVE_TEMPLATE_1");

    private static string FindRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static ContentPack LoadPack()
    {
        ContentLoadResult result = ContentLoader.LoadDirectory(Path.Combine(Root, "tests", "fixtures", "content"));
        return result.Pack ?? throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => $"{e.File}: {e.Message}")));
    }
}
