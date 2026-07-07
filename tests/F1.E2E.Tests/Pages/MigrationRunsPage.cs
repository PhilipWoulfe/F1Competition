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
        var selector = By.XPath($"//tr[td/code[@title='{runId}']]//button[normalize-space()='View']");
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
        return GetRowsAfterSection("participant-comparisons")
            .Select(ParseParticipantRow)
            .ToList();
    }

    public IReadOnlyList<MigrationRaceRow> GetRaceRows()
    {
        return GetRowsAfterSection("race-comparisons")
            .Select(ParseRaceRow)
            .ToList();
    }

    public IReadOnlyList<MigrationPickRow> GetPickRows()
    {
        return GetRowsAfterSection("pick-comparisons")
            .Select(ParsePickRow)
            .ToList();
    }

    public bool WaitForParticipantComparisonSection()
    {
        OpenTab("tab-race-participants", "Participant Comparisons");

        var sectionSelector = By.XPath(
            "//div[@id='pane-race-participants' and contains(@class,'show') and contains(@class,'active')]" +
            "//*[self::div[.//tbody/tr] or self::p[normalize-space()='No participant deltas available for this run.']]");
        return _wait.Until(driver => driver.FindElements(sectionSelector).Count > 0);
    }

    public bool WaitForPreseasonComparisonSection()
    {
        OpenTab("tab-preseason", "Expected vs Actual Review");

        return _wait.Until(driver =>
            driver.FindElements(By.XPath("//div[@id='pane-preseason' and contains(@class,'show') and contains(@class,'active')]//h3[contains(normalize-space(),'Expected vs Actual Review')]")).Count > 0 &&
            driver.FindElements(By.Id("preseason-participant-filter")).Count > 0);
    }

    public bool WaitForPreseasonQuestionDiffSection()
    {
        var sectionSelector = By.XPath(
            "//h4[normalize-space()='Preseason Question Diffs']" +
            "/following-sibling::*[1]" +
            "[self::div[.//tbody/tr] or self::p[normalize-space()='No preseason question diffs available for this run.']]");
        return _wait.Until(driver => driver.FindElements(sectionSelector).Count > 0);
    }

    public bool WaitForRaceComparisonSection()
    {
        OpenTab("tab-race-diffs", "Race Comparisons");

        var sectionSelector = By.XPath(
            "//div[@id='pane-race-diffs' and contains(@class,'show') and contains(@class,'active')]" +
            "//*[self::div[.//tbody/tr] or self::p[normalize-space()='No race diffs available for this run.']]");
        return _wait.Until(driver => driver.FindElements(sectionSelector).Count > 0);
    }

    public bool WaitForPickComparisonSection()
    {
        OpenTab("tab-pick-diffs", "Pick Comparisons");

        var sectionSelector = By.XPath(
            "//div[@id='pane-pick-diffs' and contains(@class,'show') and contains(@class,'active')]" +
            "//*[self::div[.//tbody/tr] or self::p[normalize-space()='No pick diffs available for this run.']]");
        return _wait.Until(driver => driver.FindElements(sectionSelector).Count > 0);
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

    private IReadOnlyList<IWebElement> GetRowsAfterSection(string sectionId)
    {
        var rows = _driver.FindElements(By.XPath($"//*[@id='{sectionId}']/following-sibling::*[1][self::div]//tbody/tr"));
        return rows;
    }

    private void OpenTab(string tabId, string sectionTitle)
    {
        _trace($"Opening tab {tabId} for section '{sectionTitle}'");
        var tab = _wait.Until(driver => driver.FindElement(By.Id(tabId)));
        tab.Click();
        _wait.Until(driver =>
            driver.FindElements(By.XPath($"//button[@id='{tabId}' and @aria-selected='true']")).Count > 0 &&
            driver.FindElements(By.XPath($"//*[self::h3 or self::h4][contains(normalize-space(),'{sectionTitle}')] ")).Count > 0);
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
