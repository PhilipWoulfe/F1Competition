using F1.Core.Models;
using F1.Infrastructure.Data.Entities;

namespace F1.DataSyncWorker.Services;

public interface IQuestionScoringStrategy
{
    QuestionCategory Category { get; }

    IReadOnlyList<QuestionScoreComputation> Score(QuestionScoringContext context);
}