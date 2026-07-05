using F1.Web.Components;
using F1.Web.Models;

namespace F1.Web.Tests.Components;

public class SelectionStatusBannersTests : BunitContext
{
    [Fact]
    public void SelectionStatusBanners_ShouldRenderAllConfiguredSections()
    {
        var cut = Render<SelectionStatusBanners>(parameters => parameters
            .Add(p => p.ErrorMessage, "Something went wrong")
            .Add(p => p.SuccessMessage, "Saved")
            .Add(p => p.CountdownText, "Pre-Qualy lock in 1d 2h 3m 4s (UTC).")
            .Add(p => p.LockingMessage, "Pre-Qualy lock closes at 13 Jul 2026 15:00 UTC.")
            .Add(p => p.RaceMetadata, new RaceQuestionMetadata
            {
                DisplayTitle = "Australian GP Questions",
                H2HQuestion = "Who finishes higher?",
                BonusQuestion = "How many DNFs?"
            }));

        Assert.Contains("Something went wrong", cut.Markup);
        Assert.Contains("Saved", cut.Markup);
        Assert.Contains("Australian GP Questions", cut.Markup);
        Assert.Contains("Who finishes higher?", cut.Markup);
        Assert.Contains("How many DNFs?", cut.Markup);
        Assert.Contains("Countdown:", cut.Markup);
        Assert.Contains("Pre-Qualy lock closes at 13 Jul 2026 15:00 UTC.", cut.Markup);
    }

    [Fact]
    public void SelectionStatusBanners_ShouldHideOptionalSections_WhenValuesMissing()
    {
        var cut = Render<SelectionStatusBanners>(parameters => parameters
            .Add(p => p.CountdownText, string.Empty));

        Assert.DoesNotContain("alert-danger", cut.Markup);
        Assert.DoesNotContain("alert-success", cut.Markup);
        Assert.DoesNotContain("Race Questions", cut.Markup);
        Assert.DoesNotContain("Countdown:", cut.Markup);
        Assert.Contains("configured lock deadline", cut.Markup);
        Assert.Contains("alert-warning", cut.Markup);
    }
}
