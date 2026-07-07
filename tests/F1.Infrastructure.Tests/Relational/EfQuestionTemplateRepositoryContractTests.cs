using F1.Core.Interfaces;
using F1.Core.Models;
using F1.Infrastructure.Data;
using F1.Infrastructure.Repositories;
using F1.Infrastructure.Tests.Contracts;
using Microsoft.EntityFrameworkCore;

namespace F1.Infrastructure.Tests.Relational;

[Collection(PostgresContractCollection.Name)]
public sealed class EfQuestionTemplateRepositoryContractTests : QuestionTemplateRepositoryContractTests
{
    private readonly PostgresTestContainerFixture _fixture;

    public EfQuestionTemplateRepositoryContractTests(PostgresTestContainerFixture fixture)
    {
        _fixture = fixture;
    }

    protected override IQuestionTemplateTestRepository CreateEmptyRepository()
    {
        var context = CreateContext();
        SeedCompetition(context, 1, 2026, "Main Competition");
        var repository = new EfQuestionTemplateRepository(context);
        return new QuestionTemplateTestRepository(repository, 1);
    }

    protected override IQuestionTemplateTestRepository CreateRepositoryWithTemplates(params QuestionTemplate[] templates)
    {
        var context = CreateContext();

        foreach (var competitionId in templates.Select(x => x.CompetitionId).Distinct())
        {
            var sample = templates.First(x => x.CompetitionId == competitionId);
            SeedCompetition(context, competitionId, sample.Season, $"Competition {competitionId}");
        }

        context.QuestionTemplates.AddRange(templates.Select(template => new F1.Infrastructure.Data.Entities.QuestionTemplateEntity
        {
            CompetitionId = template.CompetitionId,
            Season = template.Season,
            QuestionId = template.QuestionId,
            Category = template.Category,
            Prompt = template.Prompt,
            OptionsJson = template.OptionsJson,
            Status = template.Status,
            SortOrder = template.SortOrder,
            CreatedAtUtc = template.CreatedAtUtc,
            UpdatedAtUtc = template.UpdatedAtUtc
        }));
        context.SaveChanges();

        var repository = new EfQuestionTemplateRepository(context);
        return new QuestionTemplateTestRepository(repository, templates.FirstOrDefault()?.CompetitionId ?? 1);
    }

    private F1DbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<F1DbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        var context = new F1DbContext(options);
        context.Database.EnsureDeleted();
        context.Database.EnsureCreated();
        return context;
    }

    private static void SeedCompetition(F1DbContext context, int competitionId, int season, string name)
    {
        if (context.Competitions.Any(x => x.Id == competitionId))
        {
            return;
        }

        context.Competitions.Add(new Competition
        {
            Id = competitionId,
            Name = name,
            Year = season,
            Description = $"Contract test competition {competitionId}"
        });

        context.SaveChanges();
    }

    private sealed class QuestionTemplateTestRepository : IQuestionTemplateTestRepository
    {
        public QuestionTemplateTestRepository(IQuestionTemplateRepository repository, int defaultCompetitionId)
        {
            Repository = repository;
            DefaultCompetitionId = defaultCompetitionId;
        }

        public int DefaultCompetitionId { get; }

        public IQuestionTemplateRepository Repository { get; }
    }
}