using System.Globalization;
using System.Text.Json;
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
                var options = DeserializeOptions(template.OptionsJson);
                var (points, reasonCode) = ScoreBonusAnswer(
                    predictedValue,
                    actualValue,
                    options,
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
        RaceBonusQuestionTemplateOptions? options,
        int? fallbackPointsPerQuestion)
    {
        var mode = ResolveMode(options);
        var configuredPoints = options?.PointsForCorrectPick > 0
            ? options.PointsForCorrectPick
            : fallbackPointsPerQuestion;

        if (!configuredPoints.HasValue)
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

        return mode switch
        {
            RaceBonusMode.Exact => ScoreExact(predictedValue, actualValue, configuredPoints.Value),
            RaceBonusMode.Tolerance => ScoreTolerance(predictedValue, actualValue, configuredPoints.Value, options),
            RaceBonusMode.Range => ScoreRange(predictedValue, actualValue, configuredPoints.Value, options),
            RaceBonusMode.FormulaMaxMinusGap => ScoreFormula(predictedValue, actualValue, options),
            _ => ScoreExact(predictedValue, actualValue, configuredPoints.Value)
        };
    }

    private static (int Points, string ReasonCode) ScoreExact(string predictedValue, string actualValue, int pointsForCorrect)
    {
        return string.Equals(predictedValue, actualValue, StringComparison.OrdinalIgnoreCase)
            ? (Math.Max(0, pointsForCorrect), "RACE_BONUS_EXACT")
            : (0, "RACE_BONUS_MISMATCH");
    }

    private static (int Points, string ReasonCode) ScoreTolerance(
        string predictedValue,
        string actualValue,
        int pointsForCorrect,
        RaceBonusQuestionTemplateOptions? options)
    {
        if (!TryParseDecimal(predictedValue, out var predicted) || !TryParseDecimal(actualValue, out var actual))
        {
            return (0, "RACE_BONUS_NUMERIC_PARSE_FAILED");
        }

        var tolerance = options?.Tolerance ?? 0m;
        if (tolerance < 0m)
        {
            tolerance = 0m;
        }

        var gap = Math.Abs(predicted - actual);
        return gap <= tolerance
            ? (Math.Max(0, pointsForCorrect), "RACE_BONUS_WITHIN_TOLERANCE")
            : (0, "RACE_BONUS_OUTSIDE_TOLERANCE");
    }

    private static (int Points, string ReasonCode) ScoreRange(
        string predictedValue,
        string actualValue,
        int pointsForCorrect,
        RaceBonusQuestionTemplateOptions? options)
    {
        if (!TryParseDecimal(predictedValue, out var predicted) || !TryParseDecimal(actualValue, out var actual))
        {
            return (0, "RACE_BONUS_NUMERIC_PARSE_FAILED");
        }

        var lowerTolerance = options?.LowerTolerance ?? options?.Tolerance ?? 0m;
        var upperTolerance = options?.UpperTolerance ?? options?.Tolerance ?? 0m;
        if (lowerTolerance < 0m)
        {
            lowerTolerance = 0m;
        }

        if (upperTolerance < 0m)
        {
            upperTolerance = 0m;
        }

        var lowerBound = actual - lowerTolerance;
        var upperBound = actual + upperTolerance;
        return predicted >= lowerBound && predicted <= upperBound
            ? (Math.Max(0, pointsForCorrect), "RACE_BONUS_WITHIN_RANGE")
            : (0, "RACE_BONUS_OUTSIDE_RANGE");
    }

    private static (int Points, string ReasonCode) ScoreFormula(
        string predictedValue,
        string actualValue,
        RaceBonusQuestionTemplateOptions? options)
    {
        if (!TryParseDecimal(predictedValue, out var predicted) || !TryParseDecimal(actualValue, out var actual))
        {
            return (0, "RACE_BONUS_NUMERIC_PARSE_FAILED");
        }

        var maxPoints = options?.FormulaMaxPoints ?? options?.PointsForCorrectPick ?? 0;
        var penaltyPerUnit = options?.FormulaPenaltyPerUnit ?? 1m;
        if (maxPoints <= 0)
        {
            return (0, "RACE_BONUS_FORMULA_CONFIG_INVALID");
        }

        if (penaltyPerUnit <= 0m)
        {
            penaltyPerUnit = 1m;
        }

        var gap = Math.Abs(predicted - actual);
        var rawScore = maxPoints - (gap * penaltyPerUnit);
        var points = Math.Max(0, (int)Math.Round(rawScore, MidpointRounding.AwayFromZero));

        return points > 0
            ? (points, "RACE_BONUS_FORMULA_SCORED")
            : (0, "RACE_BONUS_FORMULA_ZERO");
    }

    private static RaceBonusQuestionTemplateOptions? DeserializeOptions(string? optionsJson)
    {
        if (string.IsNullOrWhiteSpace(optionsJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<RaceBonusQuestionTemplateOptions>(optionsJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryParseDecimal(string value, out decimal parsed)
    {
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed) ||
               decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out parsed);
    }

    private static RaceBonusMode ResolveMode(RaceBonusQuestionTemplateOptions? options)
    {
        var raw = options?.Mode?.Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return RaceBonusMode.Exact;
        }

        if (raw.Equals("Exact", StringComparison.OrdinalIgnoreCase))
        {
            return RaceBonusMode.Exact;
        }

        if (raw.Equals("Tolerance", StringComparison.OrdinalIgnoreCase))
        {
            return RaceBonusMode.Tolerance;
        }

        if (raw.Equals("Range", StringComparison.OrdinalIgnoreCase))
        {
            return RaceBonusMode.Range;
        }

        if (raw.Equals("FormulaMaxMinusGap", StringComparison.OrdinalIgnoreCase) ||
            raw.Equals("Formula", StringComparison.OrdinalIgnoreCase))
        {
            return RaceBonusMode.FormulaMaxMinusGap;
        }

        return RaceBonusMode.Exact;
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

    private enum RaceBonusMode
    {
        Exact,
        Tolerance,
        Range,
        FormulaMaxMinusGap
    }
}
