namespace F1.Infrastructure.Data.Entities;

public sealed class RacePickScoreEntity
{
    public long Id { get; set; }
    public string RaceId { get; set; } = string.Empty;
    public string RaceCode { get; set; } = string.Empty;
    public string PickType { get; set; } = string.Empty;
    public string ParticipantId { get; set; } = string.Empty;
    public string? PredictedValue { get; set; }
    public string? ActualValue { get; set; }
    public int? ImportedPoints { get; set; }
    public int CalculatedPoints { get; set; }
    public int? OverrideScore { get; set; }
    public string? OverrideReasonCode { get; set; }
    public Guid SourceRunId { get; set; }
    public int DeltaPoints { get; set; }
    public string ReasonCode { get; set; } = string.Empty;
    public string? Explanation { get; set; }
    public DateTime RecordedAtUtc { get; set; }
}