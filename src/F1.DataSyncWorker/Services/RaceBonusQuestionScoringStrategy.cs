using F1.Core.Models;

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

        var actualByTemplate = context.Actuals
            .Where(x => templates.Any(template => template.Id == x.QuestionTemplateId))
            .ToDictionary(x => x.QuestionTemplateId);

        var answersByTemplate = context.Answers
            .Where(x => templates.Any(template => template.Id == x.QuestionTemplateId))
            .GroupBy(x => x.QuestionTemplateId)
            .ToDictionary(x => x.Key, x => x.OrderBy(y => y.ParticipantId, StringComparer.OrdinalIgnoreCase).ToList());

        var computed = new List<QuestionScoreComputation>();

        foreach (var template in templates)
        {
            actualByTemplate.TryGetValue(template.Id, out var actual);
            var actualValue = NormalizeValue(actual?.NormalizedAnswer);

            if (!answersByTemplate.TryGetValue(template.Id, out var participants))
            {
                continue;
            }

            foreach (var participant in participants)
            {
                var predictedValue = NormalizeValue(participant.NormalizedAnswer);
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
}
