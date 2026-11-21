using System.Diagnostics;
using System.Net;
using System.Text;
using static Web.Tests.ContentFormsTests;

namespace Web.Tests;

// Wall-clock tests run alone, after the rest of the assembly: the bench tests (TuningRunsTests and others) take every core while they run, and a page timed beside them is timed against them.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SpeedCollection
{
    public const string Name = "Speed";
}

// A pack the size an author may reach: each page reads every file once, so it stays quick as the pack grows.
[Collection(SpeedCollection.Name)]
public sealed class ContentSpeedTests : IClassFixture<RepoContentGuard>
{
    private const int Count = 300;

    // Placeholder encounters no stage draws (no objective asks for the tag), each with a decision.
    private static void AddEncounters(string root)
    {
        var text = new StringBuilder();
        for (int i = 1; i <= Count; i++)
        {
            text.Append($"- id: ENCOUNTER_BULK_{i:000}\n  tags: [bulk]\n  approaches:\n    - {{ id: careful, skill: SKILL_A, difficulty: 3 }}\n    - {{ id: fast, skill: SKILL_B, difficulty: 4 }}\n");
            text.Append($"  decision:\n    id: DECISION_BULK_{i:000}\n    stakes: 1\n    bold: [{{ momentum: 1 }}]\n    safe: []\n");
        }

        File.WriteAllText(Path.Combine(root, "encounters", "bulk.yaml"), text.ToString());
    }

    [Theory]
    [InlineData("/dev/content/encounters")]
    [InlineData("/dev/content/encounters/ENCOUNTER_BULK_150")]
    [InlineData("/dev/content/objectives/OBJECTIVE_TEMPLATE_1")]
    [InlineData("/dev/content")]
    public async Task A_page_of_a_pack_with_300_encounters_answers_well_under_a_second(string url)
    {
        using TestApp app = NewApp(AddEncounters);
        HttpClient client = app.NewClient();
        Assert.Equal(HttpStatusCode.OK, (await Get(client, url)).Status);

        // The best of three, so other test assemblies running beside this one do not decide it (the tests in this one are kept off by the Speed collection). About 2,100 ms before one parse per file; about 120 ms after.
        long best = long.MaxValue;
        for (int i = 0; i < 3; i++)
        {
            var clock = Stopwatch.StartNew();
            (HttpStatusCode status, string body) = await Get(client, url);
            clock.Stop();
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Contains("bulk", body, StringComparison.Ordinal);
            best = Math.Min(best, clock.ElapsedMilliseconds);
        }

        Assert.True(best < 1000, $"{url} took {best} ms at best.");
    }
}
