using System.Text.RegularExpressions;

namespace F1.Api.Tests.Architecture;

public sealed class CanonicalRuntimeBoundaryTests
{
    private static readonly Regex MigrationSymbolRegex = new(@"\b(MigrationImport\w*|MigrationRun\w*|IMigrationRunAdminService)\b", RegexOptions.Compiled);
    private static readonly HashSet<string> AllowedFiles =
    [
        Path.Combine("src", "F1.Api", "Controllers", "MigrationRunsController.cs"),
        Path.Combine("src", "F1.Api", "Dtos", "AdminMigrationRunDtos.cs"),
        Path.Combine("src", "F1.Api", "Services", "IMigrationRunAdminService.cs"),
        Path.Combine("src", "F1.Api", "Services", "MigrationRunAdminService.cs"),
        Path.Combine("src", "F1.Api", "Program.cs")
    ];

    [Fact]
    public void NonAdminApiSources_ShouldNotReferenceMigrationPrefixedSymbols()
    {
        var repositoryRoot = FindRepositoryRoot();
        var apiRoot = Path.Combine(repositoryRoot.FullName, "src", "F1.Api");
        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(apiRoot, "*.cs", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(repositoryRoot.FullName, file);
            if (AllowedFiles.Contains(relativePath))
            {
                continue;
            }

            var source = File.ReadAllText(file);
            var matches = MigrationSymbolRegex.Matches(source)
                .Select(match => match.Value)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (matches.Length == 0)
            {
                continue;
            }

            violations.Add($"{relativePath}: {string.Join(", ", matches)}");
        }

        Assert.True(
            violations.Count == 0,
            $"Non-admin API files must stay canonical-only. Violations:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "F1Competition.sln")))
            {
                return directory;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Unable to locate repository root.");
    }
}