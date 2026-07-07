using F1.Core.Models;
using F1.Infrastructure.Data.Entities;

namespace F1.DataSyncWorker.Services;

public sealed class RaceBonusQuestionScoringStrategy : IQuestionScoringStrategy
{
    public QuestionCategory Category => QuestionCategory.RaceBonus;

    public IReadOnlyList<QuestionScoreComputation> Score(QuestionScoringContext context)
    {
        var templates = context.Templates
            .Where(x => x.Category == QuestionCategory.RaceBonus)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.QuestionId)
            .ToList();

        if (templates.Count == 0)
        {
            return [];
        }

        var templateIds = templates.Select(t => t.Id).ToHashSet();

        var actualByTemplate = context.Actuals
            .Where(x => templateIds.Contains(x.QuestionTemplateId))
            .ToDictionary(x => x.QuestionTemplateId);

        var answersByTemplate = context.Answers
            .Where(x => templateIds.Contains(x.QuestionTemplateId))
            .GroupBy(x => x.QuestionTemplateId)
            .ToDictionary(x => x.Key, x => x.OrderBy(y => y.ParticipantId, StringComparer.OrdinalIgnoreCase).ToList());

        var computed = new List<QuestionScoreComputation>();

        foreach (var template in templates)
        {
            actualByTemplate.TryGetValue(template.Id, out var actual);
            var actualValue = NormalizeValue(ResolveEffectiveAnswer(actual));

            if (!answersByTemplate.TryGetValue(template.Id, out var participants))
            {
                continue;
            }

            foreach (var participant in participants)
            {
                var predictedValue = NormalizeValue(ResolveEffectiveAnswer(participant));
                var (points, reasonCode) = ScoreBonusAnswer(
                    predictedValue,
                    actualValue,
                    context.PreseasonPolicy?.PointsPerQuestion);

                computed.Add(new QuestionScoreComputation(
                    QuestionTemplateId: template.Id,
                    QuestionId: template.QuestionId,
                    Prompt: template.Prompt,
                    Category: QuestionCategory.RaceBonus,
                    ParticipantId: participant.ParticipantId,
                    PredictedAnswer: predictedValue,
                    ActualAnswer: actualValue,
                    ImportedPoints: null,
                    CalculatedPoints: points,
                    DeltaPoints: 0,
                    ReasonCode: reasonCode,
                    SortOrder: template.SortOrder));
            }
        }

        return computed;
    }

    private static (int Points, string ReasonCode) ScoreBonusAnswer(
        string? predictedValue,
        string? actualValue,
        int? pointsPerQuestion)
    {
        if (!pointsPerQuestion.HasValue)
        {
            return (0, "RACE_BONUS_POLICY_MISSING");
        }

        if (string.IsNullOrWhiteSpace(predictedValue))
        {
            return (0, "RACE_BONUS_PREDICTION_NULL");
        }

        if (string.IsNullOrWhiteSpace(actualValue))
        {
            return (0, "RACE_BONUS_ACTUAL_MISSING");
        }

        return string.Equals(predictedValue, actualValue, StringComparison.OrdinalIgnoreCase)
            ? (Math.Max(0, pointsPerQuestion.Value), "RACE_BONUS_EXACT")
            : (0, "RACE_BONUS_MISMATCH");
    }

    private static string? NormalizeValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToUpperInvariant();
    }

    private static string? ResolveEffectiveAnswer(QuestionAnswerEntity? answer)
    {
        return string.IsNullOrWhiteSpace(answer?.OverrideAnswer) ? answer?.ImportedAnswer : answer.OverrideAnswer;
    }

    private static string? ResolveEffectiveAnswer(QuestionActualEntity? actual)
    {
        return string.IsNullOrWhiteSpace(actual?.OverrideAnswer) ? actual?.ImportedAnswer : actual.OverrideAnswer;
    }
}
