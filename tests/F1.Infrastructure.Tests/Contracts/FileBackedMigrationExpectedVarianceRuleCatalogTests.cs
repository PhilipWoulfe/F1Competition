using F1.DataSyncWorker.Options;
using F1.DataSyncWorker.Services;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace F1.Infrastructure.Tests.Contracts;

public sealed class FileBackedMigrationExpectedVarianceRuleCatalogTests
{
    [Fact]
    public async Task Constructor_LoadsOnlyRulesTargetedToCurrentEnvironment()
    {
        var manifestPath = Path.Combine(Path.GetTempPath(), $"expected-variance-{Guid.NewGuid():N}.json");
        var manifest = """
        {
          "ruleSetId": "phil-2025-expected-variance",
          "ruleSetVersion": "2026.07.06.1",
          "rules": [
            {
              "ruleId": "dev-only-rule",
              "reasonCode": "KNOWN_LEGACY_POINTS_ERROR",
              "subject": "Philip",
              "raceCode": "AUS",
              "pickType": "1",
              "targetEnvironments": ["Development"]
            },
            {
              "ruleId": "shared-rule",
              "reasonCode": "KNOWN_LEGACY_POINTS_ERROR",
              "subject": "Philip",
              "raceCode": "AUS",
              "pickType": "2",
              "targetEnvironments": ["Development", "Test", "Production"]
            }
          ]
        }
        """;

        try
        {
            await File.WriteAllTextAsync(manifestPath, manifest);

            var options = Options.Create(new MigrationExpectedVarianceOptions
            {
                Enabled = true,
                RuleManifestPath = manifestPath
            });

            var catalog = new FileBackedMigrationExpectedVarianceRuleCatalog(
                options,
                new TestHostEnvironment("Test"),
                NullLogger<FileBackedMigrationExpectedVarianceRuleCatalog>.Instance);

            Assert.True(catalog.IsEnabled);
            Assert.Equal("phil-2025-expected-variance", catalog.RuleSetId);
            Assert.Equal("2026.07.06.1", catalog.RuleSetVersion);
            Assert.Equal("Test", catalog.ActiveEnvironment);
            Assert.Equal(1, catalog.ActiveRuleCount);
            Assert.Single(catalog.Rules);
            Assert.Equal("shared-rule", catalog.Rules[0].RuleId);
            Assert.False(string.IsNullOrWhiteSpace(catalog.RuleSetChecksum));
            Assert.Equal(manifestPath, catalog.RuleSource);
        }
        finally
        {
            if (File.Exists(manifestPath))
            {
                File.Delete(manifestPath);
            }
        }
    }

    [Fact]
    public void Constructor_WhenManifestFileMissing_ReturnsEmptyCatalogWithMissingMetadata()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"expected-variance-missing-{Guid.NewGuid():N}.json");

        var options = Options.Create(new MigrationExpectedVarianceOptions
        {
            Enabled = true,
            RuleManifestPath = missingPath
        });

        var catalog = new FileBackedMigrationExpectedVarianceRuleCatalog(
            options,
            new TestHostEnvironment("Production"),
            NullLogger<FileBackedMigrationExpectedVarianceRuleCatalog>.Instance);

        Assert.True(catalog.IsEnabled);
        Assert.Equal("missing", catalog.RuleSetId);
        Assert.Equal("missing", catalog.RuleSetVersion);
        Assert.Equal("missing", catalog.RuleSetChecksum);
        Assert.Empty(catalog.Rules);
        Assert.Equal(0, catalog.ActiveRuleCount);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "F1.DataSyncWorker.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
