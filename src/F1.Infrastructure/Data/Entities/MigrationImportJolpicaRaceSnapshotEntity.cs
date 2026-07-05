namespace F1.Infrastructure.Data.Entities;

public sealed class MigrationImportJolpicaRaceSnapshotEntity
{
    public long Id { get; set; }
    public Guid ImportRunId { get; set; }
    public int Season { get; set; }
    public int Round { get; set; }
    public string RaceName { get; set; } = string.Empty;
    public string? CircuitName { get; set; }
    public DateTime? StartTimeUtc { get; set; }
}