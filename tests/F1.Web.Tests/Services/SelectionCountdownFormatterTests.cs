using F1.Web.Models;
using F1.Web.Services;

namespace F1.Web.Tests.Services;

public class SelectionCountdownFormatterTests
{
    private readonly SelectionCountdownFormatter _formatter = new();

    [Fact]
    public void FormatCountdown_WhenRaceConfigMissing_ReturnsEmpty()
    {
        var result = _formatter.FormatCountdown(null, DateTime.UtcNow);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void FormatCountdown_WhenBeforePreQualy_ReturnsPreQualyLabel()
    {
        var config = CreateRaceConfig();
        var now = new DateTime(2025, 12, 6, 9, 0, 0, DateTimeKind.Utc);

        var result = _formatter.FormatCountdown(config, now);

        Assert.StartsWith("Pre-Qualy lock in", result, StringComparison.Ordinal);
        Assert.Contains("(UTC).", result, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatCountdown_WhenAfterPreQualyAndBeforeFinal_ReturnsFinalLabel()
    {
        var config = CreateRaceConfig();
        var now = new DateTime(2025, 12, 7, 14, 0, 0, DateTimeKind.Utc);

        var result = _formatter.FormatCountdown(config, now);

        Assert.StartsWith("Final submission in", result, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatCountdown_WhenPastFinalDeadline_ReturnsPassedMessage()
    {
        var config = CreateRaceConfig();
        var now = new DateTime(2025, 12, 8, 12, 0, 1, DateTimeKind.Utc);

        var result = _formatter.FormatCountdown(config, now);

        Assert.Equal("All deadlines have passed.", result);
    }

    private static RaceConfig CreateRaceConfig()
    {
        return new RaceConfig
        {
            RaceId = "2025-24-yas_marina",
            PreQualyDeadlineUtc = new DateTime(2025, 12, 7, 13, 0, 0, DateTimeKind.Utc),
            FinalDeadlineUtc = new DateTime(2025, 12, 8, 12, 0, 0, DateTimeKind.Utc),
            EarlyLockLabel = "Pre-Qualy lock",
            FinalSubmissionLabel = "Final submission"
        };
    }
}