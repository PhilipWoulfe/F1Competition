namespace F1.Infrastructure.Data.Entities;

public sealed class MigrationImportRaceRoundMappingEntity
{
    public long Id { get; set; }
    public Guid ImportRunId { get; set; }
    public int RaceSequence { get; set; }
    public int SourceRowNumber { get; set; }
    public string SourceRaceCode { get; set; } = string.Empty;
    public int? Season { get; set; }
    public int? Round { get; set; }
    public string? MappedCircuitId { get; set; }
    public string? MappedRaceName { get; set; }
    public string? Warning { get; set; }
}