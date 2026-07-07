using F1.E2E.Tests.Infrastructure;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace F1.E2E.Tests.Pages;

internal sealed record MigrationParticipantRow(
    string Participant,
    int Imported,
    int Calculated,
    int Delta,
    string TopReason);

internal sealed record MigrationRaceRow(
    string RaceCode,
    string Participant,
    int Imported,
    int Calculated,
    int Delta,
    string Reason,
    string Explanation);

internal sealed record MigrationPickRow(
    string RaceCode,
    string Participant,
    string PickType,
    int? Imported,
    int? Calculated,
    int Delta,
    string Reason,
    string Explanation);

internal class MigrationRunsPage
{
    private readonly IWebDriver _driver;
    private readonly WebDriverWait _wait;
    private readonly string _baseUrl;
    private readonly Action<string> _trace;

    public MigrationRunsPage(IWebDriver driver, WebDriverWait wait, string baseUrl, Action<string>? trace = null)
    {
        _driver = driver;
        _wait = wait;
        _baseUrl = baseUrl.TrimEnd('/');
        _trace = trace ?? (_ => { });
    }

    public void Navigate()
    {
        _trace($"Navigate -> {_baseUrl}/admin/migration-runs");
        _driver.Navigate().GoToUrl(_baseUrl + "/admin/migration-runs");
        _trace($"Navigation complete. Current URL: {_driver.Url}");
    }

    public void WaitUntilReady()
    {
        _trace("Waiting for migration runs filters to render...");
        PageReadiness.WaitForAppReady(
            _driver,
            _wait.Timeout,
            driver => driver.FindElements(By.Id("status-filter")).Count > 0);
        _trace("Migration runs filters rendered.");
    }

    public void SelectRun(Guid runId)
    {
        _trace($"Selecting run {runId}");
        var selector = By.XPath($"//tr[td/code[normalize-space()='{runId}']]//button[normalize-space()='View']");
        _wait.Until(driver => driver.FindElements(selector).Count > 0);
        _driver.FindElement(selector).Click();
    }

    public void SelectFirstRun()
    {
        _trace("Selecting first migration run from list");
        var selector = By.XPath("(//tr[td/code]//button[normalize-space()='View'])[1]");
        var firstRunButton = _wait.Until(driver => driver.FindElement(selector));
        firstRunButton.Click();
    }

    public void WaitForRunDetail()
    {
        _trace("Waiting for run detail section...");
        _wait.Until(driver =>
            driver.FindElements(By.XPath("//h2[normalize-space()='Run Detail']")).Count > 0 &&
            driver.FindElements(By.Id("detail-participant-filter")).Count > 0);
        _trace("Run detail section is visible.");
    }

    public void SetParticipantFilter(string value)
    {
        SetInputValue("detail-participant-filter", value);
    }

    public void SetRaceFilter(string value)
    {
        SetInputValue("detail-race-filter", value);
    }

    public void SetReasonFilter(string value)
    {
        SetInputValue("detail-reason-filter", value);
    }

    public void SetNonZeroOnly(bool enabled)
    {
        var checkbox = _driver.FindElement(By.Id("detail-non-zero-only"));
        if (checkbox.Selected != enabled)
        {
            checkbox.Click();
        }
    }

    public IReadOnlyList<MigrationParticipantRow> GetParticipantRows()
    {
        return GetRowsInPane("pane-race-participants")
            .Select(ParseParticipantRow)
            .ToList();
    }

    public IReadOnlyList<MigrationRaceRow> GetRaceRows()
    {
        return GetRowsInPane("pane-race-diffs")
            .Select(ParseRaceRow)
            .ToList();
    }

    public IReadOnlyList<MigrationPickRow> GetPickRows()
    {
        return GetRowsInPane("pane-pick-diffs")
            .Select(ParsePickRow)
            .ToList();
    }

    public bool WaitForParticipantComparisonSection()
    {
        EnsureTabSelected("tab-race-participants", "pane-race-participants");
        return WaitForPaneComparisonSection("pane-race-participants", "No participant deltas available for this run.");
    }

    public bool WaitForPreseasonComparisonSection()
    {
        EnsureTabSelected("tab-preseason", "pane-preseason");
        return _wait.Until(driver =>
            driver.FindElements(By.Id("pane-preseason")).Count > 0 &&
            driver.FindElements(By.Id("preseason-participant-filter")).Count > 0);
    }

    public bool WaitForPreseasonQuestionDiffSection()
    {
        EnsureTabSelected("tab-preseason", "pane-preseason");
        var sectionSelector = By.XPath(
            "//*[@id='pane-preseason']//h4[normalize-space()='Preseason Question Diffs']" +
            "/following-sibling::*[1]" +
            "[self::div[.//tbody/tr] or self::p[normalize-space()='No preseason question diffs available for this run.']]");
        return _wait.Until(driver => driver.FindElements(sectionSelector).Count > 0);
    }

    public bool WaitForRaceComparisonSection()
    {
        EnsureTabSelected("tab-race-diffs", "pane-race-diffs");
        return WaitForPaneComparisonSection("pane-race-diffs", "No race diffs available for this run.");
    }

    public bool WaitForPickComparisonSection()
    {
        EnsureTabSelected("tab-pick-diffs", "pane-pick-diffs");
        return WaitForPaneComparisonSection("pane-pick-diffs", "No pick diffs available for this run.");
    }

    public void WaitUntil(Func<bool> condition)
    {
        _wait.Until(_ => condition());
    }

    private void SetInputValue(string inputId, string value)
    {
        var input = _driver.FindElement(By.Id(inputId));
        input.Clear();
        if (!string.IsNullOrWhiteSpace(value))
        {
            input.SendKeys(value);
        }

        // Blazor @bind uses change events for text inputs; blur to trigger updates.
        input.SendKeys(Keys.Tab);
    }

    private IReadOnlyList<IWebElement> GetRowsInPane(string paneId)
    {
        var rows = _driver.FindElements(By.XPath($"//*[@id='{paneId}']//tbody/tr"));
        return rows;
    }

    private bool WaitForPaneComparisonSection(string paneId, string emptyStateMessage)
    {
        return _wait.Until(driver =>
        {
            var pane = driver.FindElements(By.Id(paneId)).FirstOrDefault();
            if (pane is null || !pane.Displayed)
            {
                return false;
            }

            var hasTable = pane.FindElements(By.XPath(".//table")).Count > 0;
            var hasEmptyState = pane.FindElements(By.XPath($".//p[normalize-space()='{emptyStateMessage}']")).Count > 0;
            return hasTable || hasEmptyState;
        });
    }

    private void EnsureTabSelected(string tabId, string paneId)
    {
        var tabButton = _wait.Until(driver => driver.FindElement(By.Id(tabId)));
        if (!string.Equals(tabButton.GetAttribute("aria-selected"), "true", StringComparison.OrdinalIgnoreCase))
        {
            tabButton.Click();
        }

        _wait.Until(driver =>
        {
            var currentTabButton = driver.FindElements(By.Id(tabId)).FirstOrDefault();
            var pane = driver.FindElements(By.Id(paneId)).FirstOrDefault();
            return currentTabButton is not null
                && pane is not null
                && string.Equals(currentTabButton.GetAttribute("aria-selected"), "true", StringComparison.OrdinalIgnoreCase)
                && pane.Displayed;
        });
    }

    private static MigrationParticipantRow ParseParticipantRow(IWebElement row)
    {
        var cells = row.FindElements(By.TagName("td"));
        return new MigrationParticipantRow(
            Participant: cells[0].Text.Trim(),
            Imported: ParseInt(cells[1].Text),
            Calculated: ParseInt(cells[2].Text),
            Delta: ParseInt(cells[3].Text),
            TopReason: cells[4].Text.Trim());
    }

    private static MigrationRaceRow ParseRaceRow(IWebElement row)
    {
        var cells = row.FindElements(By.TagName("td"));
        return new MigrationRaceRow(
            RaceCode: cells[0].Text.Trim(),
            Participant: cells[1].Text.Trim(),
            Imported: ParseInt(cells[2].Text),
            Calculated: ParseInt(cells[3].Text),
            Delta: ParseInt(cells[4].Text),
            Reason: cells[5].Text.Trim(),
            Explanation: cells[6].Text.Trim());
    }

    private static MigrationPickRow ParsePickRow(IWebElement row)
    {
        var cells = row.FindElements(By.TagName("td"));
        return new MigrationPickRow(
            RaceCode: cells[0].Text.Trim(),
            Participant: cells[1].Text.Trim(),
            PickType: cells[2].Text.Trim(),
            Imported: ParseNullableInt(cells[3].Text),
            Calculated: ParseNullableInt(cells[4].Text),
            Delta: ParseInt(cells[5].Text),
            Reason: cells[6].Text.Trim(),
            Explanation: cells[7].Text.Trim());
    }

    private static int ParseInt(string value)
    {
        return int.TryParse(value.Trim(), out var parsed) ? parsed : 0;
    }

    private static int? ParseNullableInt(string value)
    {
        var trimmed = value.Trim();
        if (trimmed == "-")
        {
            return null;
        }

        return int.TryParse(trimmed, out var parsed) ? parsed : null;
    }
}
