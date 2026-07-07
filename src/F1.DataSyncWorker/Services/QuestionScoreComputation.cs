using F1.Core.Models;

namespace F1.DataSyncWorker.Services;

public sealed record QuestionScoreComputation(
    long QuestionTemplateId,
    string QuestionId,
    string Prompt,
    QuestionCategory Category,
    string ParticipantId,
    string? PredictedAnswer,
    string? ActualAnswer,
    int? ImportedPoints,
    int CalculatedPoints,
    int DeltaPoints,
    string ReasonCode,
    int SortOrder);