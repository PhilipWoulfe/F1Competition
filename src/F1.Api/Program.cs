using F1.Api.Middleware;
using F1.Api.Infrastructure;
using Serilog;
using Serilog.Formatting.Compact;
using F1.Api.Services;
using F1.Core.Interfaces;
using F1.Infrastructure.Data;
using F1.Infrastructure.Repositories;
using F1.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, logConfig) =>
{
    logConfig
        .ReadFrom.Configuration(ctx.Configuration)
        .Enrich.FromLogContext();

    logConfig.WriteTo.Console(new CompactJsonFormatter());

    var configuredLogPath = Environment.GetEnvironmentVariable("LOG_FILE_PATH");
    var writableLogPath = ResolveWritableLogPath(configuredLogPath, "/tmp/f1api-logs", out var fallbackReason);

    if (!string.IsNullOrWhiteSpace(fallbackReason))
    {
        Console.Error.WriteLine($"[Startup] {fallbackReason}");
    }

    if (!string.IsNullOrWhiteSpace(writableLogPath))
    {
        logConfig.WriteTo.File(
            new CompactJsonFormatter(),
            Path.Combine(writableLogPath, "f1api-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30,
            fileSizeLimitBytes: 100 * 1024 * 1024,
            rollOnFileSizeLimit: true,
            shared: true);
    }
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddControllers(); // Add this line to register controller services
builder.Services.AddSwaggerGen();
builder.Services.AddScoped<IRaceService, RaceService>();
builder.Services.AddScoped<IRaceMetadataService, RaceMetadataService>();
builder.Services.AddScoped<ISelectionService, SelectionService>();
if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Test"))
{
    builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
    builder.Services.AddSingleton<IGlobalMockDateService, GlobalMockDateService>();
    builder.Services.AddSingleton<IDateTimeProvider, MockableDateTimeProvider>();
}
else
{
    builder.Services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
}
builder.Services.AddMemoryCache();
builder.Services
    .AddOptions<CloudflareAccessOptions>()
    .Bind(builder.Configuration.GetSection("CloudflareAccess"))
    .Validate(options => !string.IsNullOrWhiteSpace(options.Issuer), "CloudflareAccess:Issuer must be configured.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.Audience), "CloudflareAccess:Audience must be configured.")
    .ValidateOnStart();
builder.Services.AddHttpClient<ICloudflareJwtValidator, CloudflareJwtValidator>();
var postgresConnectionString = builder.Configuration.GetConnectionString("Postgres");
if (string.IsNullOrWhiteSpace(postgresConnectionString))
{
    throw new InvalidOperationException("ConnectionStrings:Postgres must be configured.");
}

builder.Services.AddDbContext<F1DbContext>(options =>
{
    options.UseNpgsql(postgresConnectionString);
});
builder.Services.AddScoped<IDriverRepository, EfDriverRepository>();
builder.Services.AddScoped<IRaceMetadataRepository, EfRaceMetadataRepository>();
builder.Services.AddScoped<ISelectionRepository, EfSelectionRepository>();
builder.Services.AddScoped<IRaceRepository, EfRaceRepository>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowBlazorOrigin",
        policy =>
        {
            if (builder.Environment.IsDevelopment())
            {
                policy.SetIsOriginAllowed(origin => Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.Host == "localhost")
                       .AllowAnyMethod()
                       .AllowAnyHeader();
            }
            else
            {
                var origins = builder.Configuration["AllowedOrigins"]?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [];
                policy.WithOrigins(origins)
                       .AllowAnyMethod()
                       .AllowAnyHeader();
            }
        });
});

builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

var app = builder.Build();

await DatabaseStartupInitializer.InitializeAsync(app.Services, app.Configuration);

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Test"))
{
    app.UseSwagger(c =>
    {
        c.PreSerializeFilters.Add((swagger, httpReq) =>
        {
            // If accessed via Nginx (which adds X-Forwarded-For), tell Swagger we are at /api
            if (httpReq.Headers.ContainsKey("X-Forwarded-For"))
            {
                swagger.Servers = new List<OpenApiServer> { new() { Url = "/api" } };
            }
        });
    });
    app.UseSwaggerUI();
}

app.UseCors("AllowBlazorOrigin");

app.UseMiddleware<CloudflareAccessMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapControllers(); // This line is crucial for mapping your controllers

app.MapGet("/races/results", (IRaceService raceService) =>
{
    var results = raceService.GetMockResults();
    return Results.Ok(results);
}).RequireAuthorization();

app.Run();

static string? ResolveWritableLogPath(string? preferredPath, string fallbackPath, out string? fallbackReason)
{
    fallbackReason = null;

    if (TryEnsureWritableDirectory(preferredPath, out _))
    {
        return preferredPath;
    }

    if (!string.IsNullOrWhiteSpace(preferredPath))
    {
        fallbackReason = $"File logging path '{preferredPath}' is unavailable or not writable by the runtime user. Falling back to '{fallbackPath}'.";
    }

    if (TryEnsureWritableDirectory(fallbackPath, out var fallbackError))
    {
        return fallbackPath;
    }

    fallbackReason = string.IsNullOrWhiteSpace(fallbackReason)
        ? $"File logging disabled: fallback path '{fallbackPath}' is not writable ({fallbackError})."
        : $"{fallbackReason} File logging disabled because fallback path is also not writable ({fallbackError}).";

    return null;
}

static bool TryEnsureWritableDirectory(string? path, out string? error)
{
    error = null;
    if (string.IsNullOrWhiteSpace(path))
    {
        error = "Path is empty.";
        return false;
    }

    try
    {
        Directory.CreateDirectory(path);

        var probeFile = Path.Combine(path, $".write-test-{Guid.NewGuid():N}");
        File.WriteAllText(probeFile, "ok");
        File.Delete(probeFile);

        return true;
    }
    catch (Exception ex)
    {
        error = ex.Message;
        return false;
    }
}



public partial class Program { }
