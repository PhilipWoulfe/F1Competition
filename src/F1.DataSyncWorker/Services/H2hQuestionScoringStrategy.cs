using System.Text.Json;
using F1.Core.Models;
using F1.Infrastructure.Data.Entities;

namespace F1.DataSyncWorker.Services;

public sealed class H2hQuestionScoringStrategy : IQuestionScoringStrategy
{
    public QuestionCategory Category => QuestionCategory.H2H;

    public IReadOnlyList<QuestionScoreComputation> Score(QuestionScoringContext context)
    {
        var computed = new List<QuestionScoreComputation>();

        foreach (var template in context.Templates.Where(x => x.Category == QuestionCategory.H2H).OrderBy(x => x.SortOrder).ThenBy(x => x.QuestionId))
        {
            var options = DeserializeOptions(template.OptionsJson);
            var actual = context.Actuals.SingleOrDefault(x => x.QuestionTemplateId == template.Id);
            var actualAnswer = NormalizeDriver(ResolveEffectiveAnswer(actual));
            var answers = context.Answers
                .Where(x => x.QuestionTemplateId == template.Id)
                .OrderBy(x => x.ParticipantId, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var answer in answers)
            {
                var predicted = NormalizeDriver(ResolveEffectiveAnswer(answer));
                var (points, reasonCode) = ScorePick(predicted, actualAnswer, options);
                computed.Add(new QuestionScoreComputation(
                    QuestionTemplateId: template.Id,
                    QuestionId: template.QuestionId,
                    Prompt: template.Prompt,
                    Category: template.Category,
                    ParticipantId: answer.ParticipantId,
                    PredictedAnswer: predicted,
                    ActualAnswer: actualAnswer,
                    ImportedPoints: null,
                    CalculatedPoints: points,
                    DeltaPoints: 0,
                    ReasonCode: reasonCode,
                    SortOrder: template.SortOrder));
            }
        }

        return computed;
    }

    private static (int Points, string ReasonCode) ScorePick(string? predicted, string? actual, H2hQuestionTemplateOptions? options)
    {
        if (options is null || string.IsNullOrWhiteSpace(options.LeftDriverId) || string.IsNullOrWhiteSpace(options.RightDriverId) || options.PointsForCorrectPick <= 0)
        {
            return (0, "H2H_OPTIONS_MISSING");
        }

        var allowedDrivers = new[]
            {
                NormalizeDriver(options.LeftDriverId),
                NormalizeDriver(options.RightDriverId)
            }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(predicted))
        {
            return (0, "H2H_PREDICTION_NULL");
        }

        if (!allowedDrivers.Contains(predicted))
        {
            return (0, "H2H_PREDICTION_UNSUPPORTED");
        }

        if (string.IsNullOrWhiteSpace(actual))
        {
            return (0, "H2H_ACTUAL_MISSING");
        }

        if (!allowedDrivers.Contains(actual))
        {
            return (0, "H2H_ACTUAL_UNSUPPORTED");
        }

        return string.Equals(predicted, actual, StringComparison.OrdinalIgnoreCase)
            ? (options.PointsForCorrectPick, "H2H_CORRECT_PICK")
            : (0, "H2H_WRONG_PICK");
    }

    private static H2hQuestionTemplateOptions? DeserializeOptions(string? optionsJson)
    {
        if (string.IsNullOrWhiteSpace(optionsJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<H2hQuestionTemplateOptions>(optionsJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? NormalizeDriver(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
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