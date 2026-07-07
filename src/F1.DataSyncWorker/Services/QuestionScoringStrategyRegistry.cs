using F1.Core.Models;

namespace F1.DataSyncWorker.Services;

public sealed class QuestionScoringStrategyRegistry : IQuestionScoringStrategyRegistry
{
    private readonly IReadOnlyDictionary<QuestionCategory, IQuestionScoringStrategy> _strategies;

    public QuestionScoringStrategyRegistry(IEnumerable<IQuestionScoringStrategy> strategies)
    {
        _strategies = strategies.ToDictionary(x => x.Category);
    }

    public IQuestionScoringStrategy? Resolve(QuestionCategory category)
    {
        return _strategies.TryGetValue(category, out var strategy)
            ? strategy
            : null;
    }
}