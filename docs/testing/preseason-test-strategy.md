# Preseason Test Strategy and CI Gates

This guide defines the preseason-focused validation matrix and quick commands used to catch regressions early.

## Scope

Preseason test coverage must validate:

- CSV contract and section classification for preseason row windows.
- Parser normalization and traceability for preseason answers and actual outcomes.
- Scoring and reconciliation behavior for preseason expected-vs-actual paths.
- Worker run lifecycle safeguards (dry-run/write-run metadata and contamination guard).
- Admin API payload and auth behavior for preseason detail sections.
- Admin web preseason review rendering/filtering/export links.
- E2E run list to preseason question-level detail navigation.

## CI Gate

Pull request validation runs a preseason-focused gate in `code-quality.yml`:

- `tests/F1.Infrastructure.Tests/F1.Infrastructure.Tests.csproj`
- Filter:
  - `MigrationPhil2025CsvContractPolicyTests`
  - `MigrationRaceSelectionParserTests`
  - `MigrationScoreRecalculatorTests`
  - `MigrationImportRunServiceTests`
- Coverage output:
  - `./coverage/infra-coverage`

This gate complements full API and Web unit-test coverage jobs.

## Quick Local Commands

Use these commands for fast preseason validation before opening a PR.

### 1. Worker Unit and Integration Coverage

```bash
dotnet test tests/F1.Infrastructure.Tests/F1.Infrastructure.Tests.csproj \
  --configuration Debug \
  --nologo \
  --verbosity minimal \
  --filter 'FullyQualifiedName~MigrationPhil2025CsvContractPolicyTests|FullyQualifiedName~MigrationRaceSelectionParserTests|FullyQualifiedName~MigrationScoreRecalculatorTests|FullyQualifiedName~MigrationImportRunServiceTests' \
  --collect:'XPlat Code Coverage' \
  --settings coverlet.runsettings \
  --results-directory ./coverage/infra-coverage
```

### 2. Admin API Preseason Payload Coverage

```bash
dotnet test tests/F1.Api.Tests/F1.Api.Tests.csproj \
  --configuration Debug \
  --nologo \
  --verbosity minimal \
  --filter 'FullyQualifiedName~MigrationRunAdminServiceTests|FullyQualifiedName~MigrationRunsControllerTests|FullyQualifiedName~AdminMigrationRunsRouteAccessIntegrationTests' \
  --collect:'XPlat Code Coverage' \
  --settings coverlet.api.runsettings \
  --results-directory ./coverage/api-coverage
```

### 3. Admin Web Preseason Review Coverage

```bash
dotnet test tests/F1.Web.Tests/F1.Web.Tests.csproj \
  --configuration Debug \
  --nologo \
  --verbosity minimal \
  --filter 'FullyQualifiedName~AdminMigrationRunsTests' \
  --collect:'XPlat Code Coverage' \
  --settings coverlet.web.runsettings \
  --results-directory ./coverage/web-coverage
```

### 4. E2E Preseason Review Flow

```bash
dotnet test tests/F1.E2E.Tests/F1.E2E.Tests.csproj \
  --configuration Debug \
  --nologo \
  --verbosity minimal \
  --filter 'FullyQualifiedName~MigrationRunsFlowsTests'
```

## Required Story-9 Assertions

The preseason suite must continue validating all of the following:

- Unit: classifier/parser/normalization/scoring logic.
- Integration: worker persistence and admin API preseason payload projection.
- E2E: migration run detail shows preseason section and question-level table rows.
- Coverage reports are generated for API, Web, and Infrastructure preseason paths.

## Related Runbook

- `docs/runbooks/preseason-migration-signoff.md`
