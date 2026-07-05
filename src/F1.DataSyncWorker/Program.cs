using F1.Infrastructure.Data;
using F1.DataSyncWorker;
using F1.DataSyncWorker.Clients;
using F1.DataSyncWorker.Options;
using F1.DataSyncWorker.Services;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

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
builder.Services.AddSingleton<IMigrationImportRunService, MigrationImportRunService>();
builder.Services.AddSingleton<IMigrationImportOrchestrator, MigrationImportOrchestrator>();
builder.Services.AddSingleton<IMigrationImportRowClassifier, MigrationImportRowClassifier>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
await host.RunAsync();
