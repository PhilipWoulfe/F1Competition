using System.Text.RegularExpressions;
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

    public MigrationScoreRecalculator(IDbContextFactory<F1DbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<MigrationScoreRecalculationResult> RecalculateAndPersistAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var selections = await dbContext.MigrationImportRaceSelections
            .Where(x => x.ImportRunId == runId)
            .OrderBy(x => x.RowNumber)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        dbContext.MigrationImportCalculatedScores.RemoveRange(
            dbContext.MigrationImportCalculatedScores.Where(x => x.ImportRunId == runId));

        if (selections.Count == 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return new MigrationScoreRecalculationResult(ScoredPickCount: 0, TotalPoints: 0);
        }

        var calculatedScores = new List<MigrationImportCalculatedScoreEntity>();
        var groupedByRaceAndPick = selections.GroupBy(x => new { x.RaceCode, x.PickType });

        foreach (var group in groupedByRaceAndPick)
        {
            var participants = group
                .Where(x => !x.IsActualOutcome && !string.Equals(x.Subject, ActualSubject, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (participants.Count == 0)
            {
                continue;
            }

            var actualByPickType = selections
                .Where(x => x.IsActualOutcome && string.Equals(x.RaceCode, group.Key.RaceCode, StringComparison.OrdinalIgnoreCase))
                .GroupBy(x => x.PickType, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.OrderBy(y => y.RowNumber).Select(y => y.NormalizedValue).FirstOrDefault(), StringComparer.OrdinalIgnoreCase);

            var actualTop3 = actualByPickType
                .Where(x => PodiumPickTypes.Contains(x.Key) && !string.IsNullOrWhiteSpace(x.Value))
                .Select(x => x.Value!.Trim().ToUpperInvariant())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var actualDnfTokens = ExtractDriverTokens(
                actualByPickType.TryGetValue("DNF", out var dnfActualRaw) ? dnfActualRaw : null);

            foreach (var participant in participants)
            {
                var score = CalculateScore(participant, actualByPickType, actualTop3, actualDnfTokens);
                calculatedScores.Add(score);
            }
        }

        if (calculatedScores.Count > 0)
        {
            await dbContext.MigrationImportCalculatedScores.AddRangeAsync(calculatedScores, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new MigrationScoreRecalculationResult(
            ScoredPickCount: calculatedScores.Count,
            TotalPoints: calculatedScores.Sum(x => x.Points));
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
}