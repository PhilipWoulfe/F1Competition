using System.Text.RegularExpressions;
using F1.Core.Models;
using F1.Infrastructure.Data.Entities;

namespace F1.DataSyncWorker.Services;

public sealed partial class PreseasonQuestionScoringStrategy : IQuestionScoringStrategy
{
    public QuestionCategory Category => QuestionCategory.Preseason;

    public IReadOnlyList<QuestionScoreComputation> Score(QuestionScoringContext context)
    {
        var templates = context.Templates
            .Where(x => x.Category == QuestionCategory.Preseason)
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
            var actualValue = NormalizeToken(ResolveEffectiveAnswer(actual));
            var actualTokenSet = BuildPreseasonActualTokenSet(actualValue);

            if (!answersByTemplate.TryGetValue(template.Id, out var participants))
            {
                continue;
            }

            foreach (var participant in participants)
            {
                var predictedValue = NormalizeToken(ResolveEffectiveAnswer(participant));
                var (points, reasonCode) = ScorePreseasonAnswer(
                    predictedValue,
                    actualValue,
                    actualTokenSet,
                    context.PreseasonPolicy?.PointsPerQuestion);

                computed.Add(new QuestionScoreComputation(
                    QuestionTemplateId: template.Id,
                    QuestionId: template.QuestionId,
                    Prompt: template.Prompt,
                    Category: QuestionCategory.Preseason,
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

    private static (int Points, string ReasonCode) ScorePreseasonAnswer(
        string? predictedValue,
        string? actualValue,
        ISet<string> actualTokenSet,
        int? pointsPerQuestion)
    {
        if (!pointsPerQuestion.HasValue)
        {
            return (0, "PRESEASON_POLICY_MISSING");
        }

        if (string.IsNullOrWhiteSpace(predictedValue))
        {
            return (0, "PRESEASON_PREDICTION_NULL");
        }

        if (string.IsNullOrWhiteSpace(actualValue))
        {
            return (0, "PRESEASON_ACTUAL_MISSING");
        }

        if (actualTokenSet.Contains(predictedValue))
        {
            return (Math.Max(0, pointsPerQuestion.Value), "PRESEASON_EXACT");
        }

        return (0, "PRESEASON_MISMATCH");
    }

    private static HashSet<string> BuildPreseasonActualTokenSet(string? actualValue)
    {
        if (string.IsNullOrWhiteSpace(actualValue))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var tokenSet = PreseasonActualSplitRegex()
            .Split(actualValue)
            .Select(token => token.Trim().ToUpperInvariant())
            .Where(token => token.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (tokenSet.Count == 0)
        {
            tokenSet.Add(actualValue.Trim().ToUpperInvariant());
        }

        return tokenSet;
    }

    private static string? NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToUpperInvariant();
    }

    [GeneratedRegex("\\s*\\|\\s*", RegexOptions.Compiled)]
    private static partial Regex PreseasonActualSplitRegex();

    private static string? ResolveEffectiveAnswer(QuestionAnswerEntity? answer)
    {
        return string.IsNullOrWhiteSpace(answer?.OverrideAnswer) ? answer?.ImportedAnswer : answer.OverrideAnswer;
    }

    private static string? ResolveEffectiveAnswer(QuestionActualEntity? actual)
    {
        return string.IsNullOrWhiteSpace(actual?.OverrideAnswer) ? actual?.ImportedAnswer : actual.OverrideAnswer;
    }
}