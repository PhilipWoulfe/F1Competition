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
            QuestionTemplateId = 900,
            ParticipantId = "Philip",
            ImportedAnswer = "YES",
            RecordedAtUtc = DateTime.UtcNow
        });

        dbContext.QuestionActuals.Add(new QuestionActualEntity
        {
            QuestionTemplateId = 900,
            ImportedAnswer = "YES",
            RecordedAtUtc = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync();

        var recalculator = new MigrationScoreRecalculator(
            new TestDbContextFactory(options),
            new QuestionScoringStrategyRegistry([new MockQuestionScoringStrategy()]));

        await recalculator.RecalculateAndPersistAsync(runId, CancellationToken.None);

        var score = await dbContext.QuestionScores.SingleAsync(x => x.ParticipantId == "Philip");
        Assert.Equal(7, score.CalculatedPoints);
    }

    [Fact]
    public void CalculateGenericQuestionScores_ShouldUseStrategyRegistry_WithoutCategorySpecificBranching()
    {
        var sourcePath = FindRepositoryFilePath(
            Path.Combine("src", "F1.DataSyncWorker", "Services", "Scoring", "MigrationScoreRecalculator.cs"),
            Path.Combine("src", "F1.DataSyncWorker", "Services", "MigrationScoreRecalculator.cs"));
        var source = File.ReadAllText(sourcePath);

        var methodStart = source.IndexOf(
            "private IReadOnlyList<QuestionScoreComputation> CalculateGenericQuestionScores(",
            StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "Expected CalculateGenericQuestionScores method to exist.");

        var methodEnd = source.IndexOf(
            "private static List<MigrationImportPreseasonCalculatedScoreEntity> CalculatePreseasonScores(",
            methodStart,
            StringComparison.Ordinal);
        Assert.True(methodEnd > methodStart, "Expected CalculateGenericQuestionScores method boundary to be identifiable.");

        var methodBody = source.Substring(methodStart, methodEnd - methodStart);

        Assert.Contains("_questionScoringStrategyRegistry.Resolve(categoryGroup.Key)", methodBody, StringComparison.Ordinal);
        Assert.DoesNotContain("switch (", methodBody, StringComparison.Ordinal);
        Assert.DoesNotContain("QuestionCategory.Mock", methodBody, StringComparison.Ordinal);
    }

    [Fact]
    public void QuestionCategoryOnboardingDoc_ShouldContainRequiredOnboardingSteps()
    {
        var docPath = GetRepositoryFilePath("docs", "architecture", "question-category-onboarding.md");
        var doc = File.ReadAllText(docPath);

        Assert.Contains("# Question Category Onboarding", doc, StringComparison.Ordinal);
        Assert.Contains("1. Add the category enum value", doc, StringComparison.Ordinal);
        Assert.Contains("Implement `IQuestionScoringStrategy`", doc, StringComparison.Ordinal);
        Assert.Contains("Add one extensibility test", doc, StringComparison.Ordinal);
        Assert.Contains("without adding category-specific branching", doc, StringComparison.Ordinal);
        Assert.Contains("QuestionFrameworkExtensibilityTests", doc, StringComparison.Ordinal);
    }

    private static DbContextOptions<F1DbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<F1DbContext>()
            .UseInMemoryDatabase($"question-extensibility-{Guid.NewGuid():N}")
            .Options;
    }

    private static string GetRepositoryFilePath(params string[] relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "F1Competition.sln")))
            {
                return Path.Combine([directory.FullName, .. relativePath]);
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Unable to locate repository root.");
    }

    private static string FindRepositoryFilePath(params string[] candidateRelativePaths)
    {
        foreach (var candidate in candidateRelativePaths)
        {
            var fullPath = GetRepositoryFilePath(candidate.Split(Path.DirectorySeparatorChar));
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        throw new FileNotFoundException($"Unable to locate any expected source file path. Candidates: {string.Join(", ", candidateRelativePaths)}");
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
                    PredictedAnswer: Resolve(answer),
                    ActualAnswer: Resolve(actual),
                    ImportedPoints: null,
                    CalculatedPoints: string.Equals(Resolve(answer), Resolve(actual), StringComparison.OrdinalIgnoreCase) ? 7 : 0,
                    DeltaPoints: 0,
                    ReasonCode: string.Equals(Resolve(answer), Resolve(actual), StringComparison.OrdinalIgnoreCase) ? "MOCK_MATCH" : "MOCK_MISS",
                    SortOrder: template.SortOrder)
            ];
        }

        private static string? Resolve(QuestionAnswerEntity answer)
        {
            return string.IsNullOrWhiteSpace(answer.OverrideAnswer) ? answer.ImportedAnswer : answer.OverrideAnswer;
        }

        private static string? Resolve(QuestionActualEntity actual)
        {
            return string.IsNullOrWhiteSpace(actual.OverrideAnswer) ? actual.ImportedAnswer : actual.OverrideAnswer;
        }
    }
}