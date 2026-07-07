using F1.Infrastructure.Data.Entities;

namespace F1.DataSyncWorker.Services;

public sealed record QuestionScoringContext(
    Guid ImportRunId,
    IReadOnlyList<QuestionTemplateEntity> Templates,
    IReadOnlyList<QuestionAnswerEntity> Answers,
    IReadOnlyList<QuestionActualEntity> Actuals,
    MigrationImportPreseasonPolicyEntity? PreseasonPolicy);