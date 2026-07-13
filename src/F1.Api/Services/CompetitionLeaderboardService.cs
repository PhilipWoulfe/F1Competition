using F1.Api.Configuration;
using F1.Api.Dtos;
using F1.Core.Models;
using F1.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace F1.Api.Services;

public interface ICompetitionLeaderboardService
{
    Task<CompetitionLeaderboardResponseDto> GetLeaderboardAsync(string competitionSlug, int season, string scoreView, bool isAdmin, CancellationToken cancellationToken = default);

    Task<CompetitionParticipantDetailResponseDto> GetParticipantDetailAsync(string competitionSlug, int season, string participantName, CancellationToken cancellationToken = default);
}

public sealed class CompetitionLeaderboardService(F1DbContext dbContext, IOptions<CompetitionLeaderboardOptions> options) : ICompetitionLeaderboardService
{
    private const string SourceTypeUnavailable = "Unavailable";
    private const string ViewActive = "active";
    private const string ViewImported = "imported";
    private const string ViewRecalculated = "recalculated";
    private const string ActiveScoreSourceImportedLegacy = "ImportedLegacy";
    private const string CdpPickType = "CDP";

    public async Task<CompetitionLeaderboardResponseDto> GetLeaderboardAsync(string competitionSlug, int season, string scoreView, bool isAdmin, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(competitionSlug);
        if (season <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(season));
        }

        var normalizedCompetitionSlug = competitionSlug.Trim().ToLowerInvariant();
        var normalizedScoreView = NormalizeScoreView(scoreView);
        var forceRecalculatedOnly = string.Equals(normalizedCompetitionSlug, "david", StringComparison.OrdinalIgnoreCase);
        var requestedScoreView = forceRecalculatedOnly ? ViewRecalculated : normalizedScoreView;
        var context = ResolveContextOption(normalizedCompetitionSlug, season);
        var displayName = GetDisplayName(context, normalizedCompetitionSlug, season);

        if (context is null || string.Equals(context.SourceType, SourceTypeUnavailable, StringComparison.OrdinalIgnoreCase))
        {
            return CreateUnavailableResponse(
                normalizedCompetitionSlug,
                season,
                displayName,
                requestedScoreView,
                isAdmin,
                context?.UnavailableMessage ?? "Leaderboard data is not available for this competition yet.");
        }

        var competition = await ResolveCompetitionAsync(displayName, season, cancellationToken);
        if (competition is null)
        {
            return CreateUnavailableResponse(
                normalizedCompetitionSlug,
                season,
                displayName,
                requestedScoreView,
                isAdmin,
                "No canonical leaderboard data is available for this competition yet.");
        }

        var raceTotals = await dbContext.RacePickScores
            .AsNoTracking()
            .Where(score => score.PickType != CdpPickType)
            .Join(
                dbContext.Races.AsNoTracking().Where(race => race.CompetitionId == competition.Id && race.Season == season),
                score => score.RaceId,
                race => race.Id,
                (score, _) => score)
            .ToListAsync(cancellationToken);

        var questionTotals = await dbContext.QuestionScores
            .AsNoTracking()
            .Join(
                dbContext.QuestionTemplates.AsNoTracking().Where(template => template.CompetitionId == competition.Id && template.Season == season),
                score => score.QuestionTemplateId,
                template => template.Id,
                (score, _) => score)
            .ToListAsync(cancellationToken);

        var combined = raceTotals
            .GroupBy(row => row.ParticipantId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => new ScoreTotals(
                    ImportedPoints: group.Sum(item => (decimal)(item.ImportedPoints ?? 0)),
                    RecalculatedPoints: group.Sum(item => item.CalculatedPoints),
                    ActivePoints: group.Sum(item => item.OverrideScore ?? item.CalculatedPoints),
                    SourceRunId: group.Select(item => (Guid?)item.SourceRunId).OrderByDescending(item => item).FirstOrDefault()),
                StringComparer.OrdinalIgnoreCase);

        foreach (var questionRow in questionTotals)
        {
            if (combined.TryGetValue(questionRow.ParticipantId, out var existingTotals))
            {
                combined[questionRow.ParticipantId] = existingTotals with
                {
                    ImportedPoints = existingTotals.ImportedPoints + (questionRow.ImportedPoints ?? 0),
                    RecalculatedPoints = existingTotals.RecalculatedPoints + questionRow.CalculatedPoints,
                    ActivePoints = existingTotals.ActivePoints + (questionRow.OverrideScore ?? questionRow.CalculatedPoints)
                };
            }
            else
            {
                combined[questionRow.ParticipantId] = new ScoreTotals(
                    ImportedPoints: questionRow.ImportedPoints ?? 0,
                    RecalculatedPoints: questionRow.CalculatedPoints,
                    ActivePoints: questionRow.OverrideScore ?? questionRow.CalculatedPoints,
                    SourceRunId: questionRow.OverrideSourceRunId);
            }
        }

        var effectiveView = forceRecalculatedOnly || requestedScoreView == ViewActive || isAdmin
            ? requestedScoreView
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
            IsComparisonAvailable: !forceRecalculatedOnly && isAdmin,
            IsDataAvailable: leaderboardItems.Length > 0,
            EmptyStateMessage: leaderboardItems.Length > 0 ? null : "No participant totals are available for this competition yet.",
            SourceRunId: combined.Values.Select(item => item.SourceRunId).OrderByDescending(item => item).FirstOrDefault(),
            Items: leaderboardItems);
    }

    public async Task<CompetitionParticipantDetailResponseDto> GetParticipantDetailAsync(string competitionSlug, int season, string participantName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(competitionSlug);
        ArgumentException.ThrowIfNullOrWhiteSpace(participantName);
        if (season <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(season));
        }

        var normalizedCompetitionSlug = competitionSlug.Trim().ToLowerInvariant();
        var normalizedParticipantName = participantName.Trim();
        var context = ResolveContextOption(normalizedCompetitionSlug, season);
        var displayName = GetDisplayName(context, normalizedCompetitionSlug, season);

        var competition = await ResolveCompetitionAsync(displayName, season, cancellationToken);

        var racePickItems = competition is null
            ? []
            : await dbContext.RacePickScores
                .AsNoTracking()
                .Where(score => score.PickType != CdpPickType)
                .Join(
                    dbContext.Races.AsNoTracking().Where(race => race.CompetitionId == competition.Id && race.Season == season),
                    score => score.RaceId,
                    race => race.Id,
                    (score, race) => new
                    {
                        race.Round,
                        score.RaceCode,
                        score.PickType,
                        score.ParticipantId,
                        score.ImportedPoints,
                        score.CalculatedPoints,
                        score.DeltaPoints,
                        score.ReasonCode,
                        score.Explanation
                    })
                .Where(item => item.ParticipantId == normalizedParticipantName)
                .OrderBy(item => item.Round)
                .ThenBy(item => item.PickType)
                .Select(item => new CompetitionParticipantDetailItemDto(
                    item.RaceCode,
                    item.PickType,
                    item.ImportedPoints,
                    item.CalculatedPoints,
                    item.DeltaPoints,
                    item.ReasonCode,
                    item.Explanation))
                .ToArrayAsync(cancellationToken);

        var preseasonItems = competition is null
            ? []
            : await dbContext.QuestionScores
                .AsNoTracking()
                .Join(
                    dbContext.QuestionTemplates.AsNoTracking().Where(template => template.CompetitionId == competition.Id && template.Season == season && template.Category == QuestionCategory.Preseason),
                    score => score.QuestionTemplateId,
                    template => template.Id,
                    (score, template) => new
                    {
                        score.ParticipantId,
                        template.SortOrder,
                        template.QuestionId,
                        template.Prompt,
                        score.ImportedPoints,
                        score.CalculatedPoints,
                        score.DeltaPoints
                    })
                .Where(item => item.ParticipantId == normalizedParticipantName)
                .OrderBy(item => item.SortOrder)
                .Select(item => new CompetitionParticipantDetailItemDto(
                    item.QuestionId,
                    item.Prompt,
                    item.ImportedPoints,
                    item.CalculatedPoints,
                    item.DeltaPoints,
                    null,
                    null))
                .ToArrayAsync(cancellationToken);

        var h2hItems = await BuildH2hItemsAsync(displayName, season, normalizedParticipantName, cancellationToken);

        return new CompetitionParticipantDetailResponseDto(
            CompetitionSlug: normalizedCompetitionSlug,
            Season: season,
            DisplayName: displayName,
            ParticipantName: normalizedParticipantName,
            RacePicks: BuildSection("Race Picks", racePickItems),
            Preseason: BuildSection("Preseason Questions", preseasonItems),
            H2h: BuildSection("H2H Questions", h2hItems));
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

    private static decimal ResolveDisplayPoints(ScoreTotals totals, string scoreView, string activeScoreSource)
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

    private CompetitionLeaderboardContextOption? ResolveContextOption(string competitionSlug, int season)
    {
        return options.Value.Contexts.FirstOrDefault(option =>
            string.Equals(option.CompetitionSlug, competitionSlug, StringComparison.OrdinalIgnoreCase)
            && option.Season == season);
    }

    private static string GetDisplayName(CompetitionLeaderboardContextOption? context, string competitionSlug, int season)
    {
        var displayName = context?.DisplayName?.Trim();
        return string.IsNullOrWhiteSpace(displayName)
            ? $"{ToDisplayName(competitionSlug)} {season}"
            : displayName;
    }

    private async Task<Competition?> ResolveCompetitionAsync(string competitionDisplayName, int season, CancellationToken cancellationToken)
    {
        var exactMatch = await dbContext.Competitions
            .AsNoTracking()
            .Where(item => item.Name == competitionDisplayName && item.Year == season)
            .OrderBy(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (exactMatch is not null)
        {
            return exactMatch;
        }

        if (competitionDisplayName.Contains("David", StringComparison.OrdinalIgnoreCase))
        {
            return await dbContext.Competitions
                .AsNoTracking()
                .Where(item => item.Name == competitionDisplayName.Replace("David", "Dave", StringComparison.OrdinalIgnoreCase) && item.Year == season)
                .OrderBy(item => item.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (competitionDisplayName.Contains("Dave", StringComparison.OrdinalIgnoreCase))
        {
            return await dbContext.Competitions
                .AsNoTracking()
                .Where(item => item.Name == competitionDisplayName.Replace("Dave", "David", StringComparison.OrdinalIgnoreCase) && item.Year == season)
                .OrderBy(item => item.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return null;
    }

    private async Task<CompetitionParticipantDetailItemDto[]> BuildH2hItemsAsync(string competitionDisplayName, int season, string participantName, CancellationToken cancellationToken)
    {
        var competition = await dbContext.Competitions
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Name == competitionDisplayName && item.Year == season, cancellationToken);

        if (competition is null)
        {
            return [];
        }

        var rows = await dbContext.QuestionScores
            .AsNoTracking()
            .Where(score => score.ParticipantId == participantName)
            .Join(
                dbContext.QuestionTemplates.AsNoTracking().Where(template => template.CompetitionId == competition.Id && template.Season == season && template.Category == F1.Core.Models.QuestionCategory.H2H),
                score => score.QuestionTemplateId,
                template => template.Id,
                (score, template) => new
                {
                    template.QuestionId,
                    template.Prompt,
                    score.ImportedPoints,
                    score.CalculatedPoints,
                    score.DeltaPoints
                })
            .OrderBy(item => item.QuestionId)
            .Select(item => new CompetitionParticipantDetailItemDto(
                item.QuestionId,
                item.Prompt,
                item.ImportedPoints,
                item.CalculatedPoints,
                item.DeltaPoints,
                null,
                null))
            .ToArrayAsync(cancellationToken);

        return rows;
    }

    private static CompetitionParticipantSectionSummaryDto BuildSection(string title, IReadOnlyList<CompetitionParticipantDetailItemDto> items)
    {
        return new CompetitionParticipantSectionSummaryDto(
            Title: title,
            ImportedTotalPoints: items.Sum(item => item.ImportedPoints ?? 0),
            RecalculatedTotalPoints: items.Sum(item => item.CalculatedPoints),
            Items: items);
    }

    private sealed record ScoreTotals(decimal ImportedPoints, decimal RecalculatedPoints, decimal ActivePoints, Guid? SourceRunId);
}