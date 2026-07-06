using F1.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace F1.Api.Infrastructure;

public static class DatabaseStartupInitializer
{
    public static async Task InitializeAsync(IServiceProvider serviceProvider, IConfiguration configuration)
    {
        var hostEnvironment = serviceProvider.GetRequiredService<IHostEnvironment>();
        var autoMigrate = configuration.GetValue<bool>("Database:AutoMigrate");
        var shouldAutoMigrate = autoMigrate || hostEnvironment.IsDevelopment();
        if (!shouldAutoMigrate)
        {
            return;
        }

        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<F1DbContext>();

        await dbContext.Database.MigrateAsync();
        Log.Information("Database migration completed.");
    }
}
