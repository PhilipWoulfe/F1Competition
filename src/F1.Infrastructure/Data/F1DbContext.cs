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
    public DbSet<RacePickScoreEntity> RacePickScores => Set<RacePickScoreEntity>();
    public DbSet<RaceMetadataEntity> RaceMetadata => Set<RaceMetadataEntity>();
    public DbSet<QuestionTemplateEntity> QuestionTemplates => Set<QuestionTemplateEntity>();
    public DbSet<QuestionAnswerEntity> QuestionAnswers => Set<QuestionAnswerEntity>();
    public DbSet<QuestionActualEntity> QuestionActuals => Set<QuestionActualEntity>();
    public DbSet<QuestionScoreEntity> QuestionScores => Set<QuestionScoreEntity>();
    public DbSet<MigrationImportRunEntity> MigrationImportRuns => Set<MigrationImportRunEntity>();
    public DbSet<MigrationImportRawRowEntity> MigrationImportRawRows => Set<MigrationImportRawRowEntity>();
    public DbSet<MigrationImportPreseasonAnswerEntity> MigrationImportPreseasonAnswers => Set<MigrationImportPreseasonAnswerEntity>();
    public DbSet<MigrationImportPreseasonPolicyEntity> MigrationImportPreseasonPolicies => Set<MigrationImportPreseasonPolicyEntity>();
    public DbSet<MigrationImportPreseasonImportedTallyEntity> MigrationImportPreseasonImportedTallies => Set<MigrationImportPreseasonImportedTallyEntity>();
    public DbSet<MigrationImportPreseasonCalculatedScoreEntity> MigrationImportPreseasonCalculatedScores => Set<MigrationImportPreseasonCalculatedScoreEntity>();
    public DbSet<MigrationImportPreseasonCalculatedTotalEntity> MigrationImportPreseasonCalculatedTotals => Set<MigrationImportPreseasonCalculatedTotalEntity>();
    public DbSet<MigrationImportRaceSelectionEntity> MigrationImportRaceSelections => Set<MigrationImportRaceSelectionEntity>();
    public DbSet<MigrationImportCalculatedScoreEntity> MigrationImportCalculatedScores => Set<MigrationImportCalculatedScoreEntity>();
    public DbSet<MigrationImportLegacyPickScoreEntity> MigrationImportLegacyPickScores => Set<MigrationImportLegacyPickScoreEntity>();
    public DbSet<MigrationImportImportedTotalEntity> MigrationImportImportedTotals => Set<MigrationImportImportedTotalEntity>();
    public DbSet<MigrationImportCalculatedTotalEntity> MigrationImportCalculatedTotals => Set<MigrationImportCalculatedTotalEntity>();
    public DbSet<MigrationImportPreseasonQuestionDiffEntity> MigrationImportPreseasonQuestionDiffs => Set<MigrationImportPreseasonQuestionDiffEntity>();
    public DbSet<MigrationImportPreseasonParticipantDeltaSummaryEntity> MigrationImportPreseasonParticipantDeltaSummaries => Set<MigrationImportPreseasonParticipantDeltaSummaryEntity>();
    public DbSet<MigrationImportPreseasonReasonCategorySummaryEntity> MigrationImportPreseasonReasonCategorySummaries => Set<MigrationImportPreseasonReasonCategorySummaryEntity>();
    public DbSet<MigrationImportPickDiffEntity> MigrationImportPickDiffs => Set<MigrationImportPickDiffEntity>();
    public DbSet<MigrationImportRaceDiffEntity> MigrationImportRaceDiffs => Set<MigrationImportRaceDiffEntity>();
    public DbSet<MigrationImportParticipantDeltaSummaryEntity> MigrationImportParticipantDeltaSummaries => Set<MigrationImportParticipantDeltaSummaryEntity>();
    public DbSet<MigrationImportReasonCategorySummaryEntity> MigrationImportReasonCategorySummaries => Set<MigrationImportReasonCategorySummaryEntity>();
    public DbSet<MigrationImportUnresolvedTokenEntity> MigrationImportUnresolvedTokens => Set<MigrationImportUnresolvedTokenEntity>();
    public DbSet<MigrationImportJolpicaRaceSnapshotEntity> MigrationImportJolpicaRaceSnapshots => Set<MigrationImportJolpicaRaceSnapshotEntity>();
    public DbSet<MigrationImportRaceRoundMappingEntity> MigrationImportRaceRoundMappings => Set<MigrationImportRaceRoundMappingEntity>();
    public DbSet<MigrationImportConflictDiagnosticEntity> MigrationImportConflictDiagnostics => Set<MigrationImportConflictDiagnosticEntity>();
    public DbSet<MigrationImportRollbackAuditEntity> MigrationImportRollbackAudits => Set<MigrationImportRollbackAuditEntity>();

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

        modelBuilder.Entity<RacePickScoreEntity>(entity =>
        {
            entity.ToTable("RacePickScores");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.RaceId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.RaceCode).HasMaxLength(64).IsRequired();
            entity.Property(x => x.PickType).HasMaxLength(32).IsRequired();
            entity.Property(x => x.ParticipantId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.PredictedValue).HasMaxLength(256);
            entity.Property(x => x.ActualValue).HasMaxLength(256);
            entity.Property(x => x.CalculatedPoints).HasColumnType("numeric(10,2)");
            entity.Property(x => x.OverrideScore).HasColumnType("numeric(10,2)");
            entity.Property(x => x.DeltaPoints).HasColumnType("numeric(10,2)");
            entity.Property(x => x.OverrideReasonCode).HasMaxLength(64);
            entity.Property(x => x.ReasonCode).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Explanation).HasMaxLength(1024);
            entity.HasIndex(x => new { x.RaceId, x.PickType, x.ParticipantId }).IsUnique();
            entity.HasIndex(x => new { x.RaceId, x.ParticipantId });
            entity.HasIndex(x => x.SourceRunId);

            entity.HasOne<Race>()
                .WithMany()
                .HasForeignKey(x => x.RaceId)
                .OnDelete(DeleteBehavior.Cascade);
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

        modelBuilder.Entity<QuestionTemplateEntity>(entity =>
        {
            entity.ToTable("QuestionTemplates");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.QuestionId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Category).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(x => x.Prompt).HasMaxLength(512).IsRequired();
            entity.Property(x => x.OptionsJson).HasColumnType("text");
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.HasIndex(x => new { x.CompetitionId, x.Season, x.QuestionId }).IsUnique();
            entity.HasIndex(x => new { x.CompetitionId, x.Season, x.Category, x.SortOrder });

            entity.HasOne<Competition>()
                .WithMany()
                .HasForeignKey(x => x.CompetitionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<QuestionAnswerEntity>(entity =>
        {
            entity.ToTable("QuestionAnswers");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ParticipantId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.ImportedAnswer).HasMaxLength(512);
            entity.Property(x => x.OverrideAnswer).HasMaxLength(512);
            entity.HasIndex(x => new { x.QuestionTemplateId, x.ParticipantId }).IsUnique();

            entity.HasOne<QuestionTemplateEntity>()
                .WithMany()
                .HasForeignKey(x => x.QuestionTemplateId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<QuestionActualEntity>(entity =>
        {
            entity.ToTable("QuestionActuals");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ImportedAnswer).HasMaxLength(512);
            entity.Property(x => x.OverrideAnswer).HasMaxLength(512);
            entity.HasIndex(x => x.QuestionTemplateId).IsUnique();

            entity.HasOne<QuestionTemplateEntity>()
                .WithMany()
                .HasForeignKey(x => x.QuestionTemplateId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<QuestionScoreEntity>(entity =>
        {
            entity.ToTable("QuestionScores");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ParticipantId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.OverrideReasonCode).HasMaxLength(64);
            entity.HasIndex(x => new { x.QuestionTemplateId, x.ParticipantId }).IsUnique();
            entity.HasIndex(x => x.DeltaPoints);
            entity.HasIndex(x => x.OverrideSourceRunId);

            entity.HasOne<QuestionTemplateEntity>()
                .WithMany()
                .HasForeignKey(x => x.QuestionTemplateId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<MigrationImportRunEntity>(entity =>
        {
            entity.ToTable("MigrationImportRuns");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SourceFilePath).HasMaxLength(512).IsRequired();
            entity.Property(x => x.SourceFileChecksum).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.Property(x => x.PreseasonParseStatus).HasMaxLength(32).IsRequired();
            entity.Property(x => x.PreseasonScoringStatus).HasMaxLength(32).IsRequired();
            entity.Property(x => x.ParitySnapshotChecksum).HasMaxLength(128);
            entity.Property(x => x.ParityStatus).HasMaxLength(32).IsRequired();
            entity.Property(x => x.ParityComparedChecksum).HasMaxLength(128);
            entity.Property(x => x.IdempotencyScopeKey).HasMaxLength(256);
            entity.Property(x => x.IdempotencyOutcome).HasMaxLength(32).IsRequired();
            entity.Property(x => x.ErrorMessage).HasMaxLength(4000);
            entity.HasIndex(x => x.SourceFileChecksum);
            entity.HasIndex(x => x.StartedAtUtc);
        });

        modelBuilder.Entity<MigrationImportRawRowEntity>(entity =>
        {
            entity.ToTable("MigrationImportRawRows");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SourceFileName).HasMaxLength(256);
            entity.Property(x => x.SectionType).HasMaxLength(64).IsRequired();
            entity.Property(x => x.RawPayload).HasColumnType("text").IsRequired();
            entity.Property(x => x.ClassificationReason).HasMaxLength(512);

            entity.HasOne<MigrationImportRunEntity>()
                .WithMany()
                .HasForeignKey(x => x.ImportRunId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.ImportRunId, x.SourceFileName, x.RowNumber }).IsUnique();
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

        modelBuilder.Entity<MigrationImportPreseasonAnswerEntity>(entity =>
        {
            entity.ToTable("MigrationImportPreseasonAnswers");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.QuestionKey).HasMaxLength(64).IsRequired();
            entity.Property(x => x.QuestionText).HasMaxLength(512).IsRequired();
            entity.Property(x => x.Subject).HasMaxLength(128).IsRequired();
            entity.Property(x => x.RawAnswer).HasMaxLength(512);
            entity.Property(x => x.NormalizedAnswer).HasMaxLength(512);
            entity.Property(x => x.NormalizedAnswerBoolean);

            entity.HasOne<MigrationImportRunEntity>()
                .WithMany()
                .HasForeignKey(x => x.ImportRunId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.ImportRunId, x.RowNumber, x.QuestionKey, x.Subject, x.IsActualOutcome }).IsUnique();
        });

        modelBuilder.Entity<MigrationImportPreseasonPolicyEntity>(entity =>
        {
            entity.ToTable("MigrationImportPreseasonPolicies");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.CellReference).HasMaxLength(16).IsRequired();
            entity.Property(x => x.RawPointsPerQuestion).HasMaxLength(128);

            entity.HasOne<MigrationImportRunEntity>()
                .WithMany()
                .HasForeignKey(x => x.ImportRunId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => x.ImportRunId).IsUnique();
        });

        modelBuilder.Entity<MigrationImportPreseasonImportedTallyEntity>(entity =>
        {
            entity.ToTable("MigrationImportPreseasonImportedTallies");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.QuestionKey).HasMaxLength(64).IsRequired();
            entity.Property(x => x.QuestionText).HasMaxLength(512).IsRequired();
            entity.Property(x => x.Subject).HasMaxLength(128).IsRequired();
            entity.Property(x => x.RawPoints).HasMaxLength(128);

            entity.HasOne<MigrationImportRunEntity>()
                .WithMany()
                .HasForeignKey(x => x.ImportRunId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.ImportRunId, x.RowNumber, x.QuestionKey, x.Subject }).IsUnique();
        });

        modelBuilder.Entity<MigrationImportPreseasonCalculatedScoreEntity>(entity =>
        {
            entity.ToTable("MigrationImportPreseasonCalculatedScores");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.QuestionKey).HasMaxLength(64).IsRequired();
            entity.Property(x => x.QuestionText).HasMaxLength(512).IsRequired();
            entity.Property(x => x.Subject).HasMaxLength(128).IsRequired();
            entity.Property(x => x.PredictedValue).HasMaxLength(512);
            entity.Property(x => x.ActualValue).HasMaxLength(512);
            entity.Property(x => x.ReasonCode).HasMaxLength(64).IsRequired();

            entity.HasOne<MigrationImportRunEntity>()
                .WithMany()
                .HasForeignKey(x => x.ImportRunId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.ImportRunId, x.RowNumber, x.QuestionKey, x.Subject }).IsUnique();
        });

        modelBuilder.Entity<MigrationImportPreseasonCalculatedTotalEntity>(entity =>
        {
            entity.ToTable("MigrationImportPreseasonCalculatedTotals");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Subject).HasMaxLength(128).IsRequired();

            entity.HasOne<MigrationImportRunEntity>()
                .WithMany()
                .HasForeignKey(x => x.ImportRunId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.ImportRunId, x.Subject }).IsUnique();
        });

        modelBuilder.Entity<MigrationImportCalculatedScoreEntity>(entity =>
        {
            entity.ToTable("MigrationImportCalculatedScores");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.RaceCode).HasMaxLength(16).IsRequired();
            entity.Property(x => x.PickType).HasMaxLength(16).IsRequired();
            entity.Property(x => x.Subject).HasMaxLength(128).IsRequired();
            entity.Property(x => x.PredictedValue).HasMaxLength(512);
            entity.Property(x => x.ActualValue).HasMaxLength(512);
            entity.Property(x => x.Points).HasPrecision(10, 1);
            entity.Property(x => x.ReasonCode).HasMaxLength(64).IsRequired();

            entity.HasOne<MigrationImportRunEntity>()
                .WithMany()
                .HasForeignKey(x => x.ImportRunId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.ImportRunId, x.RaceCode, x.PickType, x.Subject, x.RowNumber }).IsUnique();
        });

        modelBuilder.Entity<MigrationImportLegacyPickScoreEntity>(entity =>
        {
            entity.ToTable("MigrationImportLegacyPickScores");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.RaceCode).HasMaxLength(16).IsRequired();
            entity.Property(x => x.PickType).HasMaxLength(16).IsRequired();
            entity.Property(x => x.Subject).HasMaxLength(128).IsRequired();
            entity.Property(x => x.RawLegacyPoints).HasMaxLength(128);

            entity.HasOne<MigrationImportRunEntity>()
                .WithMany()
                .HasForeignKey(x => x.ImportRunId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.ImportRunId, x.RaceCode, x.PickType, x.Subject, x.RowNumber }).IsUnique();
        });

        modelBuilder.Entity<MigrationImportImportedTotalEntity>(entity =>
        {
            entity.ToTable("MigrationImportImportedTotals");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Subject).HasMaxLength(128).IsRequired();
            entity.Property(x => x.RawTotal).HasMaxLength(128);

            entity.HasOne<MigrationImportRunEntity>()
                .WithMany()
                .HasForeignKey(x => x.ImportRunId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.ImportRunId, x.Subject }).IsUnique();
        });

        modelBuilder.Entity<MigrationImportCalculatedTotalEntity>(entity =>
        {
            entity.ToTable("MigrationImportCalculatedTotals");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Subject).HasMaxLength(128).IsRequired();
            entity.Property(x => x.CalculatedTotalPoints).HasPrecision(10, 1);

            entity.HasOne<MigrationImportRunEntity>()
                .WithMany()
                .HasForeignKey(x => x.ImportRunId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.ImportRunId, x.Subject }).IsUnique();
        });

        modelBuilder.Entity<MigrationImportPreseasonQuestionDiffEntity>(entity =>
        {
            entity.ToTable("MigrationImportPreseasonQuestionDiffs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.QuestionKey).HasMaxLength(64).IsRequired();
            entity.Property(x => x.QuestionText).HasMaxLength(512).IsRequired();
            entity.Property(x => x.Subject).HasMaxLength(128).IsRequired();
            entity.Property(x => x.ReasonCode).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Explanation).HasMaxLength(1024).IsRequired();

            entity.HasOne<MigrationImportRunEntity>()
                .WithMany()
                .HasForeignKey(x => x.ImportRunId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.ImportRunId, x.RowNumber, x.QuestionKey, x.Subject }).IsUnique();
        });

        modelBuilder.Entity<MigrationImportPreseasonParticipantDeltaSummaryEntity>(entity =>
        {
            entity.ToTable("MigrationImportPreseasonParticipantDeltaSummaries");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Subject).HasMaxLength(128).IsRequired();
            entity.Property(x => x.TopReasonCode).HasMaxLength(64);

            entity.HasOne<MigrationImportRunEntity>()
                .WithMany()
                .HasForeignKey(x => x.ImportRunId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.ImportRunId, x.Subject }).IsUnique();
        });

        modelBuilder.Entity<MigrationImportPreseasonReasonCategorySummaryEntity>(entity =>
        {
            entity.ToTable("MigrationImportPreseasonReasonCategorySummaries");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ReasonCode).HasMaxLength(64).IsRequired();

            entity.HasOne<MigrationImportRunEntity>()
                .WithMany()
                .HasForeignKey(x => x.ImportRunId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.ImportRunId, x.ReasonCode }).IsUnique();
        });

        modelBuilder.Entity<MigrationImportPickDiffEntity>(entity =>
        {
            entity.ToTable("MigrationImportPickDiffs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.RaceCode).HasMaxLength(16).IsRequired();
            entity.Property(x => x.PickType).HasMaxLength(16).IsRequired();
            entity.Property(x => x.Subject).HasMaxLength(128).IsRequired();
            entity.Property(x => x.CalculatedPoints).HasPrecision(10, 1);
            entity.Property(x => x.DeltaPoints).HasPrecision(10, 1);
            entity.Property(x => x.ReasonCode).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ExpectedVarianceReasonCode).HasMaxLength(64);
            entity.Property(x => x.ExpectedVarianceRuleId).HasMaxLength(128);
            entity.Property(x => x.Explanation).HasMaxLength(1024).IsRequired();

            entity.HasOne<MigrationImportRunEntity>()
                .WithMany()
                .HasForeignKey(x => x.ImportRunId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.ImportRunId, x.RaceCode, x.Subject, x.PickType }).IsUnique();
        });

        modelBuilder.Entity<MigrationImportRaceDiffEntity>(entity =>
        {
            entity.ToTable("MigrationImportRaceDiffs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.RaceCode).HasMaxLength(16).IsRequired();
            entity.Property(x => x.Subject).HasMaxLength(128).IsRequired();
            entity.Property(x => x.CalculatedPoints).HasPrecision(10, 1);
            entity.Property(x => x.DeltaPoints).HasPrecision(10, 1);
            entity.Property(x => x.ReasonCode).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ExpectedVarianceReasonCode).HasMaxLength(64);
            entity.Property(x => x.ExpectedVarianceRuleId).HasMaxLength(128);
            entity.Property(x => x.Explanation).HasMaxLength(1024).IsRequired();

            entity.HasOne<MigrationImportRunEntity>()
                .WithMany()
                .HasForeignKey(x => x.ImportRunId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.ImportRunId, x.RaceCode, x.Subject }).IsUnique();
        });

        modelBuilder.Entity<MigrationImportParticipantDeltaSummaryEntity>(entity =>
        {
            entity.ToTable("MigrationImportParticipantDeltaSummaries");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Subject).HasMaxLength(128).IsRequired();
            entity.Property(x => x.CalculatedTotalPoints).HasPrecision(10, 1);
            entity.Property(x => x.NetDeltaPoints).HasPrecision(10, 1);
            entity.Property(x => x.TopReasonCode).HasMaxLength(64);

            entity.HasOne<MigrationImportRunEntity>()
                .WithMany()
                .HasForeignKey(x => x.ImportRunId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.ImportRunId, x.Subject }).IsUnique();
        });

        modelBuilder.Entity<MigrationImportReasonCategorySummaryEntity>(entity =>
        {
            entity.ToTable("MigrationImportReasonCategorySummaries");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ReasonCode).HasMaxLength(64).IsRequired();
            entity.Property(x => x.TotalDeltaPoints).HasPrecision(10, 1);

            entity.HasOne<MigrationImportRunEntity>()
                .WithMany()
                .HasForeignKey(x => x.ImportRunId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.ImportRunId, x.ReasonCode }).IsUnique();
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
            entity.Property(x => x.MappedCircuitId).HasMaxLength(64);
            entity.Property(x => x.MappedRaceName).HasMaxLength(256);
            entity.Property(x => x.Warning).HasMaxLength(512);

            entity.HasOne<MigrationImportRunEntity>()
                .WithMany()
                .HasForeignKey(x => x.ImportRunId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.ImportRunId, x.RaceSequence }).IsUnique();
        });

        modelBuilder.Entity<MigrationImportConflictDiagnosticEntity>(entity =>
        {
            entity.ToTable("MigrationImportConflictDiagnostics");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EntityType).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ConflictType).HasMaxLength(64).IsRequired();
            entity.Property(x => x.KeyFields).HasMaxLength(512).IsRequired();
            entity.Property(x => x.SourceReference).HasMaxLength(512).IsRequired();
            entity.Property(x => x.PolicyOutcome).HasMaxLength(32).IsRequired();
            entity.Property(x => x.RecommendedAction).HasMaxLength(256).IsRequired();

            entity.HasOne<MigrationImportRunEntity>()
                .WithMany()
                .HasForeignKey(x => x.ImportRunId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.ImportRunId, x.EntityType, x.KeyFields });
        });

        modelBuilder.Entity<MigrationImportRollbackAuditEntity>(entity =>
        {
            entity.ToTable("MigrationImportRollbackAudits");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Actor).HasMaxLength(256).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.Outcome).HasMaxLength(32).IsRequired();

            entity.HasOne<MigrationImportRunEntity>()
                .WithMany()
                .HasForeignKey(x => x.ImportRunId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.ImportRunId, x.RequestedAtUtc });
        });
    }
}
