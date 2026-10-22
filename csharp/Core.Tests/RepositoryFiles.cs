/// Files from the repository the tests read where they are (decks, reference recordings), found by walking up from
/// the test's build folder to the repository's root.
public static class RepositoryFiles
{
    public static string PathOf(string relativePath)
    {
        DirectoryInfo? folder = new(AppContext.BaseDirectory);
        while (folder != null && !Directory.Exists(Path.Combine(folder.FullName, "decks")))
        {
            folder = folder.Parent;
        }
        if (folder == null)
        {
            throw new DirectoryNotFoundException($"no repository folder above {AppContext.BaseDirectory}");
        }
        return Path.Combine(folder.FullName, relativePath);
    }
}
