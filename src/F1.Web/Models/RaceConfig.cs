namespace F1.Web.Models;

public class RaceConfig
{
    public string RaceId { get; set; } = string.Empty;
    public DateTime PreQualyDeadlineUtc { get; set; }
    public DateTime FinalDeadlineUtc { get; set; }
    public BetType? EarlyLockBetType { get; set; } = BetType.PreQualy;
    public string EarlyLockLabel { get; set; } = "Pre-Qualy lock";
    public string FinalSubmissionLabel { get; set; } = "Final submission";
    public string LockMessage { get; set; } = "Locking for Pre-Qualy gives +50% points and prevents changes after the configured lock deadline.";
    public string LockedSelectionMessage { get; set; } = "This pre-qualy selection is locked.";
    public List<BetOption> BetOptions { get; set; } = [];
}
