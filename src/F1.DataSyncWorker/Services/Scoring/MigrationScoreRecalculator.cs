using System.Text.RegularExpressions;
using F1.Core.Models;
using F1.DataSyncWorker.Models;
using F1.Infrastructure.Data;
using F1.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace F1.DataSyncWorker.Services;

public sealed partial class MigrationScoreRecalculator : IMigrationScoreRecalculator
{
    private const string ActualSubject = "ACTUAL";
    private static readonly HashSet<string> PodiumPickTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "1",
        "2",
        "3"
    };

    private readonly IDbContextFactory<F1DbContext> _dbContextFactory;
    private readonly IQuestionScoringStrategyRegistry _questionScoringStrategyRegistry;

    public MigrationScoreRecalculator(
        IDbContextFactory<F1DbContext> dbContextFactory,
        IQuestionScoringStrategyRegistry questionScoringStrategyRegistry)
    {
        _dbContextFactory = dbContextFactory;
        _questionScoringStrategyRegistry = questionScoringStrategyRegistry;
    }

    public MigrationScoreRecalculator(IDbContextFactory<F1DbContext> dbContextFactory)
        : this(
            dbContextFactory,
            new QuestionScoringStrategyRegistry([
                new PreseasonQuestionScoringStrategy(),
                new H2hQuestionScoringStrategy(),
                new RaceBonusQuestionScoringStrategy()
            ]))
    {
    }

    public async Task<MigrationScoreRecalculationResult> RecalculateAndPersistAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var selections = await dbContext.MigrationImportRaceSelections
            .Where(x => x.ImportRunId == runId)
            .OrderBy(x => x.RowNumber)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var preseasonAnswers = await dbContext.MigrationImportPreseasonAnswers
            .Where(x => x.ImportRunId == runId)
            .OrderBy(x => x.RowNumber)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var preseasonPolicy = await dbContext.MigrationImportPreseasonPolicies
            .Where(x => x.ImportRunId == runId)
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

        var preseasonImportedTallies = await dbContext.MigrationImportPreseasonImportedTallies
            .Where(x => x.ImportRunId == runId)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        dbContext.MigrationImportCalculatedScores.RemoveRange(
            dbContext.MigrationImportCalculatedScores.Where(x => x.ImportRunId == runId));
        dbContext.MigrationImportPreseasonCalculatedScores.RemoveRange(
            dbContext.MigrationImportPreseasonCalculatedScores.Where(x => x.ImportRunId == runId));
        dbContext.MigrationImportPreseasonCalculatedTotals.RemoveRange(
            dbContext.MigrationImportPreseasonCalculatedTotals.Where(x => x.ImportRunId == runId));

        var genericQuestionAnswers = await dbContext.QuestionAnswers
            .OrderBy(x => x.QuestionTemplateId)
            .ThenBy(x => x.ParticipantId)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var genericQuestionActuals = await dbContext.QuestionActuals
            .OrderBy(x => x.QuestionTemplateId)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var genericQuestionTemplateIds = genericQuestionAnswers.Select(x => x.QuestionTemplateId)
            .Concat(genericQuestionActuals.Select(x => x.QuestionTemplateId))
            .Where(x => x != 0)
            .Distinct()
            .ToArray();

        var genericQuestionTemplates = genericQuestionTemplateIds.Length == 0
            ? []
            : await dbContext.QuestionTemplates
                .Where(x => genericQuestionTemplateIds.Contains(x.Id))
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.QuestionId)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (genericQuestionTemplateIds.Length > 0)
            {
                dbContext.QuestionScores.RemoveRange(
                dbContext.QuestionScores.Where(x => genericQuestionTemplateIds.Contains(x.QuestionTemplateId)));
            }

        if (selections.Count == 0 && preseasonAnswers.Count == 0 && genericQuestionAnswers.Count == 0 && genericQuestionActuals.Count == 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return new MigrationScoreRecalculationResult(
                ScoredPickCount: 0,
                TotalPoints: 0,
                PreseasonScoredQuestionCount: 0,
                PreseasonTotalPoints: 0,
                PreseasonScoringWarningCount: 0);
        }

        var calculatedScores = new List<MigrationImportCalculatedScoreEntity>();
        var groupedByRace = selections.GroupBy(x => x.RaceCode, StringComparer.OrdinalIgnoreCase);

        foreach (var raceGroup in groupedByRace)
        {
            var actualByPickType = raceGroup
                .Where(x => x.IsActualOutcome)
                .GroupBy(x => x.PickType, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    x => x.Key,
                    x => x.OrderBy(y => y.RowNumber).Select(y => y.NormalizedValue).FirstOrDefault(),
                    StringComparer.OrdinalIgnoreCase);

            var actualTop3 = actualByPickType
                .Where(x => PodiumPickTypes.Contains(x.Key) && !string.IsNullOrWhiteSpace(x.Value))
                .Select(x => x.Value!.Trim().ToUpperInvariant())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var actualDnfTokens = ExtractDriverTokens(
                actualByPickType.TryGetValue("DNF", out var dnfActualRaw) ? dnfActualRaw : null);

            var participants = raceGroup
                .Where(x => !x.IsActualOutcome && !string.Equals(x.Subject, ActualSubject, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.RowNumber)
                .ThenBy(x => x.PickType)
                .ThenBy(x => x.Subject)
                .ToList();

            foreach (var participant in participants)
            {
                var score = CalculateScore(participant, actualByPickType, actualTop3, actualDnfTokens);
                calculatedScores.Add(score);
            }
        }

        var questionScoreComputations = genericQuestionAnswers.Count == 0 && genericQuestionActuals.Count == 0
            ? []
            : CalculateGenericQuestionScores(
                runId,
                genericQuestionTemplates,
                genericQuestionAnswers,
                genericQuestionActuals,
                preseasonPolicy,
                preseasonImportedTallies);

        var questionScores = questionScoreComputations
            .Select(computation => new QuestionScoreEntity
            {
                QuestionTemplateId = computation.QuestionTemplateId,
                ParticipantId = computation.ParticipantId,
                ImportedPoints = computation.ImportedPoints,
                CalculatedPoints = computation.CalculatedPoints,
                OverrideScore = computation.ImportedPoints.HasValue && computation.ImportedPoints.Value != computation.CalculatedPoints
                    ? computation.ImportedPoints.Value
                    : null,
                OverrideReasonCode = computation.ImportedPoints.HasValue && computation.ImportedPoints.Value != computation.CalculatedPoints
                    ? computation.ReasonCode
                    : null,
                OverrideSourceRunId = computation.ImportedPoints.HasValue && computation.ImportedPoints.Value != computation.CalculatedPoints
                    ? runId
                    : null,
                DeltaPoints = computation.DeltaPoints,
                RecordedAtUtc = DateTime.UtcNow
            })
            .ToList();

        var fallbackPreseasonCalculatedScores = CalculatePreseasonScores(runId, preseasonAnswers, preseasonPolicy?.PointsPerQuestion);
        var genericPreseasonCalculatedScores = questionScoreComputations
            .Where(x => x.Category == QuestionCategory.Preseason)
            .Select(computation => new MigrationImportPreseasonCalculatedScoreEntity
            {
                ImportRunId = runId,
                RowNumber = computation.SortOrder,
                QuestionKey = computation.QuestionId,
                QuestionText = computation.Prompt,
                Subject = computation.ParticipantId,
                PredictedValue = computation.PredictedAnswer,
                ActualValue = computation.ActualAnswer,
                Points = computation.CalculatedPoints,
                ReasonCode = computation.ReasonCode
            })
            .ToList();

        var preseasonCalculatedScores = MergePreseasonCalculatedScores(
            fallbackPreseasonCalculatedScores,
            genericPreseasonCalculatedScores);
        var preseasonCalculatedTotals = preseasonCalculatedScores
            .GroupBy(x => x.Subject, StringComparer.OrdinalIgnoreCase)
            .Select(group => new MigrationImportPreseasonCalculatedTotalEntity
            {
                ImportRunId = runId,
                Subject = group.Key,
                CalculatedTotalPoints = group.Sum(x => x.Points)
            })
            .ToList();

        if (calculatedScores.Count > 0)
        {
            await dbContext.MigrationImportCalculatedScores.AddRangeAsync(calculatedScores, cancellationToken);
        }

        if (questionScores.Count > 0)
        {
            await dbContext.QuestionScores.AddRangeAsync(questionScores, cancellationToken);
        }

        if (preseasonCalculatedScores.Count > 0)
        {
            await dbContext.MigrationImportPreseasonCalculatedScores.AddRangeAsync(preseasonCalculatedScores, cancellationToken);
        }

        if (preseasonCalculatedTotals.Count > 0)
        {
            await dbContext.MigrationImportPreseasonCalculatedTotals.AddRangeAsync(preseasonCalculatedTotals, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var preseasonScoringWarningCount = preseasonCalculatedScores.Count(x =>
            string.Equals(x.ReasonCode, "PRESEASON_POLICY_MISSING", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.ReasonCode, "PRESEASON_ACTUAL_MISSING", StringComparison.OrdinalIgnoreCase));

        return new MigrationScoreRecalculationResult(
            ScoredPickCount: calculatedScores.Count,
            TotalPoints: calculatedScores.Sum(x => x.Points),
            PreseasonScoredQuestionCount: preseasonCalculatedScores.Count,
            PreseasonTotalPoints: preseasonCalculatedScores.Sum(x => x.Points),
            PreseasonScoringWarningCount: preseasonScoringWarningCount);
    }

    private IReadOnlyList<QuestionScoreComputation> CalculateGenericQuestionScores(
        Guid runId,
        IReadOnlyList<QuestionTemplateEntity> templates,
        IReadOnlyList<QuestionAnswerEntity> answers,
        IReadOnlyList<QuestionActualEntity> actuals,
        MigrationImportPreseasonPolicyEntity? preseasonPolicy,
        IReadOnlyCollection<MigrationImportPreseasonImportedTallyEntity> preseasonImportedTallies)
    {
        if (templates.Count == 0 || answers.Count == 0)
        {
            return [];
        }

        var importedPointsByQuestionAndSubject = preseasonImportedTallies
            .GroupBy(
                x => (QuestionKey: x.QuestionKey?.Trim() ?? string.Empty, Subject: x.Subject?.Trim() ?? string.Empty),
                new QuestionParticipantKeyComparer())
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(x => x.ImportedPoints.HasValue).First().ImportedPoints,
                new QuestionParticipantKeyComparer());

        var scored = new List<QuestionScoreComputation>();
        foreach (var categoryGroup in templates.GroupBy(x => x.Category))
        {
            var strategy = _questionScoringStrategyRegistry.Resolve(categoryGroup.Key);
            var categoryTemplateIds = categoryGroup.Select(x => x.Id).ToHashSet();
            var categoryTemplates = categoryGroup.OrderBy(x => x.SortOrder).ThenBy(x => x.QuestionId).ToList();
            var categoryAnswers = answers.Where(x => categoryTemplateIds.Contains(x.QuestionTemplateId)).ToList();
            var categoryActuals = actuals.Where(x => categoryTemplateIds.Contains(x.QuestionTemplateId)).ToList();

            if (strategy is null)
            {
                scored.AddRange(categoryAnswers.Select(answer =>
                {
                    var template = categoryTemplates.Single(x => x.Id == answer.QuestionTemplateId);
                    var actual = categoryActuals.SingleOrDefault(x => x.QuestionTemplateId == answer.QuestionTemplateId);
                    return new QuestionScoreComputation(
                        QuestionTemplateId: answer.QuestionTemplateId,
                        QuestionId: template.QuestionId,
                        Prompt: template.Prompt,
                        Category: template.Category,
                        ParticipantId: answer.ParticipantId,
                        PredictedAnswer: ResolveEffectiveAnswer(answer),
                        ActualAnswer: ResolveEffectiveAnswer(actual),
                        ImportedPoints: null,
                        CalculatedPoints: 0,
                        DeltaPoints: 0,
                        ReasonCode: "QUESTION_CATEGORY_STRATEGY_MISSING",
                        SortOrder: template.SortOrder);
                }));

                continue;
            }

            scored.AddRange(strategy.Score(new QuestionScoringContext(
                runId,
                categoryTemplates,
                categoryAnswers,
                categoryActuals,
                preseasonPolicy)));
        }

        var hydrated = new List<QuestionScoreComputation>(scored.Count);
        foreach (var score in scored)
        {
            var importedPoints = importedPointsByQuestionAndSubject.TryGetValue((score.QuestionId, score.ParticipantId), out var value)
                ? value
                : null;

            var deltaPoints = importedPoints.HasValue
                ? score.CalculatedPoints - importedPoints.Value
                : 0;

            hydrated.Add(score with
            {
                ImportedPoints = importedPoints,
                DeltaPoints = deltaPoints
            });
        }

        return hydrated;
    }

    private sealed class QuestionParticipantKeyComparer : IEqualityComparer<(string QuestionKey, string Subject)>
    {
        public bool Equals((string QuestionKey, string Subject) x, (string QuestionKey, string Subject) y)
        {
            return string.Equals(x.QuestionKey, y.QuestionKey, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(x.Subject, y.Subject, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode((string QuestionKey, string Subject) obj)
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.QuestionKey),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Subject));
        }
    }

    private static string? ResolveEffectiveAnswer(QuestionAnswerEntity? answer)
    {
        return string.IsNullOrWhiteSpace(answer?.OverrideAnswer) ? answer?.ImportedAnswer : answer.OverrideAnswer;
    }

    private static string? ResolveEffectiveAnswer(QuestionActualEntity? actual)
    {
        return string.IsNullOrWhiteSpace(actual?.OverrideAnswer) ? actual?.ImportedAnswer : actual.OverrideAnswer;
    }

    private static List<MigrationImportPreseasonCalculatedScoreEntity> CalculatePreseasonScores(
        Guid runId,
        IReadOnlyCollection<MigrationImportPreseasonAnswerEntity> answers,
        int? pointsPerQuestion)
    {
        if (answers.Count == 0)
        {
            return [];
        }

        var groupedByQuestion = answers
            .GroupBy(x => new { x.RowNumber, x.QuestionKey, x.QuestionText })
            .OrderBy(x => x.Key.RowNumber)
            .ToList();

        var calculated = new List<MigrationImportPreseasonCalculatedScoreEntity>();

        foreach (var questionGroup in groupedByQuestion)
        {
            var actual = questionGroup.FirstOrDefault(x => x.IsActualOutcome || string.Equals(x.Subject, ActualSubject, StringComparison.OrdinalIgnoreCase));
            var actualValue = NormalizeToken(actual?.NormalizedAnswer);
            var actualTokenSet = BuildPreseasonActualTokenSet(actualValue);

            foreach (var participant in questionGroup.Where(x => !x.IsActualOutcome && !string.Equals(x.Subject, ActualSubject, StringComparison.OrdinalIgnoreCase)))
            {
                var predictedValue = NormalizeToken(participant.NormalizedAnswer);
                var (points, reasonCode) = ScorePreseasonAnswer(predictedValue, actualValue, actualTokenSet, pointsPerQuestion);

                calculated.Add(new MigrationImportPreseasonCalculatedScoreEntity
                {
                    ImportRunId = runId,
                    RowNumber = participant.RowNumber,
                    QuestionKey = participant.QuestionKey,
                    QuestionText = participant.QuestionText,
                    Subject = participant.Subject,
                    PredictedValue = predictedValue,
                    ActualValue = actualValue,
                    Points = points,
                    ReasonCode = reasonCode
                });
            }
        }

        return calculated;
    }

    private static List<MigrationImportPreseasonCalculatedScoreEntity> MergePreseasonCalculatedScores(
        IReadOnlyCollection<MigrationImportPreseasonCalculatedScoreEntity> fallback,
        IReadOnlyCollection<MigrationImportPreseasonCalculatedScoreEntity> preferred)
    {
        if (fallback.Count == 0)
        {
            return preferred.ToList();
        }

        if (preferred.Count == 0)
        {
            return fallback.ToList();
        }

        var merged = preferred.ToDictionary(
            x => (QuestionKey: x.QuestionKey?.Trim() ?? string.Empty, Subject: x.Subject?.Trim() ?? string.Empty),
            new QuestionParticipantKeyComparer());

        foreach (var score in fallback)
        {
            var key = (QuestionKey: score.QuestionKey?.Trim() ?? string.Empty, Subject: score.Subject?.Trim() ?? string.Empty);
            if (!merged.ContainsKey(key))
            {
                merged[key] = score;
            }
        }

        return merged.Values
            .OrderBy(x => x.RowNumber)
            .ThenBy(x => x.QuestionKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Subject, StringComparer.OrdinalIgnoreCase)
            .ToList();
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

    private static MigrationImportCalculatedScoreEntity CalculateScore(
        MigrationImportRaceSelectionEntity participant,
        IReadOnlyDictionary<string, string?> actualByPickType,
        ISet<string> actualTop3,
        ISet<string> actualDnfTokens)
    {
        var predicted = NormalizeToken(participant.NormalizedValue);
        var actualForPickType = actualByPickType.TryGetValue(participant.PickType, out var actualValue)
            ? NormalizeToken(actualValue)
            : null;

        if (PodiumPickTypes.Contains(participant.PickType))
        {
            if (!string.IsNullOrWhiteSpace(predicted) && string.Equals(predicted, actualForPickType, StringComparison.OrdinalIgnoreCase))
            {
                return CreateCalculated(participant, predicted, actualForPickType, 10, "PODIUM_EXACT");
            }

            if (!string.IsNullOrWhiteSpace(predicted) && actualTop3.Contains(predicted))
            {
                return CreateCalculated(participant, predicted, actualForPickType, 5, "PODIUM_TOP3_WRONG_SLOT");
            }

            return CreateCalculated(participant, predicted, actualForPickType, 0, "PODIUM_MISS");
        }

        if (string.Equals(participant.PickType, "DNF", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(predicted))
            {
                if (actualDnfTokens.Count == 0)
                {
                    return CreateCalculated(participant, predicted, actualForPickType, 5, "DNF_BLANK_NO_ACTUAL");
                }

                return CreateCalculated(participant, predicted, actualForPickType, 0, "DNF_BLANK_HAS_ACTUAL");
            }

            if (actualDnfTokens.Contains(predicted))
            {
                return CreateCalculated(participant, predicted, actualForPickType, 5, "DNF_MATCH");
            }

            return CreateCalculated(participant, predicted, actualForPickType, 0, "DNF_MISS");
        }

        return CreateCalculated(participant, predicted, actualForPickType, 0, "UNSUPPORTED_PICKTYPE");
    }

    private static MigrationImportCalculatedScoreEntity CreateCalculated(
        MigrationImportRaceSelectionEntity participant,
        string? predicted,
        string? actual,
        int points,
        string reasonCode)
    {
        return new MigrationImportCalculatedScoreEntity
        {
            ImportRunId = participant.ImportRunId,
            RowNumber = participant.RowNumber,
            RaceCode = participant.RaceCode,
            PickType = participant.PickType,
            Subject = participant.Subject,
            PredictedValue = predicted,
            ActualValue = actual,
            Points = Math.Max(0, points),
            ReasonCode = reasonCode
        };
    }

    private static string? NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToUpperInvariant();
    }

    private static HashSet<string> ExtractDriverTokens(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return DriverCodeRegex()
            .Matches(value.ToUpperInvariant())
            .Select(x => x.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    [GeneratedRegex("[A-Z]{3}", RegexOptions.Compiled)]
    private static partial Regex DriverCodeRegex();

    [GeneratedRegex("\\s*\\|\\s*", RegexOptions.Compiled)]
    private static partial Regex PreseasonActualSplitRegex();
}