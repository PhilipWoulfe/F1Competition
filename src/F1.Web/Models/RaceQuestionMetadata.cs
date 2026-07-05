namespace F1.Web.Models;

public class RaceQuestionMetadata
{
    public string RaceId { get; set; } = string.Empty;

    public string? DisplayTitle { get; set; }

    public string? LockMessage { get; set; }

    public string H2HQuestion { get; set; } = string.Empty;

    public string BonusQuestion { get; set; } = string.Empty;

    public bool IsPublished { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public string? ETag { get; set; }
}

public class RaceQuestionMetadataUpdateRequest
{
    public string H2HQuestion { get; set; } = string.Empty;

    public string BonusQuestion { get; set; } = string.Empty;

    public bool IsPublished { get; set; }

    public string? ExpectedEtag { get; set; }
}