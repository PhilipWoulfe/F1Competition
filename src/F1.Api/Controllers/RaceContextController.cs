using F1.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace F1.Api.Controllers;

[ApiController]
[Route("races/context/{competitionSlug}/{season:int}")]
public class RaceContextController(IRaceContextResolver raceContextResolver) : ControllerBase
{
    [HttpGet("round/{round:int}")]
    public async Task<IActionResult> ResolveByRound(string competitionSlug, int season, int round)
    {
        if (round <= 0)
        {
            return BadRequest(new { message = "Round must be greater than zero." });
        }

        try
        {
            var resolved = await raceContextResolver.ResolveByRoundAsync(competitionSlug, season, round);
            return resolved is null ? NotFound() : Ok(resolved);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("slug/{raceSlug}")]
    public async Task<IActionResult> ResolveBySlug(string competitionSlug, int season, string raceSlug)
    {
        try
        {
            var resolved = await raceContextResolver.ResolveBySlugAsync(competitionSlug, season, raceSlug);
            return resolved is null ? NotFound() : Ok(resolved);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
