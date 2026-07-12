using System.Text.RegularExpressions;
using F1.Core.Models;
using F1.DataSyncWorker.Models;
using F1.DataSyncWorker.Options;
using F1.Infrastructure.Data;
using F1.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

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
    private const int PodiumExactPointsPostMode = 10;
    private const int PodiumTop3WrongSlotPointsPostMode = 5;
    private const int PodiumExactPointsYesMode = 15;
    private const decimal PodiumTop3WrongSlotPointsYesMode = 7.5m;
    private const int AllModeJackpotPoints = 100;
    private const int RaceBonusBq1ExactPoints = 5;
    private const int RaceBonusBq2PlusExactPoints = 20;
    private const decimal SaudiGapPenaltyPerSecond = 2m;

    private readonly IDbContextFactory<F1DbContext> _dbContextFactory;
    private readonly IQuestionScoringStrategyRegistry _questionScoringStrategyRegistry;
    private readonly MigrationImportOptions _importOptions;

    public MigrationScoreRecalculator(
        IDbContextFactory<F1DbContext> dbContextFactory,
        IQuestionScoringStrategyRegistry questionScoringStrategyRegistry,
        IOptions<MigrationImportOptions> importOptions)
    {
        _dbContextFactory = dbContextFactory;
        _questionScoringStrategyRegistry = questionScoringStrategyRegistry;
        _importOptions = importOptions.Value;
    }

    public MigrationScoreRecalculator(
        IDbContextFactory<F1DbContext> dbContextFactory,
        IQuestionScoringStrategyRegistry questionScoringStrategyRegistry)
        : this(
            dbContextFactory,
            questionScoringStrategyRegistry,
            Microsoft.Extensions.Options.Options.Create(new MigrationImportOptions()))
    {
    }

    public MigrationScoreRecalculator(IDbContextFactory<F1DbContext> dbContextFactory)
        : this(
            dbContextFactory,
            new QuestionScoringStrategyRegistry([
                new PreseasonQuestionScoringStrategy(),
                new H2hQuestionScoringStrategy(),
                new RaceBonusQuestionScoringStrategy()
            ]),
            Microsoft.Extensions.Options.Options.Create(new MigrationImportOptions()))
    {
    }

    public async Task<MigrationScoreRecalculationResult> RecalculateAndPersistAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var sourceFilePath = await dbContext.MigrationImportRuns
            .Where(x => x.Id == runId)
            .Select(x => x.SourceFilePath)
            .SingleOrDefaultAsync(cancellationToken);
        var sourceProfile = MigrationSourceProfileResolver.Resolve(sourceFilePath ?? string.Empty);

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

        var runParticipants = selections
            .Where(x => !x.IsActualOutcome && !string.Equals(x.Subject, ActualSubject, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Subject)
            .Concat(preseasonAnswers
                .Where(x => !x.IsActualOutcome && !string.Equals(x.Subject, ActualSubject, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Subject))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var targetCompetitionId = await ResolveTargetCompetitionIdAsync(
            dbContext,
            sourceProfile,
            runParticipants,
            cancellationToken);

        dbContext.MigrationImportCalculatedScores.RemoveRange(
            dbContext.MigrationImportCalculatedScores.Where(x => x.ImportRunId == runId));
        dbContext.MigrationImportPreseasonCalculatedScores.RemoveRange(
            dbContext.MigrationImportPreseasonCalculatedScores.Where(x => x.ImportRunId == runId));
        dbContext.MigrationImportPreseasonCalculatedTotals.RemoveRange(
            dbContext.MigrationImportPreseasonCalculatedTotals.Where(x => x.ImportRunId == runId));

        List<QuestionAnswerEntity> genericQuestionAnswers;
        List<QuestionActualEntity> genericQuestionActuals;

        var useDaveGenericQuestionScoping = sourceProfile == MigrationSourceProfile.Dave2025Package ||
                            (sourceProfile != MigrationSourceProfile.Phil2025Csv &&
                             selections.Any(x => IsDaveRaceQuestionPickType(x.PickType)));

        if (useDaveGenericQuestionScoping)
        {
            var daveQuestionIds = selections
                .Where(x => IsDaveRaceQuestionPickType(x.PickType))
                .Select(x => BuildDaveRaceQuestionId(x.RaceCode, x.PickType))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var daveTemplateIds = daveQuestionIds.Length == 0
                ? []
                : await dbContext.QuestionTemplates
                    .Where(x =>
                        daveQuestionIds.Contains(x.QuestionId) &&
                        (!targetCompetitionId.HasValue ||
                         (x.CompetitionId == targetCompetitionId.Value && x.Season == _importOptions.Season)))
                    .Select(x => x.Id)
                    .ToArrayAsync(cancellationToken);

            genericQuestionAnswers = daveTemplateIds.Length == 0 || runParticipants.Length == 0
                ? []
                : await dbContext.QuestionAnswers
                    .Where(x => daveTemplateIds.Contains(x.QuestionTemplateId) && runParticipants.Contains(x.ParticipantId))
                    .OrderBy(x => x.QuestionTemplateId)
                    .ThenBy(x => x.ParticipantId)
                    .AsNoTracking()
                    .ToListAsync(cancellationToken);

            genericQuestionActuals = daveTemplateIds.Length == 0
                ? []
                : await dbContext.QuestionActuals
                    .Where(x => daveTemplateIds.Contains(x.QuestionTemplateId))
                    .OrderBy(x => x.QuestionTemplateId)
                    .AsNoTracking()
                    .ToListAsync(cancellationToken);
        }
        else
        {
            if (!targetCompetitionId.HasValue)
            {
                genericQuestionAnswers = await dbContext.QuestionAnswers
                    .OrderBy(x => x.QuestionTemplateId)
                    .ThenBy(x => x.ParticipantId)
                    .AsNoTracking()
                    .ToListAsync(cancellationToken);

                genericQuestionActuals = await dbContext.QuestionActuals
                    .OrderBy(x => x.QuestionTemplateId)
                    .AsNoTracking()
                    .ToListAsync(cancellationToken);
            }
            else
            {
                var scopedTemplateIds = await dbContext.QuestionTemplates
                    .Where(x => x.CompetitionId == targetCompetitionId.Value && x.Season == _importOptions.Season)
                    .Select(x => x.Id)
                    .ToArrayAsync(cancellationToken);

                genericQuestionAnswers = scopedTemplateIds.Length == 0
                    ? []
                    : await dbContext.QuestionAnswers
                        .Where(x => scopedTemplateIds.Contains(x.QuestionTemplateId))
                        .OrderBy(x => x.QuestionTemplateId)
                        .ThenBy(x => x.ParticipantId)
                        .AsNoTracking()
                        .ToListAsync(cancellationToken);

                genericQuestionActuals = scopedTemplateIds.Length == 0
                    ? []
                    : await dbContext.QuestionActuals
                        .Where(x => scopedTemplateIds.Contains(x.QuestionTemplateId))
                        .OrderBy(x => x.QuestionTemplateId)
                        .AsNoTracking()
                        .ToListAsync(cancellationToken);
            }
        }

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

            foreach (var subjectGroup in participants.GroupBy(x => x.Subject, StringComparer.OrdinalIgnoreCase))
            {
                var picksByType = subjectGroup
                    .GroupBy(x => x.PickType, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => group.OrderBy(x => x.RowNumber).First(),
                        StringComparer.OrdinalIgnoreCase);

                var preQualyMode = ResolvePreQualyMode(
                    picksByType.TryGetValue("PQ", out var preQualySelection)
                        ? preQualySelection.NormalizedValue
                        : null);

                var allModeJackpotHit = preQualyMode == PreQualyMode.All && IsAllModeJackpotHit(picksByType, actualByPickType, actualTop3, actualDnfTokens);

                foreach (var participant in subjectGroup)
                {
                    var score = CalculateScore(participant, actualByPickType, actualTop3, actualDnfTokens, preQualyMode, allModeJackpotHit);
                    calculatedScores.Add(score);
                }
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

    private static bool IsDaveRaceQuestionPickType(string pickType)
    {
        return string.Equals(pickType, "H2H", StringComparison.OrdinalIgnoreCase) ||
               pickType.StartsWith("BQ", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<int?> ResolveTargetCompetitionIdAsync(
        F1DbContext dbContext,
        MigrationSourceProfile sourceProfile,
        IReadOnlyCollection<string> participants,
        CancellationToken cancellationToken)
    {
        var competition = await MigrationCompetitionScopeResolver.ResolveCompetitionAsync(
            dbContext,
            _importOptions.Season,
            sourceProfile,
            participants,
            cancellationToken);

        return competition?.Id;
    }

    private static string BuildDaveRaceQuestionId(string raceCode, string pickType)
    {
        return string.Equals(pickType, "H2H", StringComparison.OrdinalIgnoreCase)
            ? $"H2H-{raceCode.ToUpperInvariant()}"
            : $"RB-{raceCode.ToUpperInvariant()}-{pickType.ToUpperInvariant()}";
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
        ISet<string> actualDnfTokens,
        PreQualyMode preQualyMode,
        bool allModeJackpotHit)
    {
        var predicted = NormalizeToken(participant.NormalizedValue);
        var actualForPickType = actualByPickType.TryGetValue(participant.PickType, out var actualValue)
            ? NormalizeToken(actualValue)
            : null;

        if (string.Equals(participant.PickType, "PQ", StringComparison.OrdinalIgnoreCase))
        {
            return CreateCalculated(participant, predicted, actualForPickType, 0, $"PQ_MODE_{preQualyMode.ToString().ToUpperInvariant()}");
        }

        if (preQualyMode == PreQualyMode.All && (PodiumPickTypes.Contains(participant.PickType) || string.Equals(participant.PickType, "DNF", StringComparison.OrdinalIgnoreCase)))
        {
            if (allModeJackpotHit)
            {
                if (string.Equals(participant.PickType, "1", StringComparison.OrdinalIgnoreCase))
                {
                    return CreateCalculated(participant, predicted, actualForPickType, AllModeJackpotPoints, "ALL_MODE_JACKPOT");
                }

                return CreateCalculated(participant, predicted, actualForPickType, 0, "ALL_MODE_JACKPOT_CREDITED_ON_P1");
            }

            return CreateCalculated(participant, predicted, actualForPickType, 0, "ALL_MODE_NO_JACKPOT");
        }

        if (PodiumPickTypes.Contains(participant.PickType))
        {
            decimal exactPoints = preQualyMode == PreQualyMode.Yes ? PodiumExactPointsYesMode : PodiumExactPointsPostMode;
            decimal top3WrongSlotPoints = preQualyMode == PreQualyMode.Yes ? PodiumTop3WrongSlotPointsYesMode : PodiumTop3WrongSlotPointsPostMode;
            var exactReason = preQualyMode == PreQualyMode.Yes ? "PODIUM_EXACT_PQ_YES" : "PODIUM_EXACT";
            var wrongSlotReason = preQualyMode == PreQualyMode.Yes ? "PODIUM_TOP3_WRONG_SLOT_PQ_YES" : "PODIUM_TOP3_WRONG_SLOT";
            var missReason = preQualyMode == PreQualyMode.Yes ? "PODIUM_MISS_PQ_YES" : "PODIUM_MISS";

            if (!string.IsNullOrWhiteSpace(predicted) && string.Equals(predicted, actualForPickType, StringComparison.OrdinalIgnoreCase))
            {
                return CreateCalculated(participant, predicted, actualForPickType, exactPoints, exactReason);
            }

            if (!string.IsNullOrWhiteSpace(predicted) && actualTop3.Contains(predicted))
            {
                return CreateCalculated(participant, predicted, actualForPickType, top3WrongSlotPoints, wrongSlotReason);
            }

            return CreateCalculated(participant, predicted, actualForPickType, 0, missReason);
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

        if (participant.PickType.StartsWith("BQ", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(predicted))
            {
                return CreateCalculated(participant, predicted, actualForPickType, 0, "RACE_BONUS_PREDICTION_NULL");
            }

            if (string.IsNullOrWhiteSpace(actualForPickType))
            {
                return CreateCalculated(participant, predicted, actualForPickType, 0, "RACE_BONUS_ACTUAL_MISSING");
            }

            if (IsSaudiGapBonusPick(participant.RaceCode, participant.PickType))
            {
                if (!TryParseDecimal(predicted, out var predictedGapSeconds) || !TryParseDecimal(actualForPickType, out var actualGapSeconds))
                {
                    return CreateCalculated(participant, predicted, actualForPickType, 0, "RACE_BONUS_NUMERIC_PARSE_FAILED");
                }

                var roundedPredicted = decimal.Round(predictedGapSeconds, 0, MidpointRounding.AwayFromZero);
                var roundedActual = decimal.Round(actualGapSeconds, 0, MidpointRounding.AwayFromZero);
                var gap = decimal.Abs(roundedPredicted - roundedActual);
                var formulaPoints = decimal.Max(0m, RaceBonusBq2PlusExactPoints - (gap * SaudiGapPenaltyPerSecond));

                return formulaPoints > 0m
                    ? CreateCalculated(participant, predicted, actualForPickType, formulaPoints, "RACE_BONUS_FORMULA_SCORED")
                    : CreateCalculated(participant, predicted, actualForPickType, 0m, "RACE_BONUS_FORMULA_ZERO");
            }

            decimal raceBonusPoints = ResolveRaceBonusExactPoints(participant.PickType);

            return string.Equals(predicted, actualForPickType, StringComparison.OrdinalIgnoreCase)
                ? CreateCalculated(participant, predicted, actualForPickType, raceBonusPoints, "RACE_BONUS_EXACT")
                : CreateCalculated(participant, predicted, actualForPickType, 0, "RACE_BONUS_MISS");
        }

        return CreateCalculated(participant, predicted, actualForPickType, 0, "UNSUPPORTED_PICKTYPE");
    }

    private static bool IsAllModeJackpotHit(
        IReadOnlyDictionary<string, MigrationImportRaceSelectionEntity> picksByType,
        IReadOnlyDictionary<string, string?> actualByPickType,
        ISet<string> actualTop3,
        ISet<string> actualDnfTokens)
    {
        if (!picksByType.TryGetValue("1", out var p1) ||
            !picksByType.TryGetValue("2", out var p2) ||
            !picksByType.TryGetValue("3", out var p3) ||
            !picksByType.TryGetValue("DNF", out var dnf))
        {
            return false;
        }

        var p1Predicted = NormalizeToken(p1.NormalizedValue);
        var p2Predicted = NormalizeToken(p2.NormalizedValue);
        var p3Predicted = NormalizeToken(p3.NormalizedValue);

        var p1Actual = actualByPickType.TryGetValue("1", out var p1ActualRaw) ? NormalizeToken(p1ActualRaw) : null;
        var p2Actual = actualByPickType.TryGetValue("2", out var p2ActualRaw) ? NormalizeToken(p2ActualRaw) : null;
        var p3Actual = actualByPickType.TryGetValue("3", out var p3ActualRaw) ? NormalizeToken(p3ActualRaw) : null;

        if (!string.Equals(p1Predicted, p1Actual, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(p2Predicted, p2Actual, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(p3Predicted, p3Actual, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var dnfPredicted = NormalizeToken(dnf.NormalizedValue);
        if (string.IsNullOrWhiteSpace(dnfPredicted))
        {
            return actualDnfTokens.Count == 0;
        }

        return actualDnfTokens.Contains(dnfPredicted);
    }

    private static PreQualyMode ResolvePreQualyMode(string? rawMode)
    {
        var normalized = NormalizeToken(rawMode);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return PreQualyMode.Post;
        }

        if (normalized is "YES" or "Y" or "PRE")
        {
            return PreQualyMode.Yes;
        }

        if (normalized == "ALL")
        {
            return PreQualyMode.All;
        }

        if (normalized is "POST" or "P")
        {
            return PreQualyMode.Post;
        }

        return PreQualyMode.Post;
    }

    private static decimal ResolveRaceBonusExactPoints(string pickType)
    {
        if (!pickType.StartsWith("BQ", StringComparison.OrdinalIgnoreCase))
        {
            return RaceBonusBq1ExactPoints;
        }

        if (pickType.Length <= 2)
        {
            return RaceBonusBq1ExactPoints;
        }

        return int.TryParse(pickType.AsSpan(2), out var bqNumber) && bqNumber >= 2
            ? RaceBonusBq2PlusExactPoints
            : RaceBonusBq1ExactPoints;
    }

    private static bool IsSaudiGapBonusPick(string raceCode, string pickType)
    {
        return string.Equals(raceCode, "jeddah", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(pickType, "BQ2", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseDecimal(string value, out decimal parsed)
    {
        return decimal.TryParse(value, out parsed);
    }

    private enum PreQualyMode
    {
        Post,
        Yes,
        All
    }

    private static MigrationImportCalculatedScoreEntity CreateCalculated(
        MigrationImportRaceSelectionEntity participant,
        string? predicted,
        string? actual,
        decimal points,
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
            Points = decimal.Max(0m, points),
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

        // DNF values can contain either 3-letter codes (BOR) or mapped driver IDs (bortoleto).
        // Tokenize on common delimiters so both representations compare consistently.
        return DnfTokenSplitRegex()
            .Split(value.Trim().ToUpperInvariant())
            .Where(token => token.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    [GeneratedRegex("[\\s,;/|]+", RegexOptions.Compiled)]
    private static partial Regex DnfTokenSplitRegex();

    [GeneratedRegex("\\s*\\|\\s*", RegexOptions.Compiled)]
    private static partial Regex PreseasonActualSplitRegex();
}