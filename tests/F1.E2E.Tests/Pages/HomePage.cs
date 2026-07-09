using F1.E2E.Tests.Infrastructure;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace F1.E2E.Tests.Pages;

internal class HomePage
{
    private static readonly string[] AuthenticatedNavigationSelectors =
    [
        "a[href='results']",
        "a[href='/results']",
        "a[href='drivers']",
        "a[href='/drivers']",
        "a[href='selection']",
        "a[href='/selection']",
        "a[href^='selection/']",
        "a[href^='/selection/']",
        "a[href='admin']",
        "a[href='/admin']",
        "a[href='admin/migration-runs']",
        "a[href='/admin/migration-runs']"
    ];

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
            driver => AuthenticatedNavigationSelectors.Any(selector => driver.FindElements(By.CssSelector(selector)).Count > 0));
        _trace("Authenticated navigation link rendered.");
    }

    public bool IsAccessDeniedVisible()
    {
        return _driver.PageSource.Contains("Access Denied", StringComparison.OrdinalIgnoreCase);
    }
}
