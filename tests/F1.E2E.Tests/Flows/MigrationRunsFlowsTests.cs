using F1.E2E.Tests.Infrastructure;
using F1.E2E.Tests.Pages;
using Xunit.Abstractions;

namespace F1.E2E.Tests.Flows;

public sealed class MigrationRunsFlowsTests(ITestOutputHelper output)
{
    [E2EFact]
    public async Task AdminMigrationRuns_ShouldLoadKnownRun_AndApplyDetailFilters()
    {
        var options = E2eOptions.FromEnvironment();

        using var trace = E2eStepTrace.Start(nameof(AdminMigrationRuns_ShouldLoadKnownRun_AndApplyDetailFilters), output);
        using var driver = WebDriverFactory.Create(options);
        var wait = WebDriverFactory.CreateWait(driver, options.Timeout);

        var testPassed = false;

        try
        {
            var fixture = await MigrationRunE2eFixtureSeeder.EnsureSeededAsync(options, trace.Log, CancellationToken.None);

            var homePage = new HomePage(driver, wait, options.BaseUrl, trace.Log);
            homePage.Navigate();
            homePage.WaitForAuthenticatedNavigation();
            Assert.False(homePage.IsAccessDeniedVisible());

            var migrationRunsPage = new MigrationRunsPage(driver, wait, options.BaseUrl, trace.Log);
            migrationRunsPage.Navigate();
            migrationRunsPage.WaitUntilReady();
            migrationRunsPage.SelectRun(fixture.RunId);
            migrationRunsPage.WaitForRunDetail();

            var baselineParticipantRows = migrationRunsPage.GetParticipantRows();
            var baselineRaceRows = migrationRunsPage.GetRaceRows();
            var baselinePickRows = migrationRunsPage.GetPickRows();

            Assert.True(baselineParticipantRows.Count >= 2, "Expected fixture to provide participant rows.");
            Assert.True(baselineRaceRows.Count >= 2, "Expected fixture to provide race diff rows.");
            Assert.True(baselinePickRows.Count >= 2, "Expected fixture to provide pick diff rows.");
            Assert.Contains(baselineParticipantRows, row => row.Delta == 0);
            Assert.Contains(baselineRaceRows, row => row.Delta == 0);
            Assert.Contains(baselinePickRows, row => row.Delta == 0);

            migrationRunsPage.SetParticipantFilter(fixture.PrimaryParticipant);
            migrationRunsPage.WaitUntil(() =>
            {
                var participantRows = migrationRunsPage.GetParticipantRows();
                var raceRows = migrationRunsPage.GetRaceRows();
                var pickRows = migrationRunsPage.GetPickRows();

                return participantRows.Count == 1 &&
                       raceRows.Count == 1 &&
                       pickRows.Count == 1 &&
                       participantRows.All(row => row.Participant.Contains(fixture.PrimaryParticipant, StringComparison.OrdinalIgnoreCase)) &&
                       raceRows.All(row => row.Participant.Contains(fixture.PrimaryParticipant, StringComparison.OrdinalIgnoreCase)) &&
                       pickRows.All(row => row.Participant.Contains(fixture.PrimaryParticipant, StringComparison.OrdinalIgnoreCase));
            });

            migrationRunsPage.SetParticipantFilter(string.Empty);
            migrationRunsPage.SetRaceFilter(fixture.RaceCode);
            migrationRunsPage.WaitUntil(() =>
            {
                var raceRows = migrationRunsPage.GetRaceRows();
                var pickRows = migrationRunsPage.GetPickRows();
                return raceRows.Count >= 2 &&
                       pickRows.Count >= 2 &&
                       raceRows.All(row => string.Equals(row.RaceCode, fixture.RaceCode, StringComparison.OrdinalIgnoreCase)) &&
                       pickRows.All(row => string.Equals(row.RaceCode, fixture.RaceCode, StringComparison.OrdinalIgnoreCase));
            });

            migrationRunsPage.SetReasonFilter(fixture.ReasonCode);
            migrationRunsPage.WaitUntil(() =>
            {
                var participantRows = migrationRunsPage.GetParticipantRows();
                var raceRows = migrationRunsPage.GetRaceRows();
                var pickRows = migrationRunsPage.GetPickRows();

                return participantRows.Count == 1 &&
                       raceRows.Count == 1 &&
                       pickRows.Count == 1 &&
                       participantRows.All(row => row.TopReason.Contains(fixture.ReasonCode, StringComparison.OrdinalIgnoreCase)) &&
                       raceRows.All(row => row.Reason.Contains(fixture.ReasonCode, StringComparison.OrdinalIgnoreCase)) &&
                       pickRows.All(row => row.Reason.Contains(fixture.ReasonCode, StringComparison.OrdinalIgnoreCase));
            });

            migrationRunsPage.SetRaceFilter(string.Empty);
            migrationRunsPage.SetReasonFilter(string.Empty);
            migrationRunsPage.SetNonZeroOnly(true);
            migrationRunsPage.WaitUntil(() =>
            {
                var participantRows = migrationRunsPage.GetParticipantRows();
                var raceRows = migrationRunsPage.GetRaceRows();
                var pickRows = migrationRunsPage.GetPickRows();

                return participantRows.Count >= 1 &&
                       raceRows.Count >= 1 &&
                       pickRows.Count >= 1 &&
                       participantRows.All(row => row.Delta != 0) &&
                       raceRows.All(row => row.Delta != 0) &&
                       pickRows.All(row => row.Delta != 0);
            });

            testPassed = true;
        }
        finally
        {
            trace.Log($"Test completed with status: {(testPassed ? "PASSED" : "FAILED")}");
            if (!testPassed)
            {
                E2eArtifacts.CaptureOnFailure(driver, nameof(AdminMigrationRuns_ShouldLoadKnownRun_AndApplyDetailFilters), output);
            }

            DebugHold.WaitIfEnabled("AdminMigrationRuns_ShouldLoadKnownRun_AndApplyDetailFilters teardown");
        }
    }
}
