using F1.Core.Exceptions;
using F1.Core.Interfaces;
using F1.Core.Models;

namespace F1.Services;

public class RaceMetadataService : IRaceMetadataService
{
    private readonly IRaceMetadataRepository _raceMetadataRepository;
    private readonly IDateTimeProvider _dateTimeProvider;

    public RaceMetadataService(IRaceMetadataRepository raceMetadataRepository, IDateTimeProvider dateTimeProvider)
    {
        _raceMetadataRepository = raceMetadataRepository;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<RaceQuestionMetadata?> GetMetadataAsync(string raceId, bool publishedOnly)
    {
        var metadata = await _raceMetadataRepository.GetMetadataAsync(raceId);
        if (metadata is null)
        {
            return null;
        }

        if (publishedOnly && !metadata.IsPublished)
        {
            return null;
        }

        return metadata;
    }

    public async Task<RaceQuestionMetadata> UpsertMetadataAsync(string raceId, RaceQuestionMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(raceId))
        {
            throw new MetadataValidationException("Race ID is required.");
        }

        if (string.IsNullOrWhiteSpace(metadata.H2HQuestion))
        {
            throw new MetadataValidationException("H2H question is required.");
        }

        if (string.IsNullOrWhiteSpace(metadata.BonusQuestion))
        {
            throw new MetadataValidationException("Bonus question is required.");
        }

        var hasAnyH2hOption =
            !string.IsNullOrWhiteSpace(metadata.H2HLeftDriverId) ||
            !string.IsNullOrWhiteSpace(metadata.H2HRightDriverId) ||
            metadata.H2HPoints.HasValue;

        if (hasAnyH2hOption)
        {
            if (string.IsNullOrWhiteSpace(metadata.H2HLeftDriverId) || string.IsNullOrWhiteSpace(metadata.H2HRightDriverId))
            {
                throw new MetadataValidationException("H2H questions require exactly two driver choices.");
            }

            if (string.Equals(metadata.H2HLeftDriverId.Trim(), metadata.H2HRightDriverId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new MetadataValidationException("H2H driver choices must be different.");
            }

            if (!metadata.H2HPoints.HasValue || metadata.H2HPoints.Value <= 0)
            {
                throw new MetadataValidationException("H2H points must be greater than zero.");
            }
        }

        metadata.RaceId = raceId;
        metadata.UpdatedAtUtc = _dateTimeProvider.UtcNow;

        return await _raceMetadataRepository.UpsertMetadataAsync(raceId, metadata);
    }
}