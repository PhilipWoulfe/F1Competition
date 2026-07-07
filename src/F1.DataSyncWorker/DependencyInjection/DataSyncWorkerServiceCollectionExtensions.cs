using F1.DataSyncWorker.Clients;
using F1.DataSyncWorker.Options;
using F1.DataSyncWorker.Services;
using F1.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace F1.DataSyncWorker.DependencyInjection;

public static class DataSyncWorkerServiceCollectionExtensions
{
    public static IServiceCollection AddDataSyncWorker(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<DataSyncOptions>()
            .Bind(configuration.GetSection(DataSyncOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services
            .AddOptions<MigrationImportOptions>()
            .Bind(configuration.GetSection(MigrationImportOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services
            .AddOptions<MigrationExpectedVarianceOptions>()
            .Bind(configuration.GetSection(MigrationExpectedVarianceOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var postgresConnectionString = configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(postgresConnectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:Postgres must be configured.");
        }

        services.AddDbContextFactory<F1DbContext>(options => options.UseNpgsql(postgresConnectionString));

        services
            .AddHttpClient("Jolpica", (sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<DataSyncOptions>>().Value;
                client.BaseAddress = new Uri(options.JolpicaBaseUrl, UriKind.Absolute);
            });

        services.AddSingleton<IJolpicaClient, JolpicaClient>();
        services.AddSingleton<IDataSyncOrchestrator, DataSyncOrchestrator>();
        services.AddSingleton<FileBackedMigrationExpectedVarianceRuleCatalog>();
        services.AddSingleton<IMigrationExpectedVarianceRuleCatalog>(sp =>
            sp.GetRequiredService<FileBackedMigrationExpectedVarianceRuleCatalog>());
        services.AddSingleton<IMigrationExpectedVarianceRuleSetMetadataProvider>(sp =>
            sp.GetRequiredService<FileBackedMigrationExpectedVarianceRuleCatalog>());
        services.AddSingleton<IMigrationImportRunService, MigrationImportRunService>();
        services.AddSingleton<IMigrationImportOrchestrator, MigrationImportOrchestrator>();
        services.AddSingleton<IMigrationImportRowClassifier, MigrationImportRowClassifier>();
        services.AddSingleton<IMigrationRaceSelectionParser, MigrationRaceSelectionParser>();
        services.AddSingleton<IMigrationRaceRoundMapper, MigrationRaceRoundMapper>();
        services.AddSingleton<IQuestionScoringStrategy, PreseasonQuestionScoringStrategy>();
        services.AddSingleton<IQuestionScoringStrategy, H2hQuestionScoringStrategy>();
        services.AddSingleton<IQuestionScoringStrategy, RaceBonusQuestionScoringStrategy>();
        services.AddSingleton<IQuestionScoringStrategyRegistry, QuestionScoringStrategyRegistry>();
        services.AddSingleton<IMigrationScoreRecalculator, MigrationScoreRecalculator>();
        services.AddSingleton<IMigrationLegacyScoreImporter, MigrationLegacyScoreImporter>();
        services.AddSingleton<IMigrationReconciliationService, MigrationReconciliationService>();
        services.AddSingleton<IMigrationCanonicalWriteService, MigrationCanonicalWriteService>();

        return services;
    }
}