using F1.Core.Models;

namespace F1.Core.Dtos;

public class RaceConfigDto
{
    public string RaceId { get; set; } = string.Empty;
    public int SelectionCount { get; set; } = 5;
    public DateTime PreQualyDeadlineUtc { get; set; }
    public DateTime FinalDeadlineUtc { get; set; }
    public BetType? EarlyLockBetType { get; set; } = BetType.PreQualy;
    public string EarlyLockLabel { get; set; } = "Pre-Qualy lock";
    public string FinalSubmissionLabel { get; set; } = "Final submission";
    public string LockMessage { get; set; } = "Locking for Pre-Qualy gives +50% points and prevents changes after the configured lock deadline.";
    public string LockedSelectionMessage { get; set; } = "This pre-qualy selection is locked.";
    public List<BetOptionDto> BetOptions { get; set; } = [];
}
