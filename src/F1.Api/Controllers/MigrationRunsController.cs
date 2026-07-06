using F1.Api.Dtos;
using F1.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
        var detail = await _migrationRunAdminService.GetRunDetailAsync(runId, cancellationToken);
        if (detail is null)
        {
            return NotFound();
        }

        return Ok(detail);
    }
}