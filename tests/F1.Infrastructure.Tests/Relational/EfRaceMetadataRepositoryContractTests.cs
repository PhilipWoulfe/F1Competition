using F1.Core.Models;
using F1.Infrastructure.Data;
using F1.Infrastructure.Data.Entities;
using F1.Infrastructure.Repositories;
using F1.Infrastructure.Tests.Contracts;
using Microsoft.EntityFrameworkCore;

namespace F1.Infrastructure.Tests.Relational;

[Collection(PostgresContractCollection.Name)]
public class EfRaceMetadataRepositoryContractTests : RaceMetadataRepositoryContractTests
{
    private readonly PostgresTestContainerFixture _fixture;

    public EfRaceMetadataRepositoryContractTests(PostgresTestContainerFixture fixture)
    {
        _fixture = fixture;
    }

    protected override IMetadataTestRepository CreateEmptyRepository()
    {
        var context = CreateContext();
        var repository = new EfRaceMetadataRepository(context);
        return new MetadataTestRepository(repository);
    }

    protected override IMetadataTestRepository CreateRepositoryWithMetadata(string raceId, RaceQuestionMetadata metadata)
    {
        var context = CreateContext();
        SeedCompetitionAndRace(context, raceId);

        context.RaceMetadata.Add(new RaceMetadataEntity
        {
            RaceId = raceId,
            H2HQuestion = metadata.H2HQuestion,
            BonusQuestion = metadata.BonusQuestion,
            IsPublished = metadata.IsPublished,
            UpdatedAtUtc = metadata.UpdatedAtUtc
        });
        context.SaveChanges();

        var repository = new EfRaceMetadataRepository(context);
        return new MetadataTestRepository(repository);
    }

    protected override IMetadataTestRepository CreateRepositoryWithRaceDocumentOnly(string raceId)
    {
        var context = CreateContext();
        SeedCompetitionAndRace(context, raceId);

        var repository = new EfRaceMetadataRepository(context);
        return new MetadataTestRepository(repository);
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

    private static void SeedCompetitionAndRace(F1DbContext context, string raceId)
    {
        context.Competitions.Add(new Competition
        {
            Id = 1,
            Name = "Main Competition",
            Year = 2025,
            Description = "Contract test competition"
        });

        context.Races.Add(new Race
        {
            Id = raceId,
            CompetitionId = 1,
            Season = 2025,
            Round = 24,
            RaceName = "Abu Dhabi Grand Prix",
            CircuitName = "Yas Marina Circuit",
            StartTimeUtc = new DateTime(2025, 12, 8, 12, 0, 0, DateTimeKind.Utc),
            PreQualyDeadlineUtc = new DateTime(2025, 12, 7, 13, 0, 0, DateTimeKind.Utc),
            FinalDeadlineUtc = new DateTime(2025, 12, 8, 12, 0, 0, DateTimeKind.Utc)
        });

        context.SaveChanges();
    }

    private sealed class MetadataTestRepository : IMetadataTestRepository
    {
        public MetadataTestRepository(EfRaceMetadataRepository repository)
        {
            Repository = repository;
        }

        public F1.Core.Interfaces.IRaceMetadataRepository Repository { get; }
    }
}
