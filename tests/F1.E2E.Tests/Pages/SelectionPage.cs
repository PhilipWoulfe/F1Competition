using F1.E2E.Tests.Infrastructure;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace F1.E2E.Tests.Pages;

internal class SelectionPage
{
    private readonly IWebDriver _driver;
    private readonly WebDriverWait _wait;
    private readonly string _baseUrl;
    private readonly string _selectionRoutePath;
    private readonly Action<string> _trace;

    public SelectionPage(IWebDriver driver, WebDriverWait wait, string baseUrl, string selectionRoutePath, Action<string>? trace = null)
    {
        _driver = driver;
        _wait = wait;
        _baseUrl = baseUrl.TrimEnd('/');
        _selectionRoutePath = selectionRoutePath.Trim('/');
        _trace = trace ?? (_ => { });
    }

    public void Navigate()
    {
        _trace($"Navigate -> {_baseUrl}/{_selectionRoutePath}");
        _driver.Navigate().GoToUrl($"{_baseUrl}/{_selectionRoutePath}");
        _trace($"Navigation complete. Current URL: {_driver.Url}");
    }

    public void WaitUntilReady()
    {
        _trace("Waiting for selection form to render...");
        PageReadiness.WaitForAppReady(
            _driver,
            _wait.Timeout,
            driver => driver.FindElements(By.CssSelector("select[id^='driver-select-']")).Count > 0);

        var slotCount = GetSelectionSlotCount();
        _trace($"Selection form rendered with {slotCount} dropdown(s).");
    }

    public int GetSelectionSlotCount()
    {
        return _driver.FindElements(By.CssSelector("select[id^='driver-select-']")).Count;
    }

    public IReadOnlyList<string> GetSelectableDriverIds()
    {
        _trace("Reading selectable driver ids from dropdown...");
        var firstDropdown = _driver.FindElement(By.Id("driver-select-1"));
        var select = new SelectElement(firstDropdown);

        var result = select.Options
            .Select(option => option.GetAttribute("value"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        _trace($"Found {result.Count} selectable driver ids.");
        return result;
    }

    public void SelectDrivers(IReadOnlyList<string> driverIds)
    {
        var slotCount = GetSelectionSlotCount();
        if (slotCount <= 0)
        {
            throw new InvalidOperationException("No selection slots were found on the page.");
        }

        if (driverIds.Count < slotCount)
        {
            throw new InvalidOperationException($"At least {slotCount} unique driver IDs are required.");
        }

        var chosenDriverIds = driverIds
            .Take(slotCount)
            .Where(static driverId => !string.IsNullOrWhiteSpace(driverId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        if (chosenDriverIds < slotCount)
        {
            throw new InvalidOperationException($"At least {slotCount} unique driver IDs are required.");
        }

        for (var i = 0; i < slotCount; i++)
        {
            var dropdown = new SelectElement(_driver.FindElement(By.Id($"driver-select-{i + 1}")));
            _trace($"Selecting position {i + 1} -> {driverIds[i]}");
            dropdown.SelectByValue(driverIds[i]);
        }
    }

    public void ClickSubmit()
    {
        _trace("Waiting for submit button to become interactable...");
        var submit = _wait.Until(driver =>
        {
            var element = driver.FindElement(By.Id("submit-selection"));
            return element.Displayed && element.Enabled ? element : null;
        })!;

        ((IJavaScriptExecutor)_driver).ExecuteScript(
            "arguments[0].scrollIntoView({block: 'center', inline: 'center'});",
            submit);

        _wait.Until(_ => submit.Displayed && submit.Enabled);

        try
        {
            _trace("Clicking submit button (native click).");
            submit.Click();
        }
        catch (ElementClickInterceptedException)
        {
            _trace("Native click intercepted; retrying submit with JavaScript click.");
            ((IJavaScriptExecutor)_driver).ExecuteScript("arguments[0].click();", submit);
        }
    }

    public void WaitForSaveConfirmation()
    {
        _trace("Waiting for 'Selection saved successfully.' confirmation...");
        _wait.Until(driver => driver.PageSource.Contains("Selection saved successfully.", StringComparison.Ordinal));
        _trace("Selection save confirmation displayed.");
    }

    public bool IsAnyDropdownDisabled()
    {
        var slotCount = GetSelectionSlotCount();
        for (var i = 0; i < slotCount; i++)
        {
            var dropdown = _driver.FindElement(By.Id($"driver-select-{i + 1}"));
            if (dropdown.GetAttribute("disabled") != null)
                return true;
        }
        return false;
    }

    public bool IsSubmitDisabled()
    {
        var submit = _driver.FindElement(By.Id("submit-selection"));
        return !submit.Enabled || submit.GetAttribute("disabled") != null;
    }

    public bool IsLockedMessageVisible()
    {
        return _driver.PageSource.Contains("locked", StringComparison.OrdinalIgnoreCase) ||
               _driver.PageSource.Contains("forbidden", StringComparison.OrdinalIgnoreCase) ||
               _driver.PageSource.Contains("error", StringComparison.OrdinalIgnoreCase);
    }
}
