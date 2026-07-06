namespace F1.DataSyncWorker.Options;

public static class MigrationImportCliParser
{
    private const string MigrationImportEnabledKey = "MigrationImport:Enabled";
    private const string MigrationImportSeasonKey = "MigrationImport:Season";
    private const string MigrationImportSourceFilePathKey = "MigrationImport:SourceFilePath";
    private const string MigrationImportDryRunKey = "MigrationImport:DryRun";

    public static IReadOnlyDictionary<string, string?> ParseToConfiguration(string[] args)
    {
        if (args.Length == 0)
        {
            return EmptyConfiguration;
        }

        bool? enabled = null;
        string? sourceFilePath = null;
        int? season = null;
        bool? dryRun = null;
        var migrationArgumentProvided = false;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];

            if (TryReadOption(argument, "--migration-import", out var migrationEnabledRaw))
            {
                migrationArgumentProvided = true;
                enabled = migrationEnabledRaw is null
                    ? true
                    : ParseBooleanValue("--migration-import", migrationEnabledRaw);
                continue;
            }

            if (TryReadOption(argument, "--source-file-path", out var sourceValueRaw) ||
                TryReadOption(argument, "--source", out sourceValueRaw))
            {
                migrationArgumentProvided = true;
                var sourceValue = sourceValueRaw ?? ReadRequiredValue(args, ref index, argument);
                if (string.IsNullOrWhiteSpace(sourceValue))
                {
                    throw new ArgumentException("--source-file-path cannot be empty.");
                }

                sourceFilePath = sourceValue;
                continue;
            }

            if (TryReadOption(argument, "--season", out var seasonRaw))
            {
                migrationArgumentProvided = true;
                var seasonValue = seasonRaw ?? ReadRequiredValue(args, ref index, argument);
                season = ParseIntValue("--season", seasonValue);
                continue;
            }

            if (TryReadOption(argument, "--dry-run", out var dryRunRaw))
            {
                migrationArgumentProvided = true;
                dryRun = dryRunRaw is null
                    ? true
                    : ParseBooleanValue("--dry-run", dryRunRaw);
                continue;
            }

            if (TryReadOption(argument, "--write-mode", out var writeModeRaw))
            {
                migrationArgumentProvided = true;
                var writeModeEnabled = writeModeRaw is null || ParseBooleanValue("--write-mode", writeModeRaw);
                if (writeModeEnabled)
                {
                    dryRun = false;
                }

                continue;
            }
        }

        if (!migrationArgumentProvided)
        {
            return EmptyConfiguration;
        }

        // Any migration-specific argument should route execution into migration mode
        // unless the caller explicitly disabled it.
        enabled ??= true;

        var configuration = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [MigrationImportEnabledKey] = enabled.Value.ToString().ToLowerInvariant()
        };

        if (season.HasValue)
        {
            configuration[MigrationImportSeasonKey] = season.Value.ToString();
        }

        if (sourceFilePath is not null)
        {
            configuration[MigrationImportSourceFilePathKey] = sourceFilePath;
        }

        if (dryRun.HasValue)
        {
            configuration[MigrationImportDryRunKey] = dryRun.Value.ToString().ToLowerInvariant();
        }

        return configuration;
    }

    private static bool TryReadOption(string argument, string optionName, out string? inlineValue)
    {
        inlineValue = null;

        if (string.Equals(argument, optionName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var prefix = $"{optionName}=";
        if (!argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        inlineValue = argument[prefix.Length..];
        return true;
    }

    private static string ReadRequiredValue(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"{optionName} requires a value.");
        }

        index++;
        return args[index];
    }

    private static int ParseIntValue(string optionName, string rawValue)
    {
        if (int.TryParse(rawValue, out var value))
        {
            return value;
        }

        throw new ArgumentException($"{optionName} expects an integer value, received '{rawValue}'.");
    }

    private static bool ParseBooleanValue(string optionName, string rawValue)
    {
        if (bool.TryParse(rawValue, out var value))
        {
            return value;
        }

        throw new ArgumentException($"{optionName} expects true or false, received '{rawValue}'.");
    }

    private static readonly IReadOnlyDictionary<string, string?> EmptyConfiguration =
        new Dictionary<string, string?>(0, StringComparer.OrdinalIgnoreCase);
}