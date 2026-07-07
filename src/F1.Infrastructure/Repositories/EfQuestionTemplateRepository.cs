using F1.Core.Interfaces;
using F1.Core.Models;
using F1.Infrastructure.Data;
using F1.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace F1.Infrastructure.Repositories;

public sealed class EfQuestionTemplateRepository : IQuestionTemplateRepository
{
    private readonly F1DbContext _dbContext;

    public EfQuestionTemplateRepository(F1DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<QuestionTemplate?> GetTemplateAsync(int competitionId, int season, string questionId)
    {
        var entity = await _dbContext.QuestionTemplates
            .AsNoTracking()
            .SingleOrDefaultAsync(x =>
                x.CompetitionId == competitionId &&
                x.Season == season &&
                x.QuestionId == questionId);

        return entity is null ? null : Map(entity);
    }

    public async Task<IReadOnlyList<QuestionTemplate>> GetTemplatesAsync(int competitionId, int season, QuestionCategory? category = null)
    {
        var query = _dbContext.QuestionTemplates
            .AsNoTracking()
            .Where(x => x.CompetitionId == competitionId && x.Season == season);

        if (category.HasValue)
        {
            query = query.Where(x => x.Category == category.Value);
        }

        return await query
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.QuestionId)
            .Select(x => Map(x))
            .ToListAsync();
    }

    public async Task<QuestionTemplate> UpsertTemplateAsync(QuestionTemplate template)
    {
        if (template.CompetitionId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(template.CompetitionId), "CompetitionId must be a positive identifier.");
        }

        if (template.Season <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(template.Season), "Season must be a positive year.");
        }

        if (string.IsNullOrWhiteSpace(template.QuestionId))
        {
            throw new ArgumentException("QuestionId is required.", nameof(template));
        }

        if (string.IsNullOrWhiteSpace(template.Prompt))
        {
            throw new ArgumentException("Prompt is required.", nameof(template));
        }

        var competitionExists = await _dbContext.Competitions.AnyAsync(x => x.Id == template.CompetitionId);
        if (!competitionExists)
        {
            throw new KeyNotFoundException($"Competition '{template.CompetitionId}' was not found.");
        }

        var existing = await _dbContext.QuestionTemplates
            .SingleOrDefaultAsync(x =>
                x.CompetitionId == template.CompetitionId &&
                x.Season == template.Season &&
                x.QuestionId == template.QuestionId);

        if (existing is null)
        {
            existing = new QuestionTemplateEntity
            {
                CompetitionId = template.CompetitionId,
                Season = template.Season,
                QuestionId = template.QuestionId.Trim(),
                CreatedAtUtc = template.CreatedAtUtc,
                UpdatedAtUtc = template.UpdatedAtUtc
            };
            _dbContext.QuestionTemplates.Add(existing);
        }

        existing.Category = template.Category;
        existing.Prompt = template.Prompt.Trim();
        existing.OptionsJson = template.OptionsJson;
        existing.Status = template.Status;
        existing.SortOrder = template.SortOrder;
        existing.UpdatedAtUtc = template.UpdatedAtUtc;
        if (existing.CreatedAtUtc == default)
        {
            existing.CreatedAtUtc = template.CreatedAtUtc;
        }

        await _dbContext.SaveChangesAsync();
        return Map(existing);
    }

    private static QuestionTemplate Map(QuestionTemplateEntity entity)
    {
        return new QuestionTemplate
        {
            Id = entity.Id,
            CompetitionId = entity.CompetitionId,
            Season = entity.Season,
            QuestionId = entity.QuestionId,
            Category = entity.Category,
            Prompt = entity.Prompt,
            OptionsJson = entity.OptionsJson,
            Status = entity.Status,
            SortOrder = entity.SortOrder,
            CreatedAtUtc = entity.CreatedAtUtc,
            UpdatedAtUtc = entity.UpdatedAtUtc
        };
    }
}