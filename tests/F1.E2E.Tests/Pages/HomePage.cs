using F1.E2E.Tests.Infrastructure;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace F1.E2E.Tests.Pages;

internal class HomePage
{
    private readonly IWebDriver _driver;
    private readonly WebDriverWait _wait;
    private readonly string _baseUrl;
    private readonly Action<string> _trace;

    public HomePage(IWebDriver driver, WebDriverWait wait, string baseUrl, Action<string>? trace = null)
    {
        _driver = driver;
        _wait = wait;
        _baseUrl = baseUrl.TrimEnd('/');
        _trace = trace ?? (_ => { });
    }

    public void Navigate()
    {
        _trace($"Navigate -> {_baseUrl}/");
        _driver.Navigate().GoToUrl(_baseUrl + "/");
        _trace($"Navigation complete. Current URL: {_driver.Url}");
    }

    public void WaitForAuthenticatedNavigation()
    {
        _trace("Waiting for authenticated navigation link to render...");
        PageReadiness.WaitForAppReady(
            _driver,
            _wait.Timeout,
            driver => driver.FindElements(By.CssSelector("a[href^='selection/']")).Count > 0);
        _trace("Authenticated navigation link rendered.");
    }

    public bool IsAccessDeniedVisible()
    {
        return _driver.PageSource.Contains("Access Denied", StringComparison.OrdinalIgnoreCase);
    }
}
