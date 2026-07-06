using F1.Api.Dtos;
using F1.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace F1.Api.Controllers;

[ApiController]
[Route("admin/migration-runs")]
[Authorize(Roles = "Admin")]
public sealed class MigrationRunsController : ControllerBase
{
    private readonly IMigrationRunAdminService _migrationRunAdminService;

    public MigrationRunsController(IMigrationRunAdminService migrationRunAdminService)
    {
        _migrationRunAdminService = migrationRunAdminService;
    }

    [HttpGet]
    public async Task<ActionResult<AdminMigrationRunListResponseDto>> GetRuns(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? status = null,
        [FromQuery] DateTime? startedFromUtc = null,
        [FromQuery] DateTime? startedToUtc = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _migrationRunAdminService.GetRunsAsync(
            new MigrationRunListQuery(page, pageSize, status, startedFromUtc, startedToUtc),
            cancellationToken);

        return Ok(response);
    }

    [HttpGet("{runId:guid}")]
    public async Task<ActionResult<AdminMigrationRunDetailResponseDto>> GetRunDetail(Guid runId, CancellationToken cancellationToken = default)
    {
        var detail = await _migrationRunAdminService.GetRunDetailAsync(runId, ResolveActor(), cancellationToken);
        if (detail is null)
        {
            return NotFound();
        }

        return Ok(detail);
    }

    [HttpGet("{runId:guid}/exports/{exportType}")]
    public async Task<IActionResult> ExportRunDiffs(
        Guid runId,
        string exportType,
        [FromQuery] string format = "csv",
        CancellationToken cancellationToken = default)
    {
        var export = await _migrationRunAdminService.ExportRunDiffsAsync(
            runId,
            exportType,
            format,
            ResolveActor(),
            cancellationToken);

        if (export is null)
        {
            return NotFound();
        }

        if (!export.Success)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid export request",
                Detail = export.Error,
                Status = StatusCodes.Status400BadRequest
            });
        }

        return File(export.Payload, export.ContentType, export.FileName);
    }

    [HttpPost("kickoff")]
    public async Task<IActionResult> KickoffRun(
        [FromBody] AdminMigrationRunKickoffRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var result = await _migrationRunAdminService.KickoffRunAsync(
            new MigrationRunKickoffCommand(
                request.SourceFilePath,
                request.Mode,
                ResolveActor()),
            cancellationToken);

        if (!result.Success)
        {
            if (result.Conflict)
            {
                return Conflict(new
                {
                    message = result.Error ?? "An active migration run already exists for this source/checksum.",
                    code = "active_run_conflict",
                    existingRunId = result.ExistingRunId
                });
            }

            return BadRequest(new
            {
                message = result.Error ?? "Unable to start migration run kickoff.",
                code = "kickoff_invalid_request"
            });
        }

        return CreatedAtAction(nameof(GetRunDetail), new { runId = result.Run!.RunId }, result.Run);
    }

    private string ResolveActor()
    {
        return User.FindFirstValue(ClaimTypes.Email)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.Identity?.Name
            ?? "unknown";
    }
}