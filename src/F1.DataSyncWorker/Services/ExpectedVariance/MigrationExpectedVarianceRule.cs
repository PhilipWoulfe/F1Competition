namespace F1.DataSyncWorker.Services;

public sealed record MigrationExpectedVarianceRule(
    string RuleId,
    string ReasonCode,
    string? Subject = null,
    string? RaceCode = null,
    string? PickType = null,
    string? ImportedSourcePattern = null,
    string? CalculatedSourcePattern = null);

public sealed record MigrationExpectedVarianceContext(
    string Subject,
    string RaceCode,
    string PickType,
    string ImportedSourceReference,
    string CalculatedSourceReference);

public sealed record MigrationExpectedVarianceClassification(
    bool IsExpected,
    string? ReasonCode,
    string? RuleId);