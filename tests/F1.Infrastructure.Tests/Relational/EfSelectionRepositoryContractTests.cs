using F1.Core.Interfaces;
using F1.Core.Models;
using F1.Infrastructure.Data;
using F1.Infrastructure.Repositories;
using F1.Infrastructure.Tests.Contracts;
using Microsoft.EntityFrameworkCore;

namespace F1.Infrastructure.Tests.Relational;

[Collection(PostgresContractCollection.Name)]
public class EfSelectionRepositoryContractTests : SelectionRepositoryContractTests
{
    private readonly PostgresTestContainerFixture _fixture;

    public EfSelectionRepositoryContractTests(PostgresTestContainerFixture fixture)
    {
        _fixture = fixture;
    }

    protected override ISelectionRepository CreateEmptyRepository()
    {
        var context = CreateContext();
        SeedCompetitionAndRace(context, "no-such-race", 24);
        SeedCompetitionAndRace(context, "main-2026-2-test-grand-prix", 25);
        SeedDrivers(context);
        return new EfSelectionRepository(context);
    }

    protected override Task<(ISelectionRepository Repo, Selection Existing)> ArrangeRepositoryWithSelection(
        string raceId, string userId, Selection selection)
    {
        var context = CreateContext();
        SeedCompetitionAndRace(context, raceId);
        SeedDrivers(context);

        var repository = new EfSelectionRepository(context);

        var seeded = new Selection
        {
            Id = selection.Id == Guid.Empty ? Guid.NewGuid() : selection.Id,
            RaceId = raceId,
            UserId = userId,
            BetType = selection.BetType,
            SubmittedAtUtc = selection.SubmittedAtUtc,
            OrderedSelections = selection.OrderedSelections
        };

        context.Selections.Add(new Selection
        {
            Id = seeded.Id,
            RaceId = seeded.RaceId,
            UserId = seeded.UserId,
            BetType = seeded.BetType,
            SubmittedAtUtc = seeded.SubmittedAtUtc
        });

        context.SelectionPositions.AddRange(seeded.OrderedSelections.Select(x => new F1.Infrastructure.Data.Entities.SelectionPositionEntity
        {
            SelectionId = seeded.Id,
            Position = x.Position,
            DriverId = x.DriverId
        }));

        context.SaveChanges();

        return Task.FromResult(((ISelectionRepository)repository, seeded));
    }

    private F1DbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<F1DbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        var context = new F1DbContext(options);
        context.Database.EnsureDeleted();
        context.Database.EnsureCreated();
        return context;
    }

    private static void SeedCompetitionAndRace(F1DbContext context, string raceId, int round = 24)
    {
        if (!context.Competitions.Any(x => x.Id == 1))
        {
            context.Competitions.Add(new Competition
            {
                Id = 1,
                Name = "Main Competition",
                Year = 2025,
                Description = "Contract test competition"
            });
        }

        if (context.Races.Any(x => x.Id == raceId))
        {
            context.SaveChanges();
            return;
        }

        context.Races.Add(new Race
        {
            Id = raceId,
            CompetitionId = 1,
            Season = 2025,
            Round = round,
            RaceName = "Abu Dhabi Grand Prix",
            CircuitName = "Yas Marina Circuit",
            StartTimeUtc = new DateTime(2025, 12, 8, 12, 0, 0, DateTimeKind.Utc),
            PreQualyDeadlineUtc = new DateTime(2025, 12, 7, 13, 0, 0, DateTimeKind.Utc),
            FinalDeadlineUtc = new DateTime(2025, 12, 8, 12, 0, 0, DateTimeKind.Utc)
        });

        context.SaveChanges();
    }

    private static void SeedDrivers(F1DbContext context)
    {
        var driverIds = new[] { "norris", "leclerc", "hamilton", "piastri", "verstappen" };
        context.Drivers.AddRange(driverIds.Select(id => new Driver
        {
            DriverId = id,
            FullName = id
        }));
        context.SaveChanges();
    }
}
