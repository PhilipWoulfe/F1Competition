namespace F1.E2E.Tests.Infrastructure;

internal static class E2ePathResolver
{
    public static string ResolveRepositoryRoot()
    {
        var githubWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        if (!string.IsNullOrWhiteSpace(githubWorkspace) && Directory.Exists(githubWorkspace))
        {
            return githubWorkspace;
        }

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (current.GetFiles("*.sln").Length > 0)
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    public static string ResolveArtifactsDir(string? configuredPath, params string[] defaultRelativeSegments)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.GetFullPath(Path.Combine(ResolveRepositoryRoot(), configuredPath));
        }

        var relative = Path.Combine(defaultRelativeSegments);
        return Path.GetFullPath(Path.Combine(ResolveRepositoryRoot(), relative));
    }
}
