using F1.Core.Models;

namespace F1.Core.Interfaces;

public interface IQuestionTemplateRepository
{
    Task<QuestionTemplate?> GetTemplateAsync(int competitionId, int season, string questionId);

    Task<IReadOnlyList<QuestionTemplate>> GetTemplatesAsync(int competitionId, int season, QuestionCategory? category = null);

    Task<QuestionTemplate> UpsertTemplateAsync(QuestionTemplate template);
}