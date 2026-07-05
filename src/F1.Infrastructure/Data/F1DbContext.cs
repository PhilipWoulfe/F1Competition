using F1.Core.Models;
using F1.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace F1.Infrastructure.Data;

public class F1DbContext : DbContext
{
    public F1DbContext(DbContextOptions<F1DbContext> options)
        : base(options)
    {
    }

    public DbSet<Competition> Competitions => Set<Competition>();
    public DbSet<Race> Races => Set<Race>();
    public DbSet<Driver> Drivers => Set<Driver>();
    public DbSet<Selection> Selections => Set<Selection>();
    public DbSet<SelectionPositionEntity> SelectionPositions => Set<SelectionPositionEntity>();
    public DbSet<RaceMetadataEntity> RaceMetadata => Set<RaceMetadataEntity>();
    public DbSet<MigrationImportRunEntity> MigrationImportRuns => Set<MigrationImportRunEntity>();
    public DbSet<MigrationImportRawRowEntity> MigrationImportRawRows => Set<MigrationImportRawRowEntity>();
    public DbSet<MigrationImportRaceSelectionEntity> MigrationImportRaceSelections => Set<MigrationImportRaceSelectionEntity>();
    public DbSet<MigrationImportUnresolvedTokenEntity> MigrationImportUnresolvedTokens => Set<MigrationImportUnresolvedTokenEntity>();
    public DbSet<MigrationImportJolpicaRaceSnapshotEntity> MigrationImportJolpicaRaceSnapshots => Set<MigrationImportJolpicaRaceSnapshotEntity>();
    public DbSet<MigrationImportRaceRoundMappingEntity> MigrationImportRaceRoundMappings => Set<MigrationImportRaceRoundMappingEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Competition>(entity =>
        {
            entity.ToTable("Competitions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500);
        });

        modelBuilder.Entity<Race>(entity =>
        {
            entity.ToTable("Races");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(128);
            entity.Property(x => x.RaceName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.CircuitName).HasMaxLength(200).IsRequired();

            entity.HasOne<Competition>()
                .WithMany()
                .HasForeignKey(x => x.CompetitionId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(x => new { x.CompetitionId, x.Season, x.Round }).IsUnique();
        });

        modelBuilder.Entity<Driver>(entity =>
        {
            entity.ToTable("Drivers");
            entity.HasKey(x => x.DriverId);
            entity.Property(x => x.DriverId).HasMaxLength(64).IsRequired();
            entity.Property(x => x.FullName).HasMaxLength(200);
            entity.Property(x => x.Code).HasMaxLength(8);
            entity.Property(x => x.Nationality).HasMaxLength(100);
            entity.Ignore(x => x.Id);
        });

        modelBuilder.Entity<Selection>(entity =>
        {
            entity.ToTable("Selections");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.UserId).HasMaxLength(256).IsRequired();
            entity.Property(x => x.RaceId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.BetType).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Ignore(x => x.OrderedSelections);
            entity.Ignore(x => x.IsLocked);
            entity.HasIndex(x => new { x.RaceId, x.UserId }).IsUnique();

            entity.HasOne<Race>()
                .WithMany()
                .HasForeignKey(x => x.RaceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SelectionPositionEntity>(entity =>
        {
            entity.ToTable("SelectionPositions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.DriverId).HasMaxLength(64).IsRequired();

            entity.HasOne<Selection>()
                .WithMany()
                .HasForeignKey(x => x.SelectionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<Driver>()
                .WithMany()
                .HasForeignKey(x => x.DriverId)
                .HasPrincipalKey(x => x.DriverId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(x => new { x.SelectionId, x.Position }).IsUnique();
        });

        modelBuilder.Entity<RaceMetadataEntity>(entity =>
        {
            entity.ToTable("RaceMetadata");
            entity.HasKey(x => x.RaceId);
            entity.Property(x => x.RaceId).HasMaxLength(128);
            entity.Property(x => x.H2HQuestion).HasMaxLength(500).IsRequired();
            entity.Property(x => x.BonusQuestion).HasMaxLength(500).IsRequired();

            entity.HasOne<Race>()
                .WithMany()
                .HasForeignKey(x => x.RaceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MigrationImportRunEntity>(entity =>
        {
            entity.ToTable("MigrationImportRuns");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SourceFilePath).HasMaxLength(512).IsRequired();
            entity.Property(x => x.SourceFileChecksum).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.Property(x => x.ErrorMessage).HasMaxLength(4000);
            entity.HasIndex(x => x.SourceFileChecksum);
            entity.HasIndex(x => x.StartedAtUtc);
        });

        modelBuilder.Entity<MigrationImportRawRowEntity>(entity =>
        {
            entity.ToTable("MigrationImportRawRows");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SectionType).HasMaxLength(64).IsRequired();
            entity.Property(x => x.RawPayload).HasColumnType("text").IsRequired();
            entity.Property(x => x.ClassificationReason).HasMaxLength(512);

            entity.HasOne<MigrationImportRunEntity>()
                .WithMany()
                .HasForeignKey(x => x.ImportRunId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.ImportRunId, x.RowNumber }).IsUnique();
        });

        modelBuilder.Entity<MigrationImportRaceSelectionEntity>(entity =>
        {
            entity.ToTable("MigrationImportRaceSelections");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.RaceCode).HasMaxLength(16).IsRequired();
            entity.Property(x => x.PickType).HasMaxLength(16).IsRequired();
            entity.Property(x => x.Subject).HasMaxLength(128).IsRequired();
            entity.Property(x => x.RawValue).HasMaxLength(512);
            entity.Property(x => x.NormalizedValue).HasMaxLength(512);

            entity.HasOne<MigrationImportRunEntity>()
                .WithMany()
                .HasForeignKey(x => x.ImportRunId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.ImportRunId, x.RaceCode, x.PickType, x.Subject, x.RowNumber }).IsUnique();
        });

        modelBuilder.Entity<MigrationImportUnresolvedTokenEntity>(entity =>
        {
            entity.ToTable("MigrationImportUnresolvedTokens");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.RaceCode).HasMaxLength(16).IsRequired();
            entity.Property(x => x.PickType).HasMaxLength(16).IsRequired();
            entity.Property(x => x.Subject).HasMaxLength(128).IsRequired();
            entity.Property(x => x.RawToken).HasMaxLength(512).IsRequired();

            entity.HasOne<MigrationImportRunEntity>()
                .WithMany()
                .HasForeignKey(x => x.ImportRunId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.ImportRunId, x.RowNumber, x.RaceCode, x.PickType, x.Subject, x.RawToken }).IsUnique();
        });

        modelBuilder.Entity<MigrationImportJolpicaRaceSnapshotEntity>(entity =>
        {
            entity.ToTable("MigrationImportJolpicaRaceSnapshots");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.RaceName).HasMaxLength(256).IsRequired();
            entity.Property(x => x.CircuitName).HasMaxLength(256);

            entity.HasOne<MigrationImportRunEntity>()
                .WithMany()
                .HasForeignKey(x => x.ImportRunId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.ImportRunId, x.Season, x.Round }).IsUnique();
        });

        modelBuilder.Entity<MigrationImportRaceRoundMappingEntity>(entity =>
        {
            entity.ToTable("MigrationImportRaceRoundMappings");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SourceRaceCode).HasMaxLength(16).IsRequired();
            entity.Property(x => x.MappedRaceName).HasMaxLength(256);
            entity.Property(x => x.Warning).HasMaxLength(512);

            entity.HasOne<MigrationImportRunEntity>()
                .WithMany()
                .HasForeignKey(x => x.ImportRunId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.ImportRunId, x.RaceSequence }).IsUnique();
        });
    }
}
