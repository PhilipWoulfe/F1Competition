namespace F1.Web.Models;

public sealed record CompetitionParticipantDetailResponse(
    string CompetitionSlug,
    int Season,
    string DisplayName,
    string ParticipantName,
    CompetitionParticipantSectionSummary RacePicks,
    CompetitionParticipantSectionSummary Preseason,
    CompetitionParticipantSectionSummary H2h);

public sealed record CompetitionParticipantSectionSummary(
    string Title,
    decimal ImportedTotalPoints,
    decimal RecalculatedTotalPoints,
    IReadOnlyList<CompetitionParticipantDetailItem> Items);

public sealed record CompetitionParticipantDetailItem(
    string Label,
    string Description,
    decimal? ImportedPoints,
    decimal CalculatedPoints,
    decimal DeltaPoints,
    string? ReasonCode,
    string? Explanation);