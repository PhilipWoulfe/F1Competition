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
    int ImportedTotalPoints,
    int RecalculatedTotalPoints,
    IReadOnlyList<CompetitionParticipantDetailItem> Items);

public sealed record CompetitionParticipantDetailItem(
    string Label,
    string Description,
    int? ImportedPoints,
    int CalculatedPoints,
    int DeltaPoints,
    string? ReasonCode,
    string? Explanation);