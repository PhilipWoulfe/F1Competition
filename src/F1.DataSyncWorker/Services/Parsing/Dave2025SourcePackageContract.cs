using System.Security.Cryptography;
using System.Text;

namespace F1.DataSyncWorker.Services;

public static class Dave2025SourcePackageContract
{
    public const string RacesFile = "races.csv";
    public const string BonusFile = "bonus.csv";
    public const string BonusAnswersFile = "bonusAnswers.csv";
    public const string LeaderboardFile = "Leaderboard.csv";
    public const string RulesReferenceFile = "raceResults.ps1";
    public const string QuestionSupplementFile = "MostOF the boionus Questions.txt";

    private static readonly string[] RequiredFiles =
    [
        RacesFile,
        BonusFile,
        BonusAnswersFile,
        LeaderboardFile
    ];

    private static readonly string[] OptionalFiles =
    [
        RulesReferenceFile,
        QuestionSupplementFile
    ];

    public static SourcePackageValidationResult Validate(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return new SourcePackageValidationResult(false, false, RequiredFiles, OptionalFiles, ["<directory-not-found>"], []);
        }

        var files = Directory
            .EnumerateFiles(directoryPath, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToArray();

        var fileSet = files.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var appliesContract = fileSet.Contains(RacesFile) || fileSet.Contains(BonusFile) || fileSet.Contains(LeaderboardFile);

        if (!appliesContract)
        {
            return new SourcePackageValidationResult(false, false, RequiredFiles, OptionalFiles, [], files);
        }

        var missing = RequiredFiles
            .Where(required => !fileSet.Contains(required))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var allowed = RequiredFiles.Concat(OptionalFiles).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var extras = files
            .Where(file => !allowed.Contains(file))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new SourcePackageValidationResult(
            AppliesContract: true,
            IsValid: missing.Length == 0,
            RequiredFiles,
            OptionalFiles,
            missing,
            extras);
    }

    public static async Task<string> ComputeManifestChecksumAsync(string directoryPath, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"Migration source package directory was not found: {directoryPath}");
        }

        var files = Directory
            .EnumerateFiles(directoryPath, "*", SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (files.Length == 0)
        {
            throw new InvalidOperationException($"Migration source package directory is empty: {directoryPath}");
        }

        var manifest = new StringBuilder(files.Length * 128);
        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            var hash = await ComputeFileSha256Async(file, cancellationToken);
            manifest.Append(fileName).Append('|').Append(hash).Append('\n');
        }

        var checksumBytes = SHA256.HashData(Encoding.UTF8.GetBytes(manifest.ToString()));
        return Convert.ToHexString(checksumBytes).ToLowerInvariant();
    }

    private static async Task<string> ComputeFileSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public sealed record SourcePackageValidationResult(
    bool AppliesContract,
    bool IsValid,
    IReadOnlyList<string> RequiredFiles,
    IReadOnlyList<string> OptionalFiles,
    IReadOnlyList<string> MissingFiles,
    IReadOnlyList<string> ExtraFiles);