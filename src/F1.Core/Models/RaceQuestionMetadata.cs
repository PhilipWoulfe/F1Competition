using Newtonsoft.Json;

namespace F1.Core.Models;

public class RaceQuestionMetadata
{
    public string RaceId { get; set; } = string.Empty;

    public string H2HQuestion { get; set; } = string.Empty;

    public string? H2HLeftDriverId { get; set; }

    public string? H2HRightDriverId { get; set; }

    public int? H2HPoints { get; set; }

    public string BonusQuestion { get; set; } = string.Empty;

    public bool IsPublished { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    [JsonProperty("_etag")]
    public string? ETag { get; set; }
}