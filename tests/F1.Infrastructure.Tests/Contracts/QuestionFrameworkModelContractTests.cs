using F1.Core.Models;
using F1.Infrastructure.Data;
using F1.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace F1.Infrastructure.Tests.Contracts;

public sealed class QuestionFrameworkModelContractTests
{
    [Fact]
    public void OnModelCreating_ConfiguresRequiredQuestionContracts()
    {
        using var dbContext = new F1DbContext(CreateOptions());

        var template = dbContext.Model.FindEntityType(typeof(QuestionTemplateEntity));
        var answer = dbContext.Model.FindEntityType(typeof(QuestionAnswerEntity));
        var actual = dbContext.Model.FindEntityType(typeof(QuestionActualEntity));
        var score = dbContext.Model.FindEntityType(typeof(QuestionScoreEntity));

        Assert.NotNull(template);
        Assert.NotNull(answer);
        Assert.NotNull(actual);
        Assert.NotNull(score);

        Assert.False(template!.FindProperty(nameof(QuestionTemplateEntity.QuestionId))!.IsNullable);
        Assert.False(template.FindProperty(nameof(QuestionTemplateEntity.Prompt))!.IsNullable);
        Assert.False(template.FindProperty(nameof(QuestionTemplateEntity.Category))!.IsNullable);
        Assert.False(template.FindProperty(nameof(QuestionTemplateEntity.Status))!.IsNullable);

        Assert.False(answer!.FindProperty(nameof(QuestionAnswerEntity.ParticipantId))!.IsNullable);
        Assert.False(actual!.FindProperty(nameof(QuestionActualEntity.QuestionTemplateId))!.IsNullable);
        Assert.False(score!.FindProperty(nameof(QuestionScoreEntity.ParticipantId))!.IsNullable);
        Assert.True(score.FindProperty(nameof(QuestionScoreEntity.OverrideScore))!.IsNullable);
        Assert.True(score.FindProperty(nameof(QuestionScoreEntity.OverrideReasonCode))!.IsNullable);
        Assert.True(score.FindProperty(nameof(QuestionScoreEntity.OverrideSourceRunId))!.IsNullable);

        Assert.Contains(template.GetIndexes(), index => index.IsUnique && Matches(index.Properties, nameof(QuestionTemplateEntity.CompetitionId), nameof(QuestionTemplateEntity.Season), nameof(QuestionTemplateEntity.QuestionId)));
        Assert.Contains(answer.GetIndexes(), index => index.IsUnique && Matches(index.Properties, nameof(QuestionAnswerEntity.QuestionTemplateId), nameof(QuestionAnswerEntity.ParticipantId)));
        Assert.Contains(actual.GetIndexes(), index => index.IsUnique && Matches(index.Properties, nameof(QuestionActualEntity.QuestionTemplateId)));
        Assert.Contains(score.GetIndexes(), index => index.IsUnique && Matches(index.Properties, nameof(QuestionScoreEntity.QuestionTemplateId), nameof(QuestionScoreEntity.ParticipantId)));
        Assert.Contains(score.GetIndexes(), index => Matches(index.Properties, nameof(QuestionScoreEntity.OverrideSourceRunId)));
    }

    [Fact]
    public void OnModelCreating_StoresQuestionCategoryAsStringSoNewEnumMembersDoNotNeedSchemaChanges()
    {
        using var dbContext = new F1DbContext(CreateOptions());

        var template = dbContext.Model.FindEntityType(typeof(QuestionTemplateEntity));
        var categoryProperty = template!.FindProperty(nameof(QuestionTemplateEntity.Category));
        var statusProperty = template.FindProperty(nameof(QuestionTemplateEntity.Status));

        Assert.NotNull(categoryProperty);
        Assert.NotNull(statusProperty);
        Assert.Equal(typeof(string), categoryProperty!.GetTypeMapping().Converter!.ProviderClrType);
        Assert.Equal(typeof(string), statusProperty!.GetTypeMapping().Converter!.ProviderClrType);
        Assert.Contains(QuestionCategory.Preseason, Enum.GetValues<QuestionCategory>());
        Assert.Contains(QuestionCategory.H2H, Enum.GetValues<QuestionCategory>());
    }

    private static bool Matches(IReadOnlyList<Microsoft.EntityFrameworkCore.Metadata.IProperty> properties, params string[] names)
    {
        return properties.Select(x => x.Name).SequenceEqual(names, StringComparer.Ordinal);
    }

    private static DbContextOptions<F1DbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<F1DbContext>()
            .UseInMemoryDatabase($"question-framework-model-{Guid.NewGuid():N}")
            .Options;
    }
}