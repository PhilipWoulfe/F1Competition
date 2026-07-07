using F1.Api.Configuration;
using F1.Api.Dtos;
using F1.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace F1.Api.Services;

public interface ICompetitionLeaderboardService
{
    Task<CompetitionLeaderboardResponseDto> GetLeaderboardAsync(string competitionSlug, int season, string scoreView, bool isAdmin, CancellationToken cancellationToken = default);
}

public sealed class CompetitionLeaderboardService(F1DbContext dbContext, IOptions<CompetitionLeaderboardOptions> options) : ICompetitionLeaderboardService
{
    private const string SourceTypeMigrationRun = "MigrationRun";
    private const string SourceTypeUnavailable = "Unavailable";
    private const string ViewActive = "active";
    private const string ViewImported = "imported";
    private const string ViewRecalculated = "recalculated";
    private const string ActiveScoreSourceImportedLegacy = "ImportedLegacy";

    public async Task<CompetitionLeaderboardResponseDto> GetLeaderboardAsync(string competitionSlug, int season, string scoreView, bool isAdmin, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(competitionSlug);
        if (season <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(season));
        }

        var normalizedCompetitionSlug = competitionSlug.Trim().ToLowerInvariant();
        var normalizedScoreView = NormalizeScoreView(scoreView);
        var context = options.Value.Contexts.FirstOrDefault(option =>
            string.Equals(option.CompetitionSlug, normalizedCompetitionSlug, StringComparison.OrdinalIgnoreCase)
            && option.Season == season);

        var displayName = context?.DisplayName?.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = $"{ToDisplayName(normalizedCompetitionSlug)} {season}";
        }

        if (context is null || string.Equals(context.SourceType, SourceTypeUnavailable, StringComparison.OrdinalIgnoreCase))
        {
            return CreateUnavailableResponse(
                normalizedCompetitionSlug,
                season,
                displayName,
                normalizedScoreView,
                isAdmin,
                context?.UnavailableMessage ?? "Leaderboard data is not available for this competition yet.");
        }

        if (!string.Equals(context.SourceType, SourceTypeMigrationRun, StringComparison.OrdinalIgnoreCase))
        {
            return CreateUnavailableResponse(
                normalizedCompetitionSlug,
                season,
                displayName,
                normalizedScoreView,
                isAdmin,
                "Leaderboard data source is not supported for this competition.");
        }

        var completedRuns = dbContext.MigrationImportRuns
            .AsNoTracking()
            .Where(run => string.Equals(run.Status, "Completed", StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(context.MigrationSourcePathContains))
        {
            var sourcePathToken = context.MigrationSourcePathContains.Trim().ToLowerInvariant();
            completedRuns = completedRuns.Where(run => run.SourceFilePath.ToLower().Contains(sourcePathToken));
        }

        var sourceRun = await completedRuns
            .OrderByDescending(run => run.FinishedAtUtc ?? run.StartedAtUtc)
            .ThenByDescending(run => run.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (sourceRun is null)
        {
            return CreateUnavailableResponse(
                normalizedCompetitionSlug,
                season,
                displayName,
                normalizedScoreView,
                isAdmin,
                "No approved leaderboard run is available for this competition yet.");
        }

        var raceTotals = await dbContext.MigrationImportParticipantDeltaSummaries
            .AsNoTracking()
            .Where(row => row.ImportRunId == sourceRun.Id)
            .ToListAsync(cancellationToken);

        var preseasonTotals = await dbContext.MigrationImportPreseasonParticipantDeltaSummaries
            .AsNoTracking()
            .Where(row => row.ImportRunId == sourceRun.Id)
            .ToListAsync(cancellationToken);

        var combined = raceTotals
            .GroupBy(row => row.Subject, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => new ScoreTotals(
                    ImportedPoints: group.Sum(item => item.ImportedTotalPoints),
                    RecalculatedPoints: group.Sum(item => item.CalculatedTotalPoints)),
                StringComparer.OrdinalIgnoreCase);

        foreach (var preseasonRow in preseasonTotals)
        {
            if (combined.TryGetValue(preseasonRow.Subject, out var existingTotals))
            {
                combined[preseasonRow.Subject] = existingTotals with
                {
                    ImportedPoints = existingTotals.ImportedPoints + preseasonRow.ImportedTotalPoints,
                    RecalculatedPoints = existingTotals.RecalculatedPoints + preseasonRow.CalculatedTotalPoints
                };
            }
            else
            {
                combined[preseasonRow.Subject] = new ScoreTotals(preseasonRow.ImportedTotalPoints, preseasonRow.CalculatedTotalPoints);
            }
        }

        var effectiveView = normalizedScoreView == ViewActive || isAdmin
            ? normalizedScoreView
            : ViewActive;

        var leaderboardItems = combined
            .Select(entry => new CompetitionLeaderboardEntryDto(
                Position: 0,
                ParticipantName: entry.Key,
                DisplayPoints: ResolveDisplayPoints(entry.Value, effectiveView, context.ActiveScoreSource),
                ImportedPoints: entry.Value.ImportedPoints,
                RecalculatedPoints: entry.Value.RecalculatedPoints))
            .OrderByDescending(item => item.DisplayPoints)
            .ThenBy(item => item.ParticipantName, StringComparer.Ordinal)
            .Select((item, index) => item with { Position = index + 1 })
            .ToArray();

        var (scoreSourceLabel, scoreSourceHelperText) = CreateScoreSourceText(context.ActiveScoreSource, effectiveView, displayName);

        return new CompetitionLeaderboardResponseDto(
            CompetitionSlug: normalizedCompetitionSlug,
            Season: season,
            DisplayName: displayName,
            ActiveScoreSource: context.ActiveScoreSource,
            ScoreView: effectiveView,
            ScoreSourceLabel: scoreSourceLabel,
            ScoreSourceHelperText: scoreSourceHelperText,
            IsComparisonAvailable: isAdmin,
            IsDataAvailable: leaderboardItems.Length > 0,
            EmptyStateMessage: leaderboardItems.Length > 0 ? null : "No participant totals are available for this competition yet.",
            SourceRunId: sourceRun.Id,
            Items: leaderboardItems);
    }

    private static CompetitionLeaderboardResponseDto CreateUnavailableResponse(
        string competitionSlug,
        int season,
        string displayName,
        string scoreView,
        bool isAdmin,
        string message)
    {
        var (scoreSourceLabel, scoreSourceHelperText) = CreateScoreSourceText(ActiveScoreSourceImportedLegacy, scoreView, displayName);

        return new CompetitionLeaderboardResponseDto(
            CompetitionSlug: competitionSlug,
            Season: season,
            DisplayName: displayName,
            ActiveScoreSource: ActiveScoreSourceImportedLegacy,
            ScoreView: scoreView,
            ScoreSourceLabel: scoreSourceLabel,
            ScoreSourceHelperText: scoreSourceHelperText,
            IsComparisonAvailable: isAdmin,
            IsDataAvailable: false,
            EmptyStateMessage: message,
            SourceRunId: null,
            Items: []);
    }

    private static int ResolveDisplayPoints(ScoreTotals totals, string scoreView, string activeScoreSource)
    {
        return scoreView switch
        {
            ViewImported => totals.ImportedPoints,
            ViewRecalculated => totals.RecalculatedPoints,
            _ when string.Equals(activeScoreSource, ActiveScoreSourceImportedLegacy, StringComparison.OrdinalIgnoreCase) => totals.ImportedPoints,
            _ => totals.RecalculatedPoints
        };
    }

    private static string NormalizeScoreView(string scoreView)
    {
        return scoreView.Trim().ToLowerInvariant() switch
        {
            ViewImported => ViewImported,
            ViewRecalculated => ViewRecalculated,
            _ => ViewActive
        };
    }

    private static (string Label, string HelperText) CreateScoreSourceText(string activeScoreSource, string scoreView, string displayName)
    {
        var officialLabel = string.Equals(activeScoreSource, ActiveScoreSourceImportedLegacy, StringComparison.OrdinalIgnoreCase)
            ? "Official Source: Imported legacy scores"
            : "Official Source: Recalculated scores";

        if (string.Equals(scoreView, ViewRecalculated, StringComparison.Ordinal))
        {
            return (
                Label: "Compare Mode: Recalculated scores",
                HelperText: $"Admin compare mode is showing recalculated totals. Official standings for {displayName} still use imported legacy scores.");
        }

        if (string.Equals(scoreView, ViewImported, StringComparison.Ordinal))
        {
            return (
                Label: "Compare Mode: Imported legacy scores",
                HelperText: $"Admin compare mode is showing imported legacy totals, which also match the current official standings for {displayName}.");
        }

        return (
            Label: officialLabel,
            HelperText: $"Official standings for {displayName} use imported legacy totals after leaderboard approval.");
    }

    private static string ToDisplayName(string value)
    {
        return string.Join(' ', value.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(segment => char.ToUpperInvariant(segment[0]) + segment[1..]));
    }

    private sealed record ScoreTotals(int ImportedPoints, int RecalculatedPoints);
}