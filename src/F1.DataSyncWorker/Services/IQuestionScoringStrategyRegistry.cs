using F1.Core.Models;

namespace F1.DataSyncWorker.Services;

public interface IQuestionScoringStrategyRegistry
{
    IQuestionScoringStrategy? Resolve(QuestionCategory category);
}