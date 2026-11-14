using Web;

namespace Web.Tests;

public sealed class PathResolverTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"starship-sim-paths-{Guid.NewGuid():N}");

    public PathResolverTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "marked", "deep", "deeper"));
        Directory.CreateDirectory(Path.Combine(root, "bare"));
        Directory.CreateDirectory(Path.Combine(root, "other"));
        File.WriteAllText(Path.Combine(root, "marked", PathResolver.RootMarker), "");
    }

    public void Dispose()
    {
        Directory.Delete(root, true);
    }

    [Fact]
    public void A_relative_path_resolves_against_the_nearest_ancestor_with_the_marker()
    {
        string resolved = PathResolver.Resolve("content", Path.Combine(root, "marked", "deep", "deeper"), Path.Combine(root, "bare"));

        Assert.Equal(Path.Combine(root, "marked", "content"), resolved);
    }

    [Fact]
    public void The_base_directory_is_tried_when_the_current_directory_has_no_marker_above_it()
    {
        string resolved = PathResolver.Resolve("db.sqlite", Path.Combine(root, "bare"), Path.Combine(root, "marked", "deep"));

        Assert.Equal(Path.Combine(root, "marked", "db.sqlite"), resolved);
    }

    [Fact]
    public void Without_a_marker_anywhere_the_path_resolves_against_the_current_directory()
    {
        string resolved = PathResolver.Resolve("content", Path.Combine(root, "bare"), Path.Combine(root, "other"));

        Assert.Equal(Path.Combine(root, "bare", "content"), resolved);
    }

    [Fact]
    public void An_absolute_path_is_used_as_given()
    {
        string absolute = Path.Combine(root, "elsewhere", "x.db");

        Assert.Equal(absolute, PathResolver.Resolve(absolute, Path.Combine(root, "marked"), Path.Combine(root, "marked")));
    }
}
