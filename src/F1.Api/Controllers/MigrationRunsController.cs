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
    private const string UploadDirectory = "data/imports/uploads";
    private const string TempUploadDirectory = "f1-imports/uploads";
    private static readonly HashSet<string> AllowedExpectedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "all",
        "expected",
        "unexpected"
    };
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
    public async Task<ActionResult<AdminMigrationRunDetailResponseDto>> GetRunDetail(
        Guid runId,
        [FromQuery] string? expectedStatus = "all",
        CancellationToken cancellationToken = default)
    {
        if (!IsValidExpectedStatus(expectedStatus))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid expected status filter",
                Detail = "expectedStatus must be one of: all, expected, unexpected.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var detail = await _migrationRunAdminService.GetRunDetailAsync(
            runId,
            ResolveActor(),
            cancellationToken,
            expectedStatus);
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
        [FromQuery] string? expectedStatus = "all",
        CancellationToken cancellationToken = default)
    {
        if (!IsValidExpectedStatus(expectedStatus))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid expected status filter",
                Detail = "expectedStatus must be one of: all, expected, unexpected.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var export = await _migrationRunAdminService.ExportRunDiffsAsync(
            runId,
            exportType,
            format,
            ResolveActor(),
            cancellationToken,
            expectedStatus);

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

    [HttpPost("kickoff/upload")]
    [RequestFormLimits(MultipartBodyLengthLimit = 20 * 1024 * 1024)]
    public async Task<IActionResult> KickoffRunFromUpload(
        [FromForm] AdminMigrationRunKickoffUploadRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (request.SourceFile is null || request.SourceFile.Length == 0)
        {
            return BadRequest(new
            {
                message = "A non-empty source file is required.",
                code = "kickoff_upload_invalid_request"
            });
        }

        var fileExtension = Path.GetExtension(request.SourceFile.FileName);
        if (!string.Equals(fileExtension, ".csv", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                message = "Only CSV uploads are supported.",
                code = "kickoff_upload_invalid_file_type"
            });
        }

        var uploadRoot = ResolveWritableUploadRoot();

        var safeBaseName = Path.GetFileNameWithoutExtension(request.SourceFile.FileName);
        if (string.IsNullOrWhiteSpace(safeBaseName))
        {
            safeBaseName = "migration-import";
        }

        var sanitizedBaseName = string.Concat(safeBaseName.Select(ch =>
            char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '-')).Trim('-');
        if (string.IsNullOrWhiteSpace(sanitizedBaseName))
        {
            sanitizedBaseName = "migration-import";
        }

        var fileName = $"{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}-{sanitizedBaseName}.csv";
        var persistedPath = Path.Combine(uploadRoot, fileName);

        await using (var stream = System.IO.File.Create(persistedPath))
        {
            await request.SourceFile.CopyToAsync(stream, cancellationToken);
        }

        var result = await _migrationRunAdminService.KickoffRunAsync(
            new MigrationRunKickoffCommand(
                persistedPath,
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

    private static string ResolveWritableUploadRoot()
    {
        var primaryRoot = Path.GetFullPath(UploadDirectory, Directory.GetCurrentDirectory());
        if (TryEnsureDirectoryWritable(primaryRoot))
        {
            return primaryRoot;
        }

        var tempRoot = Path.GetFullPath(TempUploadDirectory, Path.GetTempPath());
        if (TryEnsureDirectoryWritable(tempRoot))
        {
            return tempRoot;
        }

        throw new UnauthorizedAccessException(
            $"Unable to create a writable upload directory. Tried '{primaryRoot}' and '{tempRoot}'.");
    }

    private static bool TryEnsureDirectoryWritable(string path)
    {
        try
        {
            Directory.CreateDirectory(path);

            var probePath = Path.Combine(path, $".write-check-{Guid.NewGuid():N}");
            System.IO.File.WriteAllText(probePath, "ok");
            System.IO.File.Delete(probePath);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private string ResolveActor()
    {
        return User.FindFirstValue(ClaimTypes.Email)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.Identity?.Name
            ?? "unknown";
    }

    private static bool IsValidExpectedStatus(string? expectedStatus)
    {
        if (string.IsNullOrWhiteSpace(expectedStatus))
        {
            return true;
        }

        return AllowedExpectedStatuses.Contains(expectedStatus.Trim());
    }
}