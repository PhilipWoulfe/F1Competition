using F1.DataSyncWorker.Models;

namespace F1.DataSyncWorker.Services;

public static class MigrationSourceProfileResolver
{
    public static MigrationSourceProfile Resolve(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return MigrationSourceProfile.Unknown;
        }

        if (File.Exists(sourcePath) && MigrationPhil2025CsvContractPolicy.AppliesTo(sourcePath))
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

        return MigrationSourceProfile.Unknown;
    }
}
