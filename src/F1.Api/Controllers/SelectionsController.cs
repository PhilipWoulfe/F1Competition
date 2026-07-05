using F1.Core.Dtos;
using F1.Core.Interfaces;
using F1.Services;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace F1.Api.Controllers;

[ApiController]
[Route("selections")]
public class SelectionsController : ControllerBase
{
    private readonly ISelectionService _selectionService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public SelectionsController(
        ISelectionService selectionService,
        IDateTimeProvider dateTimeProvider)
    {
        _selectionService = selectionService;
        _dateTimeProvider = dateTimeProvider;
    }

    [HttpGet("{raceId}/config")]
    public async Task<IActionResult> GetConfig(string raceId)
    {
        var config = await _selectionService.GetRaceConfigAsync(raceId);
        if (config is null)
        {
            return NotFound();
        }

        return Ok(config);
    }

    [HttpGet("{raceId}/mine")]
    public async Task<IActionResult> GetMine(string raceId)
    {
        var userId = ResolveUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var selection = await _selectionService.GetSelectionAsync(raceId, userId);
        if (selection is null)
        {
            return NotFound();
        }

        return Ok(selection);
    }

    [HttpGet("{raceId}/current")]
    public async Task<IActionResult> GetCurrent(string raceId)
    {
        var userId = ResolveUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var selections = await _selectionService.GetCurrentSelectionsAsync(raceId, userId);
        return Ok(selections);
    }

    [HttpPut("{raceId}/mine")]
    public async Task<IActionResult> UpsertMine(string raceId, [FromBody] SelectionSubmissionDto submission)
    {
        var userId = ResolveUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        try
        {
            var selection = await _selectionService.UpsertSelectionAsync(raceId, userId, submission);
            return Ok(selection);
        }
        catch (SelectionValidationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (SelectionForbiddenException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (SelectionRaceNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    private string? ResolveUserId()
    {
        return User.FindFirstValue(ClaimTypes.Email)
               ?? Request.Headers["Cf-Access-Authenticated-User-Email"].FirstOrDefault();
    }
}
