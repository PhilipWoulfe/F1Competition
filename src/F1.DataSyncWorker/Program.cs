using F1.Infrastructure.Data;
using F1.DataSyncWorker;
using F1.DataSyncWorker.Clients;
using F1.DataSyncWorker.Options;
using F1.DataSyncWorker.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;

var migrationCliOverrides = MigrationImportCliParser.ParseToConfiguration(args);
var builder = Host.CreateApplicationBuilder(args);

if (migrationCliOverrides.Count > 0)
{
    builder.Configuration.AddInMemoryCollection(migrationCliOverrides);
}

builder.Services
	.AddOptions<DataSyncOptions>()
	.Bind(builder.Configuration.GetSection(DataSyncOptions.SectionName))
	.ValidateDataAnnotations()
	.ValidateOnStart();

builder.Services
	.AddOptions<MigrationImportOptions>()
	.Bind(builder.Configuration.GetSection(MigrationImportOptions.SectionName))
	.ValidateDataAnnotations()
	.ValidateOnStart();

builder.Services
	.AddOptions<MigrationExpectedVarianceOptions>()
	.Bind(builder.Configuration.GetSection(MigrationExpectedVarianceOptions.SectionName))
	.ValidateDataAnnotations()
	.ValidateOnStart();

var postgresConnectionString = builder.Configuration.GetConnectionString("Postgres");
if (string.IsNullOrWhiteSpace(postgresConnectionString))
{
	throw new InvalidOperationException("ConnectionStrings:Postgres must be configured.");
}

builder.Services.AddDbContextFactory<F1DbContext>(options => options.UseNpgsql(postgresConnectionString));

builder.Services
	.AddHttpClient("Jolpica", (sp, client) =>
	{
		var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<DataSyncOptions>>().Value;
		client.BaseAddress = new Uri(options.JolpicaBaseUrl, UriKind.Absolute);
	});

builder.Services.AddSingleton<IJolpicaClient, JolpicaClient>();
builder.Services.AddSingleton<IDataSyncOrchestrator, DataSyncOrchestrator>();
builder.Services.AddSingleton<FileBackedMigrationExpectedVarianceRuleCatalog>();
builder.Services.AddSingleton<IMigrationExpectedVarianceRuleCatalog>(sp =>
	sp.GetRequiredService<FileBackedMigrationExpectedVarianceRuleCatalog>());
builder.Services.AddSingleton<IMigrationExpectedVarianceRuleSetMetadataProvider>(sp =>
	sp.GetRequiredService<FileBackedMigrationExpectedVarianceRuleCatalog>());
builder.Services.AddSingleton<IMigrationImportRunService, MigrationImportRunService>();
builder.Services.AddSingleton<IMigrationImportOrchestrator, MigrationImportOrchestrator>();
builder.Services.AddSingleton<IMigrationImportRowClassifier, MigrationImportRowClassifier>();
builder.Services.AddSingleton<IMigrationRaceSelectionParser, MigrationRaceSelectionParser>();
builder.Services.AddSingleton<IMigrationRaceRoundMapper, MigrationRaceRoundMapper>();
builder.Services.AddSingleton<IQuestionScoringStrategy, PreseasonQuestionScoringStrategy>();
builder.Services.AddSingleton<IQuestionScoringStrategy, H2hQuestionScoringStrategy>();
builder.Services.AddSingleton<IQuestionScoringStrategy, RaceBonusQuestionScoringStrategy>();
builder.Services.AddSingleton<IQuestionScoringStrategyRegistry, QuestionScoringStrategyRegistry>();
builder.Services.AddSingleton<IMigrationScoreRecalculator, MigrationScoreRecalculator>();
builder.Services.AddSingleton<IMigrationLegacyScoreImporter, MigrationLegacyScoreImporter>();
builder.Services.AddSingleton<IMigrationReconciliationService, MigrationReconciliationService>();
builder.Services.AddSingleton<IMigrationCanonicalWriteService, MigrationCanonicalWriteService>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
await host.RunAsync();
