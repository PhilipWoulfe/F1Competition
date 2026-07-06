using F1.E2E.Tests.Infrastructure;
using F1.E2E.Tests.Pages;
using Xunit.Abstractions;

namespace F1.E2E.Tests.Flows;

public sealed class MigrationRunsFlowsTests(ITestOutputHelper output)
{
    [E2EFact]
    public void AdminMigrationRuns_ShouldLoadRun_AndShowResultTables()
    {
        var options = E2eOptions.FromEnvironment();

        using var trace = E2eStepTrace.Start(nameof(AdminMigrationRuns_ShouldLoadRun_AndShowResultTables), output);
        using var driver = WebDriverFactory.Create(options);
        var wait = WebDriverFactory.CreateWait(driver, options.Timeout);

        var testPassed = false;

        try
        {
            var homePage = new HomePage(driver, wait, options.BaseUrl, trace.Log);
            homePage.Navigate();
            homePage.WaitForAuthenticatedNavigation();
            Assert.False(homePage.IsAccessDeniedVisible());

            var migrationRunsPage = new MigrationRunsPage(driver, wait, options.BaseUrl, trace.Log);
            migrationRunsPage.Navigate();
            migrationRunsPage.WaitUntilReady();
            migrationRunsPage.SelectFirstRun();
            migrationRunsPage.WaitForRunDetail();

            Assert.True(migrationRunsPage.WaitForPreseasonComparisonSection());
            Assert.True(migrationRunsPage.WaitForPreseasonQuestionDiffRows());
            Assert.True(migrationRunsPage.WaitForParticipantComparisonSection());
            Assert.True(migrationRunsPage.WaitForRaceComparisonSection());
            Assert.True(migrationRunsPage.WaitForPickComparisonSection());

            testPassed = true;
        }
        finally
        {
            trace.Log($"Test completed with status: {(testPassed ? "PASSED" : "FAILED")}");
            if (!testPassed)
            {
                E2eArtifacts.CaptureOnFailure(driver, nameof(AdminMigrationRuns_ShouldLoadRun_AndShowResultTables), output);
            }

            DebugHold.WaitIfEnabled("AdminMigrationRuns_ShouldLoadRun_AndShowResultTables teardown");
        }
    }
}
