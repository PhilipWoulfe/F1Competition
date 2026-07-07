using F1.Core.Models;
using F1.DataSyncWorker.Services;
using F1.Infrastructure.Data;
using F1.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace F1.Infrastructure.Tests.Contracts;

public sealed class QuestionFrameworkExtensibilityTests
{
    [Fact]
    public async Task RecalculateAndPersistAsync_WhenCustomCategoryStrategyIsRegistered_ScoresWithoutCoreOrchestratorChanges()
    {
        var runId = Guid.NewGuid();
        var options = CreateOptions();
        await using var dbContext = new F1DbContext(options);

        dbContext.Competitions.Add(new Competition
        {
            Id = 1,
            Name = "Main Competition",
            Year = 2025,
            Description = "Extensibility harness competition"
        });

        dbContext.MigrationImportRuns.Add(new MigrationImportRunEntity
        {
            Id = runId,
            SourceFilePath = "test.csv",
            SourceFileChecksum = "abc",
            IsDryRun = true,
            Status = "Started",
            StartedAtUtc = DateTime.UtcNow
        });

        dbContext.QuestionTemplates.Add(new QuestionTemplateEntity
        {
            Id = 900,
            CompetitionId = 1,
            Season = 2025,
            QuestionId = "MOCK-001",
            Category = QuestionCategory.Mock,
            Prompt = "Mock question",
            Status = QuestionTemplateStatus.Published,
            SortOrder = 1,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        dbContext.QuestionAnswers.Add(new QuestionAnswerEntity
        {
            ImportRunId = runId,
            QuestionTemplateId = 900,
            ParticipantId = "Philip",
            ImportedAnswer = "YES",
            NormalizedAnswer = "YES",
            SourceRow = 20,
            SourceColumn = 2,
            RecordedAtUtc = DateTime.UtcNow
        });

        dbContext.QuestionActuals.Add(new QuestionActualEntity
        {
            ImportRunId = runId,
            QuestionTemplateId = 900,
            ActualAnswer = "YES",
            NormalizedAnswer = "YES",
            SourceRow = 20,
            SourceColumn = 3,
            RecordedAtUtc = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync();

        var recalculator = new MigrationScoreRecalculator(
            new TestDbContextFactory(options),
            new QuestionScoringStrategyRegistry([new MockQuestionScoringStrategy()]));

        await recalculator.RecalculateAndPersistAsync(runId, CancellationToken.None);

        var score = await dbContext.QuestionScores.SingleAsync(x => x.ImportRunId == runId && x.ParticipantId == "Philip");
        Assert.Equal(7, score.CalculatedPoints);
        Assert.Equal("MOCK_MATCH", score.ReasonCode);
    }

    private static DbContextOptions<F1DbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<F1DbContext>()
            .UseInMemoryDatabase($"question-extensibility-{Guid.NewGuid():N}")
            .Options;
    }

    private sealed class TestDbContextFactory : IDbContextFactory<F1DbContext>
    {
        private readonly DbContextOptions<F1DbContext> _options;

        public TestDbContextFactory(DbContextOptions<F1DbContext> options)
        {
            _options = options;
        }

        public F1DbContext CreateDbContext()
        {
            return new F1DbContext(_options);
        }

        public ValueTask<F1DbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(CreateDbContext());
        }
    }

    private sealed class MockQuestionScoringStrategy : IQuestionScoringStrategy
    {
        public QuestionCategory Category => QuestionCategory.Mock;

        public IReadOnlyList<QuestionScoreComputation> Score(QuestionScoringContext context)
        {
            var template = Assert.Single(context.Templates);
            var answer = Assert.Single(context.Answers);
            var actual = Assert.Single(context.Actuals);

            return
            [
                new QuestionScoreComputation(
                    QuestionTemplateId: template.Id,
                    QuestionId: template.QuestionId,
                    Prompt: template.Prompt,
                    Category: template.Category,
                    ParticipantId: answer.ParticipantId,
                    PredictedAnswer: answer.NormalizedAnswer,
                    ActualAnswer: actual.NormalizedAnswer,
                    ImportedPoints: null,
                    CalculatedPoints: string.Equals(answer.NormalizedAnswer, actual.NormalizedAnswer, StringComparison.OrdinalIgnoreCase) ? 7 : 0,
                    DeltaPoints: 0,
                    ReasonCode: string.Equals(answer.NormalizedAnswer, actual.NormalizedAnswer, StringComparison.OrdinalIgnoreCase) ? "MOCK_MATCH" : "MOCK_MISS",
                    SortOrder: template.SortOrder)
            ];
        }
    }
}