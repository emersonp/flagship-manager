using Sim.Content;
using Sim.Core.Content;

namespace Sim.Tests;

// The game's own content/ is checked for loading only; nothing here pins what it holds.
public class GameContentTests : IClassFixture<RepoContentGuard>
{
    [Fact]
    public void The_games_content_loads_without_errors()
    {
        ContentLoadResult result = ContentLoader.LoadDirectory(Path.Combine(TestContent.Root, "content"));

        Assert.True(result.Pack is not null && result.Errors.Count == 0, string.Join("; ", result.Errors.Select(e => $"{e.File}: {e.Message}")));
    }
}
