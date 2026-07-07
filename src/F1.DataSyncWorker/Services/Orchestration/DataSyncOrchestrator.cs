using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using F1.Core.Models;
using F1.Infrastructure.Data;
using F1.DataSyncWorker.Clients;
using F1.DataSyncWorker.Models;
using F1.DataSyncWorker.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace F1.DataSyncWorker.Services;

public sealed class DataSyncOrchestrator : IDataSyncOrchestrator
{
    private const int MaxRaceIdLength = 128;
    private readonly ILogger<DataSyncOrchestrator> _logger;
    private readonly IDbContextFactory<F1DbContext> _dbContextFactory;
    private readonly IJolpicaClient _jolpicaClient;
    private readonly DataSyncOptions _options;
    private int _migrationsApplied;

    public DataSyncOrchestrator(
        ILogger<DataSyncOrchestrator> logger,
        IDbContextFactory<F1DbContext> dbContextFactory,
        IJolpicaClient jolpicaClient,
        IOptions<DataSyncOptions> options)
    {
        _logger = logger;
        _dbContextFactory = dbContextFactory;
        _jolpicaClient = jolpicaClient;
        _options = options.Value;
    }

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        ValidateOptions(_options);

        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("Data sync run started for {SeasonsCount} seasons and {CompetitionsCount} competitions.", _options.Seasons.Count, _options.Competitions.Count);

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        if (_options.AutoMigrate && Interlocked.CompareExchange(ref _migrationsApplied, 1, 0) == 0)
        {
            _logger.LogInformation("Applying EF migrations before data sync.");
            await dbContext.Database.MigrateAsync(cancellationToken);
        }

        var competitionMap = await UpsertCompetitionsAsync(dbContext, cancellationToken);
        var driversResult = await UpsertDriversAsync(dbContext, cancellationToken);
        var racesResult = await UpsertRacesAsync(dbContext, competitionMap, cancellationToken);

        stopwatch.Stop();
        _logger.LogInformation(
            "Data sync run completed in {ElapsedMs} ms. Competitions inserted={CompetitionInserted}, updated={CompetitionUpdated}; Drivers inserted={DriverInserted}, updated={DriverUpdated}; Races inserted={RaceInserted}, updated={RaceUpdated}.",
            stopwatch.ElapsedMilliseconds,
            competitionMap.Inserted,
            competitionMap.Updated,
            driversResult.Inserted,
            driversResult.Updated,
            racesResult.Inserted,
            racesResult.Updated);
    }

    private async Task<CompetitionMapResult> UpsertCompetitionsAsync(F1DbContext dbContext, CancellationToken cancellationToken)
    {
        var inserted = 0;
        var updated = 0;
        var entitiesByKey = new Dictionary<string, Competition>(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in _options.Competitions)
        {
            var existing = await dbContext.Competitions
                .FirstOrDefaultAsync(
                    x => x.Year == definition.Year && x.Name == definition.Name,
                    cancellationToken);

            if (existing is null)
            {
                existing = new Competition
                {
                    Name = definition.Name,
                    Year = definition.Year,
                    Description = definition.Description
                };
                dbContext.Competitions.Add(existing);
                inserted++;
            }
            else
            {
                var changed = false;
                if (!string.Equals(existing.Description, definition.Description, StringComparison.Ordinal))
                {
                    existing.Description = definition.Description;
                    changed = true;
                }

                if (changed)
                {
                    updated++;
                }
            }

            entitiesByKey[definition.Key] = existing;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var map = entitiesByKey.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Id,
            StringComparer.OrdinalIgnoreCase);

        return new CompetitionMapResult(map, inserted, updated);
    }

    private async Task<MutationResult> UpsertDriversAsync(F1DbContext dbContext, CancellationToken cancellationToken)
    {
        var allDrivers = new Dictionary<string, JolpicaDriverDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var season in _options.Seasons.Select(x => x.Season).Distinct())
        {
            var seasonDrivers = await _jolpicaClient.GetDriversAsync(
                season,
                _options.HttpRetryCount,
                _options.HttpRetryDelayMs,
                cancellationToken);

            foreach (var driver in seasonDrivers)
            {
                if (string.IsNullOrWhiteSpace(driver.DriverId))
                {
                    continue;
                }

                allDrivers[driver.DriverId] = driver;
            }
        }

        var candidateIds = allDrivers.Keys.ToList();
        var existingDrivers = await dbContext.Drivers
            .Where(x => candidateIds.Contains(x.DriverId!))
            .ToDictionaryAsync(x => x.DriverId!, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var inserted = 0;
        var updated = 0;
        foreach (var driverDto in allDrivers.Values)
        {
            var parsedNumber = int.TryParse(driverDto.PermanentNumber, out var permanentNumber)
                ? permanentNumber
                : (int?)null;
            var fullName = BuildFullName(driverDto.GivenName, driverDto.FamilyName);

            if (!existingDrivers.TryGetValue(driverDto.DriverId, out var existing))
            {
                dbContext.Drivers.Add(new Driver
                {
                    DriverId = driverDto.DriverId,
                    FullName = fullName,
                    Code = driverDto.Code,
                    PermanentNumber = parsedNumber,
                    Nationality = driverDto.Nationality
                });
                inserted++;
                continue;
            }

            var changed = false;
            changed |= SetIfDifferentNullable(existing.FullName, fullName, value => existing.FullName = value);
            changed |= SetIfDifferentNullable(existing.Code, driverDto.Code, value => existing.Code = value);
            changed |= SetIfDifferentNullable(existing.Nationality, driverDto.Nationality, value => existing.Nationality = value);

            if (existing.PermanentNumber != parsedNumber)
            {
                existing.PermanentNumber = parsedNumber;
                changed = true;
            }

            if (changed)
            {
                updated++;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new MutationResult(inserted, updated);
    }

    private async Task<MutationResult> UpsertRacesAsync(F1DbContext dbContext, CompetitionMapResult competitionMap, CancellationToken cancellationToken)
    {
        var inserted = 0;
        var updated = 0;
        var deadlineOffset = TimeSpan.FromMinutes(_options.DeadlineMinutesBeforeStart);

        foreach (var seasonDefinition in _options.Seasons)
        {
            var races = await _jolpicaClient.GetRacesAsync(
                seasonDefinition.Season,
                _options.HttpRetryCount,
                _options.HttpRetryDelayMs,
                cancellationToken);

            foreach (var competitionKey in seasonDefinition.CompetitionKeys)
            {
                var competitionId = competitionMap.ResolveId(competitionKey);

                foreach (var raceDto in races)
                {
                    if (!TryMapRace(raceDto, seasonDefinition.Season, competitionKey, competitionId, deadlineOffset, out var mappedRace))
                    {
                        _logger.LogWarning("Skipping race with missing or invalid fields. Season={Season}, CompetitionKey={CompetitionKey}, Round={Round}, RaceName={RaceName}", seasonDefinition.Season, competitionKey, raceDto.Round, raceDto.RaceName);
                        continue;
                    }

                    var existing = await dbContext.Races.FirstOrDefaultAsync(
                        x => x.CompetitionId == competitionId && x.Season == mappedRace.Season && x.Round == mappedRace.Round,
                        cancellationToken);

                    if (existing is null)
                    {
                        dbContext.Races.Add(mappedRace);
                        inserted++;
                        continue;
                    }

                    var changed = false;
                    changed |= SetIfDifferent(existing.RaceName, mappedRace.RaceName, value => existing.RaceName = value);
                    changed |= SetIfDifferent(existing.CircuitName, mappedRace.CircuitName, value => existing.CircuitName = value);

                    if (existing.StartTimeUtc != mappedRace.StartTimeUtc)
                    {
                        existing.StartTimeUtc = mappedRace.StartTimeUtc;
                        changed = true;
                    }

                    if (existing.PreQualyDeadlineUtc != mappedRace.PreQualyDeadlineUtc)
                    {
                        existing.PreQualyDeadlineUtc = mappedRace.PreQualyDeadlineUtc;
                        changed = true;
                    }

                    if (existing.FinalDeadlineUtc != mappedRace.FinalDeadlineUtc)
                    {
                        existing.FinalDeadlineUtc = mappedRace.FinalDeadlineUtc;
                        changed = true;
                    }

                    if (!string.Equals(existing.Id, mappedRace.Id, StringComparison.Ordinal))
                    {
                        _logger.LogWarning(
                            "Race identity differs for competition {CompetitionId} season {Season} round {Round}. ExistingId={ExistingId}, ComputedId={ComputedId}. Keeping existing id.",
                            competitionId,
                            mappedRace.Season,
                            mappedRace.Round,
                            existing.Id,
                            mappedRace.Id);
                    }

                    if (changed)
                    {
                        updated++;
                    }
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new MutationResult(inserted, updated);
    }

    private static bool TryMapRace(
        JolpicaRaceDto dto,
        int defaultSeason,
        string competitionKey,
        int competitionId,
        TimeSpan deadlineOffset,
        out Race mappedRace)
    {
        mappedRace = new Race();

        if (!int.TryParse(dto.Season, out var season))
        {
            season = defaultSeason;
        }

        if (!int.TryParse(dto.Round, out var round) || round <= 0)
        {
            return false;
        }

        if (!TryParseRaceStartUtc(dto.Date, dto.Time, out var startUtc))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(dto.RaceName))
        {
            return false;
        }

        var circuitName = dto.Circuit?.CircuitName;
        if (string.IsNullOrWhiteSpace(circuitName))
        {
            return false;
        }

        var raceSlug = ToSlug(dto.RaceName);
        var competitionSlug = BuildCompetitionSlug(competitionKey, season);

        mappedRace = new Race
        {
            Id = BuildRaceId(competitionSlug, season, round, raceSlug),
            CompetitionId = competitionId,
            Season = season,
            Round = round,
            RaceName = dto.RaceName,
            CircuitName = circuitName,
            StartTimeUtc = startUtc,
            PreQualyDeadlineUtc = startUtc - deadlineOffset,
            FinalDeadlineUtc = startUtc - deadlineOffset
        };

        return true;
    }

    private static string BuildFullName(string givenName, string familyName)
    {
        var combined = string.Join(" ", [givenName?.Trim() ?? string.Empty, familyName?.Trim() ?? string.Empty]).Trim();
        return string.IsNullOrWhiteSpace(combined) ? "Unknown Driver" : combined;
    }

    private static bool SetIfDifferent(string existing, string next, Action<string> set)
    {
        if (string.Equals(existing, next, StringComparison.Ordinal))
        {
            return false;
        }

        set(next);
        return true;
    }

    private static bool SetIfDifferentNullable(string? existing, string? next, Action<string?> set)
    {
        if (string.Equals(existing, next, StringComparison.Ordinal))
        {
            return false;
        }

        set(next);
        return true;
    }

    private static bool TryParseRaceStartUtc(string date, string? time, out DateTime startUtc)
    {
        startUtc = default;
        if (string.IsNullOrWhiteSpace(date) || string.IsNullOrWhiteSpace(time))
        {
            return false;
        }

        var timestamp = $"{date}T{time}";

        if (!DateTimeOffset.TryParse(timestamp, out var dto))
        {
            return false;
        }

        startUtc = dto.UtcDateTime;
        return true;
    }

    private static string ToSlug(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousDash = false;
        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousDash = false;
                continue;
            }

            if (!previousDash)
            {
                builder.Append('-');
                previousDash = true;
            }
        }

        return builder.ToString().Trim('-');
    }

    private static string BuildCompetitionSlug(string competitionKey, int season)
    {
        var competitionSlug = ToSlug(competitionKey);
        var seasonSuffix = $"-{season}";

        if (!competitionSlug.EndsWith(seasonSuffix, StringComparison.Ordinal))
        {
            return competitionSlug;
        }

        var candidate = competitionSlug[..^seasonSuffix.Length].TrimEnd('-');
        return string.IsNullOrWhiteSpace(candidate) ? competitionSlug : candidate;
    }

    private static string BuildRaceId(string competitionSlug, int season, int round, string raceSlug)
    {
        var rawId = $"{competitionSlug}-{season}-{round}-{raceSlug}";
        if (rawId.Length <= MaxRaceIdLength)
        {
            return rawId;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawId))).ToLowerInvariant();
        var hashSuffix = hash[..12];
        var headLength = Math.Max(1, MaxRaceIdLength - hashSuffix.Length - 1);
        var truncatedHead = rawId[..headLength].TrimEnd('-');
        if (string.IsNullOrWhiteSpace(truncatedHead))
        {
            truncatedHead = rawId[..headLength];
        }

        return $"{truncatedHead}-{hashSuffix}";
    }

    private static void ValidateOptions(DataSyncOptions options)
    {
        var competitionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var competition in options.Competitions)
        {
            if (!competitionKeys.Add(competition.Key))
            {
                throw new InvalidOperationException($"Duplicate competition key configured: '{competition.Key}'.");
            }
        }

        var seasonKeys = new HashSet<int>();
        foreach (var season in options.Seasons)
        {
            if (!seasonKeys.Add(season.Season))
            {
                throw new InvalidOperationException($"Duplicate season mapping configured: '{season.Season}'.");
            }

            foreach (var competitionKey in season.CompetitionKeys)
            {
                if (!competitionKeys.Contains(competitionKey))
                {
                    throw new InvalidOperationException($"Season {season.Season} maps to unknown competition key '{competitionKey}'.");
                }
            }
        }
    }

    private sealed record MutationResult(int Inserted, int Updated);

    private sealed class CompetitionMapResult
    {
        public CompetitionMapResult(Dictionary<string, int> keyToId, int inserted, int updated)
        {
            KeyToId = keyToId;
            Inserted = inserted;
            Updated = updated;
        }

        public Dictionary<string, int> KeyToId { get; }
        public int Inserted { get; }
        public int Updated { get; }

        public int ResolveId(string key)
        {
            if (!KeyToId.TryGetValue(key, out var id))
            {
                throw new InvalidOperationException($"Competition key '{key}' was not seeded.");
            }

            return id;
        }
    }
}