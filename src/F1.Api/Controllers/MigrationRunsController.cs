using F1.Api.Dtos;
using F1.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.IO.Compression;

namespace F1.Api.Controllers;

[ApiController]
[Route("admin/migration-runs")]
[Authorize(Roles = "Admin")]
public sealed class MigrationRunsController : ControllerBase
{
    private const int MaxUploadBytes = 20 * 1024 * 1024;
    private const string UploadDirectory = "data/imports/uploads";
    private const string TempUploadDirectory = "f1-imports/uploads";
    private const string SourceProfilePhil2025Csv = "phil-2025-csv";
    private const string SourceProfileDave2025Package = "dave-2025-package";
    private static readonly string[] DavePackageRequiredFiles = ["races.csv", "bonus.csv", "bonusAnswers.csv", "Leaderboard.csv"];
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
        [FromQuery] string? category = null,
        [FromQuery] string? participant = null,
        [FromQuery] bool nonZeroDeltaOnly = false,
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
            expectedStatus,
            category,
            participant,
            nonZeroDeltaOnly);

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

    [HttpGet("{runId:guid}/question-diffs")]
    public async Task<ActionResult<AdminMigrationQuestionDiffListResponseDto>> GetQuestionDiffs(
        Guid runId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? category = null,
        [FromQuery] string? participant = null,
        [FromQuery] string? expectedStatus = "all",
        [FromQuery] bool nonZeroDeltaOnly = false,
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

        var response = await _migrationRunAdminService.GetQuestionDiffsAsync(
            runId,
            page,
            pageSize,
            ResolveActor(),
            cancellationToken,
            category,
            participant,
            expectedStatus,
            nonZeroDeltaOnly);

        if (response is null)
        {
            return NotFound();
        }

        return Ok(response);
    }

    [HttpGet("{runId:guid}/question-summary")]
    public async Task<ActionResult<AdminMigrationQuestionDiffSummaryResponseDto>> GetQuestionSummary(
        Guid runId,
        [FromQuery] string? category = null,
        [FromQuery] string? participant = null,
        [FromQuery] string? expectedStatus = "all",
        [FromQuery] bool nonZeroDeltaOnly = false,
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

        var response = await _migrationRunAdminService.GetQuestionDiffSummaryAsync(
            runId,
            ResolveActor(),
            cancellationToken,
            category,
            participant,
            expectedStatus,
            nonZeroDeltaOnly);

        if (response is null)
        {
            return NotFound();
        }

        return Ok(response);
    }

    [HttpPost("kickoff")]
    public async Task<IActionResult> KickoffRun(
        [FromBody] AdminMigrationRunKickoffRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var result = await _migrationRunAdminService.KickoffRunAsync(
            new MigrationRunKickoffCommand(
                SourceFilePath: request.SourceFilePath,
                RequestedMode: request.Mode,
                RequestedBy: ResolveActor(),
                SourceProfile: request.SourceProfile,
                ConfirmNonEmptyStrategy: request.ConfirmNonEmptyStrategy),
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
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
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

        var sourceProfile = NormalizeUploadSourceProfile(request.SourceProfile);
        var fileExtension = Path.GetExtension(request.SourceFile.FileName);

        if (!IsSupportedUploadForProfile(fileExtension, sourceProfile))
        {
            var expectedFileType = string.Equals(sourceProfile, SourceProfileDave2025Package, StringComparison.Ordinal)
                ? "zip"
                : "csv";

            return BadRequest(new
            {
                message = $"Unsupported upload type for source profile '{sourceProfile}'. Expected {expectedFileType}.",
                code = "kickoff_upload_invalid_file_type"
            });
        }

        string persistedPath;
        try
        {
            persistedPath = await PersistUploadedSourceAsync(request.SourceFile, sourceProfile, cancellationToken);
        }
        catch (InvalidDataException ex)
        {
            return BadRequest(new
            {
                message = ex.Message,
                code = "kickoff_upload_invalid_archive"
            });
        }

        var result = await _migrationRunAdminService.KickoffRunAsync(
            new MigrationRunKickoffCommand(
                SourceFilePath: persistedPath,
                RequestedMode: request.Mode,
                RequestedBy: ResolveActor(),
                SourceProfile: sourceProfile,
                ConfirmNonEmptyStrategy: request.ConfirmNonEmptyStrategy),
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

    [HttpPost("{runId:guid}/rollback")]
    public async Task<IActionResult> RollbackRun(
        Guid runId,
        [FromBody] AdminMigrationRollbackRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest(new
            {
                message = "Rollback reason is required.",
                code = "rollback_invalid_request"
            });
        }

        var result = await _migrationRunAdminService.RollbackRunAsync(
            new MigrationRunRollbackCommand(runId, ResolveActor(), request.Reason.Trim()),
            cancellationToken);

        if (!result.Success)
        {
            return BadRequest(new
            {
                message = result.Error ?? "Unable to rollback migration run.",
                code = "rollback_failed"
            });
        }

        return Ok(result.Rollback);
    }

    private static string ResolveWritableUploadRoot()
    {
        var candidateRoots = new[]
        {
            Path.GetFullPath(UploadDirectory, Directory.GetCurrentDirectory()),
            Path.GetFullPath(TempUploadDirectory, Path.GetTempPath()),
            Path.GetFullPath("f1-imports", Path.GetTempPath()),
            Path.Combine(Path.GetTempPath(), "f1-api-uploads")
        };

        foreach (var candidateRoot in candidateRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (TryEnsureDirectoryWritable(candidateRoot))
            {
                return candidateRoot;
            }
        }

        throw new UnauthorizedAccessException(
            $"Unable to create a writable upload directory. Tried '{string.Join("', '", candidateRoots)}'.");
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

    private static string NormalizeUploadSourceProfile(string? sourceProfile)
    {
        if (string.IsNullOrWhiteSpace(sourceProfile))
        {
            return SourceProfilePhil2025Csv;
        }

        return sourceProfile.Trim().ToLowerInvariant();
    }

    private static bool IsSupportedUploadForProfile(string? fileExtension, string sourceProfile)
    {
        if (string.IsNullOrWhiteSpace(fileExtension))
        {
            return false;
        }

        if (string.Equals(sourceProfile, SourceProfileDave2025Package, StringComparison.Ordinal))
        {
            return string.Equals(fileExtension, ".zip", StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(fileExtension, ".csv", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> PersistUploadedSourceAsync(IFormFile sourceFile, string sourceProfile, CancellationToken cancellationToken)
    {
        var uploadRoot = ResolveWritableUploadRoot();
        var scopedRoot = Path.Combine(uploadRoot, $"{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scopedRoot);

        try
        {
            if (string.Equals(sourceProfile, SourceProfileDave2025Package, StringComparison.Ordinal))
            {
                var extractRoot = Path.Combine(scopedRoot, "package");
                Directory.CreateDirectory(extractRoot);

                await ExtractZipArchiveAsync(sourceFile, extractRoot, cancellationToken);
                return ResolveDavePackageSourcePath(extractRoot);
            }

            var safeBaseName = Path.GetFileNameWithoutExtension(sourceFile.FileName);
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

            var persistedFilePath = Path.Combine(scopedRoot, $"{sanitizedBaseName}.csv");
            await using var stream = System.IO.File.Create(persistedFilePath);
            await sourceFile.CopyToAsync(stream, cancellationToken);
            return persistedFilePath;
        }
        catch
        {
            if (Directory.Exists(scopedRoot))
            {
                Directory.Delete(scopedRoot, recursive: true);
            }

            throw;
        }
    }

    private static async Task ExtractZipArchiveAsync(IFormFile sourceFile, string destinationRoot, CancellationToken cancellationToken)
    {
        await using var sourceStream = sourceFile.OpenReadStream();
        using var archive = new ZipArchive(sourceStream, ZipArchiveMode.Read, leaveOpen: false);
        if (archive.Entries.Count == 0)
        {
            throw new InvalidDataException("Uploaded archive is empty.");
        }

        var extractedFileCount = 0;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entryPath = entry.FullName.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(entryPath) || entryPath.EndsWith('/'))
            {
                continue;
            }

            var targetPath = Path.GetFullPath(Path.Combine(destinationRoot, entryPath), destinationRoot);
            if (!IsPathWithinRoot(targetPath, destinationRoot))
            {
                throw new InvalidDataException("Archive contains unsafe path entries.");
            }

            var targetDirectory = Path.GetDirectoryName(targetPath);
            if (string.IsNullOrWhiteSpace(targetDirectory))
            {
                throw new InvalidDataException("Archive entry target path is invalid.");
            }

            Directory.CreateDirectory(targetDirectory);
            await using var entryStream = entry.Open();
            await using var outputStream = System.IO.File.Create(targetPath);
            await entryStream.CopyToAsync(outputStream, cancellationToken);
            extractedFileCount++;
        }

        if (extractedFileCount == 0)
        {
            throw new InvalidDataException("Uploaded archive does not contain files.");
        }
    }

    private static string ResolveDavePackageSourcePath(string extractRoot)
    {
        if (HasRequiredDavePackageFiles(extractRoot))
        {
            return extractRoot;
        }

        var nestedCandidate = Directory
            .EnumerateDirectories(extractRoot)
            .Where(HasRequiredDavePackageFiles)
            .SingleOrDefault();

        if (!string.IsNullOrWhiteSpace(nestedCandidate))
        {
            return nestedCandidate;
        }

        throw new InvalidDataException("Uploaded archive is missing required Dave package files.");
    }

    private static bool HasRequiredDavePackageFiles(string candidateRoot)
    {
        if (!Directory.Exists(candidateRoot))
        {
            return false;
        }

        var fileSet = Directory
            .EnumerateFiles(candidateRoot, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return DavePackageRequiredFiles.All(fileSet.Contains);
    }

    private static bool IsPathWithinRoot(string candidatePath, string rootPath)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (string.Equals(candidatePath, rootPath, comparison))
        {
            return true;
        }

        var normalizedRoot = Path.GetFullPath(rootPath);
        var rootWithSeparator = normalizedRoot.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;

        return candidatePath.StartsWith(rootWithSeparator, comparison);
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