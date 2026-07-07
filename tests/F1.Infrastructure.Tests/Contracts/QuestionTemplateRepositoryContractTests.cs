using F1.Core.Interfaces;
using F1.Core.Models;

namespace F1.Infrastructure.Tests.Contracts;

public abstract class QuestionTemplateRepositoryContractTests
{
    protected abstract IQuestionTemplateTestRepository CreateEmptyRepository();

    protected abstract IQuestionTemplateTestRepository CreateRepositoryWithTemplates(params QuestionTemplate[] templates);

    [Fact]
    public async Task UpsertTemplateAsync_PersistsAndReadsBackWithinCompetitionSeasonScope()
    {
        var fixture = CreateEmptyRepository();
        var now = new DateTime(2026, 7, 7, 10, 0, 0, DateTimeKind.Utc);

        var saved = await fixture.Repository.UpsertTemplateAsync(new QuestionTemplate
        {
            CompetitionId = fixture.DefaultCompetitionId,
            Season = 2026,
            QuestionId = "Q-H2H-001",
            Category = QuestionCategory.H2H,
            Prompt = "Who finishes higher?",
            OptionsJson = "{\"left\":\"NOR\",\"right\":\"LEC\"}",
            Status = QuestionTemplateStatus.Published,
            SortOrder = 20,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        var reloaded = await fixture.Repository.GetTemplateAsync(fixture.DefaultCompetitionId, 2026, "Q-H2H-001");

        Assert.NotEqual(0, saved.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(saved.Id, reloaded!.Id);
        Assert.Equal(QuestionCategory.H2H, reloaded.Category);
        Assert.Equal("Who finishes higher?", reloaded.Prompt);
    }

    [Fact]
    public async Task GetTemplatesAsync_ReturnsDeterministicOrderingWithinCompetitionSeason()
    {
        var fixture = CreateRepositoryWithTemplates(
            Template(1, 2026, "Q-003", QuestionCategory.Preseason, sortOrder: 30),
            Template(1, 2026, "Q-001", QuestionCategory.H2H, sortOrder: 10),
            Template(1, 2026, "Q-002", QuestionCategory.H2H, sortOrder: 10),
            Template(1, 2025, "Q-004", QuestionCategory.H2H, sortOrder: 1),
            Template(2, 2026, "Q-005", QuestionCategory.H2H, sortOrder: 1));

        var templates = await fixture.Repository.GetTemplatesAsync(1, 2026);

        Assert.Equal(new[] { "Q-001", "Q-002", "Q-003" }, templates.Select(x => x.QuestionId).ToArray());
    }

    [Fact]
    public async Task GetTemplatesAsync_WhenCategoryFilterProvided_OnlyReturnsMatchingCategory()
    {
        var fixture = CreateRepositoryWithTemplates(
            Template(1, 2026, "Q-H2H-001", QuestionCategory.H2H, sortOrder: 1),
            Template(1, 2026, "Q-PRE-001", QuestionCategory.Preseason, sortOrder: 2));

        var templates = await fixture.Repository.GetTemplatesAsync(1, 2026, QuestionCategory.H2H);

        var result = Assert.Single(templates);
        Assert.Equal("Q-H2H-001", result.QuestionId);
    }

    [Fact]
    public async Task UpsertTemplateAsync_AllowsSameQuestionIdInDifferentSeasonWithoutCollision()
    {
        var fixture = CreateRepositoryWithTemplates(
            Template(1, 2025, "Q-SHARED-001", QuestionCategory.Preseason, sortOrder: 1));

        await fixture.Repository.UpsertTemplateAsync(Template(1, 2026, "Q-SHARED-001", QuestionCategory.Preseason, sortOrder: 1));

        var season2025 = await fixture.Repository.GetTemplatesAsync(1, 2025);
        var season2026 = await fixture.Repository.GetTemplatesAsync(1, 2026);

        Assert.Single(season2025);
        Assert.Single(season2026);
        Assert.Equal("Q-SHARED-001", season2025[0].QuestionId);
        Assert.Equal("Q-SHARED-001", season2026[0].QuestionId);
    }

    [Fact]
    public async Task UpsertTemplateAsync_UpdatesOnlyTheTargetCompetitionSeasonRecord()
    {
        var fixture = CreateRepositoryWithTemplates(
            Template(1, 2026, "Q-001", QuestionCategory.H2H, sortOrder: 1, prompt: "Original"),
            Template(2, 2026, "Q-001", QuestionCategory.H2H, sortOrder: 1, prompt: "Other competition"));

        await fixture.Repository.UpsertTemplateAsync(Template(1, 2026, "Q-001", QuestionCategory.H2H, sortOrder: 5, prompt: "Updated"));

        var target = await fixture.Repository.GetTemplateAsync(1, 2026, "Q-001");
        var other = await fixture.Repository.GetTemplateAsync(2, 2026, "Q-001");

        Assert.NotNull(target);
        Assert.NotNull(other);
        Assert.Equal("Updated", target!.Prompt);
        Assert.Equal(5, target.SortOrder);
        Assert.Equal("Other competition", other!.Prompt);
    }

    private static QuestionTemplate Template(int competitionId, int season, string questionId, QuestionCategory category, int sortOrder, string prompt = "Prompt")
    {
        return new QuestionTemplate
        {
            CompetitionId = competitionId,
            Season = season,
            QuestionId = questionId,
            Category = category,
            Prompt = prompt,
            Status = QuestionTemplateStatus.Published,
            SortOrder = sortOrder,
            CreatedAtUtc = new DateTime(2026, 7, 7, 10, 0, 0, DateTimeKind.Utc),
            UpdatedAtUtc = new DateTime(2026, 7, 7, 10, 0, 0, DateTimeKind.Utc)
        };
    }
}

public interface IQuestionTemplateTestRepository
{
    int DefaultCompetitionId { get; }

    IQuestionTemplateRepository Repository { get; }
}