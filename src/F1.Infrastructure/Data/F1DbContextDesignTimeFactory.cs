using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace F1.Infrastructure.Data;

public sealed class F1DbContextDesignTimeFactory : IDesignTimeDbContextFactory<F1DbContext>
{
    public F1DbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = "Host=localhost;Port=5432;Database=f1;Username=f1;Password=f1";
        }

        var options = new DbContextOptionsBuilder<F1DbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new F1DbContext(options);
    }
}
