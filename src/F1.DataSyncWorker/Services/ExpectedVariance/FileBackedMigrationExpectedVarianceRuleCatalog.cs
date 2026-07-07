using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using F1.DataSyncWorker.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace F1.DataSyncWorker.Services;

public sealed class FileBackedMigrationExpectedVarianceRuleCatalog : IMigrationExpectedVarianceRuleCatalog, IMigrationExpectedVarianceRuleSetMetadataProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public FileBackedMigrationExpectedVarianceRuleCatalog(
        IOptions<MigrationExpectedVarianceOptions> options,
        IHostEnvironment hostEnvironment,
        ILogger<FileBackedMigrationExpectedVarianceRuleCatalog> logger)
    {
        var config = options.Value;
        ActiveEnvironment = hostEnvironment.EnvironmentName;
        var contentRootPath = hostEnvironment.ContentRootPath;

        if (!config.Enabled)
        {
            Rules = [];
            IsEnabled = false;
            RuleSetId = "none";
            RuleSetVersion = "none";
            RuleSetChecksum = "none";
            RuleSource = ResolvePath(config.RuleManifestPath, contentRootPath);
            logger.LogInformation(
                "Expected variance catalog disabled. Environment={Environment}, ManifestPath={ManifestPath}",
                ActiveEnvironment,
                RuleSource);
            return;
        }

        var manifestPath = ResolvePath(config.RuleManifestPath, contentRootPath);
        RuleSource = manifestPath;
        IsEnabled = true;

        if (!File.Exists(manifestPath))
        {
            Rules = [];
            RuleSetId = "missing";
            RuleSetVersion = "missing";
            RuleSetChecksum = "missing";
            logger.LogWarning(
                "Expected variance manifest was not found. Environment={Environment}, ManifestPath={ManifestPath}",
                ActiveEnvironment,
                manifestPath);
            return;
        }

        var json = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<ExpectedVarianceRuleManifest>(json, JsonOptions)
            ?? new ExpectedVarianceRuleManifest();

        RuleSetId = string.IsNullOrWhiteSpace(manifest.RuleSetId) ? "unnamed" : manifest.RuleSetId.Trim();
        RuleSetVersion = string.IsNullOrWhiteSpace(manifest.RuleSetVersion) ? "unversioned" : manifest.RuleSetVersion.Trim();
        RuleSetChecksum = ComputeSha256(json);

        var activeRules = new List<MigrationExpectedVarianceRule>();
        var seenRuleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in manifest.Rules)
        {
            if (candidate is null || string.IsNullOrWhiteSpace(candidate.RuleId) || string.IsNullOrWhiteSpace(candidate.ReasonCode))
            {
                continue;
            }

            if (!IsTargetedToEnvironment(candidate.TargetEnvironments, ActiveEnvironment))
            {
                continue;
            }

            var normalizedRuleId = candidate.RuleId.Trim();
            if (!seenRuleIds.Add(normalizedRuleId))
            {
                logger.LogWarning(
                    "Duplicate expected variance RuleId found in manifest; keeping first occurrence. RuleId={RuleId}, ManifestPath={ManifestPath}",
                    normalizedRuleId,
                    manifestPath);
                continue;
            }

            activeRules.Add(new MigrationExpectedVarianceRule(
                RuleId: normalizedRuleId,
                ReasonCode: candidate.ReasonCode.Trim(),
                Subject: NormalizeOptional(candidate.Subject),
                RaceCode: NormalizeOptional(candidate.RaceCode),
                PickType: NormalizeOptional(candidate.PickType),
                ImportedSourcePattern: NormalizeOptional(candidate.ImportedSourcePattern),
                CalculatedSourcePattern: NormalizeOptional(candidate.CalculatedSourcePattern)));
        }

        Rules = activeRules;

        logger.LogInformation(
            "Loaded expected variance ruleset. Environment={Environment}, RuleSetId={RuleSetId}, RuleSetVersion={RuleSetVersion}, RuleSetChecksum={RuleSetChecksum}, ActiveRuleCount={ActiveRuleCount}, ManifestPath={ManifestPath}",
            ActiveEnvironment,
            RuleSetId,
            RuleSetVersion,
            RuleSetChecksum,
            ActiveRuleCount,
            manifestPath);
    }

    public IReadOnlyList<MigrationExpectedVarianceRule> Rules { get; }
    public bool IsEnabled { get; }
    public string RuleSetId { get; }
    public string RuleSetVersion { get; }
    public string RuleSetChecksum { get; }
    public string RuleSource { get; }
    public string ActiveEnvironment { get; }
    public int ActiveRuleCount => Rules.Count;

    private static bool IsTargetedToEnvironment(IReadOnlyList<string>? targetEnvironments, string activeEnvironment)
    {
        if (targetEnvironments is null || targetEnvironments.Count == 0)
        {
            return true;
        }

        return targetEnvironments.Any(env =>
            !string.IsNullOrWhiteSpace(env) &&
            string.Equals(env.Trim(), activeEnvironment, StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolvePath(string path, string? contentRootPath)
    {
        if (Path.IsPathRooted(path))
        {
            return path;
        }

        var trimmedPath = path.Trim();
        var contentRootCandidate = !string.IsNullOrWhiteSpace(contentRootPath)
            ? Path.GetFullPath(trimmedPath, contentRootPath)
            : null;

        if (!string.IsNullOrWhiteSpace(contentRootCandidate) && File.Exists(contentRootCandidate))
        {
            return contentRootCandidate;
        }

        var baseDirectoryCandidate = Path.GetFullPath(trimmedPath, AppContext.BaseDirectory);
        if (File.Exists(baseDirectoryCandidate))
        {
            return baseDirectoryCandidate;
        }

        return contentRootCandidate ?? baseDirectoryCandidate;
    }

    private static string ComputeSha256(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = SHA256.HashData(bytes);
        var builder = new StringBuilder(hash.Length * 2);

        foreach (var item in hash)
        {
            builder.Append(item.ToString("x2"));
        }

        return builder.ToString();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed class ExpectedVarianceRuleManifest
    {
        public string? RuleSetId { get; init; }
        public string? RuleSetVersion { get; init; }
        public List<ExpectedVarianceRuleManifestItem?> Rules { get; init; } = [];
    }

    private sealed class ExpectedVarianceRuleManifestItem
    {
        public string? RuleId { get; init; }
        public string? ReasonCode { get; init; }
        public string? Subject { get; init; }
        public string? RaceCode { get; init; }
        public string? PickType { get; init; }
        public string? ImportedSourcePattern { get; init; }
        public string? CalculatedSourcePattern { get; init; }
        public IReadOnlyList<string>? TargetEnvironments { get; init; }
    }
}
