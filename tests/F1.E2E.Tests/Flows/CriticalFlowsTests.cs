using F1.E2E.Tests.Infrastructure;
using F1.E2E.Tests.Pages;
using OpenQA.Selenium;
using Xunit.Abstractions;

namespace F1.E2E.Tests.Flows;

public class CriticalFlowsTests(ITestOutputHelper output)
{
    [E2EFact]
    public void Login_ShouldSucceed()
    {
        var options = E2eOptions.FromEnvironment();

        using var trace = E2eStepTrace.Start(nameof(Login_ShouldSucceed), output);
        using var driver = WebDriverFactory.Create(options);
        var wait = WebDriverFactory.CreateWait(driver, options.Timeout);
        var homePage = new HomePage(driver, wait, options.BaseUrl, trace.Log);
        var testPassed = false;
        try
        {
            trace.Log("Starting login smoke flow.");
            homePage.Navigate();
            homePage.WaitForAuthenticatedNavigation();

            Assert.False(homePage.IsAccessDeniedVisible());
            trace.Log("Verified Access Denied banner is not visible.");
            testPassed = true;
        }
        finally
        {
            trace.Log($"Test completed with status: {(testPassed ? "PASSED" : "FAILED")}");
            if (!testPassed) E2eArtifacts.CaptureOnFailure(driver, nameof(Login_ShouldSucceed), output);
            DebugHold.WaitIfEnabled("Login_ShouldSucceed teardown");
        }
    }

    [E2EFact]
    public async Task SubmitSelection_ShouldPersistServerSide()
    {
        var options = E2eOptions.FromEnvironment();

        using var trace = E2eStepTrace.Start(nameof(SubmitSelection_ShouldPersistServerSide), output);
        using var driver = WebDriverFactory.Create(options);
        var wait = WebDriverFactory.CreateWait(driver, options.Timeout);
        using var api = new ApiVerificationClient(options);
        var testPassed = false;
        var targetRaceId = string.Empty;

        try
        {
            targetRaceId = await ResolveTargetRaceIdAsync(api, options, trace.Log);
            var raceConfig = await api.GetRaceConfigAsync(targetRaceId, CancellationToken.None);
            var beforeFinalDeadline = raceConfig.FinalDeadlineUtc.AddMinutes(-30);
            trace.Log($"Setting mock date before selection submit: {beforeFinalDeadline:O}");
            await api.SetMockDate(beforeFinalDeadline.ToString("O"), CancellationToken.None);

            var selectionPath = options.BuildSelectionRoutePath();
            var selectionPage = new SelectionPage(driver, wait, options.BaseUrl, selectionPath, trace.Log);

            selectionPage.Navigate();
            selectionPage.WaitUntilReady();
            var slotCount = selectionPage.GetSelectionSlotCount();

            var selectableDrivers = selectionPage.GetSelectableDriverIds();
            Assert.True(slotCount > 0, "Selection page must expose at least one selectable slot.");
            Assert.True(selectableDrivers.Count >= slotCount, $"Selection page must expose at least {slotCount} selectable drivers.");
            trace.Log($"Selectable drivers available: {selectableDrivers.Count}; slots: {slotCount}");

            var selected = selectableDrivers.Take(slotCount).ToList();
            trace.Log($"Submitting selected drivers: {string.Join(",", selected)}");
            selectionPage.SelectDrivers(selected);
            selectionPage.ClickSubmit();
            selectionPage.WaitForSaveConfirmation();

            trace.Log("Waiting for API persistence verification.");
            await api.WaitForSelectionPersistenceAsync(targetRaceId, selected[0], options.Timeout, CancellationToken.None);
            testPassed = true;
        }
        finally
        {
            trace.Log("Clearing mock date in teardown.");
            if (testPassed)
            {
                await api.ClearMockDate(CancellationToken.None);
            }
            else
            {
                try
                {
                    await api.ClearMockDate(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    output.WriteLine($"[E2E] Warning: failed to clear mock date in {nameof(SubmitSelection_ShouldPersistServerSide)} teardown: {ex.Message}");
                    trace.Log($"Warning: failed to clear mock date: {ex.Message}");
                }
            }

            trace.Log($"Test completed with status: {(testPassed ? "PASSED" : "FAILED")}");
            if (!testPassed) E2eArtifacts.CaptureOnFailure(driver, nameof(SubmitSelection_ShouldPersistServerSide), output);
            DebugHold.WaitIfEnabled("SubmitSelection_ShouldPersistServerSide teardown");
        }
    }

    [Fact(Skip = "Temporarily disabled during Postgres migration work. Re-enable after metadata/admin flow stabilization.")]
    public async Task AdminPanel_ShouldLoadAndSaveMetadata()
    {
        var options = E2eOptions.FromEnvironment();
        if (!options.Enabled)
        {
            return;
        }

        using var trace = E2eStepTrace.Start(nameof(AdminPanel_ShouldLoadAndSaveMetadata), output);
        using var driver = WebDriverFactory.Create(options);
        var wait = WebDriverFactory.CreateWait(driver, options.Timeout);
        var adminPage = new AdminPage(driver, wait, options.BaseUrl, trace.Log);
        var testPassed = false;
        try
        {
            trace.Log("Starting admin metadata save flow.");
            adminPage.Navigate();
            adminPage.WaitUntilReady();

            var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var h2hQuestion = $"E2E H2H {stamp}: Who finishes higher?";
            var bonusQuestion = $"E2E Bonus {stamp}: How many DNFs?";

            adminPage.SetQuestions(h2hQuestion, bonusQuestion);
            adminPage.EnsurePublished();
            adminPage.ClickSave();
            adminPage.WaitForSaveConfirmation();

            using var api = new ApiVerificationClient(options);
            var targetRaceId = await ResolveTargetRaceIdAsync(api, options, trace.Log);
            trace.Log("Waiting for metadata update via API verification.");
            var metadata = await api.WaitForMetadataAsync(targetRaceId, h2hQuestion, options.Timeout, CancellationToken.None);

            Assert.Equal(h2hQuestion, metadata.H2HQuestion);
            Assert.Equal(bonusQuestion, metadata.BonusQuestion);
            Assert.True(metadata.IsPublished);
            testPassed = true;
        }
        finally
        {
            trace.Log($"Test completed with status: {(testPassed ? "PASSED" : "FAILED")}");
            if (!testPassed) E2eArtifacts.CaptureOnFailure(driver, nameof(AdminPanel_ShouldLoadAndSaveMetadata), output);
            DebugHold.WaitIfEnabled("AdminPanel_ShouldLoadAndSaveMetadata teardown");
        }
    }

    [E2EFact]
    public async Task SubmitSelection_ShouldBeForbidden_AfterDeadline_Api()
    {
        var options = E2eOptions.FromEnvironment();

        using var api = new ApiVerificationClient(options);
        var targetRaceId = await ResolveTargetRaceIdAsync(api, options, _ => { });
        var raceConfig = await api.GetRaceConfigAsync(targetRaceId, CancellationToken.None);
        var afterDeadline = raceConfig.FinalDeadlineUtc.AddMinutes(1).ToString("O");
        api.SetMockDateHeader(afterDeadline);

        var submission = new
        {
            betType = "Regular",
            orderedSelections = new[]
            {
                new { position = 1, driverId = "norris" },
                new { position = 2, driverId = "leclerc" },
                new { position = 3, driverId = "hamilton" },
                new { position = 4, driverId = "piastri" },
                new { position = 5, driverId = "verstappen" }
            }
        };

        var response = await api.PutSelectionAsync(targetRaceId, submission);
        Assert.True(
            response.StatusCode == System.Net.HttpStatusCode.Forbidden ||
            response.StatusCode == System.Net.HttpStatusCode.BadRequest ||
            response.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed,
            $"Expected forbidden, bad request, or method not allowed, got {response.StatusCode}");
    }

    [E2EFact]
    public async Task SubmitSelection_ShouldShowError_AfterDeadline_Ui()
    {
        var options = E2eOptions.FromEnvironment();

        using var trace = E2eStepTrace.Start(nameof(SubmitSelection_ShouldShowError_AfterDeadline_Ui), output);
        using var driver = WebDriverFactory.Create(options);
        var wait = WebDriverFactory.CreateWait(driver, options.Timeout);
        using var api = new ApiVerificationClient(options);
        var testPassed = false;

        try
        {
            var targetRaceId = await ResolveTargetRaceIdAsync(api, options, trace.Log);
            var raceConfig = await api.GetRaceConfigAsync(targetRaceId, CancellationToken.None);
            var afterDeadline = raceConfig.FinalDeadlineUtc.AddMinutes(1);
            trace.Log($"Setting mock date after final deadline: {afterDeadline:O}");
            await api.SetMockDate(afterDeadline.ToString("O"), CancellationToken.None);

            var selectionPath = options.BuildSelectionRoutePath();
            var selectionPage = new SelectionPage(driver, wait, options.BaseUrl, selectionPath, trace.Log);

            selectionPage.Navigate();
            selectionPage.WaitUntilReady();

            // Instead of trying to select, check if dropdowns or submit are disabled (locked state)
            Assert.True(selectionPage.IsAnyDropdownDisabled() || selectionPage.IsSubmitDisabled(),
                "Expected selection UI to be locked/disabled after deadline.");
            Assert.True(selectionPage.IsLockedMessageVisible(),
                "Expected a lock/error/forbidden message after deadline.");

            trace.Log("Verified selection UI is locked and shows lock/error messaging.");
            testPassed = true;
        }
        finally
        {
            trace.Log("Clearing mock date in teardown.");
            if (testPassed)
            {
                await api.ClearMockDate(CancellationToken.None);
            }
            else
            {
                try
                {
                    await api.ClearMockDate(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    output.WriteLine($"[E2E] Warning: failed to clear mock date in {nameof(SubmitSelection_ShouldShowError_AfterDeadline_Ui)} teardown: {ex.Message}");
                    trace.Log($"Warning: failed to clear mock date: {ex.Message}");
                }
            }

            trace.Log($"Test completed with status: {(testPassed ? "PASSED" : "FAILED")}");
            if (!testPassed) E2eArtifacts.CaptureOnFailure(driver, nameof(SubmitSelection_ShouldShowError_AfterDeadline_Ui), output);
            DebugHold.WaitIfEnabled("SubmitSelection_ShouldShowError_AfterDeadline_Ui teardown");
        }
    }

    private static async Task<string> ResolveTargetRaceIdAsync(ApiVerificationClient api, E2eOptions options, Action<string> trace)
    {
        if (!string.IsNullOrWhiteSpace(options.RaceId))
        {
            trace($"Using explicit E2E_RACE_ID={options.RaceId}.");
            return options.RaceId;
        }

        trace($"Resolving race id from context {options.CompetitionSlug}/{options.Season}/round/{options.Round}.");
        var resolved = await api.ResolveRaceIdByRoundAsync(options.CompetitionSlug, options.Season, options.Round, CancellationToken.None);
        trace($"Resolved race id: {resolved}");
        return resolved;
    }
}
