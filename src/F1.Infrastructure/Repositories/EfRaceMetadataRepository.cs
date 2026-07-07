using F1.Core.Interfaces;
using F1.Core.Models;
using F1.Infrastructure.Data;
using F1.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace F1.Infrastructure.Repositories;

public class EfRaceMetadataRepository : IRaceMetadataRepository
{
    private const string H2hTemplateSuffix = "H2H";
    private const string BonusTemplateSuffix = "BONUS";
    private readonly F1DbContext _dbContext;

    public EfRaceMetadataRepository(F1DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<RaceQuestionMetadata?> GetMetadataAsync(string raceId)
    {
        var race = await _dbContext.Races
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == raceId);

        if (race is not null)
        {
            var questionIds = BuildQuestionIds(raceId);
            var templates = await _dbContext.QuestionTemplates
                .AsNoTracking()
                .Where(x =>
                    x.CompetitionId == race.CompetitionId &&
                    x.Season == race.Season &&
                    (x.QuestionId == questionIds.H2hQuestionId || x.QuestionId == questionIds.BonusQuestionId))
                .ToListAsync();

            if (templates.Count > 0)
            {
                return MapFromTemplates(raceId, templates, questionIds.H2hQuestionId, questionIds.BonusQuestionId);
            }
        }

        var entity = await _dbContext.RaceMetadata
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.RaceId == raceId);

        return entity is null ? null : Map(entity);
    }

    public async Task<RaceQuestionMetadata> UpsertMetadataAsync(string raceId, RaceQuestionMetadata metadata)
    {
        var race = await _dbContext.Races.FirstOrDefaultAsync(x => x.Id == raceId);
        if (race is null)
        {
            throw new KeyNotFoundException($"Race document not found for raceId '{raceId}'.");
        }

        var questionIds = BuildQuestionIds(raceId);
        var templates = await _dbContext.QuestionTemplates
            .Where(x =>
                x.CompetitionId == race.CompetitionId &&
                x.Season == race.Season &&
                (x.QuestionId == questionIds.H2hQuestionId || x.QuestionId == questionIds.BonusQuestionId))
            .ToDictionaryAsync(x => x.QuestionId);

        UpsertTemplate(
            templates,
            race.CompetitionId,
            race.Season,
            questionIds.H2hQuestionId,
            QuestionCategory.H2H,
            metadata.H2HQuestion,
            BuildH2hOptionsJson(metadata),
            metadata.IsPublished,
            race.Round * 10 + 1,
            metadata.UpdatedAtUtc);

        UpsertTemplate(
            templates,
            race.CompetitionId,
            race.Season,
            questionIds.BonusQuestionId,
            QuestionCategory.RaceBonus,
            metadata.BonusQuestion,
            null,
            metadata.IsPublished,
            race.Round * 10 + 2,
            metadata.UpdatedAtUtc);

        await _dbContext.SaveChangesAsync();

        return MapFromTemplates(
            raceId,
            templates.Values.ToList(),
            questionIds.H2hQuestionId,
            questionIds.BonusQuestionId);
    }

    private void UpsertTemplate(
        IDictionary<string, QuestionTemplateEntity> templates,
        int competitionId,
        int season,
        string questionId,
        QuestionCategory category,
        string prompt,
        string? optionsJson,
        bool isPublished,
        int sortOrder,
        DateTime updatedAtUtc)
    {
        if (!templates.TryGetValue(questionId, out var entity))
        {
            entity = new QuestionTemplateEntity
            {
                CompetitionId = competitionId,
                Season = season,
                QuestionId = questionId,
                CreatedAtUtc = updatedAtUtc
            };
            templates[questionId] = entity;
            _dbContext.QuestionTemplates.Add(entity);
        }

        entity.Category = category;
        entity.Prompt = prompt;
    entity.OptionsJson = optionsJson;
        entity.Status = isPublished ? QuestionTemplateStatus.Published : QuestionTemplateStatus.Draft;
        entity.SortOrder = sortOrder;
        entity.UpdatedAtUtc = updatedAtUtc;
        if (entity.CreatedAtUtc == default)
        {
            entity.CreatedAtUtc = updatedAtUtc;
        }
    }

    private static RaceQuestionMetadata MapFromTemplates(
        string raceId,
        IReadOnlyCollection<QuestionTemplateEntity> templates,
        string h2hQuestionId,
        string bonusQuestionId)
    {
        var h2h = templates.FirstOrDefault(x => string.Equals(x.QuestionId, h2hQuestionId, StringComparison.OrdinalIgnoreCase));
        var bonus = templates.FirstOrDefault(x => string.Equals(x.QuestionId, bonusQuestionId, StringComparison.OrdinalIgnoreCase));
        var h2hOptions = ParseH2hOptions(h2h?.OptionsJson);

        return new RaceQuestionMetadata
        {
            RaceId = raceId,
            H2HQuestion = h2h?.Prompt ?? string.Empty,
            H2HLeftDriverId = h2hOptions?.LeftDriverId,
            H2HRightDriverId = h2hOptions?.RightDriverId,
            H2HPoints = h2hOptions?.PointsForCorrectPick,
            BonusQuestion = bonus?.Prompt ?? string.Empty,
            IsPublished = h2h?.Status == QuestionTemplateStatus.Published && bonus?.Status == QuestionTemplateStatus.Published,
            UpdatedAtUtc = new[] { h2h?.UpdatedAtUtc, bonus?.UpdatedAtUtc }
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .DefaultIfEmpty(DateTime.MinValue)
                .Max(),
            ETag = null
        };
    }

    private static (string H2hQuestionId, string BonusQuestionId) BuildQuestionIds(string raceId)
    {
        return ($"{raceId}:{H2hTemplateSuffix}", $"{raceId}:{BonusTemplateSuffix}");
    }

    private static string? BuildH2hOptionsJson(RaceQuestionMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata.H2HLeftDriverId) ||
            string.IsNullOrWhiteSpace(metadata.H2HRightDriverId) ||
            !metadata.H2HPoints.HasValue)
        {
            return null;
        }

        return JsonSerializer.Serialize(new H2hQuestionTemplateOptions
        {
            LeftDriverId = metadata.H2HLeftDriverId.Trim().ToUpperInvariant(),
            RightDriverId = metadata.H2HRightDriverId.Trim().ToUpperInvariant(),
            PointsForCorrectPick = metadata.H2HPoints.Value
        });
    }

    private static H2hQuestionTemplateOptions? ParseH2hOptions(string? optionsJson)
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

    private static RaceQuestionMetadata Map(RaceMetadataEntity entity)
    {
        return new RaceQuestionMetadata
        {
            RaceId = entity.RaceId,
            H2HQuestion = entity.H2HQuestion,
            H2HLeftDriverId = null,
            H2HRightDriverId = null,
            H2HPoints = null,
            BonusQuestion = entity.BonusQuestion,
            IsPublished = entity.IsPublished,
            UpdatedAtUtc = entity.UpdatedAtUtc,
            ETag = null
        };
    }
}
