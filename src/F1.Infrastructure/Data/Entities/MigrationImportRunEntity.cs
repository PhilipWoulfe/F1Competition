namespace F1.Infrastructure.Data.Entities;

public sealed class MigrationImportRunEntity
{
    public Guid Id { get; set; }
    public string SourceFilePath { get; set; } = string.Empty;
    public string SourceFileChecksum { get; set; } = string.Empty;
    public bool IsDryRun { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime StartedAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    public int RawRowCount { get; set; }
    public int UnresolvedTokenCount { get; set; }
    public int MappingWarningCount { get; set; }
    public string PreseasonParseStatus { get; set; } = "NotDetected";
    public string PreseasonScoringStatus { get; set; } = "NotDetected";
    public int PreseasonWarningCount { get; set; }
    public int PreseasonErrorCount { get; set; }
    public int PreseasonAnswerCount { get; set; }
    public int PreseasonScoredQuestionCount { get; set; }
    public int PreseasonQuestionDiffCount { get; set; }
    public int PreseasonTotalDeltaPoints { get; set; }
    public bool PreseasonIsolationGuardPassed { get; set; }
    public string? ParitySnapshotChecksum { get; set; }
    public string ParityStatus { get; set; } = "NotCompared";
    public string? ParityComparedChecksum { get; set; }
    public Guid? ParityComparedRunId { get; set; }
    public string? ErrorMessage { get; set; }
}