using F1.Core.Dtos;
using F1.Core.Exceptions;
using F1.Core.Interfaces;
using F1.Core.Models;
using F1.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace F1.Api.Controllers;

[ApiController]
[Route("races/{raceId}/metadata")]
public class RaceMetadataController : ControllerBase
{
    private readonly IRaceMetadataService _raceMetadataService;

    public RaceMetadataController(
        IRaceMetadataService raceMetadataService)
    {
        _raceMetadataService = raceMetadataService;
    }

    [HttpGet]
    public async Task<IActionResult> GetMetadata(string raceId, [FromQuery] bool includeDraft = false)
    {
        if (!CanonicalRaceId.IsValid(raceId))
        {
            return BadRequest(CanonicalRaceId.BuildValidationErrorPayload());
        }

        if (includeDraft && !User.IsInRole("Admin"))
        {
            return Forbid();
        }

        var metadata = await _raceMetadataService.GetMetadataAsync(raceId, publishedOnly: !includeDraft);
        if (metadata is null)
        {
            return NotFound();
        }

        return Ok(MapToDto(metadata));
    }

    [Authorize(Roles = "Admin")]
    [HttpPut]
    public async Task<IActionResult> UpsertMetadata(string raceId, [FromBody] UpsertRaceQuestionMetadataDto request)
    {
        if (!CanonicalRaceId.IsValid(raceId))
        {
            return BadRequest(CanonicalRaceId.BuildValidationErrorPayload());
        }

        try
        {
            var metadata = await _raceMetadataService.UpsertMetadataAsync(
                raceId,
                new RaceQuestionMetadata
                {
                    H2HQuestion = request.H2HQuestion,
                    BonusQuestion = request.BonusQuestion,
                    IsPublished = request.IsPublished
                });

            return Ok(MapToDto(metadata));
        }
        catch (MetadataValidationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    private static RaceQuestionMetadataDto MapToDto(RaceQuestionMetadata metadata)
    {
        return new RaceQuestionMetadataDto
        {
            RaceId = metadata.RaceId,
            H2HQuestion = metadata.H2HQuestion,
            BonusQuestion = metadata.BonusQuestion,
            IsPublished = metadata.IsPublished,
            UpdatedAtUtc = metadata.UpdatedAtUtc,
            ETag = metadata.ETag
        };
    }
}