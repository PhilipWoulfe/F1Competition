using F1.DataSyncWorker.Models;

namespace F1.DataSyncWorker.Services;

public static class MigrationSourceProfileResolver
{
    private static readonly string[] DavePathMarkers =
    [
        "dave-2025",
        "david-2025",
        "dave2025"
    ];

    public static MigrationSourceProfile Resolve(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return MigrationSourceProfile.Unknown;
        }

        // Phil profile should be selected by path contract, even for synthetic/nonexistent
        // test paths that intentionally avoid filesystem dependencies.
        if (MigrationPhil2025CsvContractPolicy.AppliesTo(sourcePath))
        {
            return MigrationSourceProfile.Phil2025Csv;
        }

        if (Directory.Exists(sourcePath))
        {
            var daveContract = Dave2025SourcePackageContract.Validate(sourcePath);
            if (daveContract.AppliesContract)
            {
                return MigrationSourceProfile.Dave2025Package;
            }
        }

        if (DavePathMarkers.Any(marker => sourcePath.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return MigrationSourceProfile.Dave2025Package;
        }

        return MigrationSourceProfile.Unknown;
    }
}
