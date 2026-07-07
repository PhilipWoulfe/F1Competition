using F1.DataSyncWorker;
using F1.DataSyncWorker.DependencyInjection;
using F1.DataSyncWorker.Options;
using Microsoft.Extensions.Configuration;

var migrationCliOverrides = MigrationImportCliParser.ParseToConfiguration(args);
var builder = Host.CreateApplicationBuilder(args);

if (migrationCliOverrides.Count > 0)
{
    builder.Configuration.AddInMemoryCollection(migrationCliOverrides);
}

builder.Services
	.AddDataSyncWorker(builder.Configuration);

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
await host.RunAsync();
