namespace Web;

// Relative settings paths resolve against the repository root, so the app starts from any directory inside it.
public static class PathResolver
{
    public const string RootMarker = "StarshipSim.slnx";

    public static string Resolve(string path, string currentDirectory, string baseDirectory)
    {
        if (Path.IsPathRooted(path))
        {
            return path;
        }

        string root = FindRoot(currentDirectory) ?? FindRoot(baseDirectory) ?? currentDirectory;
        return Path.GetFullPath(Path.Combine(root, path));
    }

    private static string? FindRoot(string start)
    {
        for (DirectoryInfo? directory = new(start); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, RootMarker)))
            {
                return directory.FullName;
            }
        }

        return null;
    }
}
